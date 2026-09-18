# CIIR Indexer

Reads CIIR JSONL, generates embeddings, and upserts documents/relations into PostgreSQL +
pgvector. See `CLAUDE.md` for architecture and tooling conventions, and `.specs/` for the
detailed functional specs (`01-Spec-inicial.md` for the core indexer, `02-upload-ciir-minio.md`
for the MinIO upload endpoint).

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
