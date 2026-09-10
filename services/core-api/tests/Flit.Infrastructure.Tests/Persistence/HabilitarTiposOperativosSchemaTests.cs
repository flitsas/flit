using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// ADR-0050 — DDL 85, que enciende la barrera de operación de los tipos canónicos.
///
/// <para>Verificación ESTÁTICA del script (la suite no levanta Postgres). Existe por un fallo
/// concreto: la primera versión filtraba los pasos por <c>deleted_at IS NULL</c>, columna que
/// <c>tramites.procedure_steps</c> NO tiene —su borrado lógico es <c>is_active</c>—, y el arranque
/// de la API murió con <c>column ps.deleted_at does not exist</c>. La validación previa se hizo
/// contra una tabla de imitación que sí traía la columna, así que no lo detectó: una imitación que
/// no calca el esquema real valida el script contra una base que no existe.</para>
/// </summary>
public sealed class HabilitarTiposOperativosSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.85-habilitar-tipos-operativos.sql";

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
    public void SoloEnciendeLosDosTiposCanonicos()
    {
        // El resto del catálogo se habilita uno a uno tras validar su parametrización con negocio.
        Ddl().Should().Contain("'MATRICULA_NUEVA', 'TRASPASO_STANDARD'");
    }

    [Fact]
    public void ExigeTipoPublicadoYActivo()
    {
        var ddl = Ddl();

        ddl.Should().Contain("pt.is_active = true");
        ddl.Should().Contain("pt.publication_status = 'published'");
    }

    [Fact]
    public void FallaElArranqueSiNingunTipoCanonicoQuedoEncendido()
    {
        // Sin esto, un catálogo mal sembrado dejaría la operación muerta en silencio: el selector no
        // ofrece nada y toda creación responde 422 sin decir por qué.
        Ddl().Should().Contain("RAISE EXCEPTION");
    }
}
