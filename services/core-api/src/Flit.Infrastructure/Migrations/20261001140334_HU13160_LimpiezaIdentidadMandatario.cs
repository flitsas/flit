using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #13160 — FASE 1 (decisión del Líder Técnico, 01-oct). Esta migración existe SOLO para que el snapshot de EF
    /// coincida con el modelo (sin PendingModelChanges): el modelo ya no mapea <c>admin.admin_identity_validations</c>,
    /// <c>admin.mandate_signers.identity_validation_ref</c> ni <c>admin.transit_office_mandate_config.custom_field_manifest</c>.
    /// Up y Down NO ejecutan SQL: en la base esas estructuras siguen existiendo, huérfanas (columnas nulables y tabla sin
    /// FK entrantes, así que EF puede escribir y borrar filas sin tocarlas).
    /// <para>FASE 2 (pendiente, DDL 127 RESERVADO): una migración posterior hará el DROP real de las tres estructuras,
    /// con respaldo y conteo previos por ambiente. NO va aquí porque la API aplica migraciones al arrancar
    /// (<c>Program.cs</c>, Migrate()) y una guarda que aborte por datos dejaría la API sin arrancar.</para>
    /// </remarks>
    public partial class HU13160_LimpiezaIdentidadMandatario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fase 1: sin SQL a propósito (ver remarks).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 1: sin SQL a propósito (ver remarks).
        }
    }
}
