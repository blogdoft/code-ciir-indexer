# CIIR Indexer

Reads CIIR JSONL, generates embeddings, and upserts documents/relations into PostgreSQL +
pgvector. See `CLAUDE.md` for architecture and tooling conventions, and `.specs/` for the
detailed functional specs (`01-Spec-inicial.md` for the core indexer, `02-upload-ciir-minio.md`
for the MinIO upload endpoint, `04-uploads-only.md` for why the old local-path endpoint is gone,
`05-keycloak-auth.md` for the optional Keycloak authentication).

## Indexing a CIIR file

There is no local-filesystem-path entry point - every CIIR file reaches indexation through MinIO,
one of two ways:

- **`POST /api/ciir-uploads`** (multipart, `projectId` + `ciirFile` fields): the default path for
  most files, up to `Uploads:MaxCiirFileSizeBytes` (200 MB by default). This service streams the
  file into its `ciir-uploads` bucket for you.
- **`POST /api/ciir-uploads/register`** (JSON, `{"projectId": "...", "objectKey": "..."}"`): for a
  file too large for a single HTTP request. Upload it directly to the same bucket yourself first
  (e.g. `mc cp big-ciir.jsonl local/ciir-uploads/<objectKey>` using the least-privilege
  `ciir-indexer-uploader` credentials below, or a presigned URL), then call this endpoint with the
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
no token) and the OpenAPI document/Swagger UI (a browser can't attach a token to page navigation).
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

## MinIO — least-privilege upload user

`POST /api/ciir-uploads` stores incoming CIIR files in a MinIO bucket (`ciir-uploads`) until the
background worker picks them up. The application should never authenticate to MinIO with the
cluster's root credentials - it only needs to read/write/delete objects in that one bucket, so it
runs as a dedicated MinIO user restricted to exactly that.

### Prerequisites

- `mc` (MinIO Client) to run the admin commands below. MinIO stopped publishing prebuilt binaries
  on `dl.min.io` and no longer allows anonymous pulls of `minio/minio`/`minio/mc` from Docker Hub
  - pull the client image from Quay.io instead and extract the binary:
  ```bash
  cid=$(docker create quay.io/minio/mc:latest)
  docker cp "$cid:/usr/bin/mc" ~/.local/bin/mc
  docker rm "$cid"
  chmod +x ~/.local/bin/mc
  ```
- `kubectl` access to the cluster running MinIO, with permission to read the `minio-credentials`
  Secret and create a Job in the `minio` namespace.

### Creating the user

Rather than typing the MinIO root password into a local `mc alias set` (which would mean an agent
or shell history handling that credential directly), the bootstrap runs as a one-off Kubernetes
Job **inside** the cluster: the Job pod reads `minio-credentials` (root user/password) via
`secretKeyRef` and never prints them, then uses the official `mc` client to:

1. Create the `ciir-uploads` bucket if it doesn't already exist.
2. Create a MinIO user `ciir-indexer-uploader` with a freshly generated password.
3. Create and attach a policy granting that user `GetObject`/`PutObject`/`DeleteObject`/
   `ListBucket` on `arn:aws:s3:::ciir-uploads` and `arn:aws:s3:::ciir-uploads/*` only - nothing
   else in the MinIO install.

The Job manifest (with the generated password already filled in) needs to be applied by hand -
this repository's automation intentionally does not get permission to create IAM-granting
resources on its own:

```bash
kubectl apply -f .eng/k8s/ciir-minio-bootstrap-job.yaml
kubectl -n minio wait --for=condition=complete job/ciir-minio-user-bootstrap --timeout=60s
kubectl -n minio logs job/ciir-minio-user-bootstrap
kubectl -n minio delete job/ciir-minio-user-bootstrap
```

### Where the credentials live afterward

- **Local development**: in this project's `dotnet user-secrets` (`Minio:Endpoint`,
  `Minio:UseSsl`, `Minio:BucketName`, `Minio:AccessKey`, `Minio:SecretKey`) - never in
  `appsettings.json`. Inspect with `dotnet user-secrets list --project src/Ciir.Indexer.Api`.
- **Cluster deployment**: `.eng/k8s/code-ciir-minio-secrets.yaml` documents the `Secret` shape
  `deployment.yaml` expects (`code-ciir-minio-secrets`, keys `access-key`/`secret-key`). That file
  contains real credentials, is git-ignored, and is **not** applied automatically - decide how you
  want it into the cluster (`kubectl apply -f`, sealed-secrets, sops, ...) and do that yourself.
  Non-secret settings (`Minio__Endpoint`, `Minio__UseSsl`, `Minio__BucketName`) are already wired
  into `.eng/k8s/configmap.yaml`.

### Rotating the password

```bash
mc admin user add local ciir-indexer-uploader '<new password>'
```
run the same way (inside a Job, or via `mc` locally if you're comfortable typing the root
password yourself) - `mc admin user add` on an existing user updates its password rather than
failing. Update `dotnet user-secrets` and `code-ciir-minio-secrets.yaml`/the cluster Secret to
match.
