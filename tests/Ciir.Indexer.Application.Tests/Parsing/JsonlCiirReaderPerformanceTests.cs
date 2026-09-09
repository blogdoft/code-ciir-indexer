using Ciir.Indexer.Application.Parsing;
using Shouldly;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Application.Tests.Parsing;

/// <summary>
/// Spec §61's mandatory benchmark: processing a much larger CIIR file must not cause a
/// proportionally larger memory footprint - the pipeline stays bounded by streaming, not by file
/// size (spec §5's requirement to never buffer the entire JSONL file in memory). Measures peak live
/// heap size (not cumulative allocations - every record is deserialized into objects either way,
/// streaming or not, so allocation volume alone can't distinguish the two; what differs is whether
/// prior records are still alive/retained at any given instant). Memory measurements are inherently
/// approximate (GC timing, background allocations from the test host itself), so this asserts a
/// generously tolerant bound rather than a tight one: a genuine "loaded the whole file into a
/// List" regression would show roughly linear (~20x) growth, easily distinguished from the ~1x
/// (flat) growth true streaming produces, even with several times more slack.
/// </summary>
[Trait("Category", "Performance")]
public sealed class JsonlCiirReaderPerformanceTests : IDisposable
{
    private const int SmallFileRecordCount = 2_000;
    private const int LargeFileRecordCount = 40_000;

    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ReadAsync_MuchLargerFile_DoesNotProduceProportionallyLargerMemoryGrowth()
    {
        var smallFile = GenerateJsonl(SmallFileRecordCount);
        var largeFile = GenerateJsonl(LargeFileRecordCount);

        // Warm up the JIT/type caches on the small file first, outside of any measurement, so the
        // comparison isn't skewed by one-time startup costs landing in whichever run happens first.
        await MeasurePeakHeapGrowthAsync(smallFile);

        var smallGrowth = await MeasurePeakHeapGrowthAsync(smallFile);
        var largeGrowth = await MeasurePeakHeapGrowthAsync(largeFile);

        largeGrowth.ShouldBeLessThan(Math.Max(smallGrowth, 1) * 4);
    }

    private static async Task<long> MeasurePeakHeapGrowthAsync(string path)
    {
        var baseline = ForceCollectionAndGetHeapSize();
        var peak = baseline;

        var reader = new JsonlCiirReader();
        var count = 0;
        await foreach (var unused in reader.ReadAsync(path))
        {
            count++;
            if (count % 500 == 0)
            {
                var current = GC.GetTotalMemory(forceFullCollection: false);
                if (current > peak)
                {
                    peak = current;
                }
            }
        }

        return Math.Max(0, peak - baseline);
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
            {"schemaVersion":"1.0","id":"<CIIR_ID>","kind":"method","language":"csharp","project":"PerfProject","symbol":{"name":"Method<I>","qualifiedName":"NS.Type.Method<I>","canonicalName":"NS.Type.Method<I>()"}}
            """
            .Replace("<CIIR_ID>", Sha256Of($"method-{indexText}"))
            .Replace("<I>", indexText);
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
