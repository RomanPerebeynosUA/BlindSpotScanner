using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlindSpotScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileClassificationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "integration_attributes",
                table: "repo_profiles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "integration_base_types",
                table: "repo_profiles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "integration_globs",
                table: "repo_profiles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "minimal_api_globs",
                table: "repo_profiles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "not_integration_globs",
                table: "repo_profiles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "integration_attributes",
                table: "repo_profiles");

            migrationBuilder.DropColumn(
                name: "integration_base_types",
                table: "repo_profiles");

            migrationBuilder.DropColumn(
                name: "integration_globs",
                table: "repo_profiles");

            migrationBuilder.DropColumn(
                name: "minimal_api_globs",
                table: "repo_profiles");

            migrationBuilder.DropColumn(
                name: "not_integration_globs",
                table: "repo_profiles");
        }
    }
}
