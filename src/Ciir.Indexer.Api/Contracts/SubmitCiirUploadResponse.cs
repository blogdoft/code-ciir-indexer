namespace Ciir.Indexer.Api.Contracts;

/// <summary>Response body for a successful <c>POST /api/ciir-uploads</c> (upload spec §4).</summary>
/// <param name="UploadId">
/// The newly created upload's id. Pass this to <c>GET /api/ciir-uploads/{uploadId}</c> to poll for
/// progress and the final outcome.
/// </param>
/// <param name="Status">The upload's status immediately after creation. Always <c>"pending"</c> at this point.</param>
public sealed record SubmitCiirUploadResponse(Guid UploadId, string Status);
