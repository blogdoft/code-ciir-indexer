# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

This repository is currently empty (greenfield). This file documents the intended purpose,
architecture, and guidelines as agreed with the project owner, so that the first code written here
follows the intended shape from the start rather than being retrofitted later — the same approach
used to bootstrap the sibling `code-csharp-ciir` repository.

## Purpose

`code-ciir-indexer` is the **successor to `code-indexer` (CodeSemanticIndexer)**. It performs the
same core job — read a stream of documents, generate an embedding for each, and upsert the
embedding plus the rest of the document as metadata into PostgreSQL + pgvector — but its input is
**`ciir.jsonl`** (the CIIR — Code Intelligence Intermediate Representation — format produced by
`../code-csharp-ciir`) instead of `semantic-ir.jsonl`. `code-indexer` is being retired in favor of
this project.

This application does not parse or analyze source code itself (that's `code-csharp-ciir`'s job),
and does not implement retrieval/RAG or call chat/completion LLMs (that's `code-rag-api`'s job).
Its only job is: consume CIIR documents → generate embeddings → persist.

CIIR documents already carry a precomputed `embeddingText` (plus `embeddingTextStrategy` and
`embeddingTextHash`) — see `../code-csharp-ciir/docs/ciir-specification.md` and
`../code-csharp-ciir/schemas/ciir.schema.json` for the exact contract this project consumes.

## Specifications

No spec exists yet for this repository. Specs will be added under `.specs/` (the convention used
in `code-csharp-ciir`) — **check there for requirements before starting new work once specs
exist**, and treat this file's architecture/tooling decisions as constraints the spec must fit
within, not the other way around. In particular, how CIIR's richer data — `relations` (e.g.
`calls`), `symbol` info, `kind` — gets modeled in the database (plain JSON metadata vs. dedicated
relational tables for the call graph) is an open question deferred to that spec; do not assume an
answer without it.

## Architecture: Hexagonal (Ports & Adapters)

Split by layer into separate projects, following the same pattern as `code-csharp-ciir`, not a
single project organized by folders:

- **Core**: the domain model (the persisted document shape, embedding-model identity, change
  detection rules). No dependency on any delivery mechanism or I/O technology.
- **Application**: ports (e.g. `IEmbeddingGenerator`, `ICodeDocumentRepository`, `IInputResolver`)
  and the main use case (read → embed → upsert), orchestration only, no I/O logic of its own.
- **Driving adapters** (trigger indexing runs):
  - **CLI** — the first adapter.
  - A future **Web API** driving adapter is plausible, matching `code-csharp-ciir`'s driving-side
    shape — reuse the Application layer's use case rather than duplicating orchestration logic.
- **Driven adapters** (things the core/application depend on, behind ports defined in Application):
  - One project per embedding provider (OpenAI/OpenAI-compatible, Ollama, local ONNX, ...), each
    independently swappable — this mirrors `code-indexer`'s proven
    `IEmbeddingGenerator`/`IEmbeddingProviderFactory` seam; consult that repository as prior art
    for the provider abstraction and its resolver pattern, but the exact provider set here is
    still open.
  - Persistence (Postgres/pgvector via Dapper).

When implementing new functionality, default to: define/extend a port in Application, implement
the behavior in an adapter, keep adapters thin.

## Tech stack

- **.NET 10**
- **xUnit** for tests, with **Shouldly** (assertions), **NSubstitute** (mocking), and **Bogus**
  (test data generation)
- **Dapper** for all database access (no EF Core / other ORMs)
- Only free/open-source libraries are allowed
- Always use the latest version of a library compatible with .NET 10 — check for updates rather
  than pinning to whatever version was scaffolded originally

### BlogDoFT.Libs — use extensively

Before writing a new utility, a Dapper access pattern, a success/failure result type, or similar
plumbing, **check whether a `BlogDoFT.Libs.*` package (by
[ftathiago](https://www.nuget.org/profiles/ftathiago)) already covers it** — this ecosystem leans
heavily on reusing these rather than hand-rolling equivalents:

- `BlogDoFT.Libs.Extensions` — general-purpose utility extensions
- `BlogDoFT.Libs.DapperUtils.Abstractions` / `BlogDoFT.Libs.DapperUtils.Postgres` — interfaces and
  Postgres implementation for Dapper access; use these instead of raw `IDbConnection` plumbing
- `BlogDoFT.Libs.ResultPattern` — Result-pattern implementation; prefer this over throwing
  exceptions for expected/domain failure paths
- `BlogDoFT.Libs.DomainNotifications` / `BlogDoFT.Libs.DomainNotifications.Extensions` — domain
  notification handling
- `BlogDoFT.Libs.Api` / `BlogDoFT.Libs.Api.OpenTelemetry` — API utilities and OpenTelemetry
  support; relevant if/when a Web API driving adapter is built
- `BlogDoFT.Libs.WarmUp` — application warm-up utilities

Check nuget.org for the current versions and full list before adding a dependency, since new
`BlogDoFT.Libs.*` packages may be added over time.

## Code quality and git hooks

- **Zero warnings**: the build must be warning-free. Treat any compiler or analyzer warning as
  something to fix, not ignore.
- If an analyzer produces a warning that is a false positive because it doesn't yet understand a
  newer C# language feature, **ask for permission before suppressing it** with a `#pragma warning
  disable` — don't add suppressions unilaterally.
- Git hooks should be managed with **Husky.Net**, with a pre-commit hook that runs `dotnet format`
  (and only that) — don't add build/test steps to it, matching `code-indexer`/`code-csharp-ciir`.

## Commits

Always use semantic commit messages (Conventional Commits), e.g.:

- `feat: add X`
- `fix: correct Y`
- `refactor: simplify Z`
- `chore: update W`
- `test: add coverage for V`
- `docs: document U`

Format: `<type>(<optional scope>): <short summary>`, imperative mood, no period at the end of the
summary line.

## Development commands

No solution/project files exist yet. Once scaffolded, the project is expected to follow standard
.NET CLI conventions:

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test (by fully-qualified name or filter expression)
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# Run the CLI adapter
dotnet run --project <CliProjectPath> -- <args>
```

Update this section with the actual project/solution paths once the solution is scaffolded.
