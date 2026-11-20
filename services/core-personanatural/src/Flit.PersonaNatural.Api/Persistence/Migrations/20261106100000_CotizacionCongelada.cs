using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <summary>HU #14395 (Feature #14392, Épica #14297) — cotización congelada al radicar.</summary>
    [DbContext(typeof(PersonaNaturalDb))]
    [Migration("20261106100000_CotizacionCongelada")]
    public partial class CotizacionCongelada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "frozen_quote",
                schema: "personanatural",
                columns: table => new
                {
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_transit_office_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    total_cop = table.Column<long>(type: "bigint", nullable: false),
                    tariff_version = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    frozen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_frozen_quote", x => x.procedure_request_id);
                    table.CheckConstraint("ck_frozen_quote_method", "method IN ('PASARELA', 'PIN')");
                    table.CheckConstraint("ck_frozen_quote_total", "total_cop >= 0");
                    table.ForeignKey(
                        name: "fk_frozen_quote_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Cotización congelada al radicar (medio, líneas, total y versión de tarifa). Solo inserción: un cambio posterior de precio o de switch no altera el trámite. Sin datos personales.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "frozen_quote",
                schema: "personanatural");
        }
    }
}
