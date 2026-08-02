using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.DeleteLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.GetLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.ListLegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.UpdateLegalRepresentative;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SignatureVaultAggregate = Flit.Admin.Domain.Companies.SignatureVault.SignatureVault;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests del CRUD de representantes legales (HU #10901, ADR-0033) ejercitando los handlers reales sobre
/// <see cref="DbLegalRepresentativeReader"/> + <see cref="LegalRepresentativeRepository"/> (InMemory) y
/// un resolutor de firma/identidad en memoria. Cubren los AC: alta feliz con firma vinculada, alta sin
/// firma ni identidad (señal <c>sin_firma_ni_identidad</c> + marca de tipos de trámite persistida),
/// validaciones 422 (campos faltantes y tipo de trámite inexistente), listado paginado
/// (<c>{ data, totalCount, page, pageSize }</c>), edición (404 si no existe) y baja lógica idempotente.
/// </summary>
public sealed class LegalRepresentativeCrudHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000dd01");
    private const string Nit = "900000000-1";
    private const string DocRep = "123456789";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_HappyPath_WithVigentSignature_LinksSignature_NoSignal()
    {
        await using var ctx = NewContext();
        var procType = Guid.NewGuid();
        var signatureId = Guid.NewGuid();
        var h = Handlers(ctx, procedureTypes: [procType], resolution: Resolution.Signature(signatureId));

        var result = await h.Create.HandleAsync(NewCreate([procType]), Ct);

        result.IsValid.Should().BeTrue();
        result.Id.Should().NotBeNull();
        result.Signals.Should().BeEmpty();

        var item = await h.Get.HandleAsync(
            new GetLegalRepresentativeByIdQuery { TenantId = Tenant, Id = result.Id!.Value }, Ct);
        item.Should().NotBeNull();
        item!.SignatureVaultId.Should().Be(signatureId);
        item.HasSignatureOrIdentity.Should().BeTrue();
        item.CompanyDocumentNumber.Should().Be(Nit);
        item.ProcedureTypeIds.Should().ContainSingle().Which.Should().Be(procType);
    }

    [Fact]
    public async Task Create_NoSignatureNoIdentity_EmitsSignal_AndPersistsProcedureTypes()
    {
        await using var ctx = NewContext();
        var procTypeA = Guid.NewGuid();
        var procTypeB = Guid.NewGuid();
        var h = Handlers(ctx, procedureTypes: [procTypeA, procTypeB], resolution: Resolution.None);

        var result = await h.Create.HandleAsync(NewCreate([procTypeA, procTypeB]), Ct);

        result.IsValid.Should().BeTrue();
        result.Signals.Should().ContainSingle().Which.Should().Be(LegalRepresentativeSignals.SinFirmaNiIdentidad);

        var item = await h.Get.HandleAsync(
            new GetLegalRepresentativeByIdQuery { TenantId = Tenant, Id = result.Id!.Value }, Ct);
        item!.HasSignatureOrIdentity.Should().BeFalse();
        item.SignatureVaultId.Should().BeNull();
        item.IdentityValidationRef.Should().BeNull();
        item.ProcedureTypeIds.Should().BeEquivalentTo([procTypeA, procTypeB]);
    }

    [Fact]
    public async Task Create_MissingRequiredFields_Returns422()
    {
        await using var ctx = NewContext();
        var h = Handlers(ctx, procedureTypes: [], resolution: Resolution.None);

        var result = await h.Create.HandleAsync(new CreateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            CompanyNit = "",
            CompanyName = "",
            DocumentType = "",
            DocumentNumber = "",
            FirstLastName = "",
            Name = "",
        }, Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "companyNit" && e.Code == "requerido");
        result.Errors.Should().Contain(e => e.Field == "documentNumber" && e.Code == "requerido");
        result.Errors.Should().Contain(e => e.Field == "name" && e.Code == "requerido");
    }

    [Fact]
    public async Task Create_UnknownProcedureType_Returns422()
    {
        await using var ctx = NewContext();
        // El catálogo solo conoce procTypeKnown; el marcado en la petición es desconocido.
        var procTypeKnown = Guid.NewGuid();
        var procTypeUnknown = Guid.NewGuid();
        var h = Handlers(ctx, procedureTypes: [procTypeKnown], resolution: Resolution.None);

        var result = await h.Create.HandleAsync(NewCreate([procTypeUnknown]), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "procedureTypeIds" && e.Code == "tipo_tramite_inexistente");
    }

    [Fact]
    public async Task List_IsPaginated_WithEnvelope()
    {
        await using var ctx = NewContext();
        var procType = Guid.NewGuid();
        var h = Handlers(ctx, procedureTypes: [procType], resolution: Resolution.None);

        for (var i = 0; i < 3; i++)
        {
            var cmd = NewCreate([procType]);
            cmd = new CreateLegalRepresentativeCommand
            {
                TenantId = Tenant,
                CompanyNit = Nit,
                CompanyName = "ACME S.A.S.",
                DocumentType = "CC",
                DocumentNumber = $"10000000{i}",
                FirstLastName = "Perez",
                Name = $"Rep {i}",
                ProcedureTypeIds = [procType],
            };
            await h.Create.HandleAsync(cmd, Ct);
        }

        var pageOne = await h.List.HandleAsync(
            new ListLegalRepresentativesQuery { TenantId = Tenant, Page = 1, PageSize = 2 }, Ct);

        pageOne.TotalCount.Should().Be(3);
        pageOne.Page.Should().Be(1);
        pageOne.PageSize.Should().Be(2);
        pageOne.Data.Should().HaveCount(2);
        pageOne.Data.Should().OnlyContain(r => r.IsActive);
    }

    [Fact]
    public async Task List_NormalizesInvalidPaging_ToDefaults()
    {
        await using var ctx = NewContext();
        var h = Handlers(ctx, procedureTypes: [], resolution: Resolution.None);

        var page = await h.List.HandleAsync(
            new ListLegalRepresentativesQuery { TenantId = Tenant, Page = 0, PageSize = -5 }, Ct);

        page.Page.Should().Be(ListLegalRepresentativesHandler.DefaultPage);
        page.PageSize.Should().Be(ListLegalRepresentativesHandler.DefaultPageSize);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        await using var ctx = NewContext();
        var h = Handlers(ctx, procedureTypes: [], resolution: Resolution.None);

        var result = await h.Update.HandleAsync(new UpdateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            Id = Guid.NewGuid(),
            CompanyNit = Nit,
            CompanyName = "ACME S.A.S.",
            DocumentType = "CC",
            DocumentNumber = DocRep,
            FirstLastName = "Perez",
            Name = "Juan Perez",
        }, Ct);

        result.NotFound.Should().BeTrue();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Update_SwitchesToIdentity_AndReplacesProcedureTypes()
    {
        await using var ctx = NewContext();
        var typeA = Guid.NewGuid();
        var typeB = Guid.NewGuid();
        var signatureId = Guid.NewGuid();
        var identityRef = Guid.NewGuid();

        // Alta con firma vinculada + tipo A.
        var createHandlers = Handlers(ctx, procedureTypes: [typeA, typeB], resolution: Resolution.Signature(signatureId));
        var created = await createHandlers.Create.HandleAsync(NewCreate([typeA]), Ct);

        // Edición: el resolutor ahora devuelve identidad + tipo B (reemplaza los tipos).
        var updateHandlers = Handlers(ctx, procedureTypes: [typeA, typeB], resolution: Resolution.Identity(identityRef));
        var updated = await updateHandlers.Update.HandleAsync(new UpdateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            Id = created.Id!.Value,
            CompanyNit = Nit,
            CompanyName = "ACME S.A.S.",
            DocumentType = "CC",
            DocumentNumber = DocRep,
            FirstLastName = "Perez",
            Name = "Juan A. Perez",
            ProcedureTypeIds = [typeB],
        }, Ct);

        updated.IsValid.Should().BeTrue();
        updated.Signals.Should().BeEmpty();

        var item = await updateHandlers.Get.HandleAsync(
            new GetLegalRepresentativeByIdQuery { TenantId = Tenant, Id = created.Id!.Value }, Ct);
        item!.Name.Should().Be("Juan A. Perez");
        item.SignatureVaultId.Should().BeNull();
        item.IdentityValidationRef.Should().Be(identityRef);
        item.ProcedureTypeIds.Should().ContainSingle().Which.Should().Be(typeB);
    }

    [Fact]
    public async Task Delete_DeactivatesAndIsIdempotent_ThenNotFoundForUnknown()
    {
        await using var ctx = NewContext();
        var h = Handlers(ctx, procedureTypes: [], resolution: Resolution.None);
        var created = await h.Create.HandleAsync(NewCreate([]), Ct);
        var id = created.Id!.Value;

        var first = await h.Delete.HandleAsync(new DeleteLegalRepresentativeCommand { TenantId = Tenant, Id = id }, Ct);
        first.Should().Be(DeleteLegalRepresentativeOutcome.Deactivated);

        // Idempotente: volver a desactivar sigue devolviendo Deactivated.
        var second = await h.Delete.HandleAsync(new DeleteLegalRepresentativeCommand { TenantId = Tenant, Id = id }, Ct);
        second.Should().Be(DeleteLegalRepresentativeOutcome.Deactivated);

        var item = await h.Get.HandleAsync(
            new GetLegalRepresentativeByIdQuery { TenantId = Tenant, Id = id }, Ct);
        item!.IsActive.Should().BeFalse();

        var unknown = await h.Delete.HandleAsync(
            new DeleteLegalRepresentativeCommand { TenantId = Tenant, Id = Guid.NewGuid() }, Ct);
        unknown.Should().Be(DeleteLegalRepresentativeOutcome.NotFound);
    }

    [Fact]
    public async Task Create_WithMultipleCompanies_LinksAllCompanies_AndFoundByEachNit()
    {
        await using var ctx = NewContext();
        var procType = Guid.NewGuid();
        var reader = new DbLegalRepresentativeReader(ctx);
        var repo = new LegalRepresentativeRepository(ctx);
        var writer = new LegalRepresentativeWriter(
            new FakeProcedureTypeCatalog([procType]),
            new FakeSignatureResolver(Resolution.None),
            new FakeSignatureVaultReader(),
            repo, reader,
            new StubTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero)));
        var create = new CreateLegalRepresentativeHandler(writer);

        const string nitA = "900000000-1";
        const string nitB = "800000000-2";
        var result = await create.HandleAsync(new CreateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = DocRep,
            FirstLastName = "Perez",
            Name = "Juan Perez",
            ProcedureTypeIds = [procType],
            Companies =
            [
                new LegalRepresentativeCompanyInput(nitA, "ACME S.A.S.", null, null, null, null),
                new LegalRepresentativeCompanyInput(nitB, "Beta S.A.S.", null, null, null, null),
            ],
        }, Ct);

        result.IsValid.Should().BeTrue();

        var item = await reader.GetByIdAsync(Tenant, result.Id!.Value, Ct);
        item!.Companies.Should().HaveCount(2);
        item.Companies.Select(c => c.Nit).Should().BeEquivalentTo([nitA, nitB]);

        // El representante-persona se encuentra por CUALQUIERA de sus NITs (multiempresa).
        var byA = await reader.FindActiveByCompanyNitAsync(Tenant, nitA, Ct);
        var byB = await reader.FindActiveByCompanyNitAsync(Tenant, nitB, Ct);
        byA!.Id.Should().Be(result.Id!.Value);
        byB!.Id.Should().Be(result.Id!.Value);
    }

    [Fact]
    public async Task Create_SamePersonSecondCompany_MergesInsteadOfDuplicating()
    {
        await using var ctx = NewContext();
        var reader = new DbLegalRepresentativeReader(ctx);
        var repo = new LegalRepresentativeRepository(ctx);
        var create = new CreateLegalRepresentativeHandler(new LegalRepresentativeWriter(
            new FakeProcedureTypeCatalog([]),
            new FakeSignatureResolver(Resolution.None),
            new FakeSignatureVaultReader(),
            repo, reader,
            new StubTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero))));

        const string nitA = "900000000-1";
        const string nitB = "800000000-2";
        var first = await create.HandleAsync(new CreateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = DocRep,
            FirstLastName = "Perez",
            Name = "Juan Perez",
            Companies = [new LegalRepresentativeCompanyInput(nitA, "ACME S.A.S.", null, null, null, null)],
        }, Ct);

        var second = await create.HandleAsync(new CreateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            DocumentType = "CC",
            DocumentNumber = DocRep,
            FirstLastName = "Perez",
            Name = "Juan Perez",
            Companies = [new LegalRepresentativeCompanyInput(nitB, "Beta S.A.S.", null, null, null, null)],
        }, Ct);

        // "Se crea una sola vez": el segundo alta con el mismo documento edita la persona existente.
        second.Id.Should().Be(first.Id);

        var page = await reader.ListPagedAsync(Tenant, 1, 50, Ct);
        page.Items.Count(r => r.DocumentNumber == DocRep && r.IsActive).Should().Be(1);

        var item = await reader.GetByIdAsync(Tenant, first.Id!.Value, Ct);
        item!.Companies.Select(c => c.Nit).Should().BeEquivalentTo([nitA, nitB]);
    }

    // ---------- Helpers ----------

    private static CreateLegalRepresentativeCommand NewCreate(IReadOnlyList<Guid> procedureTypeIds) =>
        new()
        {
            TenantId = Tenant,
            CompanyNit = Nit,
            CompanyName = "ACME S.A.S.",
            CompanyEmail = "acme@x.co",
            DocumentType = "CC",
            DocumentNumber = DocRep,
            FirstLastName = "Perez",
            SecondLastName = "Gomez",
            Name = "Juan Perez",
            Email = "juan@x.co",
            ProcedureTypeIds = procedureTypeIds,
        };

    private static CrudHandlers Handlers(
        FlitDbContext ctx,
        IReadOnlyList<Guid> procedureTypes,
        LegalRepresentativeSignatureResolution resolution)
    {
        var reader = new DbLegalRepresentativeReader(ctx);
        var repo = new LegalRepresentativeRepository(ctx);
        var catalog = new FakeProcedureTypeCatalog(procedureTypes);
        var resolver = new FakeSignatureResolver(resolution);
        var vaultReader = new FakeSignatureVaultReader();
        // El "hoy" no altera el resultado: el resolutor está fakeado y devuelve una resolución fija.
        var clock = new StubTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
        var writer = new LegalRepresentativeWriter(catalog, resolver, vaultReader, repo, reader, clock);

        return new CrudHandlers(
            new CreateLegalRepresentativeHandler(writer),
            new UpdateLegalRepresentativeHandler(writer),
            new ListLegalRepresentativesHandler(reader),
            new GetLegalRepresentativeByIdHandler(reader),
            new DeleteLegalRepresentativeHandler(reader, repo));
    }

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-legal-rep-crud-{Guid.NewGuid()}")
            .Options);

    private sealed record CrudHandlers(
        CreateLegalRepresentativeHandler Create,
        UpdateLegalRepresentativeHandler Update,
        ListLegalRepresentativesHandler List,
        GetLegalRepresentativeByIdHandler Get,
        DeleteLegalRepresentativeHandler Delete);

    /// <summary>Catálogo de tipos de trámite en memoria: existe solo lo que se le sembró.</summary>
    private sealed class FakeProcedureTypeCatalog : IProcedureTypeCatalog
    {
        private readonly HashSet<Guid> _known;

        public FakeProcedureTypeCatalog(IReadOnlyList<Guid> known) => _known = [.. known];

        public Task<bool> ExistsAsync(Guid procedureTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_known.Contains(procedureTypeId));

        public Task<IReadOnlyList<ProcedureTypeCatalogItem>> ListActivePublishedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureTypeCatalogItem>>(
                [.. _known.Select(id => new ProcedureTypeCatalogItem(id, "CODE", "Tipo"))]);
    }

    /// <summary>Resolutor de firma/identidad en memoria: devuelve una resolución fija.</summary>
    private sealed class FakeSignatureResolver : ILegalRepresentativeSignatureResolver
    {
        private readonly LegalRepresentativeSignatureResolution _resolution;

        public FakeSignatureResolver(LegalRepresentativeSignatureResolution resolution) => _resolution = resolution;

        public Task<LegalRepresentativeSignatureResolution> ResolveAsync(
            Guid tenantId,
            string nitCompania,
            string tipoDocumento,
            string documentoRepresentante,
            DateOnly today,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_resolution);
    }

    /// <summary>TimeProvider fijo (sin dependencias externas): ancla el "ahora" a un instante conocido.</summary>
    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public StubTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static class Resolution
    {
        public static LegalRepresentativeSignatureResolution None => LegalRepresentativeSignatureResolution.None;

        public static LegalRepresentativeSignatureResolution Signature(Guid id) =>
            LegalRepresentativeSignatureResolution.FromSignature(id);

        public static LegalRepresentativeSignatureResolution Identity(Guid id) =>
            LegalRepresentativeSignatureResolution.FromIdentity(id);
    }

    /// <summary>Lector del baúl en memoria: devuelve null para cualquier consulta (sin firmas precargadas).</summary>
    private sealed class FakeSignatureVaultReader : ISignatureVaultReader
    {
        public Task<SignatureVaultAggregate?> FindActiveByNitAsync(Guid tenantId, string nitEmpresa, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<SignatureVaultAggregate?> FindActiveByDocumentAsync(Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SignatureVaultItem>>([]);

        public Task<SignatureVaultItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultItem?>(null);
    }
}
