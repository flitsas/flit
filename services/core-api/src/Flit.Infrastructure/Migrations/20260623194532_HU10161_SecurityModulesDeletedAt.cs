using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10161_SecurityModulesDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                schema: "security",
                table: "modules",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "security",
                table: "modules",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "security",
                table: "modules",
                type: "uuid",
                nullable: false,
                defaultValueSql: "uuidv7()",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Use IF NOT EXISTS because SchemaBootstrap.cs (HU10146) already created
            // created_at/created_by/updated_at/updated_by on this table.
            migrationBuilder.Sql(@"
                ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS created_at timestamp with time zone NOT NULL DEFAULT '0001-01-01 00:00:00+00';
                ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS created_by uuid NULL;
                ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS deleted_at timestamp with time zone NULL;
                ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS deleted_by uuid NULL;
                ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0;
                ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS updated_at timestamp with time zone NULL;
                ALTER TABLE security.modules ADD COLUMN IF NOT EXISTS updated_by uuid NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "security",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "security",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "security",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "security",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "security",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "security",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "updated_by",
                schema: "security",
                table: "modules");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                schema: "security",
                table: "modules",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                schema: "security",
                table: "modules",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "security",
                table: "modules",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "uuidv7()");
        }
    }
}
