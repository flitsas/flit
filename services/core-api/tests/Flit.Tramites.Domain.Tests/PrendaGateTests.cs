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

    private static ProcedureInstancePrenda PrendaConAcreedor(
        string decision, string? acreedorNombre, string? acreedorDocumento) =>
        new()
        {
            Decision = decision,
            Estado = PrendaEstado.Vigente,
            AcreedorNombre = acreedorNombre,
            AcreedorDocumento = acreedorDocumento,
        };

    [Fact]
    public void Evaluate_no_aplica_en_matricula()
    {
        // `Evaluate` es EXCLUSIVAMENTE del gate de traspaso (semáforo de gravámenes): en matrícula
        // inicial el gate corre por EvaluateMatriculaInicial (HU #11592, ver más abajo), no por acá.
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
        // HU #11591: "registrar" también implica gravamen ⇒ exige acreedor diligenciado.
        PrendaGate.Evaluate(
                esTraspaso: true,
                hasGravamenWarn: true,
                PrendaConAcreedor(PrendaDecision.Registrar, "Banco X", "900123456"),
                docTipos: ["prenda_registro"])
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

    // ── HU #11591 — acreedor obligatorio en decisiones que constituyen gravamen ────────────────────

    [Fact]
    public void Solicitar_con_acreedor_completo_pasa()
    {
        PrendaGate.Evaluate(
                esTraspaso: true,
                hasGravamenWarn: true,
                PrendaConAcreedor(PrendaDecision.Solicitar, "Banco X", "900123456"),
                docTipos: ["prenda_solicitud"])
            .Should().BeNull();
    }

    [Fact]
    public void Registrar_sin_nombre_de_acreedor_bloquea()
    {
        PrendaGate.Evaluate(
                esTraspaso: true,
                hasGravamenWarn: true,
                PrendaConAcreedor(PrendaDecision.Registrar, acreedorNombre: null, acreedorDocumento: "900123456"),
                docTipos: ["prenda_registro"])
            .Should().Be(TramiteEstadoErrores.PrendaAcreedorRequerido);
    }

    [Fact]
    public void Registrar_sin_documento_de_acreedor_bloquea()
    {
        PrendaGate.Evaluate(
                esTraspaso: true,
                hasGravamenWarn: true,
                PrendaConAcreedor(PrendaDecision.Registrar, acreedorNombre: "Banco X", acreedorDocumento: null),
                docTipos: ["prenda_registro"])
            .Should().Be(TramiteEstadoErrores.PrendaAcreedorRequerido);
    }

    [Theory]
    [InlineData(PrendaDecision.Levantar)]
    [InlineData(PrendaDecision.Omitir)]
    [InlineData(PrendaDecision.SinPrenda)]
    public void Decision_que_no_implica_gravamen_no_exige_acreedor(string decision)
    {
        PrendaGate.Evaluate(
                esTraspaso: true,
                hasGravamenWarn: true,
                PrendaConAcreedor(decision, acreedorNombre: null, acreedorDocumento: null),
                docTipos: decision == PrendaDecision.Levantar ? ["prenda_levantamiento"] : [])
            .Should().NotBe(TramiteEstadoErrores.PrendaAcreedorRequerido);
    }

    // ── CF-06 (HU #10881) — override del OT, independiente del semáforo de gravámenes ───────────

    [Fact]
    public void OtOverride_Inactivo_NuncaBloquea()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: false, decision: "registrar", docTipos: [])
            .Should().BeNull();
    }

    [Fact]
    public void OtOverride_Activo_SinDocumento_Bloquea()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, decision: "registrar", docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
    }

    [Theory]
    [InlineData("prenda_solicitud")]
    [InlineData("prenda_registro")]
    [InlineData("prenda_levantamiento")]
    public void OtOverride_Activo_ConCualquierDocumentoDePrenda_Pasa(string docTipo)
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, decision: "registrar", docTipos: [docTipo])
            .Should().BeNull();
    }

    [Fact]
    public void OtOverride_Activo_ConOtroDocumentoNoRelacionado_Bloquea()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, decision: "registrar", docTipos: ["soat", "rtm"])
            .Should().Be(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
    }

    // ── 2026-08-12 — el override deja de exigir lo que la UI no ofrece cargar ────────────────────

    /// <summary>
    /// EL CASO REAL que motivó el cambio: matrícula inicial con <c>sin_prenda</c>, override del OT
    /// activo (su valor por DEFECTO mientras nadie configure el opt-out) y ningún adjunto de prenda.
    /// El trámite quedaba atascado en "Finalizar" exigiendo un documento que el paso de prenda no
    /// ofrece cargar para esa decisión: sin vía para cumplirlo desde el trámite.
    /// </summary>
    [Theory]
    [InlineData(PrendaDecision.SinPrenda)]
    [InlineData(PrendaDecision.Omitir)]
    public void OtOverride_ConDecisionQueNoPideDocumento_NoBloquea(string decision)
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, decision, docTipos: [])
            .Should().BeNull(
                "el paso de prenda no ofrece cargar documento para esta decisión: exigirlo deja al "
                + "gestor sin forma de desbloquear el trámite");
    }

    /// <summary>
    /// Sin decisión tomada el override NO calla: pide la decisión. Delegarlo en
    /// <see cref="PrendaGate.Evaluate"/> solo cubría el traspaso con el semáforo en warn; en matrícula
    /// inicial no hay ningún otro gate de prenda, así que callar aquí dejaba radicar sin el
    /// certificado que el OT exige con solo no abrir el paso de prenda.
    /// </summary>
    [Fact]
    public void OtOverride_SinDecision_ExigeLaDecision()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, decision: null, docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDecisionRequerida);
    }

    /// <summary>
    /// Y lo pide aunque ya haya un documento de prenda adjunto: el adjunto no dice qué se decidió, y
    /// el FUR y el trámite se arman a partir de la decisión, no del archivo.
    /// </summary>
    [Fact]
    public void OtOverride_SinDecision_ExigeLaDecisionAunqueHayaDocumento()
    {
        PrendaGate.EvaluateOtOverride(
                otRequiereDocumentoPrenda: true, decision: null, docTipos: ["prenda_registro"])
            .Should().Be(TramiteEstadoErrores.PrendaDecisionRequerida);
    }

    /// <summary>Override inactivo: sin decisión tampoco bloquea, como siempre.</summary>
    [Fact]
    public void OtOverride_Inactivo_SinDecision_NoBloquea()
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: false, decision: null, docTipos: [])
            .Should().BeNull();
    }

    /// <summary>
    /// Lo que el override NO pierde: sigue siendo independiente del SEMÁFORO. Una matrícula inicial
    /// que constituye prenda (vehículo nuevo financiado, sin gravamen previo que detectar) sigue
    /// exigiendo su soporte — ahí `Evaluate` no llega, porque solo actúa en traspaso con warn.
    /// </summary>
    [Theory]
    [InlineData(PrendaDecision.Solicitar)]
    [InlineData(PrendaDecision.Registrar)]
    [InlineData(PrendaDecision.Levantar)]
    public void OtOverride_ConDecisionQuePideDocumento_SigueExigiendoloSinGravamenDetectado(string decision)
    {
        PrendaGate.EvaluateOtOverride(otRequiereDocumentoPrenda: true, decision, docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDocumentoRequeridoOt);
    }

    /// <summary>
    /// El conjunto que dispara el override tiene que ser EXACTAMENTE el mismo que usa la UI para
    /// decidir si ofrece la carga. Si alguien añade una decisión a uno y no al otro, vuelve el
    /// bloqueo insatisfacible — este test lo convierte en un fallo, no en un misterio en producción.
    /// </summary>
    [Fact]
    public void OtOverride_SeDisparaExactamenteConElConjuntoQuePideDocumento()
    {
        var disparan = PrendaDecision.All
            .Where(d => PrendaGate.EvaluateOtOverride(true, d, docTipos: [])
                == TramiteEstadoErrores.PrendaDocumentoRequeridoOt)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        disparan.Should().BeEquivalentTo(PrendaDecision.RequierenDocumento);
    }

    // ── HU #11592 — bloqueo duro de prenda en matrícula inicial ────────────────────────────────
    // Invierte deliberadamente la HU #10596 (prenda declarativa/informativa en matrícula, sin gate).
    // Mismo núcleo que `Evaluate`, pero SIN depender del semáforo de gravámenes (irrelevante en un
    // vehículo nuevo: el gravamen se constituye, no se detecta).

    [Fact]
    public void MatriculaInicial_ConDecisionSinPrenda_Avanza()
    {
        // AC1: matrícula inicial con decisión "sin_prenda" vigente avanza.
        PrendaGate.EvaluateMatriculaInicial(Prenda(PrendaDecision.SinPrenda), docTipos: [])
            .Should().BeNull();
    }

    [Fact]
    public void MatriculaInicial_SinNingunaDecision_Bloquea()
    {
        // AC2: sin prenda vigente registrada, el gate bloquea con independencia de cualquier
        // semáforo de gravámenes (esta función ni siquiera lo recibe como parámetro).
        PrendaGate.EvaluateMatriculaInicial(prendaVigente: null, docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDecisionRequerida);
    }

    [Fact]
    public void MatriculaInicial_ConSolicitarSinDocumento_ExigeDocumento()
    {
        // AC3 (rama documento): "solicitar" sin el adjunto de soporte bloquea por documento —el
        // acreedor ni se evalúa porque el gate corta en el primer requisito insatisfecho.
        PrendaGate.EvaluateMatriculaInicial(Prenda(PrendaDecision.Solicitar), docTipos: [])
            .Should().Be(TramiteEstadoErrores.PrendaDocumentoRequerido);
    }

    [Fact]
    public void MatriculaInicial_ConSolicitarDocumentoSinAcreedor_ExigeAcreedor()
    {
        // AC3 (rama acreedor): con el documento adjunto pero sin acreedor diligenciado, el faltante
        // pasa a ser el acreedor (HU #11591 también aplica en matrícula por el mismo núcleo).
        PrendaGate.EvaluateMatriculaInicial(
                PrendaConAcreedor(PrendaDecision.Solicitar, acreedorNombre: null, acreedorDocumento: null),
                docTipos: ["prenda_solicitud"])
            .Should().Be(TramiteEstadoErrores.PrendaAcreedorRequerido);
    }

    [Fact]
    public void MatriculaInicial_ConRegistrarDocumentoYAcreedorCompletos_Avanza()
    {
        PrendaGate.EvaluateMatriculaInicial(
                PrendaConAcreedor(PrendaDecision.Registrar, "Banco X", "900123456"),
                docTipos: ["prenda_registro"])
            .Should().BeNull();
    }

    [Fact]
    public void MatriculaInicial_ConOmitir_AvanzaSinDocumento()
    {
        // "omitir" = asumo el riesgo: satisface el gate sin documento ni acreedor.
        PrendaGate.EvaluateMatriculaInicial(Prenda(PrendaDecision.Omitir), docTipos: [])
            .Should().BeNull();
    }

    [Fact]
    public void Traspaso_ConSemaforoGreenYSinPrendaVigente_NoRegresiona()
    {
        // AC4 (no regresión) — R10/HU #10597: fuera de warn, `Evaluate` (traspaso) sigue sin bloquear
        // aunque no haya prenda vigente registrada. El bloqueo duro de la HU #11592 es EXCLUSIVO de
        // `EvaluateMatriculaInicial`; `Evaluate` conserva su comportamiento previo.
        PrendaGate.Evaluate(esTraspaso: true, hasGravamenWarn: false, prendaVigente: null, docTipos: [])
            .Should().BeNull();
    }
}
