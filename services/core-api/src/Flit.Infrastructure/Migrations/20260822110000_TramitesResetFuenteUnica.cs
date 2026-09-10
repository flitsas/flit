using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 (parte 2 de 2) — reset de expedientes y eliminación de los vocabularios paralelos:
/// <c>modalidad_entrada</c> y <c>tipologia_codigo</c> desaparecen de la instancia, y las causales de
/// rechazo pasan de modalidad ×2 a familia ×3. DDL: <c>80-tramites-reset-fuente-unica.sql</c>.
/// <para>⚠️ <b>Destructiva:</b> el <c>Up</c> borra todos los expedientes. Requiere respaldo
/// verificado y aprobación explícita; <c>Database:AutoMigrate</c> está en <c>true</c> por defecto,
/// así que arrancar la aplicación la ejecuta.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260822110000_TramitesResetFuenteUnica")]
public partial class TramitesResetFuenteUnica : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("80-tramites-reset-fuente-unica.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Revierte el <b>esquema</b>, no los datos: los expedientes borrados no se restauran — no hay
    /// origen del que recuperarlos, y esa es la premisa de ADR-0050. Las columnas vuelven con su
    /// default histórico para que una versión anterior de la aplicación arranque contra una base sin
    /// trámites en lugar de fallar por columnas ausentes.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE catalogs.rejection_reasons
                DROP CONSTRAINT IF EXISTS ck_rejection_reasons_family;

            ALTER TABLE catalogs.rejection_reasons
                RENAME COLUMN family TO modalidad;

            UPDATE catalogs.rejection_reasons
               SET modalidad = CASE upper(btrim(modalidad))
                                 WHEN 'MATRICULAS' THEN 'matricula_inicial'
                                 WHEN 'TRASPASO'   THEN 'traspaso'
                                 ELSE 'matricula_inicial'
                               END;

            ALTER INDEX IF EXISTS catalogs.ix_rejection_reasons_family
                RENAME TO ix_rejection_reasons_modalidad;

            ALTER TABLE catalogs.rejection_reasons
                ADD CONSTRAINT ck_rejection_reasons_modalidad
                    CHECK (modalidad IN ('matricula_inicial', 'traspaso'));

            ALTER TABLE tramites.procedure_instances
                ADD COLUMN IF NOT EXISTS modalidad_entrada varchar(20) NOT NULL DEFAULT 'matricula_inicial',
                ADD COLUMN IF NOT EXISTS tipologia_codigo varchar(40);
            """);
}
