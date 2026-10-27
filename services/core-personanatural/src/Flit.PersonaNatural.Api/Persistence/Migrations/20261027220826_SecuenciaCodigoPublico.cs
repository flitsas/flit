using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecuenciaCodigoPublico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "codigo_publico_seq",
                schema: "personanatural");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "codigo_publico_seq",
                schema: "personanatural");
        }
    }
}
