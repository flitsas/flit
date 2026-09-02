using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// DDL 98, que enciende la barrera de operación de <c>TRASPASO_UNILATERAL</c>.
///
/// <para>Verificación ESTÁTICA del script, con el mismo alcance y por el mismo motivo que
/// <see cref="HabilitarTiposOperativosSchemaTests"/>: la suite no levanta Postgres, y el fallo que
/// aquella documenta —filtrar los pasos por una columna que la tabla no tiene, y morir en el
/// arranque de la API— se repite igual de fácil al copiar el patrón a un script nuevo.</para>
/// </summary>
public sealed class HabilitarTraspasoUnilateralSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.98-habilitar-traspaso-unilateral.sql";

    private static string Ddl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void NoFiltraLosPasosPorUnaColumnaQueNoExiste()
    {
        // Ni `procedure_steps` ni `procedure_sections` tienen `deleted_at`.
        var ddl = Ddl();

        ddl.Should().NotContain("ps.deleted_at");
        ddl.Should().NotContain("sec.deleted_at");
    }

    [Fact]
    public void ExigeQueElTipoTengaPasosActivosConSecciones()
    {
        var ddl = Ddl();

        ddl.Should().Contain("ps.is_active",
            "el borrado lógico de los pasos es is_active, no deleted_at");
        ddl.Should().Contain("procedure_sections sec ON sec.procedure_step_id = ps.id",
            "un paso sin secciones no dibuja nada: habilitarlo dejaría un asistente vacío");
    }

    [Fact]
    public void SoloTocaElTraspasoUnilateral()
    {
        var ddl = Ddl();

        ddl.Should().Contain("pt.code = 'TRASPASO_UNILATERAL'");
        // Ningún otro tipo cambia de estado aquí: los canónicos los enciende DDL 85, y el resto del
        // catálogo se habilita uno a uno cuando su parametrización esté validada.
        ddl.Should().NotContain("MATRICULA_NUEVA");
        ddl.Should().NotContain("TRASPASO_STANDARD");
    }

    [Fact]
    public void ExigeTipoPublicadoYActivo()
    {
        var ddl = Ddl();

        ddl.Should().Contain("pt.is_active = true");
        ddl.Should().Contain("pt.publication_status = 'published'");
    }

    [Fact]
    public void FallaElArranqueSiElTipoExisteYQuedoApagado()
    {
        // La opción «Traspaso Unilateral» del modal ya existe en la interfaz: si el catálogo la deja
        // apagada sin decirlo, el gestor la elige y no puede continuar, sin motivo a la vista.
        Ddl().Should().Contain("RAISE EXCEPTION");
    }
}
