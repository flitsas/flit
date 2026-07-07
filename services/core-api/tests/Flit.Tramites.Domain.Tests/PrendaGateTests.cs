using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

// HU #10597 (R10) — gate de prenda del traspaso: con gravámenes en warn se exige decisión vigente
// (y documento cuando la decisión lo requiere). "omitir" es la vía "asumo el riesgo".
public sealed class PrendaGateTests
{
    private static ProcedureInstancePrenda Prenda(string decision) =>
        new() { Decision = decision, Estado = PrendaEstado.Vigente };

    [Fact]
    public void No_aplica_en_matricula()
    {
        PrendaGate.Evaluate(esTraspaso: false, hasGravamenWarn: true, prendaVigente: null, docTipos: [])
            .Should().BeNull();
    }

    [Fact]
    public void No_aplica_sin_gravamen_en_warn()
    {
        PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: false, prendaVigente: null, docTipos: [])
            .Should().BeNull();
    }

    [Fact]
    public void Traspaso_con_gravamen_sin_decision_bloquea()
    {
        PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: true, prendaVigente: null, docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDecisionRequerida);
    }

    [Fact]
    public void Decision_que_requiere_documento_sin_adjunto_bloquea()
    {
        PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: true, Prenda("registrar"), docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDocumentoRequerido);
    }

    [Fact]
    public void Decision_que_requiere_documento_con_adjunto_pasa()
    {
        PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: true, Prenda("registrar"), docTipos: ["prenda_registro"])
            .Should().BeNull();
    }

    [Fact]
    public void Omitir_pasa_sin_documento_asumo_el_riesgo()
    {
        PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: true, Prenda("omitir"), docTipos: [])
            .Should().BeNull();
    }

    [Fact]
    public void Sin_prenda_pasa_sin_documento()
    {
        PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: true, Prenda("sin_prenda"), docTipos: [])
            .Should().BeNull();
    }
}
