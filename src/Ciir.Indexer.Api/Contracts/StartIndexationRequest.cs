namespace Ciir.Indexer.Api.Contracts;

/// <summary>Request body for <c>POST /api/indexations</c> (spec §2).</summary>
/// <param name="Path">
/// Absolute filesystem path to the CIIR JSONL file to import. Must resolve inside one of the
/// server's configured <c>Indexer:AllowedInputRoots</c> and end in <c>.jsonl</c>.
/// </param>
public sealed record StartIndexationRequest(string Path);
