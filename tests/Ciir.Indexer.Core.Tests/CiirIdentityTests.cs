using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class CiirIdentityTests
{
    private static readonly string ValidValue = "sha256:" + new string('a', 64);

    [Fact]
    public void Should_PreserveValueIntegrally_When_ValueIsValidSha256()
    {
        var result = CiirIdentity.Create(ValidValue);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(ValidValue);
        ((string)result.Value).ShouldBe(ValidValue);
        result.Value.ToString().ShouldBe(ValidValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("sha256:tooshort")]
    [InlineData("md5:a")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Should_ReturnFailure_When_ValueIsInvalid(string? value)
    {
        var result = CiirIdentity.Create(value);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Should_BeEqual_When_ValuesAreTheSame()
    {
        CiirIdentity.Create(ValidValue).Value.ShouldBe(CiirIdentity.Create(ValidValue).Value);
    }
}
