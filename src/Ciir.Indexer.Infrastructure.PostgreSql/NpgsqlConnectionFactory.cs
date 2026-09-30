using BlogDoFT.Libs.DapperUtils.Abstractions;
using Npgsql;
using System.Data;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <summary>
/// Hands <c>IDatabaseFacade</c> connections from the app-wide pgvector-enabled
/// <see cref="NpgsqlDataSource"/>, so <c>vector</c> parameters/columns keep working through the
/// BlogDoFT Dapper facade.
/// </summary>
internal sealed class NpgsqlConnectionFactory : IConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public IDbConnection GetNewConnection() => _dataSource.CreateConnection();
}
