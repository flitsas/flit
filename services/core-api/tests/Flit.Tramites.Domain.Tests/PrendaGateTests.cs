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

    // ── CF-06 (HU #10881) — override del OT, independiente del semáforo de gravámenes ───────────

    [Fact]
    public void OtOverride_Inactivo_NuncaBloquea()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: false, docTipos: [])
            .Should().BeNull();
    }

    [Fact]
    public void OtOverride_Activo_SinDocumento_Bloquea()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDocumentoRequerido);
    }

    [Theory]
    [InlineData("prenda_solicitud")]
    [InlineData("prenda_registro")]
    [InlineData("prenda_levantamiento")]
    public void OtOverride_Activo_ConCualquierDocumentoDePrenda_Pasa(string docTipo)
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, docTipos: [docTipo])
            .Should().BeNull();
    }

    [Fact]
    public void OtOverride_Activo_ConOtroDocumentoNoRelacionado_Bloquea()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, docTipos: ["soat", "rtm"])
            .Should().Be(TramiteEstadoErrores.PrendaDocumentoRequerido);
    }
}
