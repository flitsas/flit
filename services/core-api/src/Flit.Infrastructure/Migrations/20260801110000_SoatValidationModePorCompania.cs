using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// Validación del SOAT contra el RUNT al procesar, activable por compañía.
    ///
    /// <para>Cuando el gestor marca los checks en <c>asignado</c> y pulsa procesar, si la compañía
    /// tiene la opción activa se consulta el RUNT: sin SOAT vigente el avance se detiene con el
    /// error. Sin la opción activa la consulta solo informa y el trámite continúa.</para>
    ///
    /// <para>Default <c>false</c>: la validación es una decisión de cada compañía y activarla por
    /// omisión detendría trámites que hoy avanzan.</para>
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260801110000_SoatValidationModePorCompania")]
    public partial class SoatValidationModePorCompania : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- El primer diseño de esta columna fue un modo de tres valores; el criterio final es
                -- activar/no activar. Se limpia por si algún entorno alcanzó a aplicar aquella forma.
                ALTER TABLE admin.tenant_operational_policies
                    DROP CONSTRAINT IF EXISTS ck_tenant_operational_policies_soat_validation_mode;
                ALTER TABLE admin.tenant_operational_policies
                    DROP COLUMN IF EXISTS soat_validation_mode;

                ALTER TABLE admin.tenant_operational_policies
                    ADD COLUMN IF NOT EXISTS validate_soat_with_runt boolean NOT NULL DEFAULT false;

                COMMENT ON COLUMN admin.tenant_operational_policies.validate_soat_with_runt IS
                  'Al procesar un trámite en sub-estado asignado se consulta el SOAT en el RUNT. Con la opción activa, un SOAT no vigente detiene el avance con el error; sin ella, el hallazgo solo se informa y el trámite continúa.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.tenant_operational_policies
                    DROP COLUMN IF EXISTS validate_soat_with_runt;
                """);
        }
    }
}
