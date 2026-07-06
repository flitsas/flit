using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Refactor "adminOT": blinda en BD la regla de negocio "una oficina física del
    /// catálogo (<c>catalogs.transit_offices</c>) = un solo tenant OT", ya validada en
    /// <c>CreateTransitOfficeHandler</c>. Verificado antes de crear esta migración que el
    /// seed de desarrollo (<c>16-HU10133-ot-admin-dev-seed.sql</c>) solo crea un
    /// <c>TransitOfficeProfile</c>, así que no la viola.
    /// </remarks>
    public partial class AddTransitOfficeProfilesTransitOfficeIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "uq_transit_office_profiles_transit_office_id",
                schema: "admin",
                table: "transit_office_profiles",
                column: "transit_office_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_transit_office_profiles_transit_office_id",
                schema: "admin",
                table: "transit_office_profiles");
        }
    }
}
