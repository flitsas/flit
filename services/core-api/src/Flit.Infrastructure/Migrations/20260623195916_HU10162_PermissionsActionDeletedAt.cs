using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10162_PermissionsActionDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                schema: "security",
                table: "permissions",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "security",
                table: "permissions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "uuidv7()",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Use IF NOT EXISTS because SchemaBootstrap.cs (HU10146) already created
            // created_at/created_by/updated_at/updated_by on this table.
            migrationBuilder.Sql(@"
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS action character varying(20) NOT NULL DEFAULT 'CUSTOM';
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS created_at timestamp with time zone NOT NULL DEFAULT '0001-01-01 00:00:00+00';
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS created_by uuid NULL;
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS deleted_at timestamp with time zone NULL;
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS deleted_by uuid NULL;
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS description character varying(500) NULL;
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0;
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS updated_at timestamp with time zone NULL;
                ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS updated_by uuid NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "action",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "security",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "security",
                table: "permissions");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                schema: "security",
                table: "permissions",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "security",
                table: "permissions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "uuidv7()");
        }
    }
}
