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

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "security",
                table: "modules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "security",
                table: "modules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "security",
                table: "modules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by",
                schema: "security",
                table: "modules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "row_version",
                schema: "security",
                table: "modules",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "security",
                table: "modules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                schema: "security",
                table: "modules",
                type: "uuid",
                nullable: true);
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
