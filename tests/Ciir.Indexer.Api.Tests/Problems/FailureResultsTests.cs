using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Api.Problems;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Problems;

public sealed class FailureResultsTests
{
    [Fact]
    public void ToActionResult_404CodedFailure_ReturnsBodylessNotFound()
    {
        var failure = new Failure("404-path-not-found", "The file does not exist.");

        var result = failure.ToActionResult(BuildHttpContext());

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData("400-path-required", StatusCodes.Status400BadRequest)]
    [InlineData("403-path-not-allowed", StatusCodes.Status403Forbidden)]
    public void ToActionResult_OtherCodedFailure_ReturnsAProblemDetailsResultWithTheEncodedStatus(string code, int expectedStatus)
    {
        var failure = new Failure(code, "some detail");

        var result = failure.ToActionResult(BuildHttpContext());

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(expectedStatus);
        objectResult.ContentTypes.ShouldContain("application/problem+json");
        var problem = objectResult.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(expectedStatus);
        problem.Detail.ShouldBe("some detail");
    }

    [Fact]
    public void ToActionResult_CodeWithoutARecognizedLeadingStatus_DefaultsToBadRequest()
    {
        var failure = new Failure("not-a-status-code", "some detail");

        var result = failure.ToActionResult(BuildHttpContext());

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    private static HttpContext BuildHttpContext() => new DefaultHttpContext();
}
