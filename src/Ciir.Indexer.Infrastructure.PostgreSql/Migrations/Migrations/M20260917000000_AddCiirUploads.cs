using FluentMigrator;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations.Migrations;

/// <summary>
/// Adds <c>ciir_uploads</c> (upload spec §6): the durable queue backing <c>POST
/// /api/ciir-uploads</c>. Deliberately separate from <c>indexing_runs</c> - this table tracks the
/// upload/blob lifecycle (pending/processing/processed/failed, retry count, MinIO location), never
/// indexation progress, which stays on the <c>indexing_run</c> the worker creates once it starts
/// processing an upload. <c>project_id</c> is NOT NULL - unlike the local-path flow, this endpoint
/// never creates a project, so every upload row always references one that already exists. As with
/// <c>indexing_runs</c>, the real primary key is the numeric <c>id</c>; <c>public_id</c> (UUIDv7) is
/// what every port/controller/response uses instead.
/// </summary>
[Migration(20260917000000)]
public sealed class AddCiirUploads : Migration
{
    public override void Up()
    {
        Create.Table("ciir_uploads")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("public_id").AsGuid().NotNullable()
            .WithColumn("project_id").AsInt64().NotNullable().ForeignKey("projects", "id")
            .WithColumn("bucket").AsString().NotNullable()
            .WithColumn("object_key").AsString().NotNullable()
            .WithColumn("status").AsString().NotNullable().WithDefaultValue("pending")
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("processing_started_at").AsCustom("timestamptz").Nullable()
            .WithColumn("processed_at").AsCustom("timestamptz").Nullable()
            .WithColumn("retry_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("error").AsString(int.MaxValue).Nullable()

            // References indexing_runs' unique public_id (not its numeric id): the value this
            // column needs is exactly the Guid CiirUpload.IndexingRunId exposes, so storing it
            // directly here avoids translating between the two tables' numeric ids on every read.
            .WithColumn("indexing_run_id").AsGuid().Nullable().ForeignKey("indexing_runs", "public_id");

        Create.UniqueConstraint("ux_ciir_uploads_public_id").OnTable("ciir_uploads").Columns("public_id");
        Create.Index("ix_ciir_uploads_status_created_at").OnTable("ciir_uploads")
            .OnColumn("status").Ascending()
            .OnColumn("created_at").Ascending();
        Create.Index("ix_ciir_uploads_project_id").OnTable("ciir_uploads").OnColumn("project_id");
    }

    public override void Down()
    {
        Delete.Table("ciir_uploads");
    }
}
