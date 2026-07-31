using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>Gate duro de SOAT en la ruta de placa (R06, Feature #10587 · HU #10611).</summary>
public sealed class SoatGateTests
{
    [Theory]
    // HU #10611: bloquea SALVO que esté vigente (sin evidencia de SOAT el OT no aprueba).
    [InlineData("vigente", false)]  // única evidencia válida → no bloquea.
    [InlineData("VIGENTE", false)]  // case-insensitive.
    [InlineData("vencido", true)]   // vencido → bloquea.
    [InlineData("unknown", true)]   // sin registrar → bloquea.
    [InlineData(null, true)]        // ausente → bloquea.
    [InlineData("", true)]          // vacío → bloquea.
    public void BlocksApproval_BloqueaSalvoVigente(string? estado, bool bloquea)
    {
        SoatGate.BlocksApproval(estado).Should().Be(bloquea);
    }

    [Theory]
    [InlineData("vigente", true)]
    [InlineData("VIGENTE", true)]
    [InlineData("vencido", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void IsSatisfied_SoloVigente(string? estado, bool satisfecho)
    {
        SoatGate.IsSatisfied(estado).Should().Be(satisfecho);
    }

    // ── HU #10973 — normalización del estado crudo del RUNT ───────────────────

    [Theory]
    // El crudo del RUNT llega en mayúscula: se baja al vocabulario del gate.
    [InlineData("VIGENTE", "vigente")]
    [InlineData("vigente", "vigente")]
    [InlineData("  Vigente  ", "vigente")]
    [InlineData("VENCIDO", "vencido")]
    [InlineData("NO VIGENTE", "vencido")]
    [InlineData("NO_VIGENTE", "vencido")]
    // Estados que el RUNT no define como vigente/vencido NO se fuerzan a 'vencido':
    // se declaran 'unknown' para no afirmar algo que la consulta no dijo (el gate bloquea igual).
    [InlineData("ANULADA", "unknown")]
    [InlineData("CANCELADA", "unknown")]
    public void Normalize_LlevaElCrudoDelRuntAlVocabularioDelGate(string raw, string esperado)
    {
        SoatGate.Normalize(raw).Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_SinEstadoDevuelveNull_ParaNoEscribirLaLlave(string? raw)
    {
        // Valor ausente ⇒ el llamador NO escribe soat_estado ⇒ celda en blanco (regla HU #10856).
        SoatGate.Normalize(raw).Should().BeNull();
    }

    [Fact]
    public void Normalize_ElResultadoSatisfaceElGateCuandoElRuntReportaVigente()
    {
        // Regresión de la HU: el valor normalizado debe seguir satisfaciendo el gate del dominio…
        var normalizado = SoatGate.Normalize("VIGENTE");

        SoatGate.IsSatisfied(normalizado).Should().BeTrue();
        SoatGate.BlocksApproval(normalizado).Should().BeFalse();

        // …y además coincidir EXACTAMENTE con el literal que el frontend compara de forma estricta
        // (frontend/lib/tramites/estados.ts: soatEstado === 'vigente'). Sin esta igualdad exacta, la
        // aprobación del OT quedaría bloqueada con el SOAT vigente.
        normalizado.Should().Be("vigente");
    }
}
