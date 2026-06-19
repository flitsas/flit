using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class WizardStateHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetWizardStateHandler _handler;

    public WizardStateHandlerTests()
    {
        _handler = new GetWizardStateHandler(_repo);
    }

    private static ProcedureInstance Base(string modalidad, string? tipologia = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceActor Actor(string actorType, string doc = "123") =>
        new()
        {
            ActorType = actorType,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = "Persona",
            Email = "p@x.com",
        };

    private static ProcedureInstancePreflightSnapshot Preflight(string overall, string checks = "[]") =>
        new()
        {
            Id = Guid.NewGuid(),
            Overall = overall,
            Checks = checks,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceAttachment Attachment(string tipo) =>
        new() { Id = Guid.NewGuid(), Tipo = tipo, Filename = $"{tipo}.pdf" };

    /// <summary>
    /// Satisface el checklist obligatorio de matrícula inicial: 3 docs por adjunto
    /// (factura, aduana, impronta). SOAT y los demás ítems son opcionales.
    /// </summary>
    private static void CompletarDocsMatricula(ProcedureInstance instance)
    {
        instance.Attachments.Add(Attachment("factura"));
        instance.Attachments.Add(Attachment("aduana"));
        instance.Attachments.Add(Attachment("impronta"));
    }

    private void Setup(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<CancellationToken>()).Returns(instance);

    // ── 404 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    // ── Conteo de pasos por modalidad ─────────────────────────────────────────

    [Fact]
    public async Task Get_Matricula_Has5Steps()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Modalidad.Should().Be("matricula_inicial");
        result.TotalSteps.Should().Be(5);
        result.Steps.Should().HaveCount(5);
    }

    [Fact]
    public async Task Get_Traspaso_Has6Steps()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Modalidad.Should().Be("traspaso");
        result.TotalSteps.Should().Be(6);
        result.Steps.Should().HaveCount(6);
    }

    // ── Pasos diferidos (biométrica / firma) ──────────────────────────────────

    [Fact]
    public async Task Get_Matricula_DeferredStepsAreIncompleteWithReasons()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var identidad = result!.Steps.Single(s => s.Index == 4);
        identidad.Status.Should().Be("incomplete");
        identidad.Reasons.Should().Contain(GetWizardStateHandler.PendienteBiometria);

        // FUR matrícula (Slice 7): completa al GENERAR el FUR; NO requiere firma. Sin FUR generado
        // el paso queda incomplete con 'fur_pendiente' (antes diferido con 'pendiente_firma').
        var fur = result.Steps.Single(s => s.Index == 5);
        fur.Status.Should().Be("incomplete");
        fur.Reasons.Should().Contain(GetWizardStateHandler.FurPendiente);
        fur.Reasons.Should().NotContain(GetWizardStateHandler.PendienteFirma);
    }

    [Fact]
    public async Task Get_Traspaso_FurStepDefersBiometricAndFirma()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var fur = result!.Steps.Single(s => s.Index == 6);
        fur.Status.Should().Be("incomplete");
        fur.Reasons.Should().Contain(GetWizardStateHandler.PendienteBiometria);
        fur.Reasons.Should().Contain(GetWizardStateHandler.PendienteFirma);
    }

    // ── Mapeo persistencia → GateContext (pasos completan al llenarlos) ───────

    [Fact]
    public async Task Get_Matricula_VinPresent_Step1Complete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 1).Status.Should().Be("complete");
    }

    [Fact]
    public async Task Get_Matricula_NoVin_Step1IncompleteWithReason()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s1 = result!.Steps.Single(s => s.Index == 1);
        s1.Status.Should().Be("incomplete");
        s1.Reasons.Should().Contain("vin_pendiente");
    }

    [Fact]
    public async Task Get_Traspaso_VendedorMappedToParteAndRunt()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Paso 3 (vendedor) completo: parte completa + RUNT consultado (documento presente).
        result!.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        // Paso 4 (comprador) incompleto: sin comprador.
        result.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
    }

    [Fact]
    public async Task Get_Traspaso_ValorVentaMappedFromCommercial()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Paso 5 (comercial) completo gracias a ValorVenta > 0.
        result!.Steps.Single(s => s.Index == 5).Status.Should().Be("complete");
    }

    // ── Blockers / canSubmit ──────────────────────────────────────────────────

    [Fact]
    public async Task Get_PreflightRed_AddsBlockerAndBlocksSubmit()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("red"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Blockers.Should().Contain("preflight_red");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Matricula_AllNonDeferredComplete_CanSubmitTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(Actor("comprador", "777"));
        CompletarDocsMatricula(instance);
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Pasos 1-3 completos (vin, documentos completos, comprador+runt); 4-5 diferidos → no cuentan.
        result!.Steps.Single(s => s.Index == 1).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 2).Status.Should().Be("complete");
        result.Steps.Single(s => s.Index == 3).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }

    // ── Gating ESTRICTO de documentos obligatorios (Slice 4a-fix) ─────────────

    [Fact]
    public async Task Get_Matricula_DocsIncompletos_Step2IncompleteAndBlocksSubmit()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Actors.Add(Actor("comprador", "777"));
        // Sin adjuntos → faltan documentos obligatorios.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s2 = result!.Steps.Single(s => s.Index == 2);
        s2.Status.Should().Be("incomplete");
        s2.Reasons.Should().Contain("documentos_incompletos");
        result.Blockers.Should().Contain("documentos_incompletos");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Traspaso_DocsIncompletos_GlobalBlockerAndStep6Reason()
    {
        var ct = TestContext.Current.CancellationToken;
        // Tipología real → su checklist obligatorio aplica (sin adjuntos = faltan docs).
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Actor("vendedor", "555"));
        instance.Actors.Add(Actor("comprador", "666"));
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Paso 6 está diferido (biométrica/firma); el blocker GLOBAL es lo que veta el submit.
        var s6 = result!.Steps.Single(s => s.Index == 6);
        s6.Reasons.Should().Contain("documentos_incompletos");
        result.Blockers.Should().Contain("documentos_incompletos");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task Get_Traspaso_Step2RekeyedToValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 2).Key.Should().Be("validacion");
    }

    [Fact]
    public async Task Get_Matricula_Step2KeyRemainsDocumentos()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 2).Key.Should().Be("documentos");
    }

    [Fact]
    public async Task Get_Matricula_IncompleteCoreStep_CanSubmitFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial")); // sin vin ni comprador.

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.CanSubmit.Should().BeFalse();
    }
}
