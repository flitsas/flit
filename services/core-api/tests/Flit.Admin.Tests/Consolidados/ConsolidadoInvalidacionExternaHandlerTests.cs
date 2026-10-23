using Flit.Admin.Application.Companies.Deeds;
using Flit.Admin.Application.Companies.Deeds.CreateDeed;
using Flit.Admin.Application.Companies.Deeds.DeleteDeed;
using Flit.Admin.Application.Companies.Deeds.UpdateDeed;
using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.DeleteLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.UpdateLegalRepresentative;
using Flit.Admin.Application.Companies.SignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.RevokeSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.UpdateSignatureVault;
using Flit.Admin.Application.Consolidados;
using Flit.Admin.Application.OtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentPrecedence.UpdateOtDocumentPrecedence;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.OtDocumentPrecedence;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Consolidados;

/// <summary>
/// HU #12789 (Épica #12760) — los cambios de configuración que viven FUERA del expediente (prelación
/// del OT, escritura / representante legal de la compañía, firmas del baúl) invalidan los
/// consolidados de los trámites afectados a través de <see cref="IConsolidadoInvalidacionMasiva"/>.
/// <para>
/// Aquí se prueba el lado de los casos de uso: cada handler llama al puerto UNA vez, con el alcance
/// correcto (OT + tipo de trámite, o compañía), y SOLO cuando la escritura prosperó. Qué filas alcanza
/// el UPDATE y que sea set-based se prueba en <c>ConsolidadoInvalidacionMasivaTests</c>
/// (Flit.Infrastructure.Tests).
/// </para>
/// Uso de ejemplo:
/// <code>new RevokeSignatureVaultHandler(reader, repo, invalidacion).HandleAsync(cmd, ct);</code>
/// </summary>
public sealed class ConsolidadoInvalidacionExternaHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("12789000-0000-4000-8000-000000000001");
    private static readonly Guid TipoTramite = Guid.Parse("12789000-0000-4000-8000-000000000002");
    private static readonly Guid Doc = Guid.Parse("12789000-0000-4000-8000-000000000003");
    private static readonly Guid RegistroId = Guid.Parse("12789000-0000-4000-8000-000000000004");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IConsolidadoInvalidacionMasiva _invalidacion = Substitute.For<IConsolidadoInvalidacionMasiva>();

    // ── AC1 — prelación documental del OT ────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ReordenarPrelacion_InvalidaUnaVezPorOtYTipoDeTramite()
    {
        var repo = Substitute.For<IOtDocumentPrecedenceRepository>();
        repo.ReorderBatchAsync(Tenant, TipoTramite, Arg.Any<IReadOnlyList<OtDocumentPrecedenceOrderItem>>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OtDocumentPrecedenceItem>?>(
                [new OtDocumentPrecedenceItem { TenantId = Tenant, ProcedureTypeId = TipoTramite, DocumentTypeId = Doc }]));

        var result = await new UpdateOtDocumentPrecedenceHandler(repo, _invalidacion)
            .HandleAsync(Reordenar(), Ct);

        result.Status.Should().Be(UpdateOtDocumentPrecedenceStatus.Updated);
        await _invalidacion.Received(1).InvalidarPorPrelacionOtAsync(Tenant, TipoTramite, Arg.Any<CancellationToken>());
        await _invalidacion.DidNotReceive().InvalidarPorCompaniaAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_PrelacionConDocumentoDesconocido_NoInvalida()
    {
        // Edge: el repositorio rechaza el lote (422 UNKNOWN_DOCUMENT_TYPE): nada cambió, nada se invalida.
        var repo = Substitute.For<IOtDocumentPrecedenceRepository>();
        repo.ReorderBatchAsync(default, default, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<OtDocumentPrecedenceItem>?>(null));

        var result = await new UpdateOtDocumentPrecedenceHandler(repo, _invalidacion)
            .HandleAsync(Reordenar(), Ct);

        result.Status.Should().Be(UpdateOtDocumentPrecedenceStatus.ValidationFailed);
        await _invalidacion.DidNotReceiveWithAnyArgs().InvalidarPorPrelacionOtAsync(default, default, default);
    }

    [Fact]
    public async Task AC1_Contrato_SinPuertoInyectado_ElHandlerSigueFuncionando()
    {
        // Contrato: el puerto es opcional (los llamadores legados construyen el handler sin él).
        var repo = Substitute.For<IOtDocumentPrecedenceRepository>();
        repo.ReorderBatchAsync(default, default, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<OtDocumentPrecedenceItem>?>([]));

        var result = await new UpdateOtDocumentPrecedenceHandler(repo).HandleAsync(Reordenar(), Ct);

        result.Status.Should().Be(UpdateOtDocumentPrecedenceStatus.Updated);
    }

    // ── AC2 — escritura de la compañía ───────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_AltaDeEscritura_InvalidaLosTramitesDeLaCompania()
    {
        var (storage, repo, reader) = DeedMocks();
        repo.CreateAsync(Arg.Any<SaveDeedData>(), Arg.Any<CancellationToken>()).Returns(RegistroId);

        var result = await new CreateDeedHandler(storage, repo, reader, _invalidacion).HandleAsync(new CreateDeedCommand
        {
            TenantId = Tenant,
            Description = "Escritura 123",
            VigenciaDesde = new DateOnly(2026, 1, 1),
            VigenciaHasta = new DateOnly(2027, 1, 1),
            Sha256 = new string('a', 64),
            RepresentedCompanyIds = [Guid.NewGuid()],
        }, Ct);

        result.IsValid.Should().BeTrue();
        await _invalidacion.Received(1).InvalidarPorCompaniaAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_EditarEscritura_InvalidaYSiNoExisteNoInvalida()
    {
        var (storage, repo, _) = DeedMocks();
        repo.UpdateAsync(Arg.Is<SaveDeedData>(d => d.Id == RegistroId), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateDeedHandler(storage, repo, _invalidacion);

        var ok = await handler.HandleAsync(EditarEscritura(RegistroId), Ct);
        var noExiste = await handler.HandleAsync(EditarEscritura(Guid.NewGuid()), Ct);

        ok.Outcome.Should().Be(UpdateDeedOutcome.Updated);
        noExiste.Outcome.Should().Be(UpdateDeedOutcome.NotFound);
        await _invalidacion.Received(1).InvalidarPorCompaniaAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_BajaDeEscritura_InvalidaYSiNoExisteNoInvalida()
    {
        var (_, repo, reader) = DeedMocks();
        reader.GetByIdAsync(Tenant, RegistroId, Arg.Any<CancellationToken>())
            .Returns(new DeedItem { Id = RegistroId, TenantId = Tenant });
        var handler = new DeleteDeedHandler(reader, repo, _invalidacion);

        (await handler.HandleAsync(new DeleteDeedCommand { TenantId = Tenant, Id = RegistroId }, Ct))
            .Should().Be(DeleteDeedOutcome.Deleted);
        (await handler.HandleAsync(new DeleteDeedCommand { TenantId = Tenant, Id = Guid.NewGuid() }, Ct))
            .Should().Be(DeleteDeedOutcome.NotFound);

        await _invalidacion.Received(1).InvalidarPorCompaniaAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_EscrituraInvalida_NoInvalida()
    {
        // Edge: 422 por metadatos (sin descripción ni hash): nada se persistió, nada se invalida.
        var (storage, repo, reader) = DeedMocks();

        var result = await new CreateDeedHandler(storage, repo, reader, _invalidacion).HandleAsync(new CreateDeedCommand
        {
            TenantId = Tenant,
            VigenciaDesde = new DateOnly(2026, 1, 1),
            VigenciaHasta = new DateOnly(2027, 1, 1),
        }, Ct);

        result.IsValid.Should().BeFalse();
        await _invalidacion.DidNotReceiveWithAnyArgs().InvalidarPorCompaniaAsync(default, default);
    }

    [Fact]
    public async Task AC2_AltaYEdicionDeRepresentanteLegal_InvalidanLosTramitesDeLaCompania()
    {
        await using var ctx = NewContext();
        var (create, update, delete) = RepresentanteHandlers(ctx);

        var alta = await create.HandleAsync(NuevoRepresentante(), Ct);
        alta.IsValid.Should().BeTrue();

        var edicion = await update.HandleAsync(EditarRepresentante(alta.Id!.Value), Ct);
        edicion.IsValid.Should().BeTrue();

        var baja = await delete.HandleAsync(
            new DeleteLegalRepresentativeCommand { TenantId = Tenant, Id = alta.Id.Value }, Ct);
        baja.Should().Be(DeleteLegalRepresentativeOutcome.Deactivated);

        await _invalidacion.Received(3).InvalidarPorCompaniaAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_RepresentanteInvalidoOInexistente_NoInvalida()
    {
        await using var ctx = NewContext();
        var (create, update, delete) = RepresentanteHandlers(ctx);

        var invalido = await create.HandleAsync(new CreateLegalRepresentativeCommand { TenantId = Tenant }, Ct);
        var inexistente = await update.HandleAsync(EditarRepresentante(Guid.NewGuid()), Ct);
        var bajaInexistente = await delete.HandleAsync(
            new DeleteLegalRepresentativeCommand { TenantId = Tenant, Id = Guid.NewGuid() }, Ct);

        invalido.IsValid.Should().BeFalse();
        inexistente.NotFound.Should().BeTrue();
        bajaInexistente.Should().Be(DeleteLegalRepresentativeOutcome.NotFound);
        await _invalidacion.DidNotReceiveWithAnyArgs().InvalidarPorCompaniaAsync(default, default);
    }

    // ── AC3 — firma del baúl ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_AltaDeFirmaVigente_InvalidaPorCompaniaTitularDelBaul()
    {
        var storage = Substitute.For<ISignatureVaultArtifactStorage>();
        storage.SaveAsync(default, default!, default)
            .ReturnsForAnyArgs(new StoredSignatureArtifact("vault/x.png", new string('b', 64)));
        var repo = Substitute.For<ISignatureVaultRepository>();
        repo.CreateAsync(Arg.Any<CreateSignatureVaultData>(), Arg.Any<CancellationToken>()).Returns(RegistroId);

        var result = await new CreateSignatureVaultHandler(storage, repo, reader: null, _invalidacion)
            .HandleAsync(NuevaFirma(), Ct);

        result.IsValid.Should().BeTrue();
        await _invalidacion.Received(1).InvalidarPorFirmaBaulAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_FirmaQueSustituyeAOtraActiva_InvalidaUnaSolaVez()
    {
        // HU #11193 — el conflicto revoca la activa y reintenta: debe invalidar una vez, no dos.
        var storage = Substitute.For<ISignatureVaultArtifactStorage>();
        storage.SaveAsync(default, default!, default)
            .ReturnsForAnyArgs(new StoredSignatureArtifact("vault/x.png", new string('b', 64)));
        var repo = Substitute.For<ISignatureVaultRepository>();
        var intentos = 0;
        repo.CreateAsync(Arg.Any<CreateSignatureVaultData>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++intentos == 1
                ? throw new SignatureVaultActiveConflictException()
                : Task.FromResult(RegistroId));
        repo.RevokeAsync(Arg.Any<RevokeSignatureVaultData>(), Arg.Any<CancellationToken>()).Returns(true);
        var reader = Substitute.For<ISignatureVaultReader>();
        reader.FindActiveByNumberAsync(Tenant, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SignatureVaultAggregateFor(Guid.NewGuid()));

        var result = await new CreateSignatureVaultHandler(storage, repo, reader, _invalidacion)
            .HandleAsync(NuevaFirma(), Ct);

        result.IsValid.Should().BeTrue();
        await _invalidacion.Received(1).InvalidarPorFirmaBaulAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_RevocarFirma_InvalidaYSiNoExisteNoInvalida()
    {
        var reader = Substitute.For<ISignatureVaultReader>();
        reader.GetByIdAsync(Tenant, RegistroId, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultItem { Id = RegistroId, TenantId = Tenant, Estado = SignatureVaultEstado.Activa });
        var repo = Substitute.For<ISignatureVaultRepository>();
        repo.RevokeAsync(Arg.Any<RevokeSignatureVaultData>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new RevokeSignatureVaultHandler(reader, repo, _invalidacion);

        (await handler.HandleAsync(new RevokeSignatureVaultCommand { TenantId = Tenant, Id = RegistroId }, Ct))
            .Should().Be(RevokeSignatureVaultOutcome.Revoked);
        (await handler.HandleAsync(new RevokeSignatureVaultCommand { TenantId = Tenant, Id = Guid.NewGuid() }, Ct))
            .Should().Be(RevokeSignatureVaultOutcome.NotFound);

        await _invalidacion.Received(1).InvalidarPorFirmaBaulAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_EditarFirmaActiva_InvalidaYFirmaRevocadaNoInvalida()
    {
        var revocadaId = Guid.NewGuid();
        var reader = Substitute.For<ISignatureVaultReader>();
        reader.GetByIdAsync(Tenant, RegistroId, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultItem { Id = RegistroId, TenantId = Tenant, Estado = SignatureVaultEstado.Activa });
        reader.GetByIdAsync(Tenant, revocadaId, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultItem { Id = revocadaId, TenantId = Tenant, Estado = SignatureVaultEstado.Revocada });
        var repo = Substitute.For<ISignatureVaultRepository>();
        repo.UpdateAsync(Arg.Any<UpdateSignatureVaultData>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateSignatureVaultHandler(reader, repo, _invalidacion);

        (await handler.HandleAsync(EditarFirma(RegistroId), Ct)).Outcome.Should().Be(UpdateSignatureVaultOutcome.Updated);
        (await handler.HandleAsync(EditarFirma(revocadaId), Ct)).Outcome.Should().Be(UpdateSignatureVaultOutcome.Revoked);

        await _invalidacion.Received(1).InvalidarPorFirmaBaulAsync(Tenant, Arg.Any<CancellationToken>());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static UpdateOtDocumentPrecedenceCommand Reordenar() => new()
    {
        TenantId = Tenant,
        Request = new UpdateOtDocumentPrecedenceRequest
        {
            ProcedureTypeId = TipoTramite,
            Items = [new OtDocumentPrecedenceOrderRequest { DocumentTypeId = Doc, SortOrder = 1 }],
        },
    };

    private static (IDeedDocumentStorage, IDeedRepository, IDeedReader) DeedMocks()
    {
        var storage = Substitute.For<IDeedDocumentStorage>();
        storage.CreateUploadAsync(default, default)
            .ReturnsForAnyArgs(new DeedUploadTicket("deeds/x.pdf", "https://s3/x", new Dictionary<string, string>()));
        return (storage, Substitute.For<IDeedRepository>(), Substitute.For<IDeedReader>());
    }

    private static UpdateDeedCommand EditarEscritura(Guid id) => new()
    {
        TenantId = Tenant,
        Id = id,
        Description = "Escritura editada",
        VigenciaDesde = new DateOnly(2026, 1, 1),
        VigenciaHasta = new DateOnly(2027, 1, 1),
        RepresentedCompanyIds = [Guid.NewGuid()],
    };

    private static CreateSignatureVaultCommand NuevaFirma() => new()
    {
        TenantId = Tenant,
        DocumentType = "CC",
        DocumentNumber = "1020304050",
        FullName = "Firmante de prueba",
        VigenciaDesde = new DateOnly(2026, 1, 1),
        VigenciaHasta = new DateOnly(2027, 1, 1),
        ArtefactoFirmaBase64 = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]),
    };

    private static UpdateSignatureVaultCommand EditarFirma(Guid id) => new()
    {
        TenantId = Tenant,
        Id = id,
        FullName = "Firmante corregido",
        CodigoHash = "ABC123",
        VigenciaDesde = new DateOnly(2026, 1, 1),
        VigenciaHasta = new DateOnly(2027, 1, 1),
    };

    private static Flit.Admin.Domain.Companies.SignatureVault.SignatureVault? SignatureVaultAggregateFor(Guid id) =>
        Flit.Admin.Domain.Companies.SignatureVault.SignatureVault.Rehydrate(
            id, Tenant, "CC", "1020304050", null, "Firmante anterior", "hash", "vault/old.png", "sha",
            SignatureVaultEstado.Activa, new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1), null, null);

    private (CreateLegalRepresentativeHandler, UpdateLegalRepresentativeHandler, DeleteLegalRepresentativeHandler)
        RepresentanteHandlers(FlitDbContext ctx)
    {
        var reader = new DbLegalRepresentativeReader(ctx);
        var repo = new LegalRepresentativeRepository(ctx);
        var catalog = Substitute.For<IProcedureTypeCatalog>();
        catalog.ExistsAsync(default, default).ReturnsForAnyArgs(true);
        var resolver = Substitute.For<ILegalRepresentativeSignatureResolver>();
        resolver.ResolveAsync(default, default!, default!, default!, default, default)
            .ReturnsForAnyArgs(LegalRepresentativeSignatureResolution.None);
        var vaultReader = Substitute.For<ISignatureVaultReader>();
        var writer = new LegalRepresentativeWriter(catalog, resolver, vaultReader, repo, reader, TimeProvider.System);

        return (
            new CreateLegalRepresentativeHandler(writer, _invalidacion),
            new UpdateLegalRepresentativeHandler(writer, _invalidacion),
            new DeleteLegalRepresentativeHandler(reader, repo, _invalidacion));
    }

    private static CreateLegalRepresentativeCommand NuevoRepresentante() => new()
    {
        TenantId = Tenant,
        CompanyNit = "900123456",
        CompanyName = "ACME S.A.S.",
        CompanyEmail = "acme@x.co",
        DocumentType = "CC",
        DocumentNumber = "1020304050",
        FirstLastName = "Perez",
        SecondLastName = "Gomez",
        Name = "Juan Perez",
        Email = "juan@x.co",
        ProcedureTypeIds = [TipoTramite],
    };

    private static UpdateLegalRepresentativeCommand EditarRepresentante(Guid id) => new()
    {
        TenantId = Tenant,
        Id = id,
        CompanyNit = "900123456",
        CompanyName = "ACME S.A.S.",
        CompanyEmail = "acme@x.co",
        DocumentType = "CC",
        DocumentNumber = "1020304050",
        FirstLastName = "Perez",
        SecondLastName = "Gomez",
        Name = "Juan Perez Editado",
        Email = "juan@x.co",
        ProcedureTypeIds = [TipoTramite],
    };

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-12789-{Guid.NewGuid()}")
            .Options);
}
