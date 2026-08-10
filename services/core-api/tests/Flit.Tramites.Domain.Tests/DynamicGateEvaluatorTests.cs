using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-06 (CFD-09) — evaluador dinámico PURO del wizard. Cubre BE-06-AC-03 (paridad
/// funcional con la config de matrícula: canSubmit + blockers), AC-04 (puro, sin IO).
/// </summary>
public sealed class DynamicGateEvaluatorTests
{
    // Config equivalente a MATRICULA_INICIAL: VIN + comprador + docs + biometría[BUYER] + firma.
    private static ProcedureTypeGateProfile MatriculaProfile() => new()
    {
        EntryMode = "VIN",
        RequiresBuyer = true,
        RequiresBiometrics = true,
        BiometricActors = ["BUYER"],
        RequiresSignature = true,
    };

    private static List<DynamicWizardStep> MatriculaSteps() =>
    [
        new("consulta", "vehicle_query"),
        new("documentos", "document_checklist"),
        new("comprador", "actor_form"),
        new("identidad", "biometric"),
        new("fur", "signature_fur"),
    ];

    private static readonly IReadOnlySet<string> NoActors = new HashSet<string>();

    [Fact]
    public void Evaluate_EmptyInstance_AllStepsIncomplete_CannotSubmit()
    {
        var ctx = new DynamicWizardContext(); // nada hecho

        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), ctx);

        state.Steps.Should().HaveCount(5);
        state.Steps[0].SectionType.Should().Be("vehicle_query");
        state.Steps[0].Status.Should().Be("incomplete");
        state.Steps[0].Reasons.Should().Contain(DynamicGateEvaluator.VehiculoNoConsultado);
        state.CanSubmit.Should().BeFalse();
        state.Blockers.Should().Contain(DynamicGateEvaluator.DocumentosIncompletos);
        state.Blockers.Should().Contain(DynamicGateEvaluator.IdentidadNoAprobada);
        state.Blockers.Should().Contain(DynamicGateEvaluator.FurPendiente);
    }

    [Fact]
    public void Evaluate_VehiculoNoEncontrado_VehicleQueryIncompleto_YBloqueaRadicacion()
    {
        // Paridad con el camino estático (Matricula/TraspasoGates): el RUNT respondió y el vehículo
        // NO existe → bloqueo DURO, aunque la consulta se haya realizado (VehiculoConsultado=true).
        var ctx = new DynamicWizardContext
        {
            VehiculoConsultado = true,
            PreflightVehiculoNoEncontrado = true,
        };

        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), ctx);

        state.Steps[0].SectionType.Should().Be("vehicle_query");
        state.Steps[0].Status.Should().Be("incomplete");
        state.Steps[0].Reasons.Should().Contain(DynamicGateEvaluator.VehiculoNoEncontrado);
        state.CanSubmit.Should().BeFalse();
        state.Blockers.Should().Contain(DynamicGateEvaluator.VehiculoNoEncontrado);
    }

    [Fact]
    public void Evaluate_DataCompleteButNoBiometricNorFur_CannotSubmit()
    {
        var ctx = new DynamicWizardContext
        {
            VehiculoConsultado = true,
            DocumentosCompletos = true,
            HasBuyer = true,
            BuyerRuntConsultado = true,
            // sin biometría ni FUR
        };

        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), ctx);

        state.Steps[0].Status.Should().Be("complete"); // vehicle_query
        state.Steps[1].Status.Should().Be("complete"); // document_checklist
        state.Steps[2].Status.Should().Be("complete"); // actor_form
        state.Steps[3].Status.Should().Be("incomplete"); // biometric
        state.CanSubmit.Should().BeFalse();
        state.Blockers.Should().Contain(DynamicGateEvaluator.IdentidadNoAprobada);
        state.Blockers.Should().Contain(DynamicGateEvaluator.FurPendiente);
    }

    [Fact]
    public void Evaluate_AllComplete_CanSubmit()
    {
        var ctx = new DynamicWizardContext
        {
            VehiculoConsultado = true,
            DocumentosCompletos = true,
            HasBuyer = true,
            BuyerRuntConsultado = true,
            BiometricsApproved = new HashSet<string> { "BUYER" },
            FurGenerado = true,
        };

        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), ctx);

        state.Steps.Should().OnlyContain(s => s.Status == "complete");
        state.Blockers.Should().BeEmpty();
        state.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ActorForm_MissingBuyerRunt_Incomplete()
    {
        var ctx = new DynamicWizardContext { VehiculoConsultado = true, DocumentosCompletos = true, HasBuyer = true, BuyerRuntConsultado = false };

        var state = DynamicGateEvaluator.Evaluate(MatriculaProfile(), MatriculaSteps(), ctx);

        state.Steps[2].Status.Should().Be("incomplete");
        state.Steps[2].Reasons.Should().Contain(DynamicGateEvaluator.CompradorPendiente);
    }

    [Fact]
    public void Evaluate_DocumentChecklist_RequiredMissing_Blocks_DummyIgnored()
    {
        // AC-03/AC-04 de BE-04 integrados: los requisitos documentales bloquean el paso de docs.
        var profile = new ProcedureTypeGateProfile();
        var steps = new List<DynamicWizardStep> { new("documentos", "document_checklist") };
        var ctx = new DynamicWizardContext
        {
            DocumentRequirements =
            [
                new DocumentRequirementItem("CEDULA", IsRequired: true, IsDummy: false),
                new DocumentRequirementItem("PROMESA", IsRequired: true, IsDummy: true),
            ],
            UploadedDocumentCodes = new HashSet<string>(), // nada cargado
        };

        var state = DynamicGateEvaluator.Evaluate(profile, steps, ctx);

        state.Steps[0].Status.Should().Be("incomplete");
        state.Blockers.Should().Contain(DynamicGateEvaluator.DocumentosIncompletos);
    }

    [Fact]
    public void Evaluate_Commercial_RequiresValue_BlocksWhenZero()
    {
        var profile = new ProcedureTypeGateProfile { RequiresCommercialValue = true };
        var steps = new List<DynamicWizardStep> { new("comercial", "commercial") };

        var zero = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext { ValorVenta = 0m });
        zero.Steps[0].Status.Should().Be("incomplete");
        zero.Steps[0].Reasons.Should().Contain(DynamicGateEvaluator.ValorComercialPendiente);

        var withValue = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext { ValorVenta = 1000m });
        withValue.Steps[0].Status.Should().Be("complete");
    }

    [Fact]
    public void Evaluate_PlateRequestStep_BlocksUntilCompleted()
    {
        var profile = new ProcedureTypeGateProfile { RequiresPlateRequest = true };
        var steps = new List<DynamicWizardStep> { new("placa", "plate_request") };

        var pending = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext { PlateRequestCompleted = false });
        pending.Steps[0].Status.Should().Be("incomplete");
        pending.Steps[0].Reasons.Should().Contain(PlateRequestGate.PlateRequestPending);

        var done = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext { PlateRequestCompleted = true });
        done.Steps[0].Status.Should().Be("complete");
    }

    [Fact]
    public void CanSubmitBlockers_ReflectsDocsBiometricsAndSignature()
    {
        // AC-06: SubmitGate delega aquí para tipos dinámicos.
        var profile = MatriculaProfile();

        var blocked = DynamicGateEvaluator.CanSubmitBlockers(profile, new DynamicWizardContext());
        blocked.Should().Contain(DynamicGateEvaluator.DocumentosIncompletos);
        blocked.Should().Contain(DynamicGateEvaluator.IdentidadNoAprobada);

        var ok = DynamicGateEvaluator.CanSubmitBlockers(profile, new DynamicWizardContext
        {
            DocumentosCompletos = true,
            BiometricsApproved = new HashSet<string> { "BUYER" },
            FurGenerado = true,
        });
        ok.Should().BeEmpty();
    }
}
