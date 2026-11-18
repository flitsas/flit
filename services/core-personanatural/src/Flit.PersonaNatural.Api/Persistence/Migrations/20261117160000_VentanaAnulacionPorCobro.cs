using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <summary>
    /// Ventana de anulación por organismo (Feature #14453, ADR-0082, revisión 2026-11-17): al confirmar el cobro en caja se guarda el
    /// instante límite de anulación (<c>annulable_until</c> = <c>paid_at</c> + ventana del organismo). Columna nullable y aditiva: un
    /// cobro previo queda en <c>null</c> y cae a la ventana global. Lleva los atributos <c>[DbContext]</c>/<c>[Migration]</c> en línea
    /// (sin Designer) para que EF la descubra al arrancar.
    /// </summary>
    [DbContext(typeof(PersonaNaturalDb))]
    [Migration("20261117160000_VentanaAnulacionPorCobro")]
    public partial class VentanaAnulacionPorCobro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "annulable_until",
                schema: "personanatural",
                table: "payment_charge",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "annulable_until",
                schema: "personanatural",
                table: "payment_charge");
        }
    }
}
