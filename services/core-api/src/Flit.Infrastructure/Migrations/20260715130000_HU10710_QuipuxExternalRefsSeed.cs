using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Parametrización Quipux (QX): siembra la clave <c>quipux</c> de <c>external_refs</c> en los dos
    /// catálogos que deciden qué se radica — <c>tramites.procedure_types</c> (códigos 16/213 traspaso,
    /// 13 matrícula, prefijos TR/TRU/MI/MIL, campo identificador y tope de empresa) y
    /// <c>catalogs.transit_offices</c> (<c>codigoDivipo</c> de la secretaría destino). Complementa a
    /// <c>HU10710_QuipuxIntegracion</c>, que crea las tablas. DDL embebido (ADR-0018).
    ///
    /// Solo datos: no crea ni altera esquema — la columna <c>external_refs</c> jsonb ya existía en
    /// ambos catálogos. Es lo que permite que añadir un tipo de trámite a Quipux sea un UPDATE y no
    /// un despliegue; la ausencia de la clave <c>quipux</c> es el gate de elegibilidad.
    ///
    /// El seed matchea por <c>code</c>, no por UUID, porque el catálogo <c>procedure_types</c> está
    /// solapado por tres seeds históricos y los códigos presentes varían según el ambiente.
    ///
    /// PROVISIONAL: el <c>codigoDivipo</c> se limita a los 6 OT fijos de DEV/E2E usando
    /// <c>city_code</c>, porque FLIT 2.0 no tiene el campo y el dump de
    /// <c>traffic_secretaries.code_divipo</c> de FLIT 1.0 aún no está disponible. Los ~298 OT del
    /// catálogo RUNT quedan sin sembrar a propósito: sin <c>codigoDivipo</c> no son elegibles, que es
    /// el fallo seguro frente a radicar en la secretaría equivocada.
    ///
    /// El <c>Down</c> quita la clave <c>quipux</c> de <c>external_refs</c> (<c>- 'quipux'</c>)
    /// conservando el resto del jsonb; no toca la columna, que es anterior a esta HU y la comparten
    /// otras integraciones.
    /// </remarks>
    // Atributos inline en vez de un .Designer.cs, como el resto de migraciones solo-SQL del repo
    // (N03_EstadosNegocioTramite, HU10536_TramitePrioritario, HU10198_DocumentRequirementOverrides):
    // esta migración no cambia el modelo, así que no hay BuildTargetModel que aportar. El atributo
    // [Migration] NO es opcional — es lo que hace que EF descubra la clase. Sin él la migración se
    // ignora en silencio y el seed no corre nunca (le pasa hoy a HU10133_OtAdminDevSeed, que no
    // está en __EFMigrationsHistory).
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260715130000_HU10710_QuipuxExternalRefsSeed")]
    public partial class HU10710_QuipuxExternalRefsSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("33-HU10710-quipux-external-refs-seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // jsonb_exists(...) y no el operador `?`: este SQL lo ejecuta Npgsql, cuyo parser trata
            // `?` como marcador de parámetro. La función es equivalente y no depende de escapes.
            migrationBuilder.Sql(@"
UPDATE tramites.procedure_types
SET external_refs = external_refs - 'quipux'
WHERE jsonb_exists(external_refs, 'quipux');

UPDATE catalogs.transit_offices
SET external_refs = external_refs - 'quipux'
WHERE jsonb_exists(external_refs, 'quipux');
");
        }
    }
}
