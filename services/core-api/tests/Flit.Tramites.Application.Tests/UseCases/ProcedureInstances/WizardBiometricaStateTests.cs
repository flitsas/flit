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
/// Cableado del estado real de la biométrica (Slice 6) en el wizard server-driven:
/// matrícula paso 4 (identidad) y traspaso paso 6 (FUR, ambas partes).
/// </summary>
public sealed class WizardBiometricaStateTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetWizardStateHandler _handler;
    private readonly SimularBiometriaHandler _simular;

    public WizardBiometricaStateTests()
    {
        _handler = new GetWizardStateHandler(_repo);
        _simular = new SimularBiometriaHandler(_repo);
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
    private static ProcedureInstanceBiometricValidation Biometria(string? parte, string estado, string documento = "777") =>
        new()
        {
            Id = Guid.NewGuid(),
            PartyRole = parte,
            Status = estado,
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = documento,
            Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void Setup(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);

    // HU #11593 — contacto obligatorio (ciudad, dirección, teléfono) en los gates de actor: estos
    // fixtures representan actores completos por defecto para no acoplar los tests de biométrica
    // (que ejercitan otra regla) a esta exigencia nueva.
    private static ProcedureInstanceActor Comprador(string doc = "777") =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = "Maria",
            Email = "maria@x.com",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize("Bogotá", "Calle 1 # 2-3", null),
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
            Phone = "3007654321",
            Metadata = ActorMetadataReader.Serialize("Medellín", "Carrera 4 # 5-6", null),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstancePreflightSnapshot Preflight(string overall) =>
        new() { Id = Guid.NewGuid(), Overall = overall, Checks = "[]", CreatedAt = DateTimeOffset.UtcNow };

    private static ProcedureInstanceAttachment Doc(string tipo) =>
        new() { Id = Guid.NewGuid(), Tipo = tipo, Filename = $"{tipo}.pdf", StoragePath = $"x/{tipo}", UploadedAt = DateTimeOffset.UtcNow };

    /// <summary>Agrega VIN + docs obligatorios de matrícula (factura/aduana/impronta).</summary>
    private static void AgregarVinYDocsMatricula(ProcedureInstance instance)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.Attachments.Add(Doc("factura"));
        instance.Attachments.Add(Doc("aduana"));
        instance.Attachments.Add(Doc("impronta"));
    }

    /// <summary>Completa pasos 1-3 de matrícula (VIN, comprador+RUNT, docs) → Identidad (4) alcanzable.</summary>
    private static void CompletarHastaIdentidadMatricula(ProcedureInstance instance)
    {
        AgregarVinYDocsMatricula(instance);
        instance.Actors.Add(Comprador());
    }

    /// <summary>
    /// Completa pasos 1-4 de traspaso (placa, vendedor, comprador, documentos + valor de venta,
    /// absorbido en Documentos) → Identidad (5) alcanzable.
    /// </summary>
    private static void CompletarHastaIdentidadTraspaso(ProcedureInstance instance)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        // HU #10935 — los documentos gobiernan el paso 4 (tras los actores); se marcan los obligatorios.
        instance.ChecklistEstado =
            "{\"contrato_compraventa\":true,\"impronta\":true,\"soat\":true,\"rtm\":true,\"paz_salvo\":true,\"cedulas\":true}";
        instance.Actors.Add(Vendedor());
        instance.Actors.Add(Comprador("666"));
        instance.PreflightSnapshots.Add(Preflight("green"));
        instance.Commercial = new ProcedureInstanceCommercial { Id = Guid.NewGuid(), ValorVenta = 100m, CreatedAt = DateTimeOffset.UtcNow };
    }

    // ── Matrícula: paso 4 (identidad) refleja biométrica del comprador (Parte == "comprador") ──
    // Nota cascada: los pasos diferidos solo se evalúan cuando son ALCANZABLES; por eso cada
    // test completa primero los pasos previos (de lo contrario el paso sería 'locked').

    [Fact]
    public async Task Matricula_NoBiometria_IdentidadIncompleteWithReason()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        CompletarHastaIdentidadMatricula(instance); // identidad alcanzable
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s4 = result!.Steps.Single(s => s.Index == 4);
        s4.Status.Should().Be("incomplete");
        s4.Reasons.Should().Contain("identidad_pendiente");
    }

    [Fact]
    public async Task Matricula_BiometriaAprobada_IdentidadFlipsToComplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        CompletarHastaIdentidadMatricula(instance); // identidad alcanzable
        instance.BiometricValidations.Add(Biometria(parte: "comprador", estado: BiometricEstados.Aprobado));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s4 = result!.Steps.Single(s => s.Index == 4);
        s4.Status.Should().Be("complete");
        s4.Reasons.Should().BeEmpty();
    }

    [Fact]
    public async Task Matricula_BiometriaAprobadaDeOtroDocumento_NoCuentaComoIdentidad()
    {
        // Defensa en profundidad (HU #10350): si el gestor cambió de persona y quedó una validación
        // aprobada+vigente de la persona ANTERIOR (documento distinto al del actor actual), el gate de
        // identidad NO la cuenta como verificada — aunque la invalidación del ensure no haya corrido.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        CompletarHastaIdentidadMatricula(instance); // comprador doc "777"
        instance.BiometricValidations.Add(
            Biometria(parte: "comprador", estado: BiometricEstados.Aprobado, documento: "111-persona-anterior"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s4 = result!.Steps.Single(s => s.Index == 4);
        s4.Status.Should().Be("incomplete");
        s4.Reasons.Should().Contain("identidad_pendiente");
    }

    [Fact]
    public async Task Matricula_BiometriaRechazada_IdentidadStillIncomplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        CompletarHastaIdentidadMatricula(instance); // identidad alcanzable
        instance.BiometricValidations.Add(Biometria(parte: "comprador", estado: BiometricEstados.Rechazado));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
    }

    [Fact]
    public async Task Matricula_AfterSimular_IdentidadStep4FlipsToComplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureEntityId = Guid.NewGuid(),
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "999",
            FullName = "Maria",
            Email = "maria@x.com",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize("Bogotá", "Calle 1 # 2-3", null),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        AgregarVinYDocsMatricula(instance); // pasos 1-3 completos → identidad (4) alcanzable
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        Setup(instance);

        // Antes de simular: paso 4 incompleto.
        var (before, _) = await _handler.HandleAsync(instance.Id, instance.TenantId, ct);
        before!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");

        // Simular validación de identidad (mock, sin fotos).
        var (sim, simError) = await _simular.HandleAsync(instance.Id, instance.TenantId, parte: null, ct);
        simError.Should().BeNull();
        sim!.Status.Should().Be(BiometricEstados.Aprobado);

        // Después: paso 4 (identidad) completo.
        var (after, _) = await _handler.HandleAsync(instance.Id, instance.TenantId, ct);
        var s4 = after!.Steps.Single(s => s.Index == 4);
        s4.Status.Should().Be("complete");
        s4.Reasons.Should().BeEmpty();
    }

    // ── Traspaso: paso 5 (Identidad) exige biométrica de AMBAS partes ────────────
    // Paridad con matrícula (2026-08) — antes se evaluaba de forma diferida (solo para mostrar
    // razón) en el paso 6 (FUR); ahora es el gate propio del paso 5, y sin él FUR (6) queda locked.

    [Fact]
    public async Task Traspaso_NoBiometria_IdentidadHasBiometriaReasonAndFurIncomplete()
    {
        // Regla de negocio restituida (2026-08): la identidad pendiente NO atrapa al gestor en el
        // paso 5 — paridad con matrícula (HU #10350). Datos (1-4) completos ⇒ paso 6 (FUR) ALCANZABLE
        // (incomplete, no locked) aunque la biométrica de ambas partes siga pendiente.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        CompletarHastaIdentidadTraspaso(instance); // Identidad (5) alcanzable
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s5 = result!.Steps.Single(s => s.Index == 5);
        s5.Status.Should().Be("incomplete");
        s5.Reasons.Should().Contain(GetWizardStateHandler.PendienteBiometria);

        var s6 = result.Steps.Single(s => s.Index == 6);
        s6.Status.Should().Be("incomplete");
    }

    [Fact]
    public async Task Traspaso_OnlyCompradorAprobado_StillRequiresVendedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        CompletarHastaIdentidadTraspaso(instance); // Identidad (5) alcanzable
        instance.BiometricValidations.Add(Biometria(parte: "comprador", estado: BiometricEstados.Aprobado, documento: "666"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Falta vendedor → biométrica aún pendiente; Identidad (5) no completa, pero FUR (6) sigue
        // ALCANZABLE (incomplete, no locked): la identidad pendiente no bloquea avanzar.
        result!.Steps.Single(s => s.Index == 5).Reasons
            .Should().Contain(GetWizardStateHandler.PendienteBiometria);
        result.Steps.Single(s => s.Index == 6).Status.Should().Be("incomplete");
    }

    [Fact]
    public async Task Traspaso_DatosIncompletos_IdentidadYFurSiguenLocked()
    {
        // La cascada de DATOS (1-4) no se toca: sin documentos obligatorios (paso 4) los pasos
        // diferidos (5 Identidad, 6 FUR) siguen locked, exactamente como antes.
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(Vendedor());
        instance.Actors.Add(Comprador("666"));
        // Sin checklist de documentos ni comercial: el paso 4 (Documentos) no se completa.
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 5).Status.Should().Be("locked");
        result.Steps.Single(s => s.Index == 6).Status.Should().Be("locked");
    }

    [Fact]
    public async Task Traspaso_BothPartesAprobadas_IdentidadCompleteAndFurReachable()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        CompletarHastaIdentidadTraspaso(instance); // Identidad (5) alcanzable
        instance.BiometricValidations.Add(Biometria(parte: "comprador", estado: BiometricEstados.Aprobado, documento: "666"));
        instance.BiometricValidations.Add(Biometria(parte: "vendedor", estado: BiometricEstados.Aprobado, documento: "555"));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s5 = result!.Steps.Single(s => s.Index == 5);
        s5.Status.Should().Be("complete");
        s5.Reasons.Should().BeEmpty();

        // FUR (6) ya no exige biométrica (se movió a Identidad); solo queda pendiente por el FUR.
        var s6 = result.Steps.Single(s => s.Index == 6);
        s6.Reasons.Should().NotContain(GetWizardStateHandler.PendienteBiometria);
        // B12 (HU #10661, ADR-0028): la firma de compraventa ya NO se exige ni aporta pendiente_firma.
        s6.Reasons.Should().NotContain(GetWizardStateHandler.PendienteFirma);
    }
}
