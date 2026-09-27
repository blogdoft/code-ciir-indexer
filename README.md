# CIIR Indexer

Reads CIIR JSONL, generates embeddings, and upserts documents/relations into PostgreSQL +
pgvector. See `CLAUDE.md` for architecture and tooling conventions, and `.specs/` for the
detailed functional specs (`01-Spec-inicial.md` for the core indexer, `02-upload-ciir-garage.md`
for the Garage upload endpoint,
`04-uploads-only.md` for why the old local-path endpoint is gone,
`05-keycloak-auth.md` for the optional Keycloak authentication, `06-token-gateway.md` for the
token endpoint non-interactive clients use to get a token).

## Indexing a CIIR file

There is no local-filesystem-path entry point - every CIIR file reaches indexation through the object storage (Garage),
one of two ways:

- **`POST /api/ciir-uploads`** (multipart, `projectId` + `ciirFile` fields): the default path for
  most files, up to `Uploads:MaxCiirFileSizeBytes` (200 MB by default). This service streams the
  file into its `ciir-uploads` bucket for you.
- **`POST /api/ciir-uploads/register`** (JSON, `{"projectId": "...", "objectKey": "..."}"`): for a
  file too large for a single HTTP request. Upload it directly to the same bucket yourself first
  (e.g. `aws --endpoint-url <garage s3 url> --region garage s3 cp big-ciir.jsonl s3://ciir-uploads/<objectKey>`
  using the `ciir-indexer-uploader` credentials below, or a presigned URL), then call this endpoint with the
  key you uploaded it under. The object's existence is checked before it's registered.

Either way, poll `GET /api/ciir-uploads/{uploadId}` for status, then `GET /api/indexations/{indexationId}`
once that reports an `indexationId`.

## Authentication (Keycloak)

Authentication is **opt-in** and controlled by one explicit switch, `Keycloak:Enabled` (default
`false`: every endpoint is open, and the rest of the section is ignored). Setting it to `true` turns on
JWT bearer authentication: every route then requires an access token issued by the realm in
`Keycloak:Authority` (`.specs/05-keycloak-auth.md`).

> Filling in `Authority`/`Audience`/`ClientId` does **not** protect the API by itself - without
> `Keycloak__Enabled=true` they are ignored and everything stays open.

| Setting (`Keycloak__*` as env var) | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Turns authentication on/off. When `false`, all other `Keycloak` settings are ignored. |
| `Authority` | *(empty)* | The realm's URL, e.g. `https://keycloak.example/realms/my-realm`. Required when `Enabled` is `true`. |
| `Audience` | *(empty)* | If set, the token's `aud` claim must contain it. If empty, the audience isn't checked (Keycloak access tokens carry `aud: account` unless the client has an audience mapper). |
| `ClientId` | *(empty)* | `client_id` of the realm client this application is registered as - a public client, and the same one Swagger UI logs in with (there is a single client id for the API and Swagger). When set (with `Enabled`), Swagger UI's **Authorize** redirects to the Keycloak login (see below). |
| `RequireHttpsMetadata` | `true` | Set `false` only for a local, plain-HTTP Keycloak. |

With `Enabled=true`, a missing, invalid or plain-HTTP `Authority` (while `RequireHttpsMetadata` is
`true`) fails startup instead of serving traffic unprotected.

Missing, expired or otherwise invalid tokens get `401` with a `WWW-Authenticate: Bearer` header and a
Problem Details body. Any valid token from the realm is accepted - there is no per-role authorization.

