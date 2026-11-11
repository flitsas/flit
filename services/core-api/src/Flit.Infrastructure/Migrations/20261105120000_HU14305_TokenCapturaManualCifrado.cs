using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14305 (Feature #14304, Épica #14271) — columna <c>manual_capture_token_encrypted</c> en
/// <c>tramites.procedure_instance_biometric_validations</c>: el token de la captura manual cifrado, para copiar el enlace sin
/// regenerarlo. Aditiva, idempotente, sin backfill. DDL: <c>170-HU14305-token-captura-manual-cifrado.sql</c> (el 169 y el 171 están
/// reservados para otras HUs). Lleva los atributos inline (sin Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105120000_HU14305_TokenCapturaManualCifrado")]
public partial class HU14305_TokenCapturaManualCifrado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("170-HU14305-token-captura-manual-cifrado.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "ALTER TABLE tramites.procedure_instance_biometric_validations DROP COLUMN IF EXISTS manual_capture_token_encrypted;");
}
