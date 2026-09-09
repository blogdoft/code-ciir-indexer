using FluentMigrator;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations.Migrations;

/// <summary>
/// Creates the full schema from scratch: the pgvector extension, <c>projects</c>,
/// <c>indexing_runs</c>, <c>ciir_documents</c> and <c>ciir_relations</c> (spec §11/§12/§16/§26 +
/// the embedding-fingerprint addendum). All four tables are always deployed together in v1, so one
/// combined migration is used rather than one per table; later schema changes get their own
/// timestamped migrations.
/// </summary>
[Migration(20260908000000)]
public sealed class InitialSchema : Migration
{
    private readonly IndexerDatabaseOptions _options;

    public InitialSchema(IndexerDatabaseOptions options)
    {
        _options = options;
    }

    public override void Up()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

        CreateProjectsTable();
        CreateIndexingRunsTable();
        CreateCiirDocumentsTable();
        CreateCiirRelationsTable();
    }

    public override void Down()
    {
        Delete.Table("ciir_relations");
        Delete.Table("ciir_documents");
        Delete.Table("indexing_runs");
        Delete.Table("projects");
    }

    private void CreateProjectsTable()
    {
        Create.Table("projects")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("embedding_model").AsString().NotNullable()
            .WithColumn("embedding_dimensions").AsInt32().NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.UniqueConstraint("ux_projects_name").OnTable("projects").Columns("name");
    }

    private void CreateIndexingRunsTable()
    {
        Create.Table("indexing_runs")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("path").AsString(int.MaxValue).NotNullable()
            .WithColumn("status").AsString().NotNullable()
            .WithColumn("started_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("finished_at").AsCustom("timestamptz").Nullable()
            .WithColumn("documents_processed").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("documents_inserted").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("documents_updated").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("embeddings_generated").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("embeddings_reused").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("relations_processed").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("relations_resolved").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("relations_unresolved").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("error").AsString(int.MaxValue).Nullable();

        Create.Index("ix_indexing_runs_status").OnTable("indexing_runs").OnColumn("status");
    }

    private void CreateCiirDocumentsTable()
    {
        Create.Table("ciir_documents")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("project_id").AsInt64().NotNullable().ForeignKey("projects", "id")
            .WithColumn("ciir_id").AsString().NotNullable()
            .WithColumn("schema_version").AsString().NotNullable()
            .WithColumn("kind").AsString().NotNullable()
            .WithColumn("language").AsString().NotNullable()
            .WithColumn("symbol_name").AsString().Nullable()
            .WithColumn("symbol_qualified_name").AsString().Nullable()
            .WithColumn("symbol_canonical_name").AsString().Nullable()
            .WithColumn("symbol_container").AsString().Nullable()
            .WithColumn("source_path").AsString().Nullable()
            .WithColumn("embedding_text").AsString(int.MaxValue).Nullable()
            .WithColumn("embedding_text_strategy").AsString().Nullable()
            .WithColumn("embedding_text_hash").AsString().Nullable()
            .WithColumn("embedding_model").AsString().Nullable()
            .WithColumn("embedding_dimensions").AsInt32().Nullable()
            .WithColumn("embedding_fingerprint_hash").AsString().Nullable()
            .WithColumn("content").AsCustom("jsonb").NotNullable()
            .WithColumn("last_seen_run_id").AsGuid().Nullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        // No FluentMigrator typed API for "vector(N)" - v1 assumes one fixed dimensionality per
        // install (spec §10), so this is a plain fixed-width column, not the per-model
        // unconstrained-vector + partial-index approach a multi-model design would need.
        Execute.Sql($"ALTER TABLE ciir_documents ADD COLUMN embedding vector({_options.EmbeddingDimensions});");

        Create.UniqueConstraint("ux_ciir_documents_project_ciir_id").OnTable("ciir_documents").Columns("project_id", "ciir_id");
        Create.Index("ix_ciir_documents_project_id").OnTable("ciir_documents").OnColumn("project_id");
        Create.Index("ix_ciir_documents_kind").OnTable("ciir_documents").OnColumn("kind");
        Create.Index("ix_ciir_documents_symbol_qualified_name").OnTable("ciir_documents").OnColumn("symbol_qualified_name");
        Create.Index("ix_ciir_documents_last_seen_run_id").OnTable("ciir_documents").OnColumn("last_seen_run_id");

        Execute.Sql("CREATE INDEX ix_ciir_documents_content_gin ON ciir_documents USING gin (content);");
        Execute.Sql(
            """
            CREATE INDEX ix_ciir_documents_embedding_hnsw
                ON ciir_documents USING hnsw (embedding vector_cosine_ops)
                WHERE embedding IS NOT NULL;
            """);
    }

    private void CreateCiirRelationsTable()
    {
        Create.Table("ciir_relations")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("project_id").AsInt64().NotNullable().ForeignKey("projects", "id")
            .WithColumn("source_ciir_id").AsString().NotNullable()
            .WithColumn("target_ciir_id").AsString().Nullable()
            .WithColumn("source_document_id").AsInt64().Nullable()
                .ForeignKey("ciir_documents", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("target_document_id").AsInt64().Nullable()
                .ForeignKey("ciir_documents", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("kind").AsString().NotNullable()
            .WithColumn("target_symbol").AsString().NotNullable()
            .WithColumn("resolution_status").AsString().NotNullable()
            .WithColumn("resolution_origin").AsString().NotNullable()
            .WithColumn("resolution_reason").AsString(int.MaxValue).Nullable()
            .WithColumn("source_path").AsString().Nullable()
            .WithColumn("start_line").AsInt32().Nullable()
            .WithColumn("start_column").AsInt32().Nullable()
            .WithColumn("end_line").AsInt32().Nullable()
            .WithColumn("end_column").AsInt32().Nullable()
            .WithColumn("idempotency_key").AsString(64).NotNullable()
            .WithColumn("last_seen_run_id").AsGuid().Nullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.UniqueConstraint("ux_ciir_relations_idempotency_key").OnTable("ciir_relations").Columns("idempotency_key");

        Create.Index("ix_ciir_relations_project_id").OnTable("ciir_relations").OnColumn("project_id");
        Create.Index("ix_ciir_relations_source_ciir_id").OnTable("ciir_relations").OnColumn("source_ciir_id");
        Create.Index("ix_ciir_relations_target_ciir_id").OnTable("ciir_relations").OnColumn("target_ciir_id");
        Create.Index("ix_ciir_relations_kind").OnTable("ciir_relations").OnColumn("kind");
        Create.Index("ix_ciir_relations_last_seen_run_id").OnTable("ciir_relations").OnColumn("last_seen_run_id");
        Create.Index("ix_ciir_relations_source_document_kind").OnTable("ciir_relations")
            .OnColumn("source_document_id").Ascending()
            .OnColumn("kind").Ascending();
        Create.Index("ix_ciir_relations_target_document_kind").OnTable("ciir_relations")
            .OnColumn("target_document_id").Ascending()
            .OnColumn("kind").Ascending();
        Create.Index("ix_ciir_relations_target_symbol").OnTable("ciir_relations").OnColumn("target_symbol");
    }
}
