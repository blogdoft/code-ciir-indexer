using BlogDoFT.Libs.ResultPattern;
using FluentValidation;

namespace Ciir.Indexer.Core;

/// <summary>
/// One indexation execution (spec §26). This is an operational entity, not part of the CIIR
/// contract itself - it exists to drive incremental indexing (<c>last_seen_run_id</c> staleness)
/// and to report progress back to the API caller.
/// </summary>
public sealed record IndexingRun
{
    private static readonly IndexingRunValidator Validator = new();

    private IndexingRun()
    {
    }

    public required Guid Id { get; init; }

    public required string Path { get; init; }

    public required long ProjectId { get; init; }

    public required IndexingStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public IndexingCounters Counters { get; init; } = new();

    public string? Error { get; init; }

    /// <summary>Validates and creates an <see cref="IndexingRun"/>.</summary>
    /// <param name="id">The run's identity.</param>
    /// <param name="path">The staged, absolute CIIR JSONL path being indexed.</param>
    /// <param name="projectId">The project every record in the file is bound to.</param>
    /// <param name="status">The run's current lifecycle state.</param>
    /// <param name="startedAt">When the run started.</param>
    /// <param name="finishedAt">When the run reached a terminal state, if it has.</param>
    /// <param name="counters">The observability counters tracked so far, or null for a fresh run.</param>
    /// <param name="error">A human-readable description of why the run failed, if it did.</param>
    public static Result<IndexingRun> Create(
        Guid id,
        string? path,
        long projectId,
        IndexingStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? finishedAt = null,
        IndexingCounters? counters = null,
        string? error = null)
    {
        var candidate = new IndexingRun
        {
            Id = id,
            Path = path ?? string.Empty,
            ProjectId = projectId,
            Status = status,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Counters = counters ?? new IndexingCounters(),
            Error = error,
        };

        var validation = Validator.Validate(candidate);
        if (!validation.IsValid)
        {
            return Result<IndexingRun>.FromFailure(new Failure("400-indexing-run-invalid", validation.Errors[0].ErrorMessage));
        }

        return Result<IndexingRun>.FromSuccess(candidate);
    }

    private sealed class IndexingRunValidator : AbstractValidator<IndexingRun>
    {
        public IndexingRunValidator()
        {
            RuleFor(run => run.Path).NotEmpty();
            RuleFor(run => run.ProjectId).GreaterThan(0);
        }
    }
}
