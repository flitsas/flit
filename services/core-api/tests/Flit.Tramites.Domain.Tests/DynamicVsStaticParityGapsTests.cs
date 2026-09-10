using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// ADR-0050 — paridad entre <see cref="DynamicGateEvaluator"/> y el camino estático del wizard
/// (<c>MatriculaGates</c> / <c>TraspasoGates</c>).
/// <para>Esta clase nació como caracterización de cuatro brechas que impedían retirar la rama
/// estática. Cerradas, cada test pasó a afirmar la equivalencia: son la red que impide que el motor
/// dinámico vuelva a divergir del comportamiento que el wizard tenía antes de unificarse.</para>
/// </summary>
public sealed class DynamicVsStaticParityGapsTests
{
    private static ProcedureTypeGateProfile MatriculaProfile() => new()
    {
        EntryMode = ProcedureTypeGateProfile.EntryModeVin,
        RequiresBuyer = true,
        RequiresBiometrics = true,
        BiometricActors = ["BUYER"],
        RequiresSignature = true,
    };

    private static List<DynamicWizardStep> MatriculaSteps() =>
    [
        new("consulta_vin", ProcedureSectionTypes.VehicleQuery),
        new("comprador", ProcedureSectionTypes.ActorForm) { SectionCodes = ["COMPRADOR"] },
        new("documentos", ProcedureSectionTypes.DocumentChecklist),
        new("identidad", ProcedureSectionTypes.Biometric),
        new("fur", ProcedureSectionTypes.SignatureFur),
    ];

    private static DynamicWizardContext DatosCompletos() => new()
    {
        VehiculoConsultado = true,
        DocumentosCompletos = true,
        HasBuyer = true,
        BuyerRuntConsultado = true,
    };

    [Fact]
    public void LosPasosDiferidosSeBloqueanMientrasFaltanDatos()
    {
        // Paridad con BuildMatricula: identidad y FUR quedan 'locked' hasta que los datos están
        // completos; los pasos de datos NUNCA se bloquean en cascada, se muestran incompletos para
        // que el operador vea todo lo que le falta.
        var state = DynamicGateEvaluator.Evaluate(
            MatriculaProfile(), MatriculaSteps(), new DynamicWizardContext());

        state.Steps[3].Status.Should().Be("locked", "identidad es diferido y los datos no están listos");
        state.Steps[4].Status.Should().Be("locked", "FUR es diferido y los datos no están listos");
        state.Steps[1].Status.Should().Be("incomplete", "los pasos de datos no se bloquean en cascada");
        state.Steps[2].Status.Should().Be("incomplete");
    }

