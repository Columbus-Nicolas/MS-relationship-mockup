using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsRelationship.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReferentialIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_columbus_users_cb_departments_department_id",
                table: "columbus_users");

            migrationBuilder.CreateIndex(
                name: "ix_relations_ms_profile_id",
                table: "relations",
                column: "ms_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_relation_history_ms_profile_id_changed_at",
                table: "relation_history",
                columns: new[] { "ms_profile_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ms_profiles_group_id",
                table: "ms_profiles",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_ms_profiles_merged_into_id",
                table: "ms_profiles",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_ms_profiles_owner_id",
                table: "ms_profiles",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_ms_profiles_source_id",
                table: "ms_profiles",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_ms_profile_domains_domain_id",
                table: "ms_profile_domains",
                column: "domain_id");

            migrationBuilder.CreateIndex(
                name: "ix_ms_profile_customers_customer_id",
                table: "ms_profile_customers",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_entries_ms_profile_id",
                table: "contact_entries",
                column: "ms_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_entries_registered_by_user_id",
                table: "contact_entries",
                column: "registered_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_columbus_users_cb_departments_department_id",
                table: "columbus_users",
                column: "department_id",
                principalTable: "cb_departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contact_entries_columbus_users_registered_by_user_id",
                table: "contact_entries",
                column: "registered_by_user_id",
                principalTable: "columbus_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contact_entries_ms_profiles_ms_profile_id",
                table: "contact_entries",
                column: "ms_profile_id",
                principalTable: "ms_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profile_customers_customers_customer_id",
                table: "ms_profile_customers",
                column: "customer_id",
                principalTable: "customers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profile_customers_ms_profiles_ms_profile_id",
                table: "ms_profile_customers",
                column: "ms_profile_id",
                principalTable: "ms_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profile_domains_domains_domain_id",
                table: "ms_profile_domains",
                column: "domain_id",
                principalTable: "domains",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profile_domains_ms_profiles_ms_profile_id",
                table: "ms_profile_domains",
                column: "ms_profile_id",
                principalTable: "ms_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profiles_columbus_users_owner_id",
                table: "ms_profiles",
                column: "owner_id",
                principalTable: "columbus_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profiles_ms_groups_group_id",
                table: "ms_profiles",
                column: "group_id",
                principalTable: "ms_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profiles_ms_profiles_merged_into_id",
                table: "ms_profiles",
                column: "merged_into_id",
                principalTable: "ms_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ms_profiles_ms_sources_source_id",
                table: "ms_profiles",
                column: "source_id",
                principalTable: "ms_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_relations_columbus_users_columbus_user_id",
                table: "relations",
                column: "columbus_user_id",
                principalTable: "columbus_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_relations_ms_profiles_ms_profile_id",
                table: "relations",
                column: "ms_profile_id",
                principalTable: "ms_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_columbus_users_cb_departments_department_id",
                table: "columbus_users");

            migrationBuilder.DropForeignKey(
                name: "fk_contact_entries_columbus_users_registered_by_user_id",
                table: "contact_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_contact_entries_ms_profiles_ms_profile_id",
                table: "contact_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profile_customers_customers_customer_id",
                table: "ms_profile_customers");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profile_customers_ms_profiles_ms_profile_id",
                table: "ms_profile_customers");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profile_domains_domains_domain_id",
                table: "ms_profile_domains");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profile_domains_ms_profiles_ms_profile_id",
                table: "ms_profile_domains");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profiles_columbus_users_owner_id",
                table: "ms_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profiles_ms_groups_group_id",
                table: "ms_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profiles_ms_profiles_merged_into_id",
                table: "ms_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_ms_profiles_ms_sources_source_id",
                table: "ms_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_relations_columbus_users_columbus_user_id",
                table: "relations");

            migrationBuilder.DropForeignKey(
                name: "fk_relations_ms_profiles_ms_profile_id",
                table: "relations");

            migrationBuilder.DropIndex(
                name: "ix_relations_ms_profile_id",
                table: "relations");

            migrationBuilder.DropIndex(
                name: "ix_relation_history_ms_profile_id_changed_at",
                table: "relation_history");

            migrationBuilder.DropIndex(
                name: "ix_ms_profiles_group_id",
                table: "ms_profiles");

            migrationBuilder.DropIndex(
                name: "ix_ms_profiles_merged_into_id",
                table: "ms_profiles");

            migrationBuilder.DropIndex(
                name: "ix_ms_profiles_owner_id",
                table: "ms_profiles");

            migrationBuilder.DropIndex(
                name: "ix_ms_profiles_source_id",
                table: "ms_profiles");

            migrationBuilder.DropIndex(
                name: "ix_ms_profile_domains_domain_id",
                table: "ms_profile_domains");

            migrationBuilder.DropIndex(
                name: "ix_ms_profile_customers_customer_id",
                table: "ms_profile_customers");

            migrationBuilder.DropIndex(
                name: "ix_contact_entries_ms_profile_id",
                table: "contact_entries");

            migrationBuilder.DropIndex(
                name: "ix_contact_entries_registered_by_user_id",
                table: "contact_entries");

            migrationBuilder.AddForeignKey(
                name: "fk_columbus_users_cb_departments_department_id",
                table: "columbus_users",
                column: "department_id",
                principalTable: "cb_departments",
                principalColumn: "id");
        }
    }
}
