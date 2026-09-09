using Ciir.Indexer.Api.Input;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Input;

public sealed class InputPathResolverTests : IDisposable
{
    private readonly string _allowedRoot;
    private readonly string _outsideRoot;

    public InputPathResolverTests()
    {
        _allowedRoot = Directory.CreateTempSubdirectory("ciir-allowed-").FullName;
        _outsideRoot = Directory.CreateTempSubdirectory("ciir-outside-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_allowedRoot, recursive: true);
        Directory.Delete(_outsideRoot, recursive: true);
    }

    [Fact]
    public void ResolveAndValidate_ValidFileWithinAllowedRoot_Succeeds()
    {
        var path = WriteFile(_allowedRoot, "ciir.jsonl");

        var result = CreateSut().ResolveAndValidate(path);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(path);
    }

    [Fact]
    public void ResolveAndValidate_PathOutsideAllowedRoots_IsRejected()
    {
        var path = WriteFile(_outsideRoot, "ciir.jsonl");

        var result = CreateSut().ResolveAndValidate(path);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldStartWith("403");
    }

    [Fact]
    public void ResolveAndValidate_PathTraversalOutOfAllowedRootViaDotDot_IsRejected()
    {
        // "<allowedRoot>/../<outsideRoot's sibling name>/ciir.jsonl" resolves (via Path.GetFullPath)
        // to somewhere outside the allowed root before any root check ever happens.
        WriteFile(_outsideRoot, "ciir.jsonl");
        var traversalPath = Path.Combine(_allowedRoot, "..", Path.GetFileName(_outsideRoot), "ciir.jsonl");

        var result = CreateSut().ResolveAndValidate(traversalPath);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldStartWith("403");
    }

    [Fact]
    public void ResolveAndValidate_SymlinkInsideAllowedRootPointingOutside_IsRejected()
    {
        var targetOutside = WriteFile(_outsideRoot, "secret.jsonl");
        var linkPath = Path.Combine(_allowedRoot, "link.jsonl");
        File.CreateSymbolicLink(linkPath, targetOutside);

        var result = CreateSut().ResolveAndValidate(linkPath);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldStartWith("403");
    }

    [Fact]
    public void ResolveAndValidate_FileDoesNotExist_IsRejected()
    {
        var missingPath = Path.Combine(_allowedRoot, "does-not-exist.jsonl");

        var result = CreateSut().ResolveAndValidate(missingPath);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldStartWith("404");
    }

    [Fact]
    public void ResolveAndValidate_WrongExtension_IsRejected()
    {
        var path = WriteFile(_allowedRoot, "ciir.json");

        var result = CreateSut().ResolveAndValidate(path);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldStartWith("400");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveAndValidate_EmptyPath_IsRejected(string path)
    {
        var result = CreateSut().ResolveAndValidate(path);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldStartWith("400");
    }

    private static string WriteFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "{}");
        return path;
    }

    private InputPathResolver CreateSut() => new(new IndexerPathOptions
    {
        AllowedInputRoots = [_allowedRoot],
        ExpectedExtension = ".jsonl",
    });
}
