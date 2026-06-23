using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10115Fix_InvitationFullNameOptionalRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADD full_name to security.user_invitations (was missing, nullable)
            migrationBuilder.AddColumn<string>(
                name: "full_name",
                schema: "security",
                table: "user_invitations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // ALTER role_id to nullable in security.user_invitations
            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                schema: "security",
                table: "user_invitations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // ADD home_tenant_id to identity.users (nullable)
            migrationBuilder.AddColumn<Guid>(
                name: "home_tenant_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "full_name",
                schema: "security",
                table: "user_invitations");

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                schema: "security",
                table: "user_invitations",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldNullable: true,
                oldType: "uuid");

            migrationBuilder.DropColumn(
                name: "home_tenant_id",
                schema: "identity",
                table: "users");
        }
    }
}
