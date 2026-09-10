namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Fills in <c>source_document_id</c>/<c>target_document_id</c> on <c>ciir_relations</c> rows
/// after both Document Import and Relation Import finish (spec §20), via set-based SQL rather than
/// one query per relation. Source and CIIR-id-based target resolution follow spec §20 literally;
/// target resolution additionally falls back to matching <c>target_symbol</c> against
/// <c>symbol_canonical_name</c> within the same project for rows still unresolved after that -
/// confirmed as an intentional extension beyond the spec's literal SQL, because the C# CIIR
/// generator never populates <c>relation.target.id</c> in practice, so the literal
/// <c>target_ciir_id</c> match alone would leave nearly all internal edges unresolved. When this
/// symbol-based fallback resolves a row, it also backfills <c>target_ciir_id</c> from the matched
/// document's own <c>ciir_id</c>, so a resolved row always carries both the CIIR-native identity
/// and the FK (spec §17), never just one. Rows CIIR classified with
/// <c>resolution_origin = "solution"</c> (target lives in a different project of the same
/// analyzed solution, not the relation's own project) additionally get a symbol match scoped
/// across every project touched by the run, rather than just the relation's own project -
/// <c>"project"</c>-origin rows are deliberately left scoped to just their own project, so this
/// broader search can never change behavior for them. Since project identity is now
/// caller-supplied per <c>POST /api/indexations</c> request rather than derived per CIIR record
/// (spec's "Atualização — Identidade de projeto informada pelo chamador"), a single run's
/// <c>allProjectIds</c> is typically just <c>[projectId]</c> - <c>"solution"</c>-origin relations
/// between CIIR-internal components now usually already share that one project id and get
/// resolved by the plain same-project pass; the cross-project pass remains correct and only
/// matters when a caller deliberately imports two different project names that reference each
/// other. Never touches rows whose CIIR-reported <c>resolution_status</c> is anything other than
/// <c>resolved</c> - external/dynamic/ambiguous/unresolved classifications are preserved exactly
/// as CIIR reported them (spec §46).
/// </summary>
public interface IRelationResolver
{
    /// <summary>Resolves <c>ciir_relations</c> rows belonging to <paramref name="projectId"/>.</summary>
    /// <param name="projectId">Whose relations to resolve in this call.</param>
    /// <param name="allProjectIds">
    /// Every project touched by the current run (including <paramref name="projectId"/>) - the
    /// document-side search scope for <c>resolution_origin = "solution"</c> rows.
    /// </param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task<RelationResolutionCounters> ResolveAsync(
        long projectId, IReadOnlyCollection<long> allProjectIds, CancellationToken cancellationToken = default);
}
