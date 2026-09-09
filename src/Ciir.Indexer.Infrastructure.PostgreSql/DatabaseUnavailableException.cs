namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <summary>PostgreSQL could not be reached, or a database operation failed.</summary>
public sealed class DatabaseUnavailableException : Exception
{
    public DatabaseUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public DatabaseUnavailableException()
    {
    }

    public DatabaseUnavailableException(string message)
        : base(message)
    {
    }
}
