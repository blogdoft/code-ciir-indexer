namespace Ciir.Indexer.Api.Contracts;

/// <summary>Request body for <c>POST /api/indexations</c> (spec §2).</summary>
/// <param name="ProjectName">
/// Required. The project every record in the file will be bound to. Every CIIR record's own
/// <c>project</c> field is still parsed/validated but ignored for identity purposes - this is the
/// sole source of project identity (spec's "Atualização — Identidade de projeto informada pelo
/// chamador"), so that two unrelated repositories that happen to share an internal component name
/// never collide on the same project row.
/// </param>
/// <param name="Path">
/// Absolute filesystem path to the CIIR JSONL file to import. Must resolve inside one of the
/// server's configured <c>Indexer:AllowedInputRoots</c> and end in <c>.jsonl</c>.
/// </param>
/// <param name="GitUrl">Optional. The project's git repository URL.</param>
/// <param name="GitRawUrl">Optional. The project's git raw-content URL.</param>
public sealed record StartIndexationRequest(string ProjectName, string Path, string? GitUrl = null, string? GitRawUrl = null);
