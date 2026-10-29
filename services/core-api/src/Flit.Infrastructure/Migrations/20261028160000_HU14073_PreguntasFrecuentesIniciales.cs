using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14073 (Feature J, Épica #14021) — carga en el contenido de trabajo las cinco preguntas frecuentes que el portal
/// muestra por defecto, para gestionarlas en Plataforma. Solo si la tabla está vacía; no publica. DDL:
/// <c>166-HU14073-preguntas-frecuentes-iniciales.sql</c>. Lleva los atributos inline (sin Designer).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261028160000_HU14073_PreguntasFrecuentesIniciales")]
public partial class HU14073_PreguntasFrecuentesIniciales : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("166-HU14073-preguntas-frecuentes-iniciales.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM admin.persona_natural_faqs
             WHERE id IN (
                'a1407300-0001-4000-8000-000000000001',
                'a1407300-0001-4000-8000-000000000002',
                'a1407300-0001-4000-8000-000000000003',
                'a1407300-0001-4000-8000-000000000004',
                'a1407300-0001-4000-8000-000000000005');
            """);
}
