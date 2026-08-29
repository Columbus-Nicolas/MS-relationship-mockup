using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsRelationship.Api.Migrations
{
    /// <inheritdoc />
    public partial class SnakeCaseNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ms_sources",
                table: "ms_sources");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ms_groups",
                table: "ms_groups");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ms_domains",
                table: "ms_domains");

            migrationBuilder.DropPrimaryKey(
                name: "PK_columbus_users",
                table: "columbus_users");

            migrationBuilder.DropIndex(
                name: "IX_columbus_users_EntraObjectId",
                table: "columbus_users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_cb_departments",
                table: "cb_departments");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ms_sources",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "ms_sources",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_ms_sources_Name",
                table: "ms_sources",
                newName: "ix_ms_sources_name");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ms_groups",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "ms_groups",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_ms_groups_Name",
                table: "ms_groups",
                newName: "ix_ms_groups_name");

            migrationBuilder.RenameColumn(
                name: "Owner",
                table: "ms_domains",
                newName: "owner");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ms_domains",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "ms_domains",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "ms_domains",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "SortOrder",
                table: "ms_domains",
                newName: "sort_order");

            migrationBuilder.RenameColumn(
                name: "IsSystem",
                table: "ms_domains",
                newName: "is_system");

            migrationBuilder.RenameIndex(
                name: "IX_ms_domains_Name",
                table: "ms_domains",
                newName: "ix_ms_domains_name");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "columbus_users",
                newName: "title");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "columbus_users",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Skills",
                table: "columbus_users",
                newName: "skills");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "columbus_users",
                newName: "role");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "columbus_users",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "columbus_users",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "columbus_users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "LastSurveyAt",
                table: "columbus_users",
                newName: "last_survey_at");

            migrationBuilder.RenameColumn(
                name: "EntraObjectId",
                table: "columbus_users",
                newName: "entra_object_id");

            migrationBuilder.RenameColumn(
                name: "DepartmentId",
                table: "columbus_users",
                newName: "department_id");

            migrationBuilder.RenameColumn(
                name: "ArchivedAt",
                table: "columbus_users",
                newName: "archived_at");

            migrationBuilder.RenameIndex(
                name: "IX_columbus_users_Email",
                table: "columbus_users",
                newName: "ix_columbus_users_email");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "cb_departments",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "cb_departments",
                newName: "id");

            migrationBuilder.RenameIndex(
                name: "IX_cb_departments_Name",
                table: "cb_departments",
                newName: "ix_cb_departments_name");

            migrationBuilder.AddPrimaryKey(
                name: "pk_ms_sources",
                table: "ms_sources",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_ms_groups",
                table: "ms_groups",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_ms_domains",
                table: "ms_domains",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_columbus_users",
                table: "columbus_users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_cb_departments",
                table: "cb_departments",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_columbus_users_entra_object_id",
                table: "columbus_users",
                column: "entra_object_id",
                unique: true,
                filter: "entra_object_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_ms_sources",
                table: "ms_sources");

            migrationBuilder.DropPrimaryKey(
                name: "pk_ms_groups",
                table: "ms_groups");

            migrationBuilder.DropPrimaryKey(
                name: "pk_ms_domains",
                table: "ms_domains");

            migrationBuilder.DropPrimaryKey(
                name: "pk_columbus_users",
                table: "columbus_users");

            migrationBuilder.DropIndex(
                name: "ix_columbus_users_entra_object_id",
                table: "columbus_users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_cb_departments",
                table: "cb_departments");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ms_sources",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ms_sources",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "ix_ms_sources_name",
                table: "ms_sources",
                newName: "IX_ms_sources_Name");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ms_groups",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ms_groups",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "ix_ms_groups_name",
                table: "ms_groups",
                newName: "IX_ms_groups_Name");

            migrationBuilder.RenameColumn(
                name: "owner",
                table: "ms_domains",
                newName: "Owner");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ms_domains",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "ms_domains",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ms_domains",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "sort_order",
                table: "ms_domains",
                newName: "SortOrder");

            migrationBuilder.RenameColumn(
                name: "is_system",
                table: "ms_domains",
                newName: "IsSystem");

            migrationBuilder.RenameIndex(
                name: "ix_ms_domains_name",
                table: "ms_domains",
                newName: "IX_ms_domains_Name");

            migrationBuilder.RenameColumn(
                name: "title",
                table: "columbus_users",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "columbus_users",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "skills",
                table: "columbus_users",
                newName: "Skills");

            migrationBuilder.RenameColumn(
                name: "role",
                table: "columbus_users",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "columbus_users",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "email",
                table: "columbus_users",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "columbus_users",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "last_survey_at",
                table: "columbus_users",
                newName: "LastSurveyAt");

            migrationBuilder.RenameColumn(
                name: "entra_object_id",
                table: "columbus_users",
                newName: "EntraObjectId");

            migrationBuilder.RenameColumn(
                name: "department_id",
                table: "columbus_users",
                newName: "DepartmentId");

            migrationBuilder.RenameColumn(
                name: "archived_at",
                table: "columbus_users",
                newName: "ArchivedAt");

            migrationBuilder.RenameIndex(
                name: "ix_columbus_users_email",
                table: "columbus_users",
                newName: "IX_columbus_users_Email");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "cb_departments",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "cb_departments",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "ix_cb_departments_name",
                table: "cb_departments",
                newName: "IX_cb_departments_Name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ms_sources",
                table: "ms_sources",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ms_groups",
                table: "ms_groups",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ms_domains",
                table: "ms_domains",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_columbus_users",
                table: "columbus_users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_cb_departments",
                table: "cb_departments",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_columbus_users_EntraObjectId",
                table: "columbus_users",
                column: "EntraObjectId",
                unique: true,
                filter: "\"EntraObjectId\" IS NOT NULL");
        }
    }
}
