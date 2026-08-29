using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsRelationship.Api.Migrations
{
    /// <inheritdoc />
    public partial class MsProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.CreateTable(
                name: "ms_profile_domains",
                columns: table => new
                {
                    ms_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ms_profile_domains", x => new { x.ms_profile_id, x.domain_id });
                });

            migrationBuilder.CreateTable(
                name: "ms_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    organization = table.Column<string>(type: "text", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: false),
                    is_tentative = table.Column<bool>(type: "boolean", nullable: false),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    identity_key = table.Column<string>(type: "text", nullable: false, computedColumnSql: "CASE WHEN email IS NULL OR btrim(email) = '' THEN lower(btrim(name)) || '|' || lower(btrim(coalesce(organization, ''))) ELSE lower(btrim(email)) END", stored: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ms_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ms_profiles_identity_key",
                table: "ms_profiles",
                column: "identity_key",
                unique: true,
                filter: "merged_into_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ms_profile_domains");

            migrationBuilder.DropTable(
                name: "ms_profiles");
        }
    }
}
