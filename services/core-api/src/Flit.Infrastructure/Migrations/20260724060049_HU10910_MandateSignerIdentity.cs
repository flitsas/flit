using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10910_MandateSignerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_mandate_signer_companies_active",
                schema: "admin",
                table: "mandate_signer_companies");

            migrationBuilder.AddColumn<string>(
                name: "document_type",
                schema: "admin",
                table: "mandate_signers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "CC");

            migrationBuilder.AddColumn<string>(
                name: "email",
                schema: "admin",
                table: "mandate_signers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "identity_validation_ref",
                schema: "admin",
                table: "mandate_signers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "signature_vault_id",
                schema: "admin",
                table: "mandate_signers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "admin",
                table: "mandate_signers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mandate_signers_signature_vault_id",
                schema: "admin",
                table: "mandate_signers",
                column: "signature_vault_id");

            migrationBuilder.CreateIndex(
                name: "ix_mandate_signers_user_id",
                schema: "admin",
                table: "mandate_signers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_mandate_signer_companies_active",
                schema: "admin",
                table: "mandate_signer_companies",
                columns: new[] { "transit_office_id", "company_tenant_id", "mandate_signer_id" },
                unique: true,
                filter: "is_active");

            // ADR-0036 — FKs con ON DELETE SET NULL (no modeladas como navegación EF, igual que el
            // baúl en HU #10900): al borrar la firma del baúl o la cuenta de usuario, el mandatario
            // queda sin vínculo (cae al sello de texto / "sin match" en el cotejo del firmante).
            migrationBuilder.AddForeignKey(
                name: "fk_mandate_signers_signature_vault_signature_vault_id",
                schema: "admin",
                table: "mandate_signers",
                column: "signature_vault_id",
                principalSchema: "admin",
                principalTable: "signature_vault",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_mandate_signers_users_user_id",
                schema: "admin",
                table: "mandate_signers",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mandate_signers_signature_vault_signature_vault_id",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropForeignKey(
                name: "fk_mandate_signers_users_user_id",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropIndex(
                name: "ix_mandate_signers_signature_vault_id",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropIndex(
                name: "ix_mandate_signers_user_id",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropIndex(
                name: "uq_mandate_signer_companies_active",
                schema: "admin",
                table: "mandate_signer_companies");

            migrationBuilder.DropColumn(
                name: "document_type",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropColumn(
                name: "email",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropColumn(
                name: "identity_validation_ref",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropColumn(
                name: "signature_vault_id",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "admin",
                table: "mandate_signers");

            migrationBuilder.CreateIndex(
                name: "uq_mandate_signer_companies_active",
                schema: "admin",
                table: "mandate_signer_companies",
                columns: new[] { "transit_office_id", "company_tenant_id" },
                unique: true,
                filter: "is_active");
        }
    }
}
