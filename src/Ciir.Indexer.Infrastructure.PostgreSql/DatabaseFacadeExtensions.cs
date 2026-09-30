using BlogDoFT.Libs.DapperUtils.Abstractions;
using System.Data.Common;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

internal static class DatabaseFacadeExtensions
{
    public static async Task<T> QuerySingleAsync<T>(this IDatabaseFacade database, string sql, object? param = null)
        where T : class
    {
        var row = await database.QuerySingleOrDefaultAsync<T>(sql, param);

        return row ?? throw new InvalidOperationException("The statement was expected to return exactly one row.");
    }

    public static async Task<DbTransaction> BeginTransactionAsync(
        this IDatabaseFacade database, CancellationToken cancellationToken)
    {
        var connection = (DbConnection)database.GetDbConnection();

        return await connection.BeginTransactionAsync(cancellationToken);
    }
}
