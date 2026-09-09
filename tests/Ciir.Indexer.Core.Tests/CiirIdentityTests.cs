using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class CiirIdentityTests
{
    private static readonly string ValidValue = "sha256:" + new string('a', 64);

    [Fact]
    public void Constructor_ValidSha256Value_PreservesItIntegrally()
    {
        var identity = new CiirIdentity(ValidValue);

        identity.Value.ShouldBe(ValidValue);
        ((string)identity).ShouldBe(ValidValue);
        identity.ToString().ShouldBe(ValidValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("sha256:tooshort")]
    [InlineData("md5:a")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Constructor_InvalidValue_Throws(string? value)
    {
        Should.Throw<ArgumentException>(() => new CiirIdentity(value!));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        new CiirIdentity(ValidValue).ShouldBe(new CiirIdentity(ValidValue));
    }
}
