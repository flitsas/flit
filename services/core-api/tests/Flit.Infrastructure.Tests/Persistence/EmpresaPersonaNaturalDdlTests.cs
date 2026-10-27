using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #14033 (Feature #14022, Épica #14021) — el DDL 152 siembra la empresa «Persona Natural» con el MISMO id que la
/// constante del SDK, solo agrega (no toca otras compañías) y se puede correr más de una vez.
/// </summary>
public sealed class EmpresaPersonaNaturalDdlTests
{
    private const string DdlResource = "Flit.Infrastructure.Persistence.Sql.Ddl.152-HU14033-empresa-persona-natural.sql";

    private static string LoadDdl()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(linea =>
        {
            var corte = linea.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? linea[..corte] : linea;
        }));
    }

    [Fact]
    public void ElIdSembradoEsElDeLaConstanteDelSdk() =>
        LoadDdl().Should().Contain($"'{PlatformTenants.PersonaNatural}'");

    [Fact]
    public void SoloInsertaUnaFila_ConOnConflictParaPoderCorrerloOtraVez()
    {
        var ddl = LoadDdl();

        ddl.Split("INSERT INTO", StringSplitOptions.None).Length.Should().Be(2, "una sola sentencia INSERT");
        ddl.Should().Contain("ON CONFLICT (id) DO NOTHING");
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("ALTER TABLE")]
    [InlineData("DROP")]
    public void NoModificaNadaExistente(string sentencia) =>
        LoadDdl().Should().NotContainEquivalentOf(sentencia, "el DDL solo agrega la empresa nueva");

    [Fact]
    public void ElCodigoDeCompaniaLoAsignaLaSecuencia_NoElDdl() =>
        LoadDdl().Should().NotContain("COMP-", "code toma el DEFAULT de la secuencia de la Épica #13517");
}
