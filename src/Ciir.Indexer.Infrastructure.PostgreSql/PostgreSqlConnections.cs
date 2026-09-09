using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <summary>
/// Wraps every point where a repository talks to Npgsql so a connection failure surfaces as
/// <see cref="DatabaseUnavailableException"/> rather than a raw <see cref="NpgsqlException"/> -
/// shared across every writer/store in this project instead of repeating the same try/catch.
/// </summary>
internal static class PostgreSqlConnections
{
    public static async Task<NpgsqlConnection> OpenAsync(
        NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        try
        {
            return await dataSource.OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            throw new DatabaseUnavailableException($"Could not connect to PostgreSQL: {ex.Message}", ex);
        }
    }

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (NpgsqlException ex)
        {
            throw new DatabaseUnavailableException($"PostgreSQL operation failed: {ex.Message}", ex);
        }
    }

    public static async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (NpgsqlException ex)
        {
            throw new DatabaseUnavailableException($"PostgreSQL operation failed: {ex.Message}", ex);
        }
    }
}
