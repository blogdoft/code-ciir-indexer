namespace Ciir.Indexer.Core;

/// <summary>
/// Computes the deterministic idempotency key for one relation occurrence, from the composite
/// identity described in spec §25 (project + source_ciir_id + kind + target_ciir_id +
/// target_symbol + location). Re-indexing the same CIIR file must never create duplicate relation
/// rows, but PostgreSQL treats NULL as distinct from NULL in unique constraints - and
/// <c>target_ciir_id</c> is NULL for the large majority of real relations (the C# CIIR generator
/// rarely populates <c>relation.target.id</c>) - so a raw multi-column unique constraint over the
/// natural key would silently fail to deduplicate whenever the target is unresolved. Hashing a
/// canonical, NULL-normalized string sidesteps that pitfall.
/// </summary>
public interface IRelationIdentityKeyGenerator
{
    string Generate(long projectId, CiirRelation relation);
}
