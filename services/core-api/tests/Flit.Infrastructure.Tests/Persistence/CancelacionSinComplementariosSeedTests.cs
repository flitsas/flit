using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Guarda del seed embebido que apaga los trámites complementarios en <c>CANCELACION_MATRICULA</c>
/// (DDL 93). Sin BD: comprueba que el DDL sigue declarando las dos llaves y que el DDL 87 —cuya
/// guarda prohíbe apagar complementarios fuera de la familia OTROS— exime a este tipo.
///
/// <para>Sin esta excepción, el asistente le pinta «Asignación de Prenda / Limitación a la
/// Propiedad» y «Trámites Simultáneos» a un trámite que saca el vehículo del registro, porque su
/// familia (MATRICULAS) sí acumula. Y sin la exención del 87, reaplicar aquel DDL aborta acusando a
/// este tipo.</para>
/// </summary>
public sealed class CancelacionSinComplementariosSeedTests
{
    private const string Ddl93 = "93-cancelacion-sin-complementarios.sql";

    private static string LoadDdl(string fileName)
    {
        var asm = typeof(FlitDbContext).Assembly;
        var name = $"Flit.Infrastructure.Persistence.Sql.Ddl.{fileName}";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Ddl93_ApagaLasDosLlavesDelTipo()
    {
        var sql = LoadDdl(Ddl93);

        sql.Should().Contain(
            "'{\"allowsComplementaryTransformations\": false, \"allowsComplementaryPrenda\": false}'::jsonb");
        sql.Should().Contain("WHERE code = 'CANCELACION_MATRICULA'");
    }

    [Fact]
    public void Ddl93_SoloTocaLaCancelacion() =>
        // La excepción es de UN tipo. Un `family = 'MATRICULAS'` aquí apagaría los simultáneos de la
        // matrícula inicial, que sí los tiene por el art. 5.1.8.
        LoadDdl(Ddl93).Should().NotContain("family");

    [Fact]
    public void Ddl87_EximeALaCancelacionDeSuGuarda() =>
        LoadDdl("87-otros-sin-complementarios.sql")
            .Should().Contain("code <> 'CANCELACION_MATRICULA'");
}
