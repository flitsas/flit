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
/// HU #13142 (ADR-0066) — el directorio entrega origen, modelo y forma de firma efectiva, y
/// <c>GetByIdAsync</c> filtra la baja lógica salvo que se pida conservar la referencia (trámites ya firmados).
/// </summary>
public sealed class MandateSignerDirectoryPrelacionTests
{
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();

    private static MandateSignerDirectory Directory(FlitDbContext ctx)
    {
        var reader = Substitute.For<ITransitOfficeOperationalStatusReader>();
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListBiometricValidationsByPersonAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Flit.Tramites.Domain.Entities.ProcedureInstanceBiometricValidation>(), 0, false));
        return new MandateSignerDirectory(ctx, reader, new IdentityVigenciaPorDocumentoResolver(repo));
    }

    private static FlitDbContext Ctx() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-mandate-prelacion-{Guid.NewGuid()}").Options);

    private static async Task<Guid> AddSignerAsync(
        FlitDbContext ctx, string model = "natural", string? method = null, Guid? vault = null,
        DateTimeOffset? deletedAt = null, string? origin = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id, TransitOfficeId = Ot, FullName = "Firmante", DocumentType = "CC", DocumentNumber = "1020",
            IntegrityHash = new string('a', 64), RegisteredAt = now, IsActive = true, CreatedAt = now,
            SignerModel = model, SignatureMethod = method, SignatureVaultId = vault, DeletedAt = deletedAt,
        });
        if (origin is not null)
        {
            ctx.MandateSignerCompanies.Add(new MandateSignerCompany
            {
                Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = Ot, CompanyTenantId = Gestora,
                IsActive = true, ConfiguredByScope = origin, CreatedAt = now,
            });
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    [Theory]
    [InlineData("organismo")]
    [InlineData("super_admin")]
    [InlineData("compania")]
    public async Task GetCandidates_TraeElOrigenDelVinculo(string origen)
    {
        await using var ctx = Ctx();
        await AddSignerAsync(ctx, origin: origen);

        var c = (await Directory(ctx).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .Should().ContainSingle().Subject;

        c.Origen.Should().Be(origen);
        c.SignerModel.Should().Be("natural");
    }

    [Fact]
    public async Task GetCandidates_FormaDeFirmaEfectiva_ExplicitaInferidaYNulaEnJuridicaYFormatoEnBlanco()
    {
        await using var ctx = Ctx();
        var conBaulExplicito = await AddSignerAsync(ctx, method: "baul", origin: "organismo");
        var legadoConVault = await AddSignerAsync(ctx, vault: Guid.NewGuid(), origin: "organismo");
        var legadoSinVault = await AddSignerAsync(ctx, origin: "organismo");
        var juridica = await AddSignerAsync(ctx, model: "juridica", origin: "organismo");
        var blanco = await AddSignerAsync(ctx, model: "formato_blanco", origin: "organismo");

        var porId = (await Directory(ctx).GetCandidatesAsync(Ot, Gestora, null, TestContext.Current.CancellationToken))
            .ToDictionary(c => c.Id);

        porId[conBaulExplicito].SignatureMethod.Should().Be("baul");
        porId[legadoConVault].SignatureMethod.Should().Be("baul");
        porId[legadoSinVault].SignatureMethod.Should().Be("biometria");
        porId[juridica].SignatureMethod.Should().BeNull();
        porId[blanco].SignatureMethod.Should().BeNull();
        porId[conBaulExplicito].BaulVigente.Should().BeFalse("el directorio no consulta el baúl: lo completa Application");
    }

    [Fact]
    public async Task GetById_ConBajaLogica_NoSeDevuelve_PorDefecto()
    {
        await using var ctx = Ctx();
        var eliminado = await AddSignerAsync(ctx, deletedAt: DateTimeOffset.UtcNow);

        (await Directory(ctx).GetByIdAsync(eliminado, TestContext.Current.CancellationToken)).Should().BeNull();
        (await Directory(ctx).GetByIdAsync(eliminado, false, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task GetById_ConIncluirEliminados_ConservaLaReferenciaMarcadaEliminado()
    {
        await using var ctx = Ctx();
        var eliminado = await AddSignerAsync(ctx, deletedAt: DateTimeOffset.UtcNow);

        var c = await Directory(ctx).GetByIdAsync(eliminado, true, TestContext.Current.CancellationToken);

        c.Should().NotBeNull();
        c!.Eliminado.Should().BeTrue();
        MandateSignerDefaultResolver.MotivoDescarte(c).Should().Be("mandatario_eliminado");
    }

    [Fact]
    public async Task GetById_Vigente_NoSeMarcaEliminado_YTraeModeloYFormaDeFirma()
    {
        await using var ctx = Ctx();
        var id = await AddSignerAsync(ctx, method: "baul");

        var c = await Directory(ctx).GetByIdAsync(id, TestContext.Current.CancellationToken);

        c!.Eliminado.Should().BeFalse();
        c.SignerModel.Should().Be("natural");
        c.SignatureMethod.Should().Be("baul");
    }
}
