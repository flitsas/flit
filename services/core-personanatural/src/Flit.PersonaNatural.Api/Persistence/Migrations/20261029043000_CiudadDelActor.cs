using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PersonaNaturalDb))]
    [Migration("20261029043000_CiudadDelActor")]
    public partial class CiudadDelActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "city_encrypted",
                schema: "personanatural",
                table: "requester_person",
                type: "bytea",
                nullable: true,
                comment: "@pii:medium Ciudad cifrada (AES-GCM).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "city_encrypted",
                schema: "personanatural",
                table: "requester_person");
        }
    }
}
