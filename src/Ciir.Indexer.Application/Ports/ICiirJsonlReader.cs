using Ciir.Indexer.Application.Parsing;

namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Streams a CIIR JSONL file line by line - never loads the whole file into memory (spec §4/§5).
/// Called independently once per import stage (Document Import, Relation Import); each call opens
/// and reads the file from the start.
/// </summary>
public interface ICiirJsonlReader
{
    IAsyncEnumerable<CiirRecordReadResult> ReadAsync(string path, CancellationToken cancellationToken = default);
}