    [Fact]
    public void ConDatosCompletos_LosDiferidosMuestranSuEstadoReal()
    {
        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), DatosCompletos());

        state.Steps[3].Status.Should().Be("incomplete", "ya es alcanzable, con la biometría pendiente");
        state.Steps[3].Reasons.Should().Contain(DynamicGateEvaluator.IdentidadPendiente);
        state.Steps[3].Reasons.Should().Contain(DynamicGateEvaluator.PendienteBiometria);
    }

    [Theory]
    [InlineData(ProcedureTypeGateProfile.EntryModeVin, DynamicGateEvaluator.VinPendiente)]
    [InlineData(ProcedureTypeGateProfile.EntryModePlate, DynamicGateEvaluator.ConsultaPendiente)]
    public void LaConsultaDelVehiculoDiceQueIdentificadorFalta(string entryMode, string expected)
    {
        // El estático emite 'vin_pendiente' en matrícula y 'consulta_pendiente' en traspaso.
        var profile = new ProcedureTypeGateProfile { EntryMode = entryMode };
        List<DynamicWizardStep> steps = [new("consulta", ProcedureSectionTypes.VehicleQuery)];

        var state = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext());

        state.Steps[0].Reasons.Should().Contain(expected);
        state.Steps[0].Reasons.Should().Contain(DynamicGateEvaluator.VehiculoNoConsultado,
            "el código genérico se conserva para quien ya lo escuchaba");
    }

    [Fact]
    public void ElFurNoBloqueaLaRadicacion()
    {
        // El FUR se genera al validar la identidad (HU #10349): es diferido y no veta el paso
        // borrador→preparado. Incluirlo como blocker impedía radicar expedientes completos.
        var ctx = DatosCompletos() with
        {
            BiometricsApproved = new HashSet<string> { "BUYER" },
            FurGenerado = false,
        };

        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), ctx);

        state.Blockers.Should().NotContain(DynamicGateEvaluator.FurPendiente);
        state.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void ElSemaforoRojoBloquea_SalvoRiesgoAceptado()
    {
        var rojo = DatosCompletos() with
        {
            BiometricsApproved = new HashSet<string> { "BUYER" },
            PreflightRed = true,
        };

        var bloqueado = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), rojo);
        var aceptado = DynamicGateEvaluator.Evaluate(
            MatriculaProfile(), MatriculaSteps(), rojo with { RiesgoAceptado = true });

        bloqueado.Blockers.Should().Contain(DynamicGateEvaluator.PreflightRedBlocker);
        aceptado.Blockers.Should().NotContain(DynamicGateEvaluator.PreflightRedBlocker);
        aceptado.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void LosBloqueosDurosDelPreflightAlcanzanAlPasoDeDocumentos()
    {
        // MatriculaGates repite los bloqueos duros en el gate de documentos: sin vehículo verificado
        // no se avanza, y no se levanta aceptando el riesgo.
        var ctx = DatosCompletos() with { PreflightProviderError = true };

        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), ctx);

        state.Steps[2].Status.Should().NotBe("complete");
        state.Steps[2].Reasons.Should().Contain(DynamicGateEvaluator.PreflightProviderError);
    }

    [Fact]
    public void CadaPasoDeActoresExigeSoloSuActor()
    {
        // Un traspaso tiene dos pasos actor_form. Sin el código de sección, ambos exigían vendedor Y
        // comprador, así que el paso del vendedor nunca se completaba.
        var profile = new ProcedureTypeGateProfile
        {
            EntryMode = ProcedureTypeGateProfile.EntryModePlate,
            RequiresSeller = true,
            RequiresBuyer = true,
        };
        List<DynamicWizardStep> steps =
        [
            new("vendedor", ProcedureSectionTypes.ActorForm) { SectionCodes = ["VENDEDOR"] },
            new("comprador", ProcedureSectionTypes.ActorForm) { SectionCodes = ["COMPRADOR"] },
        ];
        var ctx = new DynamicWizardContext { HasSeller = true, SellerRuntConsultado = true };

        var state = DynamicGateEvaluator.Evaluate(profile, steps, ctx);

        state.Steps[0].Status.Should().Be("complete", "el vendedor está listo");
        state.Steps[1].Status.Should().Be("incomplete", "el comprador todavía no");
        state.Steps[1].Reasons.Should().Contain(DynamicGateEvaluator.CompradorPendiente);
    }

    [Fact]
    public void UnActorSinDatosDeContactoNoCompletaSuPaso()
    {
        // HU #11593 / ParteCompletaRule — existir y tener RUNT no basta: sin los seis datos de
        // contacto el organismo no puede notificar. El motor dinámico solo miraba existencia y RUNT,
        // así que daba por cerrado un paso que el estático dejaba incompleto.
        var profile = new ProcedureTypeGateProfile
        {
            EntryMode = ProcedureTypeGateProfile.EntryModeVin,
            RequiresBuyer = true,
        };
        List<DynamicWizardStep> steps =
            [new("comprador", ProcedureSectionTypes.ActorForm) { SectionCodes = ["COMPRADOR"] }];

        var incompleto = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext
        {
            HasBuyer = true,
            BuyerRuntConsultado = true,
            BuyerCompleto = false,
        });

        var completo = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext
        {
            HasBuyer = true,
            BuyerRuntConsultado = true,
            BuyerCompleto = true,
        });

        incompleto.Steps[0].Status.Should().Be("incomplete");
        incompleto.Steps[0].Reasons.Should().Contain(DynamicGateEvaluator.CompradorPendiente);
        completo.Steps[0].Status.Should().Be("complete");
    }
}
