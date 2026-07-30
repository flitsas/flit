using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Catálogo canónico de tipos de trámite: elimina duplicados legacy
/// (TRASPASO / TRASPASO_SIMPLE / TRASPASO_LEASING / MATRICULA_INICIAL /
/// MATRICULA_REACTIVACION / CAMBIO_SERVICIO), remapea FKs a
/// MATRICULA_NUEVA y TRASPASO_STANDARD, y siembra el resto de tipos de negocio.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260729120000_CatalogoTiposTramiteCanonico")]
public partial class CatalogoTiposTramiteCanonico : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("40-catalogo-tipos-tramite-canonico.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            -- No se recrean los duplicados legacy. Solo se desactivan tipos añadidos por esta migración.
            UPDATE tramites.procedure_types
            SET is_active = false, updated_at = now()
            WHERE code IN (
                'CAMBIO_CARROCERIA', 'BLINDAJE', 'CAMBIO_COLOR', 'DUPLICADO_TARJETA',
                'RADICADO_CUENTA', 'CONVERSION_COMBUSTIBLE', 'TRASLADO_CUENTA',
                'CANCELACION_MATRICULA', 'REGRABAR_MOTOR_CHASIS', 'REMATRICULA',
                'LEVANTAR_INSCRIBIR_PRENDA', 'CAMBIO_ACREEDOR'
            );
            """);
}
