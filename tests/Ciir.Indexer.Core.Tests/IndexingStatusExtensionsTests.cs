using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class IndexingStatusExtensionsTests
{
    [Theory]
    [InlineData(IndexingStatus.Pending, "pending")]
    [InlineData(IndexingStatus.Running, "running")]
    [InlineData(IndexingStatus.ResolvingRelations, "resolving_relations")]
    [InlineData(IndexingStatus.Completed, "completed")]
    [InlineData(IndexingStatus.Failed, "failed")]
    [InlineData(IndexingStatus.Cancelled, "cancelled")]
    public void ToWireString_EveryStatus_ProducesTheSpecMandatedToken(IndexingStatus status, string expected)
    {
        status.ToWireString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("pending", IndexingStatus.Pending)]
    [InlineData("running", IndexingStatus.Running)]
    [InlineData("resolving_relations", IndexingStatus.ResolvingRelations)]
    [InlineData("completed", IndexingStatus.Completed)]
    [InlineData("failed", IndexingStatus.Failed)]
    [InlineData("cancelled", IndexingStatus.Cancelled)]
    public void ParseIndexingStatus_EveryToken_RoundTripsToTheOriginalStatus(string token, IndexingStatus expected)
    {
        IndexingStatusExtensions.ParseIndexingStatus(token).ShouldBe(expected);
    }

    [Fact]
    public void ParseIndexingStatus_UnknownToken_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => IndexingStatusExtensions.ParseIndexingStatus("bogus"));
    }
}
