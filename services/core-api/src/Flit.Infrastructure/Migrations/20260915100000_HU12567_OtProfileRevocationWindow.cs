using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <remarks>
    /// HU #12567 (Feature #12565) — ventana de revocatoria por OT: columna nueva en
    /// <c>admin.transit_office_profiles</c>, nullable y SIN default numérico. "Sin configurar" =
    /// "sin límite" es una decisión de producto ya tomada: un perfil OT existente antes de esta
    /// migración debe seguir leyendo NULL después de aplicarla (no hay backfill a 0 ni a ningún
    /// otro valor). El resto del flujo de revocatoria (HU #12570 y siguientes) lee esta columna
    /// para calcular si una solicitud de revocatoria sigue dentro de la ventana permitida.
    ///
    /// Fuera del alcance de esta migración: la entidad EF (<c>TransitOfficeProfile</c>) y su
    /// <c>IEntityTypeConfiguration</c> no se tocan aquí — el mapeo de la columna nueva lo agrega el
    /// backend-agent junto con el use case que la consume, igual que ya ocurrió con
    /// <c>subsanacion_activa</c>/<c>subsanacion_count</c> en 20260728200000_SubsanacionFlag.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260915100000_HU12567_OtProfileRevocationWindow")]
    public partial class HU12567_OtProfileRevocationWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.transit_office_profiles
                  ADD COLUMN IF NOT EXISTS revocation_window_business_days integer NULL;

                ALTER TABLE admin.transit_office_profiles
                  DROP CONSTRAINT IF EXISTS ck_transit_office_profiles_revocation_window_positive;
                ALTER TABLE admin.transit_office_profiles
                  ADD CONSTRAINT ck_transit_office_profiles_revocation_window_positive
                  CHECK (revocation_window_business_days IS NULL OR revocation_window_business_days > 0);

                COMMENT ON COLUMN admin.transit_office_profiles.revocation_window_business_days IS
                  'Ventana de revocatoria del trámite aprobado, en días hábiles. NULL = sin configurar = sin límite (no confundir con 0 ni asumir ningún default).';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.transit_office_profiles
                  DROP CONSTRAINT IF EXISTS ck_transit_office_profiles_revocation_window_positive;
                ALTER TABLE admin.transit_office_profiles
                  DROP COLUMN IF EXISTS revocation_window_business_days;
                """);
        }
    }
}
