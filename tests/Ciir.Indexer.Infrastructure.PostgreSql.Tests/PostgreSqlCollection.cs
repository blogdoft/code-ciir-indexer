namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

/// <summary>
/// Groups every PostgreSQL integration test class so xUnit runs them sequentially against the one
/// shared <see cref="PostgreSqlFixture"/> container instead of in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSql";
}
