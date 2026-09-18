# Uploads-only ingestion: remove the local-path endpoint, add upload registration

**Status: concluído.** `POST /api/indexations` (the original local-filesystem-path entry point,
spec §2/§33/§40/§42) is removed. The CIIR upload flow (`02-upload-ciir-minio.md`) is now the only
way to get a file indexed, via two entry points:

- `POST /api/ciir-uploads` - unchanged, streams a file over HTTP into MinIO.
- `POST /api/ciir-uploads/register` - **new**. Registers a `.jsonl` file the caller already placed
  directly in this service's configured MinIO bucket (e.g. via `mc cp`), for files too large to
  push through a single HTTP request body.

## Why

`POST /api/indexations` required the caller and this service to share a filesystem (the path had
to resolve inside a server-side `Indexer:AllowedInputRoots` allow-list) - workable for local
development, not for how this service is actually deployed (the cluster's own configmap never
configured `AllowedInputRoots`, so the endpoint was never actually reachable in production; see
`configmap.yaml` history). The upload flow removes that coupling entirely, but its own HTTP
endpoint still has to fit a request inside Kestrel's body-size handling and `SizeLimitedStream`'s
byte-by-byte enforcement (`Uploads:MaxCiirFileSizeBytes`, 200 MB by default) - workable for most
CIIR files, not for an unusually large one. `POST /api/ciir-uploads/register` covers that case: the
caller uploads the file to MinIO with whatever tool handles large transfers well (`mc cp`, a
presigned URL, another S3 client), then tells this service where they put it.

## Contract

`POST /api/ciir-uploads/register`

- Body: `{ "projectId": "<id>", "objectKey": "<key>" }` - `projectId` must reference an
  already-registered project (same rule as `POST /api/ciir-uploads`); `objectKey` is the key the
  `.jsonl` file was already stored under **in this service's own configured bucket** - the request
  never names a bucket, matching the least-privilege MinIO credentials this service runs as
  (scoped to exactly one bucket, see `README.md`).
- The object's existence is verified (`IObjectStorage.ExistsAsync`, via `StatObjectAsync`) before
  registering it - a typo'd `objectKey` fails synchronously with `404-object-not-found` rather than
  creating a `CiirUpload` row that can never be processed (which would otherwise only resolve after
  `Uploads:StuckProcessingTimeoutMinutes` × `Uploads:MaxRetryCount`).
- Response: `202 Accepted` with the same `SubmitCiirUploadResponse` shape `POST /api/ciir-uploads`
  returns - it is the same underlying resource (a pending `CiirUpload`), just created a different
  way. From here on the two ingestion paths are indistinguishable: the same `ProcessNextCiirUpload`
  background worker claims and processes either one identically.

## Implementation

- `IObjectStorage` gained `ExistsAsync(bucket, objectKey, ct)`, implemented in
  `MinioObjectStorage` via `StatObjectAsync`, catching `ObjectNotFoundException` to return `false`.
- `RegisterCiirUpload` (Application/UseCases) mirrors `SubmitCiirUpload`'s validation
  (`projectId` parsing, `.jsonl` extension, project existence) via the shared
  `CiirUploadValidation` helper, then additionally requires `IObjectStorage.ExistsAsync` before
  calling the same `ICiirUploadStore.CreateAsync(projectId, bucket, objectKey, ct)`
  `SubmitCiirUpload` calls - `ICiirUploadStore`/`CiirUpload` already modeled "bucket + object key"
  generically; registering never needed a schema change.
- `CiirUploadsController` gained `RegisterAsync` (`POST /api/ciir-uploads/register`) alongside the
  existing `UploadAsync`/`GetAsync`.

## Removed

- `StartIndexation` (Application/UseCases), `IInputResolver`/`InputPathResolver`/
  `IndexerPathOptions` (path allow-list validation), `IndexationsController.StartAsync`,
  `StartIndexationRequest`/`IndexationAcceptedResponse` contracts.
- `IIndexationQueue`/`IndexationChannel`/`IndexationWorker` - the in-memory bounded-channel worker
  that used to drain `StartIndexation`'s queued runs. With `StartIndexation` gone, every
  `IndexingRun` is now created and executed synchronously within `CiirUploadWorker`'s own polling
  cycle (`ProcessNextCiirUpload` -> `RunIndexation`); there is no longer a second queue/worker pair
  to maintain. `IndexationWorker`'s startup responsibility (marking any run left in-progress by a
  previous process lifetime as failed, spec §27) moved to a one-shot
  `IIndexingRunStore.ReconcileOrphanedRunsAsync()` call in `Program.cs`, run once after migrations
  apply rather than at the top of an ongoing background loop.
- The `Indexer` configuration section (`AllowedInputRoots`, `ExpectedExtension`,
  `IndexationQueueCapacity`) - removed from `appsettings.json`; it was never present in
  `configmap.yaml` to begin with.

`GET /api/indexations/{id}` is unchanged - still the only way to poll an `IndexingRun`'s progress,
now reached exclusively via the `indexationId` a `CiirUpload` exposes once a worker starts
processing it (`GET /api/ciir-uploads/{uploadId}`).

## Verification

- Existing `IndexationsControllerTests`/`ProcessNextCiirUploadTests`/etc. trimmed to match, not
  rewritten around the removed flow.
- `RegisterCiirUploadTests` (validation ordering, object-existence gate), `MinioObjectStorageTests`
  (`ExistsAsync` against a real Testcontainers MinIO), `CiirUploadsControllerTests`
  (`RegisterAsync`) added.
- Full `dotnet test` and `dotnet format --verify-no-changes` green.
