using BlogDoFT.Libs.ResultPattern;
using FluentValidation;
using System.Text.RegularExpressions;

namespace Ciir.Indexer.Core;

/// <summary>
/// Shared validation for the "sha256:&lt;64 hex chars&gt;" string format used by both
/// <see cref="CiirIdentity"/> and <see cref="EmbeddingTextHash"/> - two distinct concepts that
/// happen to share a wire format but must never be treated as interchangeable.
/// </summary>
internal static partial class Sha256Value
{
    private const string Prefix = "sha256:";
    private static readonly Sha256ValueValidator Validator = new();

    /// <summary>Validates <paramref name="value"/> against the "sha256:&lt;64 hex chars&gt;" format.</summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="fieldName">The field name to report in a failure, e.g. "CiirIdentity".</param>
    public static Result<string> Create(string? value, string fieldName)
    {
        var validation = Validator.Validate(value ?? string.Empty);
        if (!validation.IsValid)
        {
            return Result<string>.FromFailure(new Failure(
                $"400-{fieldName.ToLowerInvariant()}-invalid",
                $"{fieldName} must match 'sha256:<64 hex chars>', got '{value}'."));
        }

        return Result<string>.FromSuccess(value!);
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HexPattern();

    private sealed class Sha256ValueValidator : AbstractValidator<string>
    {
        public Sha256ValueValidator()
        {
            RuleFor(value => value)
                .NotEmpty()
                .Must(value => value.StartsWith(Prefix, StringComparison.Ordinal)
                    && HexPattern().IsMatch(value.AsSpan(Prefix.Length)))
                .WithMessage("Value must match 'sha256:<64 hex chars>'.");
        }
    }
}
