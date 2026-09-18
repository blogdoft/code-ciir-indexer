namespace Ciir.Indexer.Api.Contracts;

/// <summary>Request body for <c>POST /api/ciir-uploads/register</c> (bring-your-own-upload).</summary>
/// <param name="ProjectId">Required. An already-registered project's id.</param>
/// <param name="ObjectKey">
/// Required. The key the <c>.jsonl</c> file was already stored under in this service's configured
/// MinIO bucket - upload it there yourself first (e.g. via <c>mc cp</c>), then pass the same key
/// here.
/// </param>
public sealed record RegisterCiirUploadRequest(string? ProjectId, string? ObjectKey);
