using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlindSpotScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_cache",
                columns: table => new
                {
                    blob_sha = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_cache", x => x.blob_sha);
                });

            migrationBuilder.CreateTable(
                name: "llm_cache",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_llm_cache", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    default_branch = table.Column<string>(type: "text", nullable: false),
                    html_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repositories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repo_profiles",
                columns: table => new
                {
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    markdown = table.Column<string>(type: "text", nullable: false),
                    integration_mapped_to = table.Column<string>(type: "text", nullable: false),
                    test_globs = table.Column<string[]>(type: "text[]", nullable: false),
                    controller_globs = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repo_profiles", x => new { x.repository_id, x.version });
                    table.ForeignKey(
                        name: "fk_repo_profiles_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch = table.Column<string>(type: "text", nullable: false),
                    commit_sha = table.Column<string>(type: "text", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    model_id = table.Column<string>(type: "text", nullable: false),
                    prompt_set_version = table.Column<string>(type: "text", nullable: false),
                    profile_version = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    previous_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    report_json = table.Column<string>(type: "jsonb", nullable: true),
                    score = table.Column<decimal>(type: "numeric", nullable: true),
                    weight_assessable_pct = table.Column<decimal>(type: "numeric", nullable: true),
                    endpoints_total = table.Column<int>(type: "integer", nullable: true),
                    endpoints_covered = table.Column<int>(type: "integer", nullable: true),
                    endpoints_weak = table.Column<int>(type: "integer", nullable: true),
                    endpoints_none = table.Column<int>(type: "integer", nullable: true),
                    endpoints_unverified = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scan_runs_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "findings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_finding_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    stable_key = table.Column<string>(type: "text", nullable: false),
                    headline = table.Column<string>(type: "text", nullable: false),
                    why_it_matters = table.Column<string>(type: "text", nullable: true),
                    detail = table.Column<string[]>(type: "text[]", nullable: false),
                    evidence = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_findings", x => x.id);
                    table.ForeignKey(
                        name: "fk_findings_scan_runs_scan_run_id",
                        column: x => x.scan_run_id,
                        principalTable: "scan_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phase = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric", nullable: false),
                    read_in_full = table.Column<int>(type: "integer", nullable: false),
                    sampled = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_scan_steps_scan_runs_scan_run_id",
                        column: x => x.scan_run_id,
                        principalTable: "scan_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "remediation_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_key = table.Column<string>(type: "text", nullable: false),
                    created_from_finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    external_key = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remediation_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_remediation_actions_findings_created_from_finding_id",
                        column: x => x.created_from_finding_id,
                        principalTable: "findings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_remediation_actions_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_findings_scan_run_id",
                table: "findings",
                column: "scan_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_remediation_actions_created_from_finding_id",
                table: "remediation_actions",
                column: "created_from_finding_id");

            migrationBuilder.CreateIndex(
                name: "ix_remediation_actions_repository_id_stable_key",
                table: "remediation_actions",
                columns: new[] { "repository_id", "stable_key" });

            migrationBuilder.CreateIndex(
                name: "ix_scan_runs_repository_id",
                table: "scan_runs",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_scan_steps_scan_run_id",
                table: "scan_steps",
                column: "scan_run_id");

            // Reports are immutable (principle 7): once a run has succeeded, neither its report nor
            // its status may change, so the report can't be edited by moving the status back first.
            migrationBuilder.Sql("""
                CREATE FUNCTION scan_runs_report_immutable() RETURNS trigger AS $$
                BEGIN
                    IF OLD.status = 'Succeeded'
                       AND (NEW.report_json IS DISTINCT FROM OLD.report_json
                            OR NEW.status IS DISTINCT FROM OLD.status) THEN
                        RAISE EXCEPTION 'scan run % is succeeded; its report is immutable', OLD.id;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER scan_runs_report_immutable
                    BEFORE UPDATE ON scan_runs
                    FOR EACH ROW EXECUTE FUNCTION scan_runs_report_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER scan_runs_report_immutable ON scan_runs;
                DROP FUNCTION scan_runs_report_immutable();
                """);

            migrationBuilder.DropTable(
                name: "file_cache");

            migrationBuilder.DropTable(
                name: "llm_cache");

            migrationBuilder.DropTable(
                name: "remediation_actions");

            migrationBuilder.DropTable(
                name: "repo_profiles");

            migrationBuilder.DropTable(
                name: "scan_steps");

            migrationBuilder.DropTable(
                name: "findings");

            migrationBuilder.DropTable(
                name: "scan_runs");

            migrationBuilder.DropTable(
                name: "repositories");
        }
    }
}
