using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Cableado del estado real del paso FUR (Slice 7) en el wizard server-driven:
/// matrícula paso 5 (fur, sin firma) y traspaso paso 6 (biométrica + firma + FUR).
/// </summary>
public sealed class WizardFurStateTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetWizardStateHandler _handler;

    public WizardFurStateTests()
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
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // documento debe coincidir con el del actor de la parte (el gate exige doc-match, HU #10350);
    // por defecto "777" = documento del Comprador() por defecto.
    private static ProcedureInstanceBiometricValidation Bio(string? parte, string documento = "777") =>
        new()
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = documento,
            Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceSignature Firma(string parte) =>
        new()
        {
            Id = Guid.NewGuid(),
            Parte = parte,
            DocTipo = SignatureDocTipos.Compraventa,
            Estado = SignatureEstados.Firmada,
            SolicitadoAt = DateTimeOffset.UtcNow,
            FirmadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceAttachment Fur() =>
        new()
        {
            Id = Guid.NewGuid(),
            Tipo = "fur",
            Filename = "fur.txt",
            Mimetype = "text/plain",
            StoragePath = "x/fur",
            Source = "system",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceAttachment Doc(string tipo) =>
        new() { Id = Guid.NewGuid(), Tipo = tipo, Filename = $"{tipo}.pdf", StoragePath = $"x/{tipo}", UploadedAt = DateTimeOffset.UtcNow };

    /// <summary>Satisface el checklist obligatorio de traspaso (3 docs por adjunto + 3 ítems manuales).</summary>
    private static void CompletarDocsTraspaso(ProcedureInstance instance)
    {
        instance.Attachments.Add(Doc("compraventa"));
        instance.Attachments.Add(Doc("impronta"));
        instance.Attachments.Add(Doc("soat"));
        instance.ChecklistEstado = "{\"rtm\":true,\"paz_salvo\":true,\"cedulas\":true}";
    }

    private void Setup(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);

    private static ProcedureInstanceActor Comprador(string doc = "777") =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = "Maria",
            Email = "maria@x.com",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceActor Vendedor(string doc = "555") =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = "vendedor",
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = "Juan",
            Email = "juan@x.com",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Completa pasos 1-4 de matrícula (VIN, docs, comprador+RUNT, biométrica) → FUR (5) alcanzable.</summary>
    private static void CompletarHastaFurMatricula(ProcedureInstance instance)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.Actors.Add(Comprador());
        instance.Attachments.Add(Doc("factura"));
        instance.Attachments.Add(Doc("aduana"));
        instance.Attachments.Add(Doc("impronta"));
        instance.BiometricValidations.Add(Bio("comprador")); // identidad (4) completa
    }

    /// <summary>Completa pasos 1-5 de traspaso (placa, documentos, vendedor, comprador, preflight, comercial) → FUR (6) alcanzable.</summary>
    private static void CompletarHastaFurTraspaso(ProcedureInstance instance)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        CompletarDocsTraspaso(instance); // los documentos gobiernan el paso 2; sin ellos FUR queda locked.
        instance.Actors.Add(Vendedor());
        instance.Actors.Add(Comprador("666"));
        instance.PreflightSnapshots.Add(new ProcedureInstancePreflightSnapshot { Id = Guid.NewGuid(), Overall = "green", Checks = "[]", CreatedAt = DateTimeOffset.UtcNow });
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
    }

    // ── Matrícula paso 5 (fur) — sin firma ───────────────────────────────────────
    // Nota cascada: los pasos diferidos solo se evalúan cuando son ALCANZABLES; por eso
    // cada test completa primero los pasos previos (de lo contrario el paso sería 'locked').

    [Fact]
    public async Task Matricula_NoFur_Step5IncompleteWithFurPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        CompletarHastaFurMatricula(instance); // FUR (5) alcanzable
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s5 = result!.Steps.Single(s => s.Index == 5);
        s5.Status.Should().Be("incomplete");
        s5.Reasons.Should().Contain(GetWizardStateHandler.FurPendiente);
        s5.Reasons.Should().NotContain(GetWizardStateHandler.PendienteFirma);
    }

    [Fact]
    public async Task Matricula_FurGenerated_Step5Complete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        CompletarHastaFurMatricula(instance); // FUR (5) alcanzable
        instance.Attachments.Add(Fur());
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 5).Status.Should().Be("complete");
    }

    // ── Traspaso paso 6 (fur) — biométrica + firma + FUR ─────────────────────────

    [Fact]
    public async Task Traspaso_PartialState_Step6EmitsRemainingReasons()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        CompletarHastaFurTraspaso(instance); // FUR (6) alcanzable
        instance.BiometricValidations.Add(Bio("comprador", documento: "666"));
        instance.BiometricValidations.Add(Bio("vendedor", documento: "555"));
        // firma + fur faltan
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s6 = result!.Steps.Single(s => s.Index == 6);
        s6.Status.Should().Be("incomplete");
        s6.Reasons.Should().NotContain(GetWizardStateHandler.PendienteBiometria);
        s6.Reasons.Should().Contain(GetWizardStateHandler.PendienteFirma);
        s6.Reasons.Should().Contain(GetWizardStateHandler.FurPendiente);
    }

    [Fact]
    public async Task Traspaso_FullState_Step6FlipsComplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        CompletarHastaFurTraspaso(instance); // FUR (6) alcanzable
        instance.BiometricValidations.Add(Bio("comprador", documento: "666"));
        instance.BiometricValidations.Add(Bio("vendedor", documento: "555"));
        instance.Signatures.Add(Firma("comprador"));
        instance.Signatures.Add(Firma("vendedor"));
        CompletarDocsTraspaso(instance);
        instance.Attachments.Add(Fur());
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s6 = result!.Steps.Single(s => s.Index == 6);
        s6.Status.Should().Be("complete");
        s6.Reasons.Should().BeEmpty();
    }
}
