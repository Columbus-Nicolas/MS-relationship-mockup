using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsRelationship.Api.Migrations
{
    /// <inheritdoc />
    public partial class RelationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relation_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    columbus_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ms_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_score = table.Column<short>(type: "smallint", nullable: true),
                    new_score = table.Column<short>(type: "smallint", nullable: true),
                    old_note = table.Column<string>(type: "text", nullable: true),
                    new_note = table.Column<string>(type: "text", nullable: true),
                    change_type = table.Column<int>(type: "integer", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relation_history", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relation_history");
        }
    }
}
