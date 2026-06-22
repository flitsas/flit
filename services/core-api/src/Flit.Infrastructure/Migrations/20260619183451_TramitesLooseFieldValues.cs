using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TramitesLooseFieldValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("19-tramites-loose-field-values.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-añade NOT NULL. Falla si existen filas con form_field_id NULL (valores loose);
            // en ese caso hay que limpiarlas/backfillearlas antes de revertir. Aceptable.
            migrationBuilder.Sql(@"
ALTER TABLE tramites.procedure_instance_field_values
    ALTER COLUMN form_field_id SET NOT NULL;
");
        }
    }
}
