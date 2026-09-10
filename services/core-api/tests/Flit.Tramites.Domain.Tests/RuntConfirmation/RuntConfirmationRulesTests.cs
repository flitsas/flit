using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.RuntConfirmation;

/// <summary>
/// HU #12308 (Feature #12276) — motor de confirmación sobre el historial de solicitudes del RUNT.
/// Los casos son las capturas REALES de Kyverum y Verifik del 2026-09-10 (QZU024, LKO462, PUO271,
/// QOO862, QYV381): los fixtures recortados del repo no traen <c>solicitudes[]</c> y engañan.
/// </summary>
public sealed class RuntConfirmationRulesTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuntConfirmation", "Fixtures", name));

    private static RuntVehicleSnapshot Snap(string fixture) => RuntVehicleSnapshotParser.Parse(Fixture(fixture));

    private static RuntConfirmationInput Input(
        string type,
        ProcedureFamily family,
        DateOnly cutoff,
        RuntVehicleSnapshot? primary,
        RuntVehicleSnapshot? seller = null,
        RuntVehicleSnapshot? baseline = null,
        string? expectedPlate = null,
        string? ot = null) =>
        new(type, family, cutoff, primary, seller, baseline, expectedPlate, ot);

    // ── AC1: solicitud autorizada posterior a la radicación confirma ──────────────────

    [Fact]
    public void AC1_CambioColor_ConSolicitudAutorizadaPosterior_Confirma_YElMotivoCitaLaSolicitud()
    {
        var d = RuntConfirmationRules.Evaluate(Input(
            "CAMBIO_COLOR", ProcedureFamily.Otros, new DateOnly(2026, 9, 1), Snap("kyverum-cambio-color-QZU024.json")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("302345557").And.Contain("2026-09-08").And.Contain("AUTORIZADA").And.Contain("STRIA TTEyTTO BELLO");
        d.RuleVersion.Should().Be("confirmacion-v1");
    }

    // ── AC2: el historial viejo no confirma ────────────────────────────────────────────

    [Fact]
    public void AC2_Traspaso_ConSoloTraspasosAnterioresALaRadicacion_QuedaPendiente()
    {
        // PUO271 tiene TRASPASO AUTORIZADA el 11/06/2026: un traspaso radicado el 01/08 no puede apoyarse en él.
        var comprador = Snap("verifik-cambio-carroceria-PUO271.json");
        var d = RuntConfirmationRules.Evaluate(Input(
            "TRASPASO_STANDARD", ProcedureFamily.Traspaso, new DateOnly(2026, 8, 1), comprador, seller: RuntVehicleSnapshot.NotFound("verifik")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Pending);
        d.Reason.Should().Contain("No hay solicitud de TRASPASO posterior a la radicación").And.Contain("anteriores a la radicación");
    }

    // ── AC3: la solicitud más reciente decide ─────────────────────────────────────────

    [Fact]
    public void AC3_Traspaso_RegistradaElDia10YAutorizadaElDia11_LaMasRecienteConfirma()
    {
        var comprador = Snap("verifik-cambio-carroceria-PUO271.json");
        var d = RuntConfirmationRules.Evaluate(Input(
            "TRASPASO_STANDARD", ProcedureFamily.Traspaso, new DateOnly(2026, 6, 9), comprador, seller: RuntVehicleSnapshot.NotFound("verifik")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("295751939").And.Contain("2026-06-11").And.Contain("propiedad transferida");
    }

    [Fact]
    public void AC3_SiLaMasRecienteFueraRegistrada_QuedaPendiente()
    {
        var snapshot = Sintetico(
            ("TRÁMITE TRASPASO, ", "REGISTRADA", "2026-06-10"),
            ("TRÁMITE TRASPASO, ", "AUTORIZADA", "2026-06-05"));

        var d = RuntConfirmationRules.Evaluate(Input(
            "TRASPASO_STANDARD", ProcedureFamily.Traspaso, new DateOnly(2026, 6, 1), snapshot, seller: RuntVehicleSnapshot.NotFound("kyverum")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Pending);
        d.Reason.Should().Contain("REGISTRADA").And.Contain("sin autorizar");
    }

    // ── AC4: rechazo es discrepancia inmediata ────────────────────────────────────────

    [Fact]
    public void AC4_SolicitudMasRecienteRechazada_EsDiscrepancia()
    {
        var snapshot = Sintetico(("TRÁMITE CAMBIO COLOR, ", "RECHAZADA", "2026-09-05"));

        var d = RuntConfirmationRules.Evaluate(Input("CAMBIO_COLOR", ProcedureFamily.Otros, new DateOnly(2026, 9, 1), snapshot));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Discrepancy);
        d.DejaFlag(out var flag).Should().BeTrue();
        flag.Should().Be(RuntConfirmationFlags.Discrepancia);
    }

    // ── AC5: normalización y varios trámites por solicitud ───────────────────────────

    [Fact]
    public void AC5_TramitesRealizadosConVariosSegmentos_CoincidePorSegmentoNormalizado()
    {
        // QOO862: "TRÁMITE CAMBIO COLOR, TRÁMITE TRANSFORMACIÓN, " el 16/06/2026.
        var d = RuntConfirmationRules.Evaluate(Input(
            "CAMBIO_COLOR", ProcedureFamily.Otros, new DateOnly(2026, 6, 10), Snap("kyverum-cambio-combustible-QOO862.json")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("296090366");
    }

    [Fact]
    public void AC5_RevisionTecnicoMecanicaEnMinusculas_NoCoincideConNingunTipoDelAlcance()
    {
        RuntText.SplitTramites("Tramite revision tecnico mecanica, ").Should().Equal("REVISION TECNICO MECANICA");
        RuntText.SplitTramites("TRÁMITE CAMBIO COLOR, TRÁMITE TRANSFORMACIÓN, ").Should().Equal("CAMBIO COLOR", "TRANSFORMACION");

        foreach (var eq in RuntProcedureEquivalence.All)
            new RuntSolicitud("1", new DateOnly(2026, 6, 30), "APROBADA", "Tramite revision tecnico mecanica, ", "IVESUR")
                .Contiene(eq.RuntTramite).Should().BeFalse(eq.ProcedureTypeCode);
    }

    // ── AC6: TRANSFORMACIÓN es N:1 y exige refuerzo contra el snapshot ────────────────

    [Fact]
    public void AC6_ConversionCombustible_ConCambioDeCampo_Confirma_YCitaElCambio()
    {
        var actual = Snap("kyverum-cambio-combustible-QOO862.json"); // tipoCombustible = GAS GASOL
        var baseline = actual with { TipoCombustible = "DIESEL" };

        var d = RuntConfirmationRules.Evaluate(Input(
            "CONVERSION_COMBUSTIBLE", ProcedureFamily.Otros, new DateOnly(2026, 6, 10), actual, baseline: baseline));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("TRANSFORMACION").And.Contain("DIESEL → GAS GASOL");
    }

    [Fact]
    public void AC6_ConversionCombustible_SinCambioDeCampo_QuedaPendienteAunqueHayaTransformacion()
    {
        var actual = Snap("kyverum-cambio-combustible-QOO862.json");
        var baseline = actual with { TipoCombustible = "GAS GASOL" };

        var d = RuntConfirmationRules.Evaluate(Input(
            "CONVERSION_COMBUSTIBLE", ProcedureFamily.Otros, new DateOnly(2026, 6, 10), actual, baseline: baseline));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Pending);
        d.Reason.Should().Contain("sigue en GAS GASOL");
    }

    [Fact]
    public void AC6_SinSnapshot_ConfirmaSoloPorElTramiteRunt_YLoDice()
    {
        var d = RuntConfirmationRules.Evaluate(Input(
            "CAMBIO_CARROCERIA", ProcedureFamily.Otros, new DateOnly(2026, 8, 1), Snap("kyverum-cambio-carroceria-LKO462.json")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("300426411").And.Contain("sin contraste de campo");
    }

    // ── AC7: inscripción de prenda ────────────────────────────────────────────────────

    [Fact]
    public void AC7_PrendaInscripcion_ConInscripcionAlertaYGarantia_Confirma_YCitaAlAcreedor()
    {
        var d = RuntConfirmationRules.Evaluate(Input(
            "PRENDA_INSCRIPCION", ProcedureFamily.Otros, new DateOnly(2026, 8, 25), Snap("kyverum-inscripcion-prenda-QYV381.json")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("INSCRIPCION ALERTA").And.Contain("2026-09-03").And.Contain("BANCOLOMBIA");
    }

    // ── AC8: matrícula por VIN ────────────────────────────────────────────────────────

    [Fact]
    public void AC8_MatriculaNueva_PorVin_Confirma_YElMotivoIncluyeLaPlacaAsignada()
    {
        // QZU024: MATRÍCULA INICIAL AUTORIZADA el 31/08/2026, placa QZU024, ACTIVO.
        var d = RuntConfirmationRules.Evaluate(Input(
            "MATRICULA_NUEVA", ProcedureFamily.Matriculas, new DateOnly(2026, 8, 28), Snap("kyverum-cambio-color-QZU024.json"), expectedPlate: "QZU024"));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("MATRICULA INICIAL").And.Contain("placa asignada QZU024").And.Contain("ACTIVO");
    }

    [Fact]
    public void AC8_MatriculaNueva_VehiculoNoEncontrado_QuedaPendiente()
    {
        var d = RuntConfirmationRules.Evaluate(Input(
            "MATRICULA_NUEVA", ProcedureFamily.Matriculas, new DateOnly(2026, 8, 28), RuntVehicleSnapshot.NotFound("kyverum")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Pending);
    }

    // ── AC9: traspaso con dos consultas ───────────────────────────────────────────────

    [Fact]
    public void AC9_AmbasResponden_RegistraLaAnomalia_YElHistorialDecide()
    {
        var snap = Snap("verifik-cambio-carroceria-PUO271.json");
        var d = RuntConfirmationRules.Evaluate(Input(
            "TRASPASO_STANDARD", ProcedureFamily.Traspaso, new DateOnly(2026, 6, 9), snap, seller: snap));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
        d.Reason.Should().Contain("Anomalía").And.Contain("copropiedad");
    }

    [Fact]
    public void AC9_NingunaResponde_QuedaPendienteConLaAnomalia()
    {
        var d = RuntConfirmationRules.Evaluate(Input(
            "TRASPASO_STANDARD", ProcedureFamily.Traspaso, new DateOnly(2026, 6, 9),
            RuntVehicleSnapshot.NotFound("verifik"), seller: RuntVehicleSnapshot.NotFound("verifik")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Pending);
        d.Reason.Should().Contain("ninguna de las dos consultas");
    }

    // ── AC10: no verificable ──────────────────────────────────────────────────────────

    [Fact]
    public void AC10_MostrarSolicitudesDistintoDeSi_EsNoVerificable()
    {
        var snap = Snap("kyverum-cambio-color-QZU024.json") with { MostrarSolicitudes = "" };
        var d = RuntConfirmationRules.Evaluate(Input("CAMBIO_COLOR", ProcedureFamily.Otros, new DateOnly(2026, 9, 1), snap));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Unverifiable);
        d.DejaFlag(out var flag).Should().BeTrue();
        flag.Should().Be(RuntConfirmationFlags.NoVerificable);
    }

    [Theory]
    [InlineData("BLINDAJE")]
    [InlineData("DUPLICADO_TARJETA")]
    [InlineData("CAMBIO_LOCATARIO")]
    [InlineData("RADICADO_CUENTA")]
    [InlineData("TRASLADO_CUENTA")]
    [InlineData("LEVANTAMIENTO_PRENDA")]
    public void AC10_TipoSinEquivalente_EsNoVerificableConMotivo(string type)
    {
        var d = RuntConfirmationRules.Evaluate(Input(type, ProcedureFamily.Otros, new DateOnly(2026, 9, 1), Snap("kyverum-cambio-color-QZU024.json")));

        d.Verdict.Should().Be(RuntConfirmationVerdict.Unverifiable);
        d.Reason.Should().Contain("sin equivalente definido");
    }

    // ── AC11: mismo veredicto en Kyverum y Verifik ────────────────────────────────────

    [Theory]
    [InlineData("CAMBIO_COLOR", "kyverum-cambio-color-QZU024.json", "verifik-cambio-color-QZU024.json", "2026-09-01")]
    [InlineData("CONVERSION_COMBUSTIBLE", "kyverum-cambio-combustible-QOO862.json", "verifik-cambio-combustible-QOO862.json", "2026-06-10")]
    [InlineData("PRENDA_INSCRIPCION", "kyverum-inscripcion-prenda-QYV381.json", "verifik-inscripcion-prenda-QYV381.json", "2026-08-25")]
    public void AC11_LaMismaPlacaEnKyverumYVerifik_DaElMismoVeredicto(string type, string kyverum, string verifik, string cutoff)
    {
        var day = DateOnly.Parse(cutoff, System.Globalization.CultureInfo.InvariantCulture);

        var k = RuntConfirmationRules.Evaluate(Input(type, ProcedureFamily.Otros, day, Snap(kyverum)));
        var v = RuntConfirmationRules.Evaluate(Input(type, ProcedureFamily.Otros, day, Snap(verifik)));

        k.Verdict.Should().Be(v.Verdict);
        k.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
    }

    [Fact]
    public void AC11_LasFechasSeComparanPorDia()
    {
        RuntText.ParseDay("2026-09-08T15:49:49.000-05:00").Should().Be(new DateOnly(2026, 9, 8));
        RuntText.ParseDay("08/09/2026").Should().Be(new DateOnly(2026, 9, 8));
        // Radicado el mismo día de la solicitud: cuenta (≥, no >).
        var d = RuntConfirmationRules.Evaluate(Input("CAMBIO_COLOR", ProcedureFamily.Otros, new DateOnly(2026, 9, 8), Snap("verifik-cambio-color-QZU024.json")));
        d.Verdict.Should().Be(RuntConfirmationVerdict.Confirmed);
    }

    // ── Parser: mismos campos desde ambos proveedores ─────────────────────────────────

    [Fact]
    public void Parser_LeeLosMismosCamposDeKyverumYVerifik()
    {
        var k = Snap("kyverum-cambio-color-QZU024.json");
        var v = Snap("verifik-cambio-color-QZU024.json");

        k.ProviderHint.Should().Be("kyverum");
        v.ProviderHint.Should().Be("verifik");
        (k.Placa, k.EstadoAutomotor, k.Color, k.TipoCombustible, k.OrganismoTransito, k.Solicitudes.Count)
            .Should().Be((v.Placa, v.EstadoAutomotor, v.Color, v.TipoCombustible, v.OrganismoTransito, v.Solicitudes.Count));
        k.ExponeSolicitudes.Should().BeTrue();
    }

    [Fact]
    public void Parser_KyverumOkFalse_Y_DocumentoSintetico404_SonNoEncontrado()
    {
        RuntVehicleSnapshotParser.Parse("""{"ok":false,"error":{"message":"no encontrado"}}""").Outcome.Should().Be(RuntVehicleOutcome.NotFound);
        RuntVehicleSnapshotParser.Parse(RuntVehicleSnapshotParser.NotFoundPayload("verifik", 404, "Vehicle not found")).Outcome.Should().Be(RuntVehicleOutcome.NotFound);
        RuntVehicleSnapshotParser.Parse("no es json").Outcome.Should().Be(RuntVehicleOutcome.Unreadable);
        RuntVehicleSnapshotParser.Parse(null).Outcome.Should().Be(RuntVehicleOutcome.Unreadable);
    }

    [Fact]
    public void Parser_GarantiasVerifik_LeeGarantiasFavorDe()
    {
        var v = Snap("verifik-inscripcion-prenda-QYV381.json");
        v.Garantias.Should().ContainSingle(g => g.Acreedor == "BANCOLOMBIA S.A." && g.FechaInscripcion == new DateOnly(2026, 9, 3));
    }

    private static RuntVehicleSnapshot Sintetico(params (string Tramites, string Estado, string Fecha)[] solicitudes) =>
        Snap("kyverum-cambio-color-QZU024.json") with
        {
            Solicitudes = solicitudes
                .Select((s, i) => new RuntSolicitud((100 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), RuntText.ParseDay(s.Fecha), s.Estado, s.Tramites, "STRIA TTEyTTO BELLO"))
                .ToList(),
        };
}
