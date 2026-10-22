using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13131 (ADR-0061) — la firma física deja de persistirse como exención, pero las filas históricas
/// con <c>signs_physically</c> verdadero no se borran ni se modifican, y el resolver de trámites mantiene
/// su comportamiento actual hasta que F4 active el bloqueo.
/// </summary>
public sealed class RetiroFirmaFisicaTests
{
    private static readonly Guid Ot1 = Guid.NewGuid();
    private static readonly Guid Ot2 = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-retiro-fisica-{Guid.NewGuid()}")
            .Options);

    private static async Task<(FlitDbContext Ctx, Guid SignerId)> SeedLegadoAsync()
    {
        var ctx = NewContext();
        var id = Guid.NewGuid();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = Ot1,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            IntegrityHash = new string('a', 64),
            RegisteredAt = Now,
            IsActive = true,
            CreatedAt = Now,
        });
        ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Ot1, IsActive = true,
            SignsPhysically = true, CreatedAt = Now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Ot1, CompanyTenantId = Company,
            IsActive = true, CreatedAt = Now,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (ctx, id);
    }

    [Fact]
    public async Task AC1_LaEdicionConListaDeOrganismos_NoPersisteNiBorraLaMarcaDeFirmaFisica()
    {
        var ct = TestContext.Current.CancellationToken;
        var (ctx, id) = await SeedLegadoAsync();
        await using var _ = ctx;

        // La petición trae una lista de organismos (reemplaza el conjunto) y pide firma física en un
        // organismo nuevo: se ignora. La marca histórica del organismo que ya tenía se conserva.
        var ok = await new MandateSignerRepository(ctx).UpdateAsync(
            new UpdateMandateSignerData(
                id, Guid.NewGuid(), "Ana Restrepo", "1020304050", new string('b', 64), [Company], null, null,
                "CC", null, null,
                TransitOfficeIds: [Ot1, Ot2],
                PhysicalSignatureOfficeIds: [Ot2],
                NuevoOrganismoPrimario: Ot1),
            ct);

        ok.Should().BeTrue();
        ctx.ChangeTracker.Clear();
        var filas = await ctx.MandateSignerTransitOffices.Where(o => o.MandateSignerId == id).ToListAsync(ct);
        filas.Single(o => o.TransitOfficeId == Ot1).SignsPhysically.Should().BeTrue("la fila histórica no se toca");
        filas.Single(o => o.TransitOfficeId == Ot2).SignsPhysically.Should().BeFalse("no se persiste como exención");
    }

    [Fact]
    public async Task AC1_LaEdicionSinPedirFirmaFisica_NoDesmarcaLaFilaHistorica()
    {
        var ct = TestContext.Current.CancellationToken;
        var (ctx, id) = await SeedLegadoAsync();
        await using var _ = ctx;

        await new MandateSignerRepository(ctx).UpdateAsync(
            new UpdateMandateSignerData(
                id, Guid.NewGuid(), "Ana Restrepo", "1020304050", new string('b', 64), [Company], null, null,
                "CC", null, null,
                TransitOfficeIds: [Ot1],
                PhysicalSignatureOfficeIds: []),
            ct);

        ctx.ChangeTracker.Clear();
        (await ctx.MandateSignerTransitOffices.SingleAsync(o => o.MandateSignerId == id, ct))
            .SignsPhysically.Should().BeTrue("antes se desmarcaba al reemplazar la lista; ahora las filas existentes no se borran ni cambian");
    }

    [Fact]
    public async Task AC1_UnAltaNuevaConFirmaFisicaEnLaPeticion_NaceSinLaMarca()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = NewContext();

        var id = await new MandateSignerRepository(ctx).CreateAsync(
            new CreateMandateSignerData(
                Ot1, Guid.NewGuid(), "Carlos Pérez", "9080706050", new string('c', 64), Now, [Company], null, null,
                "CC", null, null,
                TransitOfficeIds: [Ot1, Ot2],
                PhysicalSignatureOfficeIds: [Ot1, Ot2]),
            ct);

        (await ctx.MandateSignerTransitOffices.Where(o => o.MandateSignerId == id).ToListAsync(ct))
            .Should().HaveCount(2).And.OnlyContain(o => !o.SignsPhysically);
    }

    [Fact]
    public async Task AC5_ElResolverDeTramitesConservaElComportamientoActualConLaFilaHistorica()
    {
        var ct = TestContext.Current.CancellationToken;
        var (ctx, id) = await SeedLegadoAsync();
        await using var _ = ctx;
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Flit.Tramites.Domain.Entities.ProcedureInstanceBiometricValidation>(), 0, false));
        var directorio = new MandateSignerDirectory(ctx, reader, new IdentityVigenciaPorDocumentoResolver(repo));

        var candidato = (await directorio.GetCandidatesAsync(Ot1, Company, null, ct)).Should().ContainSingle().Subject;

        candidato.Id.Should().Be(id);
        candidato.FirmaFisica.Should().BeTrue("hasta que F4 active el bloqueo, el resolver sigue honrando la marca histórica");
    }
}
