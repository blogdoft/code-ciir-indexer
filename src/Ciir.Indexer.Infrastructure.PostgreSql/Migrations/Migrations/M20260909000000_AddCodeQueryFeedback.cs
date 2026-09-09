using FluentMigrator;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations.Migrations;

/// <summary>
/// Adds <c>code_query_feedback</c> - not part of this indexer's own domain (this repo never
/// writes to it), but an additive table owned by the consumer API (code-ciir-api) to record
/// whether a natural-language code-queries answer was useful. Added here, rather than as a
/// migration in code-ciir-api itself, because this indexer already owns schema evolution for
/// the whole code3rag database and code-ciir-api deliberately never runs migrations against a
/// database it doesn't otherwise own - see code-ciir-api's .specs/06-code-query-feedback.md.
/// </summary>
[Migration(20260909000000)]
public sealed class AddCodeQueryFeedback : Migration
{
    public override void Up()
    {
        Create.Table("code_query_feedback")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("project_id").AsInt64().NotNullable().ForeignKey("projects", "id")
            .WithColumn("question").AsString(int.MaxValue).NotNullable()
            .WithColumn("useful").AsBoolean().NotNullable()
            .WithColumn("similarities").AsCustom("float8[]").NotNullable()
            .WithColumn("reason").AsString(int.MaxValue).Nullable()
            .WithColumn("username").AsString().NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("ix_code_query_feedback_project_id_created_at")
            .OnTable("code_query_feedback")
            .OnColumn("project_id").Ascending()
            .OnColumn("created_at").Ascending();
    }

    public override void Down()
    {
        Delete.Table("code_query_feedback");
    }
}
