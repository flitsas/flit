using Flit.Admin.Domain.Companies.MandateSigners;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13129 (Epic #13090, F2) — estado de vigencia del mandatario calculado en servidor. Orden de
/// evaluación: inactivo → vencido → (no vigente aún) → por vencer (≤ 7 días) → vigente. Las fechas son
/// <c>date</c>; «hoy» lo decide quien llama (día calendario de Colombia).
/// <para>Uso: <c>MandateValidityStatus.Compute(true, "range", from, to, hoy)</c> ⇒ <c>"por_vencer"</c> si
/// faltan 7 días o menos para <c>to</c>.</para>
/// </summary>
public sealed class MandateValidityStatusTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 1);

    [Fact]
    public void VigenciaFija_Activa_EsVigente_SinFechas()
    {
        // AC1 — sin fechas, el estado es vigente mientras esté activo.
        MandateValidityStatus.Compute(true, MandateValidityKinds.Fixed, null, null, Hoy)
            .Should().Be(MandateValidityStatus.Vigente);
    }

    [Theory]
    [InlineData(-10, 30, "vigente")]      // dentro del rango, lejos del fin
    [InlineData(-10, 8, "vigente")]       // faltan 8 días: aún no avisa
    [InlineData(-10, 7, "por_vencer")]    // faltan exactamente 7 días
    [InlineData(-10, 1, "por_vencer")]    // falta 1 día
    [InlineData(-10, 0, "por_vencer")]    // último día del rango
    [InlineData(-10, -1, "vencido")]      // ayer fue el último día
    [InlineData(-30, -10, "vencido")]
    public void VigenciaPorRango_EvaluaVigenteXVencerYVencido(int desdeOffset, int hastaOffset, string esperado)
    {
        // AC2 — vigente dentro del rango, por vencer a 7 días o menos del fin, vencido después de la fecha fin.
        var estado = MandateValidityStatus.Compute(
            true, MandateValidityKinds.Range, Hoy.AddDays(desdeOffset), Hoy.AddDays(hastaOffset), Hoy);

        estado.Should().Be(esperado);
    }

    [Fact]
    public void RangoDeUnSoloDia_EsVigenteEseDia_YVencidoAlSiguiente()
    {
        // AC7 — inicio = fin: vigente ese día y vencido al día siguiente.
        var dia = Hoy;

        MandateValidityStatus.Compute(true, MandateValidityKinds.Range, dia, dia, dia)
            .Should().Be(MandateValidityStatus.Vigente);
        MandateValidityStatus.Compute(true, MandateValidityKinds.Range, dia, dia, dia.AddDays(1))
            .Should().Be(MandateValidityStatus.Vencido);
    }

    [Theory]
    [InlineData("fixed", 0, 0)]
    [InlineData("range", -10, 30)]   // vigente por rango
    [InlineData("range", -10, 3)]    // por vencer
    [InlineData("range", -30, -10)]  // vencido
    public void Inactivo_SiempreReportaInactivo_SinImportarElRango(string tipo, int desde, int hasta)
    {
        // AC7 — un mandatario inactivo reporta inactivo, sin importar el rango.
        var fechas = tipo == "range" ? (Hoy.AddDays(desde), Hoy.AddDays(hasta)) : ((DateOnly?)null, (DateOnly?)null);

        MandateValidityStatus.Compute(false, tipo, fechas.Item1, fechas.Item2, Hoy)
            .Should().Be(MandateValidityStatus.Inactivo);
    }

    [Fact]
    public void RangoQueAunNoEmpieza_SeTrataComoNoVigente()
    {
        // Decisión del PO: un rango futuro no es vigente (duda D-6 del ADR-0061).
        MandateValidityStatus.Compute(true, MandateValidityKinds.Range, Hoy.AddDays(2), Hoy.AddDays(60), Hoy)
            .Should().Be(MandateValidityStatus.NoVigente);
    }

    [Fact]
    public void RangoFuturoQueYaCaducariaPronto_NoSeMarcaPorVencer()
    {
        MandateValidityStatus.Compute(true, MandateValidityKinds.Range, Hoy.AddDays(1), Hoy.AddDays(4), Hoy)
            .Should().Be(MandateValidityStatus.NoVigente);
    }

    [Fact]
    public void ElItemDeLectura_CalculaElEstadoParaElDiaIndicado()
    {
        var item = new MandateSignerItem
        {
            IsActive = true,
            ValidityKind = MandateValidityKinds.Range,
            ValidFrom = Hoy.AddDays(-1),
            ValidTo = Hoy.AddDays(5),
        };

        item.ValidityStatusOn(Hoy).Should().Be(MandateValidityStatus.PorVencer);
        item.ValidityStatusOn(Hoy.AddDays(6)).Should().Be(MandateValidityStatus.Vencido);
    }
}
