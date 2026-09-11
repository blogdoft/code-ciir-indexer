using Ciir.Indexer.Application.Parsing;
using Shouldly;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Application.Tests.Parsing;

/// <summary>
/// Spec §61's mandatory streaming check. It measures the live memory retained halfway through a
/// large-file enumeration (spec §5), after a full collection. This avoids comparing process-wide
/// heap peaks, which include unrelated runtime/test-host allocations and made the prior benchmark
/// flaky. A regression that loads the file into memory retains tens of MiB at this point; the
/// streaming reader retains only its current record.
/// </summary>
[Trait("Category", "Performance")]
public sealed class JsonlCiirReaderPerformanceTests : IDisposable
{
    private const int LargeFileRecordCount = 6_000;
    private const int RecordPaddingLength = 4 * 1024;
    private const long MaximumRetainedBytes = 16 * 1024 * 1024;

    private static readonly string RecordPadding = new('x', RecordPaddingLength);

    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ReadAsync_LargeFile_DoesNotRetainPreviouslyReadRecords()
    {
        var largeFile = GenerateJsonl(LargeFileRecordCount);
        var baseline = ForceCollectionAndGetHeapSize();

        var reader = new JsonlCiirReader();
        await using var enumerator = reader.ReadAsync(largeFile).GetAsyncEnumerator();
        for (var count = 0; count < LargeFileRecordCount / 2; count++)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
        }

        var retainedBytes = Math.Max(0, ForceCollectionAndGetHeapSize() - baseline);
        retainedBytes.ShouldBeLessThan(MaximumRetainedBytes);
    }

    // Isolated in its own method, with justification, per the project's suppression policy - this
    // is the standard, necessary pattern for a before/after heap-size memory benchmark: without
    // forcing a full collection first, leftover garbage from prior test runs would pollute the
    // baseline and make the comparison meaningless.
#pragma warning disable S1215
    private static long ForceCollectionAndGetHeapSize()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
#pragma warning restore S1215

    private static string BuildDocumentLine(int index)
    {
        var indexText = index.ToString(CultureInfo.InvariantCulture);
        return """
            {"schemaVersion":"1.0","id":"<CIIR_ID>","kind":"method","language":"csharp","project":"PerfProject","symbol":{"name":"Method<I>","qualifiedName":"NS.Type.Method<I>","canonicalName":"NS.Type.Method<I>()"},"padding":"<PADDING>"}
            """
            .Replace("<CIIR_ID>", Sha256Of($"method-{indexText}"))
            .Replace("<I>", indexText)
            .Replace("<PADDING>", RecordPadding);
    }

    private static string Sha256Of(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    private string GenerateJsonl(int recordCount)
    {
        var path = Path.GetTempFileName();
        using (var writer = new StreamWriter(path))
        {
            for (var i = 0; i < recordCount; i++)
            {
                writer.WriteLine(BuildDocumentLine(i));
            }
        }

        _tempFiles.Add(path);
        return path;
    }
}
