using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Snapshot-only: tablas RBAC ya creadas por HU10148_Rbac (SQL embebido).
    /// Esta migración alinea el modelo EF con las entidades de lectura de auth.
    /// </remarks>
    public partial class HU10168_AuthRbacReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
