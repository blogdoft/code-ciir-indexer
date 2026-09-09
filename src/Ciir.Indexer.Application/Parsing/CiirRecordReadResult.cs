namespace Ciir.Indexer.Application.Parsing;

/// <summary>
/// The outcome of parsing one CIIR JSONL line: either a successfully parsed
/// <see cref="CiirParsedRecord"/>, or a per-line <see cref="Error"/> that never aborts the read of
/// the rest of the file and is never buffered (spec §43).
/// </summary>
public abstract record CiirRecordReadResult
{
    private CiirRecordReadResult()
    {
    }

    public sealed record Success(CiirParsedRecord Record) : CiirRecordReadResult;

    public sealed record Error(int LineNumber, CiirRecordErrorCategory Category, string Message) : CiirRecordReadResult;
}
