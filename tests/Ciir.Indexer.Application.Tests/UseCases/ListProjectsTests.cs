using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class ListProjectsTests
{
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();

    [Fact]
    public async Task ExecuteAsync_NoFilter_ReturnsWhateverStoreReturns()
    {
        var expected = new[] { BuildProject(1), BuildProject(2) };
        _projectStore.SearchAsync(null, 0, ProjectValidation.DefaultPageSize, Arg.Any<CancellationToken>())
            .Returns((expected, 2L));

        var result = await CreateSut().ExecuteAsync(null, null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBe(expected);
        result.Value.Page.ShouldBe(0);
        result.Value.PageSize.ShouldBe(ProjectValidation.DefaultPageSize);
        result.Value.TotalCount.ShouldBe(2);
        result.Value.TotalPages.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_EmptyOrWhitespaceFilter_ReturnsNameFilterEmpty(string nameFilter)
    {
        var result = await CreateSut().ExecuteAsync(nameFilter, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-name-filter-empty");
    }

    [Fact]
    public async Task ExecuteAsync_FilterTooLong_ReturnsNameFilterTooLong()
    {
        var tooLong = new string('a', ProjectValidation.MaxNameFilterLength + 1);

        var result = await CreateSut().ExecuteAsync(tooLong, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-name-filter-too-long");
    }

    [Fact]
    public async Task ExecuteAsync_NegativePage_ReturnsPageInvalid()
    {
        var result = await CreateSut().ExecuteAsync(null, -1, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-page-invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ProjectValidation.MaxPageSize + 1)]
    public async Task ExecuteAsync_PageSizeOutOfRange_ReturnsPageSizeInvalid(int pageSize)
    {
        var result = await CreateSut().ExecuteAsync(null, null, pageSize);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-page-size-invalid");
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitPageAndPageSize_PassesThroughToStore()
    {
        _projectStore.SearchAsync(null, 2, 5, Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Project>(), 11L));

        var result = await CreateSut().ExecuteAsync(null, 2, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(2);
        result.Value.PageSize.ShouldBe(5);
        result.Value.TotalCount.ShouldBe(11);
        result.Value.TotalPages.ShouldBe(3);
    }

    private static Project BuildProject(long id) => Project.Create(
        $"Project{id}",
        gitUrl: null,
        gitRawUrl: null,
        EmbeddingModel.Create("bge-m3", 1024).Value,
        id).Value;

    private ListProjects CreateSut() => new(_projectStore);
}
