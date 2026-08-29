using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsRelationship.Api.Migrations
{
    /// <inheritdoc />
    public partial class Taxonomies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cb_departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cb_departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ms_domains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Owner = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ms_domains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ms_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ms_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ms_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ms_sources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cb_departments_Name",
                table: "cb_departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ms_domains_Name",
                table: "ms_domains",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ms_groups_Name",
                table: "ms_groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ms_sources_Name",
                table: "ms_sources",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cb_departments");

            migrationBuilder.DropTable(
                name: "ms_domains");

            migrationBuilder.DropTable(
                name: "ms_groups");

            migrationBuilder.DropTable(
                name: "ms_sources");
        }
    }
}