Deliberately left anonymous, even with Keycloak on: `GET /health` (the Kubernetes readiness probe has
no token), the OpenAPI document/Swagger UI (a browser can't attach a token to page navigation) and
`POST /api/indexer/auth/token` (it is what produces the token; see "Token gateway" below).
In Swagger UI, **Authorize** takes an access token pasted into the `Bearer` field and, when
`ClientId` is set, also offers an `OAuth2` login: it redirects to Keycloak (authorization code
+ PKCE) and comes back authorized, so "Try it out" works without fetching a token by hand. That
needs a client in the realm with **Client authentication = off**, **Standard flow = on**,
**PKCE Code Challenge Method = S256**, the Swagger `oauth2-redirect.html` URL in **Valid redirect
URIs** (e.g. `https://blogdoft.home.arpa/code-brain/api/indexer/swagger/oauth2-redirect.html`,
`http://localhost:5223/api/indexer/swagger/oauth2-redirect.html` locally) and the Swagger origin in
**Web origins** (the browser exchanges the code for a token itself, so CORS applies). If `Audience`
is set, give that client an audience mapper for it.

```bash
curl -H "Authorization: Bearer $TOKEN" https://blogdoft.home.arpa/code-brain/api/indexer/projects
```

### Token gateway (`POST /api/indexer/auth/token`)

Non-interactive clients (e.g. the `ciir --send` pipeline) can get an access token by talking only to
this service, without knowing where or how the realm issues tokens (`.specs/06-token-gateway.md`).
The endpoint is **anonymous** and only exists when `Keycloak:Enabled` is `true`; with authentication
off it answers a body-less `404`, since there is no token to issue.

Request (`application/json`) - `clientId` and `clientSecret` are both required:

```json
{ "clientId": "ciir-pipeline", "clientSecret": "..." }
```

| Status | When | Body |
|---|---|---|
| `200` | Token issued | `{ "accessToken": "<jwt>", "tokenType": "Bearer", "expiresIn": 300 }` (`expiresIn` in seconds, omitted if Keycloak doesn't report it) |
| `400` | `clientId` or `clientSecret` missing/blank | Problem Details |
| `401` | Keycloak refused the credentials (unknown client, wrong secret, client without *Service accounts*) | No response body (logged only; Keycloak's answer is not forwarded) |
| `404` | Authentication is off (`Keycloak:Enabled=false`) | none |
| `502` | Keycloak is unreachable, timed out (15 s) or answered something unusable | Problem Details |

Things to know:

- **Only the `client_credentials` grant.** It is not a generic Keycloak proxy: the request can't pick a
  grant, a `scope` or any other parameter.
- The token endpoint is derived from `Keycloak:Authority` (the realm's *public* URL) as
  `{Authority}/protocol/openid-connect/token`, **not** from `MetadataAddress`: Keycloak derives the
  token's `iss` from the host the request arrived on, and the JWT bearer handler validates `iss` against
  `Authority`. So the public host must be reachable from this service (it already has to be for the JWKS
  unless `MetadataAddress` points elsewhere).
- The Keycloak client used must be **confidential** (**Client authentication = on**) with **Service
  accounts roles = on**. If `Audience` is set, it also needs the audience mapper, as before.
- Use **HTTPS**: the request body carries the client secret. The secret is never logged nor echoed in
  an error; logs only carry the `clientId` and Keycloak's status.
- There is no rate limiting here, and Keycloak doesn't lock out client-secret guessing: since the route is
  anonymous, throttle it at the ingress.

```bash
TOKEN=$(curl -s -X POST https://blogdoft.home.arpa/code-brain/api/indexer/auth/token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"ciir-pipeline","clientSecret":"'"$CLIENT_SECRET"'"}' | jq -r .accessToken)

curl -H "Authorization: Bearer $TOKEN" https://blogdoft.home.arpa/code-brain/api/indexer/projects
```

## Garage — upload key

`POST /api/ciir-uploads` stores incoming CIIR files in a Garage bucket (`ciir-uploads`) until the
background worker picks them up. Garage is S3-compatible, so the adapter
(`Ciir.Indexer.Infrastructure.ObjectStorage.S3`) is the plain AWS S3 SDK pointed at Garage with
path-style addressing and the Garage region (`garage`, the server's `s3_region`). The application
should never authenticate with an admin credential, so it runs with a dedicated key.

### Local development

```bash
docker compose -f .eng/docker/docker-compose.yml up -d garage
.eng/docker/garage/init.sh      # once: node layout, dev access key, "ciir-uploads" bucket
```

`appsettings.json` already points at `localhost:3900` with the development key that script
imports. Any S3 client works for the bring-your-own-upload flow, e.g.
`aws --endpoint-url http://localhost:3900 --region garage s3 cp ciir.jsonl s3://ciir-uploads/<key>`.

### Creating the key in the cluster

Garage is administered with its own CLI, run inside the pod. These commands change the cluster, so
run them yourself (this repository's automation intentionally does not get permission to create
credentials on its own):

```bash
garage() { kubectl -n garage exec -i garage-0 -c garage -- /garage "$@"; }

garage key create ciir-indexer-uploader
garage key allow --create-bucket ciir-indexer-uploader
garage key info --show-secret ciir-indexer-uploader   # Key ID and Secret key for the Secret
```

The key is allowed to create buckets, so there is no bucket to create by hand: at startup the app
checks `ciir-uploads` is reachable and creates it if missing, and the creating key becomes its
owner. Two things follow from Garage's model: a bucket created through the S3 API only gets a
*local alias* of that key (it is invisible by name to `garage bucket info` and other keys - use
`garage bucket list` for its ID), and `create-bucket` lets the key create any bucket, not just
this one. To trade that convenience for least privilege, skip `key allow --create-bucket`, create
the bucket yourself (`garage bucket create ciir-uploads`) and grant the key access with
`garage bucket allow --read --write ciir-uploads --key ciir-indexer-uploader` - then a missing
bucket fails startup fast instead of being created.

### Where the credentials live afterward

- **Local development**: in this project's `dotnet user-secrets` if you override the defaults
  (`ObjectStorage:Endpoint`, `ObjectStorage:Region`, `ObjectStorage:UseSsl`,
  `ObjectStorage:BucketName`, `ObjectStorage:AccessKey`, `ObjectStorage:SecretKey`) - never in a
  committed file other than the throwaway development key in `appsettings.json`. Inspect with
  `dotnet user-secrets list --project src/Ciir.Indexer.Api`.
- **Cluster deployment**: `.eng/k8s/code-ciir-garage-secrets.yaml` documents the `Secret` shape
  `deployment.yaml` expects (`code-ciir-garage-secrets`, keys `access-key`/`secret-key`). Fill it
  with the key printed above; the file is git-ignored and is **not** applied automatically - decide
  how you want it into the cluster (`kubectl apply -f`, sealed-secrets, sops, ...) and do that
  yourself. Non-secret settings (`ObjectStorage__Endpoint`, `ObjectStorage__Region`,
  `ObjectStorage__UseSsl`, `ObjectStorage__BucketName`) are already wired into
  `.eng/k8s/configmap.yaml`.

### Rotating the key

Garage keys have no editable secret: create a new one, switch the Secret over, then delete the old
one.

```bash
garage key create ciir-indexer-uploader-2
garage key allow --create-bucket ciir-indexer-uploader-2
# if the bucket was created by the old key it only has that key as owner (local alias): create the
# bucket yourself with a global alias and `bucket allow` the new key before switching (see above)
# update code-ciir-garage-secrets and restart the deployment, then:
garage key delete --yes ciir-indexer-uploader
```
