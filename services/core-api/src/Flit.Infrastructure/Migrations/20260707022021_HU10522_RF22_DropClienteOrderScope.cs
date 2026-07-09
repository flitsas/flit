using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// RF22 — el Organismo de Tránsito es el único nivel que reordena documentos: se retiró el
    /// ámbito por Cliente. Esta migración elimina las filas legadas de orden con
    /// <c>scope_type='CLIENTE'</c> (idempotente). El ámbito OT y el orden base del trámite se
    /// conservan intactos.
    /// </remarks>
    public partial class HU10522_RF22_DropClienteOrderScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM tramites.document_order_overrides WHERE scope_type = 'CLIENTE';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: las personalizaciones por cliente se descartan al aplicar RF22.
        }
    }
}
