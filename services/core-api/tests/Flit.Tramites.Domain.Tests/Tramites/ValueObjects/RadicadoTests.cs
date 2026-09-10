using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Tramites.ValueObjects;

/// <summary>
/// HU #12371 — el formato del radicado (<c>FT1-0000012</c>) y cómo se lee lo que escribe un usuario.
/// Es el lado C# del contrato; el lado SQL (el trigger) lo ata <c>RadicadoPrefijoFamiliaTests</c>.
/// </summary>
public sealed class RadicadoTests
{
    // ── AC1/AC2 — prefijo por familia, contador global ───────────────────────

    [Theory]
    [InlineData(ProcedureFamily.Matriculas, "FT1")]
    [InlineData(ProcedureFamily.Traspaso, "FT2")]
    [InlineData(ProcedureFamily.Otros, "FT3")]
    public void ElPrefijoEsElAcordadoConElPo(ProcedureFamily familia, string prefijo)
    {
        Radicado.Prefijo(familia).Should().Be(prefijo);
    }

    [Fact]
    public void TodaFamiliaTienePrefijo()
    {
        // Una cuarta familia sin prefijo reventaría aquí antes que en producción.
        foreach (var familia in Enum.GetValues<ProcedureFamily>())
            Radicado.Prefijo(familia).Should().MatchRegex("^FT[1-9]$");
    }

    [Fact]
    public void ElContadorEsGlobal_LaFamiliaSoloCambiaElPrefijo()
    {
        Radicado.Componer(ProcedureFamily.Matriculas, 8).Should().Be("FT1-0000008");
        Radicado.Componer(ProcedureFamily.Traspaso, 9).Should().Be("FT2-0000009");
        Radicado.Componer(ProcedureFamily.Otros, 10).Should().Be("FT3-0000010");
    }

    // ── AC3 — relleno a ancho fijo, mínimo y no tope ─────────────────────────

    [Theory]
    [InlineData(1, "FT1-0000001")]
    [InlineData(10, "FT1-0000010")]
    [InlineData(100, "FT1-0000100")]
    [InlineData(1299, "FT1-0001299")]
    [InlineData(9_999_999, "FT1-9999999")]
    [InlineData(10_000_000, "FT1-10000000")]
    [InlineData(9_100_000_001, "FT1-9100000001")]
    public void RellenaASieteYCreceSinRecortar(long consecutivo, string esperado)
    {
        var radicado = Radicado.Componer(ProcedureFamily.Matriculas, consecutivo);

        radicado.Should().Be(esperado);
        radicado.Should().MatchRegex(Radicado.PatronSql, "lo que compone C# tiene que pasar el CHECK de la base");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoComponeConUnNumeroQueLaSecuenciaNuncaDa(long consecutivo)
    {
        var act = () => Radicado.Componer(ProcedureFamily.Matriculas, consecutivo);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── AC7 — lectura tolerante ──────────────────────────────────────────────

    [Theory]
    [InlineData("12", 12, null)]
    [InlineData("0000012", 12, null)]
    [InlineData("  12 ", 12, null)]
    [InlineData("FT1-0000012", 12, "FT1")]
    [InlineData("ft1-0000012", 12, "FT1")]
    [InlineData("ft1 12", 12, "FT1")]
    [InlineData("FT1.0000012", 12, "FT1")]
    [InlineData("FT2-12", 12, "FT2")]
    [InlineData("FT3-10000000", 10_000_000, "FT3")]
    public void LeeLoQueEscribeElUsuario(string texto, long consecutivo, string? prefijo)
    {
        Radicado.TryLeer(texto, out var lectura).Should().BeTrue();

        lectura!.Value.Consecutivo.Should().Be(consecutivo);
        lectura.Value.Prefijo.Should().Be(prefijo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0000000")]
    [InlineData("ABC123")]
    [InlineData("FT1-")]
    [InlineData("FT0-0000012")]
    [InlineData("TRM-2026-000123")]
    [InlineData("99999999999999999999")]
    public void LoQueNoEsUnRadicadoNoSeLee(string? texto)
    {
        // Una placa, el formato viejo o un cero no son radicados: el que llama los busca en otros
        // campos en vez de forzar una lectura.
        Radicado.TryLeer(texto, out var lectura).Should().BeFalse();
        lectura.Should().BeNull();
    }

    [Fact]
    public void LaFormaCanonicaSinGuionEsComparableConLaColumnaNormalizada()
    {
        // Es lo que la búsqueda compara con reference_number.ToUpper().Replace("-", "").
        Radicado.TryLeer("ft1 12", out var conPrefijo);
        conPrefijo!.Value.CanonicoSinGuion.Should().Be("FT10000012");

        Radicado.TryLeer("12", out var sinPrefijo);
        sinPrefijo!.Value.CanonicoSinGuion.Should().BeNull("sin prefijo se busca por el número, no por el texto");
    }
}
