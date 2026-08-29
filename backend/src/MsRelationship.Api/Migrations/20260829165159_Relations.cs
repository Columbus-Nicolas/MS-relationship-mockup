using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsRelationship.Api.Migrations
{
    /// <inheritdoc />
    public partial class Relations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    columbus_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ms_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relations", x => x.id);
                    table.CheckConstraint("ck_relations_score", "score BETWEEN -3 AND 3");
                    table.ForeignKey(
                        name: "fk_relations_columbus_users_columbus_user_id",
                        column: x => x.columbus_user_id,
                        principalTable: "columbus_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_relations_ms_profiles_ms_profile_id",
                        column: x => x.ms_profile_id,
                        principalTable: "ms_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_relations_columbus_user_id_ms_profile_id",
                table: "relations",
                columns: new[] { "columbus_user_id", "ms_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_relations_ms_profile_id",
                table: "relations",
                column: "ms_profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relations");
        }
    }
}
