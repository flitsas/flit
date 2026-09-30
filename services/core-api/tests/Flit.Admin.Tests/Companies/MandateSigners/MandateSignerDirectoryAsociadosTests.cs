using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13180 (Feature #13119 F7, Épica #13090) — el directorio agrega al nivel 3 de la prelación los mandatarios
/// de OTRAS compañías asociados (<c>mandate_signer_associated_companies</c>) a la compañía del trámite en el
/// organismo, respetando asociación activa, baja lógica, mandatario activo y vínculo propio activo en el organismo.
/// <para>Uso de ejemplo: el mandatario de A asociado a B sale de <c>GetCandidatesAsync(ot, B)</c> con
/// <c>Origen = "asociado"</c> y <c>OwnerTenantId = A</c>.</para>
/// </summary>
public sealed class MandateSignerDirectoryAsociadosTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid OtroOt = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ac1_ElMandatarioDeAAsociadoAB_EntraComoAsociadoConSuCompaniaPropietaria()
    {
        await using var ctx = NewContext();
        var deA = await SeedSignerAsync(ctx, "Ana de A", owner: A);
        await AsociarAsync(ctx, deA, Ot, B);

        var candidatos = await Directorio(ctx).GetCandidatesAsync(Ot, B, null, Ct);

        var c = candidatos.Should().ContainSingle().Subject;
        c.Id.Should().Be(deA);
        c.Origen.Should().Be(MandateSignerOrigins.Asociado);
        c.OwnerTenantId.Should().Be(A, "su baúl y su biometría viven en el tenant de su compañía");
    }

    [Fact]
    public async Task Ac2_ElPropioDeBSigueEntrandoPorSuVinculo_ConOrigenDeSuVinculo()
    {
        await using var ctx = NewContext();
        var deA = await SeedSignerAsync(ctx, "Ana de A", owner: A);
        var deB = await SeedSignerAsync(ctx, "Beto de B", owner: B, scope: "compania");
        await AsociarAsync(ctx, deA, Ot, B);

        var candidatos = await Directorio(ctx).GetCandidatesAsync(Ot, B, null, Ct);

        candidatos.Should().HaveCount(2);
        candidatos.Single(x => x.Id == deB).Origen.Should().Be(MandateSignerOrigins.Compania);
        candidatos.Single(x => x.Id == deB).OwnerTenantId.Should().BeNull();
        candidatos.Single(x => x.Id == deA).Origen.Should().Be(MandateSignerOrigins.Asociado);
    }

    [Fact]
    public async Task Ac7_UnaAsociacionRetirada_NoSeConsidera()
    {
        await using var ctx = NewContext();
        var deA = await SeedSignerAsync(ctx, "Ana de A", owner: A);
        await AsociarAsync(ctx, deA, Ot, B, activa: false);

        (await Directorio(ctx).GetCandidatesAsync(Ot, B, null, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Ac4_UnAsociadoInactivoOConBajaLogica_NoEntra()
    {
        await using var ctx = NewContext();
        var inactivo = await SeedSignerAsync(ctx, "Inactivo", owner: A, isActive: false);
        var eliminado = await SeedSignerAsync(ctx, "Eliminado", owner: A, deleted: true, documento: "2");
        await AsociarAsync(ctx, inactivo, Ot, B);
        await AsociarAsync(ctx, eliminado, Ot, B);

        (await Directorio(ctx).GetCandidatesAsync(Ot, B, null, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task LaAsociacionEsPorOrganismoYPorCompania()
    {
        await using var ctx = NewContext();
        var deA = await SeedSignerAsync(ctx, "Ana de A", owner: A);
        await AsociarAsync(ctx, deA, Ot, B);

        (await Directorio(ctx).GetCandidatesAsync(OtroOt, B, null, Ct)).Should().BeEmpty("otro organismo");
        (await Directorio(ctx).GetCandidatesAsync(Ot, C, null, Ct)).Should().BeEmpty("asociado a B, no a C");
    }

    [Fact]
    public async Task ElAsociadoDebeSeguirSiendoDelOrganismo_ConVinculoPropioActivo()
    {
        await using var ctx = NewContext();
        var deA = await SeedSignerAsync(ctx, "Ana de A", owner: A, linkActive: false);
        await AsociarAsync(ctx, deA, Ot, B);

        (await Directorio(ctx).GetCandidatesAsync(Ot, B, null, Ct)).Should().BeEmpty(
            "sin vínculo activo con su compañía en el organismo ya no firma allí");
    }

    [Fact]
    public async Task UnMandatarioQueYaEsPropioDeB_NoApareceDosVecesComoAsociado()
    {
        await using var ctx = NewContext();
        var id = await SeedSignerAsync(ctx, "Ana", owner: B, scope: "compania");
        await AsociarAsync(ctx, id, Ot, B);

        var candidatos = await Directorio(ctx).GetCandidatesAsync(Ot, B, null, Ct);

        candidatos.Should().ContainSingle().Which.Origen.Should().Be(MandateSignerOrigins.Compania);
    }

    [Fact]
    public async Task DosMandatariosDeCompaniasDistintasAsociadosAB_SalenLosDos_ParaQueElOtElija()
    {
        await using var ctx = NewContext();
        var deA = await SeedSignerAsync(ctx, "Ana de A", owner: A);
        var deC = await SeedSignerAsync(ctx, "Carlos de C", owner: C, documento: "3");
        await AsociarAsync(ctx, deA, Ot, B);
        await AsociarAsync(ctx, deC, Ot, B);

        var candidatos = await Directorio(ctx).GetCandidatesAsync(Ot, B, null, Ct);

        candidatos.Select(c => c.Id).Should().BeEquivalentTo([deA, deC]);
        candidatos.Should().OnlyContain(c => c.Origen == MandateSignerOrigins.Asociado);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-13180-{Guid.NewGuid()}")
            .Options);

    private static async Task<Guid> SeedSignerAsync(
        FlitDbContext ctx,
        string nombre,
        Guid owner,
        string scope = "organismo",
        bool isActive = true,
        bool deleted = false,
        bool linkActive = true,
        string documento = "1")
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = Ot,
            FullName = nombre,
            DocumentType = "CC",
            DocumentNumber = documento + id.ToString("N")[..6],
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = isActive,
            DeletedAt = deleted ? now : null,
            SignerModel = "natural",
            SignatureMethod = "biometria",
            CreatedAt = now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Ot, CompanyTenantId = owner,
            IsActive = linkActive, ConfiguredByScope = scope, CreatedAt = now,
        });
        await ctx.SaveChangesAsync(Ct);
        return id;
    }

    private static async Task AsociarAsync(
        FlitDbContext ctx, Guid signer, Guid office, Guid company, bool activa = true)
    {
        ctx.MandateSignerAssociatedCompanies.Add(new MandateSignerAssociatedCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = signer, TransitOfficeId = office,
            AssociatedCompanyTenantId = company, IsActive = activa, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
    }

    private static MandateSignerDirectory Directorio(FlitDbContext ctx)
    {
        var otStatusReader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        otStatusReader.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TransitOfficeOperationalStatusItem?)null);
        var identityRepo = Substitute.For<IProcedureInstanceRepository>();
        identityRepo.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Flit.Tramites.Domain.Entities.ProcedureInstanceBiometricValidation>(), 0, false));
        return new MandateSignerDirectory(ctx, otStatusReader, new IdentityVigenciaPorDocumentoResolver(identityRepo));
    }
}
