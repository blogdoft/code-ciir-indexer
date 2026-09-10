using FluentMigrator;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations.Migrations;

/// <summary>
/// Supports caller-supplied project identity (spec's "Atualização — Identidade de projeto
/// informada pelo chamador"): <c>projects</c> gains optional git metadata, and
/// <c>indexing_runs</c> gains the project it was resolved for. <c>indexing_runs.project_id</c> is
/// added NOT NULL with no backfill/default - under the new model every run always resolves
/// exactly one project before the row is inserted (<c>StartIndexation</c>), so there is no valid
/// state for a run to exist without one, and this repo is pre-release with no production data.
/// </summary>
[Migration(20260909010000)]
public sealed class AddProjectIdentityFields : Migration
{
    public override void Up()
    {
        Alter.Table("projects")
            .AddColumn("git_url").AsString(int.MaxValue).Nullable()
            .AddColumn("git_raw_url").AsString(int.MaxValue).Nullable();

        Alter.Table("indexing_runs")
            .AddColumn("project_id").AsInt64().NotNullable().ForeignKey("projects", "id");

        Create.Index("ix_indexing_runs_project_id").OnTable("indexing_runs").OnColumn("project_id");
    }

    public override void Down()
    {
        Delete.Column("project_id").FromTable("indexing_runs");
        Delete.Column("git_raw_url").FromTable("projects");
        Delete.Column("git_url").FromTable("projects");
    }
}
