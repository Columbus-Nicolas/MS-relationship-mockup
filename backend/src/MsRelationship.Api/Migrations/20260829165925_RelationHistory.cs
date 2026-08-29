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
                    old_score = table.Column<int>(type: "integer", nullable: true),
                    new_score = table.Column<int>(type: "integer", nullable: true),
                    old_note = table.Column<string>(type: "text", nullable: true),
                    new_note = table.Column<string>(type: "text", nullable: true),
                    change_type = table.Column<string>(type: "text", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relation_history", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_relation_history_changed_at",
                table: "relation_history",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "ix_relation_history_columbus_user_id_ms_profile_id",
                table: "relation_history",
                columns: new[] { "columbus_user_id", "ms_profile_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relation_history");
        }
    }
}
