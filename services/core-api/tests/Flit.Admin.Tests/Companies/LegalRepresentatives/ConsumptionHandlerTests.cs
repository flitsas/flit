using Flit.Admin.Application.Companies.Deeds.ListActiveDeeds;
using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using SignatureVaultAggregate = Flit.Admin.Domain.Companies.SignatureVault.SignatureVault;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests de los handlers de CONSUMO del wizard (HU #10903, ADR-0033 §5.4) sobre los readers reales
/// (<see cref="DbDeedReader"/>/<see cref="DbLegalRepresentativeReader"/> InMemory) + fakes del baúl e
/// identidad. Cubren los AC: escrituras vigentes filtran vencidas/inactivas y proyectan nit/nombre/
/// días restantes/vigencia; lookup por NIT found/not-found; banderas de firma/identidad vigentes.
/// </summary>
public sealed class ConsumptionHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000ee01");
    private const string Nit = "900000000-1";
    private const string OtherNit = "800000000-2";
    private static readonly DateOnly Today = new(2026, 7, 23);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-consumption-{Guid.NewGuid()}")
            .Options);

    // Reloj fijo: 2026-07-23 07:00 en Colombia (12:00Z) → hoy = 2026-07-23.
    private static TimeProvider Clock => new StubTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));

    // ---------------- Escrituras vigentes ----------------

    [Fact]
    public async Task ActiveDeeds_FiltersExpiredAndInactive_ProjectsNitNameDiasRestantesVigencia()
    {
        await using var ctx = NewContext();
        var deedRepo = new DeedRepository(ctx);
        var companyRepo = new LegalRepresentativeRepository(ctx);

        var companyId = await companyRepo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, Nit, "ACME S.A.S.", null, null, null, null, null), Ct);

        var hasta = new DateOnly(2026, 12, 31);
        await deedRepo.CreateAsync(new SaveDeedData(
            Tenant, null, "Vigente", "path-1", "sha-1",
            new DateOnly(2026, 1, 1), hasta, [companyId], null), Ct);
        // Vencida.
        await deedRepo.CreateAsync(new SaveDeedData(
            Tenant, null, "Vencida", "path-2", "sha-2",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), [companyId], null), Ct);
        // Activa+vigente pero dada de baja.
        var baja = await deedRepo.CreateAsync(new SaveDeedData(
            Tenant, null, "Baja", "path-3", "sha-3",
            new DateOnly(2026, 1, 1), hasta, [companyId], null), Ct);
        await deedRepo.DeactivateAsync(Tenant, baja, null, Ct);

        var handler = new ListActiveDeedsForTenantHandler(
            new DbDeedReader(ctx), new DbLegalRepresentativeReader(ctx), Clock);

        var result = await handler.HandleAsync(new ListActiveDeedsForTenantQuery { TenantId = Tenant }, Ct);

        result.Should().ContainSingle();
        result[0].Nit.Should().Be(Nit);
        result[0].Name.Should().Be("ACME S.A.S.");
        result[0].Description.Should().Be("Vigente");
        result[0].VigenciaHasta.Should().Be(hasta);
        result[0].DiasRestantes.Should().Be(hasta.DayNumber - Today.DayNumber);
        result[0].DiasRestantes.Should().BePositive();
    }

    [Fact]
    public async Task ActiveDeeds_ReturnsAllVigentesForSameCompany_DoesNotCollapse()
    {
        // Feature #10929: dos escrituras VIGENTES de la MISMA compañía → el paso 1 debe ver AMBAS,
        // ya no se colapsa a una sola fila por NIT.
        await using var ctx = NewContext();
        var deedRepo = new DeedRepository(ctx);
        var companyRepo = new LegalRepresentativeRepository(ctx);

        var companyId = await companyRepo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, Nit, "ACME S.A.S.", null, null, null, null, null), Ct);

        var nearest = new DateOnly(2026, 9, 30);
        var farthest = new DateOnly(2027, 3, 31);
        var idCorta = await deedRepo.CreateAsync(new SaveDeedData(
            Tenant, null, "Corta", "path-1", "sha-1", new DateOnly(2026, 1, 1), nearest, [companyId], null), Ct);
        var idLarga = await deedRepo.CreateAsync(new SaveDeedData(
            Tenant, null, "Larga", "path-2", "sha-2", new DateOnly(2026, 1, 1), farthest, [companyId], null), Ct);

        var handler = new ListActiveDeedsForTenantHandler(
            new DbDeedReader(ctx), new DbLegalRepresentativeReader(ctx), Clock);

        var result = await handler.HandleAsync(new ListActiveDeedsForTenantQuery { TenantId = Tenant }, Ct);

        // Ambas escrituras del mismo NIT, ordenadas por vigencia más próxima primero.
        result.Should().HaveCount(2);
        result.Should().OnlyContain(d => d.Nit == Nit);
        result[0].VigenciaHasta.Should().Be(nearest);
        result[0].Id.Should().Be(idCorta);
        result[0].Description.Should().Be("Corta");
        result[1].VigenciaHasta.Should().Be(farthest);
        result[1].Id.Should().Be(idLarga);
        result[1].Description.Should().Be("Larga");
    }

    [Fact]
    public async Task ActiveDeeds_ProjectsRepresentative_WhenDeedHasRepresentativeId()
    {
        await using var ctx = NewContext();
        var deedRepo = new DeedRepository(ctx);
        var companyRepo = new LegalRepresentativeRepository(ctx);

        var companyId = await companyRepo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, Nit, "ACME S.A.S.", null, null, null, null, null), Ct);
        var repId = await companyRepo.SaveAsync(new SaveLegalRepresentativeData(
            Tenant, null, companyId, "CC", "1038409485", "Pérez", "Gómez", "Juan",
            null, null, null, null, null, null, [], null), Ct);

        var hasta = new DateOnly(2026, 12, 31);
        await deedRepo.CreateAsync(new SaveDeedData(
            Tenant, null, "Con RL", "path-rl", "sha-rl",
            new DateOnly(2026, 1, 1), hasta, [companyId], null, RepresentativeId: repId), Ct);

        var handler = new ListActiveDeedsForTenantHandler(
            new DbDeedReader(ctx), new DbLegalRepresentativeReader(ctx), Clock);

        var result = await handler.HandleAsync(new ListActiveDeedsForTenantQuery { TenantId = Tenant }, Ct);

        result.Should().ContainSingle();
        result[0].RepresentativeId.Should().Be(repId);
        result[0].RepresentativeName.Should().Be("Juan Pérez Gómez");
        result[0].RepresentativeDocumentType.Should().Be("CC");
        result[0].RepresentativeDocumentNumber.Should().Be("1038409485");
    }

    [Fact]
    public async Task ActiveDeeds_RepresentativeNull_WhenLegacyDeedWithoutRepresentative()
    {
        await using var ctx = NewContext();
        var deedRepo = new DeedRepository(ctx);
        var companyRepo = new LegalRepresentativeRepository(ctx);

        var companyId = await companyRepo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, Nit, "ACME S.A.S.", null, null, null, null, null), Ct);

        await deedRepo.CreateAsync(new SaveDeedData(
            Tenant, null, "Legada", "path-leg", "sha-leg",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [companyId], null), Ct);

        var handler = new ListActiveDeedsForTenantHandler(
            new DbDeedReader(ctx), new DbLegalRepresentativeReader(ctx), Clock);

        var result = await handler.HandleAsync(new ListActiveDeedsForTenantQuery { TenantId = Tenant }, Ct);

        result.Should().ContainSingle();
        result[0].RepresentativeId.Should().BeNull();
        result[0].RepresentativeName.Should().BeNull();
        result[0].RepresentativeDocumentType.Should().BeNull();
        result[0].RepresentativeDocumentNumber.Should().BeNull();
    }

    [Fact]
    public async Task ActiveDeeds_Empty_WhenNoVigente()
    {
        await using var ctx = NewContext();

        var handler = new ListActiveDeedsForTenantHandler(
            new DbDeedReader(ctx), new DbLegalRepresentativeReader(ctx), Clock);

        var result = await handler.HandleAsync(new ListActiveDeedsForTenantQuery { TenantId = Tenant }, Ct);

        result.Should().BeEmpty();
    }

    // ---------------- Lookup por NIT ----------------

    [Fact]
    public async Task Lookup_NotFound_WhenNoActiveRepresentative()
    {
        await using var ctx = NewContext();
        var signature = Substitute.For<ISignatureVaultReader>();
        var identity = Substitute.For<IRepresentativeIdentityLookup>();
        var handler = new FindRepresentativeByNitHandler(
            new DbLegalRepresentativeReader(ctx), signature, identity, Clock);

        var result = await handler.HandleAsync(
            new FindRepresentativeByNitQuery { TenantId = Tenant, Nit = OtherNit }, Ct);

        result.Should().BeNull();
        // Sin match no se consultan las banderas de firma/identidad.
        await signature.DidNotReceiveWithAnyArgs().FindActiveByDocumentAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Lookup_Found_WithFirmaVigente_ProjectsCompanyAndRepresentative()
    {
        await using var ctx = NewContext();
        await SeedRepresentativeAsync(ctx);

        var signature = Substitute.For<ISignatureVaultReader>();
        // HU #10930/#10937 — la firma se resuelve por el DOCUMENTO del representante, no por el NIT.
        signature.FindActiveByDocumentAsync(Tenant, "CC", "123456789", Arg.Any<CancellationToken>())
            .Returns(VigenteSignature(document: "123456789"));
        var identity = Substitute.For<IRepresentativeIdentityLookup>();
        identity.FindVigenteIdentityRefAsync(Tenant, "CC", "123456789", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var handler = new FindRepresentativeByNitHandler(
            new DbLegalRepresentativeReader(ctx), signature, identity, Clock);

        var result = await handler.HandleAsync(
            new FindRepresentativeByNitQuery { TenantId = Tenant, Nit = Nit }, Ct);

        result.Should().NotBeNull();
        result!.Company.Nit.Should().Be(Nit);
        result.Company.RazonSocial.Should().Be("ACME S.A.S.");
        result.Company.Email.Should().Be("acme@x.co");
        result.Representante.Documento.Should().Be("123456789");
        result.Representante.TipoDoc.Should().Be("CC");
        result.Representante.Nombres.Should().Be("Juan Perez");
        result.Representante.PrimerApellido.Should().Be("Perez");
        result.FirmaVigente.Should().BeTrue();
        result.IdentidadVigente.Should().BeFalse();
        // HU #10937 — la lista trae al representante con sus banderas por documento.
        result.Representantes.Should().ContainSingle();
        result.Representantes[0].Documento.Should().Be("123456789");
        result.Representantes[0].FirmaVigente.Should().BeTrue();
    }

    [Fact]
    public async Task Lookup_Found_WithIdentidadVigente_WhenNoFirma()
    {
        await using var ctx = NewContext();
        await SeedRepresentativeAsync(ctx);

        var signature = Substitute.For<ISignatureVaultReader>();
        signature.FindActiveByDocumentAsync(Tenant, "CC", "123456789", Arg.Any<CancellationToken>())
            .Returns((SignatureVaultAggregate?)null);
        var identity = Substitute.For<IRepresentativeIdentityLookup>();
        identity.FindVigenteIdentityRefAsync(Tenant, "CC", "123456789", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var handler = new FindRepresentativeByNitHandler(
            new DbLegalRepresentativeReader(ctx), signature, identity, Clock);

        var result = await handler.HandleAsync(
            new FindRepresentativeByNitQuery { TenantId = Tenant, Nit = Nit }, Ct);

        result.Should().NotBeNull();
        result!.FirmaVigente.Should().BeFalse();
        result.IdentidadVigente.Should().BeTrue();
    }

    [Fact]
    public async Task Lookup_FirmaExpirada_FlagsFirmaFalse()
    {
        await using var ctx = NewContext();
        await SeedRepresentativeAsync(ctx);

        var signature = Substitute.For<ISignatureVaultReader>();
        // Firma del representante pero VENCIDA → no cuenta (HU #10930: el baúl se resuelve por documento).
        signature.FindActiveByDocumentAsync(Tenant, "CC", "123456789", Arg.Any<CancellationToken>())
            .Returns(ExpiredSignature(document: "123456789"));
        var identity = Substitute.For<IRepresentativeIdentityLookup>();
        identity.FindVigenteIdentityRefAsync(Tenant, "CC", "123456789", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var handler = new FindRepresentativeByNitHandler(
            new DbLegalRepresentativeReader(ctx), signature, identity, Clock);

        var result = await handler.HandleAsync(
            new FindRepresentativeByNitQuery { TenantId = Tenant, Nit = Nit }, Ct);

        result.Should().NotBeNull();
        result!.FirmaVigente.Should().BeFalse();
        result.IdentidadVigente.Should().BeFalse();
    }

    // ---------------- Helpers ----------------

    private static async Task SeedRepresentativeAsync(FlitDbContext ctx)
    {
        var repo = new LegalRepresentativeRepository(ctx);
        var companyId = await repo.UpsertRepresentedCompanyAsync(
            new UpsertRepresentedCompanyData(Tenant, Nit, "ACME S.A.S.", "acme@x.co", "Cll 1", "Bogotá", "3001112233", null),
            CancellationToken.None);
        await repo.SaveAsync(new SaveLegalRepresentativeData(
            Tenant, null, companyId, "CC", "123456789", "Perez", "Gomez", "Juan Perez",
            "juan@x.co", null, null, "3009998877", null, null, [], null), CancellationToken.None);
    }

    private static SignatureVaultAggregate VigenteSignature(string document) =>
        SignatureVaultAggregate.Create(
            Tenant, "CC", document, Nit, "Apoderada S.A.S.", "sha", "path", "sha256",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

    private static SignatureVaultAggregate ExpiredSignature(string document) =>
        SignatureVaultAggregate.Create(
            Tenant, "CC", document, Nit, "Apoderada S.A.S.", "sha", "path", "sha256",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)); // venció antes de "hoy" (2026-07-23)

    /// <summary>TimeProvider fijo: ancla el "ahora" a un instante conocido (sin dependencias externas).</summary>
    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
