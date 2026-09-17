using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class CiirUploadStatusExtensionsTests
{
    [Theory]
    [InlineData(CiirUploadStatus.Pending, "pending")]
    [InlineData(CiirUploadStatus.Processing, "processing")]
    [InlineData(CiirUploadStatus.Processed, "processed")]
    [InlineData(CiirUploadStatus.Failed, "failed")]
    public void ToWireString_EveryStatus_ProducesTheSpecMandatedToken(CiirUploadStatus status, string expected)
    {
        status.ToWireString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("pending", CiirUploadStatus.Pending)]
    [InlineData("processing", CiirUploadStatus.Processing)]
    [InlineData("processed", CiirUploadStatus.Processed)]
    [InlineData("failed", CiirUploadStatus.Failed)]
    public void ParseCiirUploadStatus_EveryToken_RoundTripsToTheOriginalStatus(string token, CiirUploadStatus expected)
    {
        CiirUploadStatusExtensions.ParseCiirUploadStatus(token).ShouldBe(expected);
    }

    [Fact]
    public void ParseCiirUploadStatus_UnknownToken_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CiirUploadStatusExtensions.ParseCiirUploadStatus("bogus"));
    }
}
