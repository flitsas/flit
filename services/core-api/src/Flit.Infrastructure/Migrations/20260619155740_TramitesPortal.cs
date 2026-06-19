using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Trámites Rework — Slice 7 Part B: portal público de participantes (magic-link) + consent
    /// Ley 1581. El DDL (18-tramites-portal.sql) crea procedure_instance_participants con sus índices
    /// (FK + composite tenant/instance + unique token_hash), la FK a identity.tenants, RLS por tenant
    /// y trigger de auditoría. El snapshot/Designer reconcilian el modelo EF (no se emite CreateTable
    /// desde el MigrationBuilder; se usa el DDL embebido para incluir RLS/trigger/FK de tenant, igual
    /// que la migración de firma). El token magic-link se persiste solo como SHA-256.
    /// </remarks>
    public partial class TramitesPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("18-tramites-portal.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS tramites.procedure_instance_participants;
");
        }
    }
}
