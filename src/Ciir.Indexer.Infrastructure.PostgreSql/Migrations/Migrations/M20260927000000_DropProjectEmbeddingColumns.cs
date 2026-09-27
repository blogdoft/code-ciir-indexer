using FluentMigrator;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations.Migrations;

/// <summary>
/// Drops <c>projects.embedding_model</c>/<c>embedding_dimensions</c>: the embedding configuration is
/// deployment-wide (<c>Embeddings:*</c>) and already recorded per row in <c>ciir_documents</c>, so
/// keeping a second, per-project copy only invited drift. <c>Down</c> restores the columns with
/// placeholder values, since the original per-project values cannot be recovered.
/// </summary>
[Migration(20260927000000)]
public sealed class DropProjectEmbeddingColumns : Migration
{
    public override void Up()
    {
        Delete.Column("embedding_model").FromTable("projects");
        Delete.Column("embedding_dimensions").FromTable("projects");
    }

    public override void Down()
    {
        Create.Column("embedding_model").OnTable("projects").AsString().NotNullable().WithDefaultValue(string.Empty);
        Create.Column("embedding_dimensions").OnTable("projects").AsInt32().NotNullable().WithDefaultValue(0);
    }
}
