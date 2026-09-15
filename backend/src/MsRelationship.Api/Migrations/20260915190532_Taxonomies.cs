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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cb_departments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ms_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ms_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ms_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ms_sources", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cb_departments_name",
                table: "cb_departments",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ms_groups_name",
                table: "ms_groups",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ms_sources_name",
                table: "ms_sources",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cb_departments");

            migrationBuilder.DropTable(
                name: "ms_groups");

            migrationBuilder.DropTable(
                name: "ms_sources");
        }
    }
}
