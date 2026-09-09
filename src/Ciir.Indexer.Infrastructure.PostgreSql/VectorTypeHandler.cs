using Dapper;
using Pgvector;
using System.Data;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <summary>
/// Lets Dapper pass a <see cref="Vector"/> as a query parameter. Dapper inspects parameter types
/// up front to pick a <c>DbType</c> and has no built-in mapping for pgvector's CLR type (even
/// though Npgsql itself resolves it fine once <c>NpgsqlDataSourceBuilder.UseVector()</c> has run) -
/// registering this handler is what lets the writers pass a <see cref="Vector"/> straight through
/// as an anonymous-object parameter. Explicitly maps a null vector to <see cref="DBNull.Value"/> -
/// unlike a fully non-nullable embedding column, this schema stores NULL for documents that have
/// no <c>embeddingText</c> at all (spec §45), and ADO.NET providers require <see cref="DBNull"/>
/// rather than CLR null for correct SQL NULL semantics.
/// </summary>
public sealed class VectorTypeHandler : SqlMapper.TypeHandler<Vector>
{
    public static void Register() => SqlMapper.AddTypeHandler(new VectorTypeHandler());

    public override void SetValue(IDbDataParameter parameter, Vector? value)
    {
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    public override Vector Parse(object value) => (Vector)value;
}
