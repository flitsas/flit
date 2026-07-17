using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Quipux (HU #10710) — parametrización de la secretaría DESTINO. Añade a
    /// <c>catalogs.transit_offices</c> el <c>divipo_code</c> (se carga a mano: solo se conocen 6 de
    /// 317) y tres banderas por familia de trámite, que replican el modelo de FLIT 1.0
    /// (<c>traffic_secretaries.id_parinttrasec_registration/_transfer/_otherservice</c>): el alta es
    /// gradual y una secretaría puede estar integrada para traspasos y no para matrículas.
    ///
    /// <para><b>Por qué aquí y no en un perfil.</b> La primera versión usaba
    /// <c>admin.transit_office_profiles.operation_mode = 'quipux'</c> como filtro de radicación, y
    /// era incorrecto: ese perfil describe al OT-CLIENTE cuya consola queda en solo lectura porque
    /// aprueba dentro de Quipux, y solo existe para los 12 organismos que son tenants de FLIT. La
    /// secretaría destino a la que se radica normalmente NO es cliente —de hecho se usa Quipux
    /// precisamente porque no usa el dashboard—, así que aquel filtro dejaba fuera a 305 de 317.
    /// Ambos conceptos comparten el nombre "Quipux" pero son distintos; el perfil se conserva con
    /// su significado original.</para>
    ///
    /// <para>El DIVIPO deja de vivir en <c>external_refs</c> (jsonb): se carga a mano de forma
    /// sostenida y un jsonb no valida el formato ni delata un typo en la clave, que dejaría la
    /// secretaría fuera del gate en silencio; una columna además se expone en la consola de
    /// administración. El <c>UPDATE</c> del final rescata lo que una versión previa del seed había
    /// dejado en el jsonb y retira la clave, para no quedarse con dos fuentes de verdad; en una
    /// base nueva no hay nada que migrar y queda en no-op.</para>
    ///
    /// <para><b>No se siembra ningún <c>divipo_code</c>, a propósito.</b> No se conocen los de las
    /// 317 secretarías —el dato real sale de <c>traffic_secretaries.code_divipo</c> de FLIT 1.0— y
    /// sembrar un valor plausible (p. ej. <c>city_code</c>) sería peor que dejarlo vacío: el gate
    /// lo daría por bueno y se radicarían trámites en la secretaría equivocada, que es un error
    /// caro y silencioso. Sin DIVIPO la secretaría no es elegible y queda visible como pendiente.
    /// </para>
    /// </remarks>
    public partial class HU10710_TransitOfficeQuipuxFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "divipo_code",
                schema: "catalogs",
                table: "transit_offices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "quipux_registration",
                schema: "catalogs",
                table: "transit_offices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "quipux_other",
                schema: "catalogs",
                table: "transit_offices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "quipux_transfer",
                schema: "catalogs",
                table: "transit_offices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_transit_offices_divipo_code",
                schema: "catalogs",
                table: "transit_offices",
                column: "divipo_code",
                filter: "divipo_code IS NOT NULL");

            migrationBuilder.Sql(
                """
                COMMENT ON COLUMN catalogs.transit_offices.divipo_code IS
                  'HU #10710 — código DIVIPO que espera Quipux. Se carga a mano (no se tiene el de las 317). NO se deriva de code ni de city_code: el traffic_agency_code del RUNT pierde el cero a la izquierda (Medellín: code=5001000, city_code=05001). NULL = desconocido = la secretaría no es elegible para radicar.';
                COMMENT ON COLUMN catalogs.transit_offices.quipux_transfer IS
                  'HU #10710 — ¿los traspasos de esta secretaría se radican por Quipux? Equivale a traffic_secretaries.id_parinttrasec_transfer=2 de FLIT 1.0. No confundir con admin.transit_office_profiles.operation_mode, que describe al OT-cliente y no al destino.';
                COMMENT ON COLUMN catalogs.transit_offices.quipux_registration IS
                  'HU #10710 — ¿las matrículas de esta secretaría se radican por Quipux? Equivale a id_parinttrasec_registration=2 de FLIT 1.0.';
                COMMENT ON COLUMN catalogs.transit_offices.quipux_other IS
                  'HU #10710 — ¿los otros trámites de esta secretaría se radican por Quipux? Equivale a id_parinttrasec_otherservice=2 de FLIT 1.0.';

                -- Migra el DIVIPO sembrado en external_refs a su columna y retira la clave, para no
                -- dejar dos fuentes de verdad. Idempotente: si no hay clave, no toca nada.
                UPDATE catalogs.transit_offices
                SET divipo_code = external_refs -> 'quipux' ->> 'codigoDivipo'
                WHERE external_refs -> 'quipux' ->> 'codigoDivipo' IS NOT NULL
                  AND divipo_code IS NULL;

                UPDATE catalogs.transit_offices
                SET external_refs = external_refs - 'quipux'
                WHERE external_refs ? 'quipux';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transit_offices_divipo_code",
                schema: "catalogs",
                table: "transit_offices");

            migrationBuilder.DropColumn(
                name: "divipo_code",
                schema: "catalogs",
                table: "transit_offices");

            migrationBuilder.DropColumn(
                name: "quipux_registration",
                schema: "catalogs",
                table: "transit_offices");

            migrationBuilder.DropColumn(
                name: "quipux_other",
                schema: "catalogs",
                table: "transit_offices");

            migrationBuilder.DropColumn(
                name: "quipux_transfer",
                schema: "catalogs",
                table: "transit_offices");
        }
    }
}
