using System.Text.Json;
using Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13179b (Feature #13119 F7) — la lectura del mandatario devuelve, para cada compañía asociada, su id, nombre y
/// NIT (y nada más, Ley 1581), con UNA sola consulta al directorio de compañías gestoras y conservando los campos
/// actuales. Sirve al formulario del OT / Super Admin, que antes recibía solo ids.
/// <para>Uso de ejemplo: <c>GET .../mandate-signers</c> trae
/// <c>officeCompanies[].associatedCompanies = [{ id, name, nit }]</c> además de <c>associatedCompanyTenantIds</c>.</para>
/// </summary>
public sealed class MandatarioAsociadasLecturaTests
{
    private static readonly Guid Ot1 = Guid.NewGuid();
    private static readonly Guid Ot2 = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Hija1 = Guid.NewGuid();
    private static readonly Guid Hija2 = Guid.NewGuid();
    private static readonly Guid Hija3 = Guid.NewGuid();
    private static readonly Guid OtTenant = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ElOt_RecibeLasAsociadasConIdNombreYNit_YConservaLosIds()
    {
        await using var ctx = await SeedAsync();
        var handler = new ListMandateSignersHandler(new DbMandateSignerReader(ctx), new ManagingCompanyDirectory(ctx));

        var lista = await handler.HandleAsync(
            new ListMandateSignersQuery { TransitOfficeId = Ot1, Visibility = OtCompanyVisibility.WholeNetwork }, Ct);

        var porOrganismo = lista.Single(s => s.FullName == "Ana").OfficeCompanies!.Single(o => o.TransitOfficeId == Ot1);
        porOrganismo.AssociatedCompanyTenantIds.Should().BeEquivalentTo([Hija1, Hija2]);
        porOrganismo.AssociatedCompanies.Should().BeEquivalentTo(
        [
            new AssociableCompany(Hija1, "Hija Uno SAS", "900000011-1"),
            new AssociableCompany(Hija2, "Hija Dos SAS", "900000012-1"),
        ]);
    }

    [Fact]
    public async Task LaCompania_RecibeLasAsociadasConIdNombreYNit()
    {
        await using var ctx = await SeedAsync();
        var handler = new ListCompanyMandateSignersHandler(new DbMandateSignerReader(ctx), new ManagingCompanyDirectory(ctx));

        var lista = await handler.HandleAsync(Owner, Ct);

        var o1 = lista.Single(s => s.FullName == "Ana").OfficeCompanies!.Single(o => o.TransitOfficeId == Ot1);
        o1.AssociatedCompanies!.Select(c => c.Name).Should().BeEquivalentTo("Hija Uno SAS", "Hija Dos SAS");
        o1.AssociatedCompanyTenantIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task UnaSolaConsultaAlDirectorio_AunqueHayaVariosMandatariosYOrganismos()
    {
        var directory = Substitute.For<IManagingCompanyDirectory>();
        directory.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ManagingCompanyRow(Hija1, "Hija Uno SAS", "900000011-1", DateTimeOffset.UtcNow, true)]);
        var reader = Substitute.For<IMandateSignerReader>();
        reader.ListByCompanyAsync(Owner, Arg.Any<CancellationToken>()).Returns(
        [
            Signer("A", new MandateSignerOfficeCompanies(Ot1, [Hija1]), new MandateSignerOfficeCompanies(Ot2, [Hija1, Hija2])),
            Signer("B", new MandateSignerOfficeCompanies(Ot1, [Hija1])),
            Signer("C", new MandateSignerOfficeCompanies(Ot2, [Hija3])),
        ]);

        var lista = await new ListCompanyMandateSignersHandler(reader, directory).HandleAsync(Owner, Ct);

        await directory.Received(1).ListByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 3 && ids.Contains(Hija1) && ids.Contains(Hija2) && ids.Contains(Hija3)),
            Arg.Any<CancellationToken>());
        await directory.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        lista.Should().HaveCount(3);
    }

    [Fact]
    public async Task UnIdQueYaNoEsCompaniaGestora_SeConservaSoloComoId()
    {
        var directory = Substitute.For<IManagingCompanyDirectory>();
        directory.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ManagingCompanyRow(Hija1, "Hija Uno SAS", "900000011-1", DateTimeOffset.UtcNow, true)]);
        var reader = Substitute.For<IMandateSignerReader>();
        reader.ListByCompanyAsync(Owner, Arg.Any<CancellationToken>()).Returns(
            [Signer("A", new MandateSignerOfficeCompanies(Ot1, [Hija1, Hija2]))]);

        var lista = await new ListCompanyMandateSignersHandler(reader, directory).HandleAsync(Owner, Ct);

        var o = lista.Single().OfficeCompanies!.Single();
        o.AssociatedCompanyTenantIds.Should().BeEquivalentTo([Hija1, Hija2]);
        o.AssociatedCompanies!.Select(c => c.Id).Should().Equal(Hija1);
    }

    [Fact]
    public async Task SinAsociaciones_NoSeConsultaElDirectorio_YSinDirectorioSeDevuelvenSoloLosIds()
    {
        var directory = Substitute.For<IManagingCompanyDirectory>();
        var reader = Substitute.For<IMandateSignerReader>();
        reader.ListByCompanyAsync(Owner, Arg.Any<CancellationToken>()).Returns([Signer("A")]);

        await new ListCompanyMandateSignersHandler(reader, directory).HandleAsync(Owner, Ct);
        await directory.DidNotReceiveWithAnyArgs().ListByIdsAsync(default!, default);

        reader.ListByCompanyAsync(Owner, Arg.Any<CancellationToken>()).Returns(
            [Signer("B", new MandateSignerOfficeCompanies(Ot1, [Hija1]))]);
        var sinDirectorio = await new ListCompanyMandateSignersHandler(reader).HandleAsync(Owner, Ct);
        sinDirectorio.Single().OfficeCompanies!.Single().AssociatedCompanies.Should().BeNull();
    }

    [Fact]
    public void LaRespuestaSoloExponeIdNombreYNit_DeCadaAsociada()
    {
        typeof(AssociableCompany).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("Id", "Name", "Nit");

        var json = JsonSerializer.Serialize(
            new MandateSignerOfficeCompanies(Ot1, [Hija1], [new AssociableCompany(Hija1, "Hija Uno SAS", "900000011-1")]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "transitOfficeId", "associatedCompanyTenantIds", "associatedCompanies");
        doc.RootElement.GetProperty("associatedCompanies")[0].EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("id", "name", "nit");
    }

    [Fact]
    public async Task ElDirectorio_ListByIds_DevuelveSoloLasPedidas_SinOrganismosNiPlataforma()
    {
        await using var ctx = await SeedAsync();
        var plataforma = Guid.NewGuid();
        ctx.Tenants.Add(Tenant(plataforma, "Plataforma", "1-1", type: "FLIT"));
        await ctx.SaveChangesAsync(Ct);
        var directory = new ManagingCompanyDirectory(ctx);

        var filas = await directory.ListByIdsAsync([Hija1, OtTenant, plataforma, Guid.NewGuid()], Ct);

        filas.Select(f => f.Id).Should().Equal(Hija1);
        (await directory.ListByIdsAsync([], Ct)).Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static MandateSignerItem Signer(string nombre, params MandateSignerOfficeCompanies[] offices) =>
        new()
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = Ot1,
            FullName = nombre,
            DocumentType = "CC",
            IntegrityHash = "h",
            RegisteredAt = DateTimeOffset.UtcNow,
            IsActive = true,
            OfficeCompanies = offices,
        };

    private static Tenant Tenant(Guid id, string name, string nit, bool active = true, string type = "CONCESIONARIO") => new()
    {
        Id = id, Code = "T-" + id.ToString("N")[..8], LegalName = name, TaxId = nit, TenantType = type,
        IsActive = active, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<FlitDbContext> SeedAsync()
    {
        var ctx = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-13179b-{Guid.NewGuid()}").Options);
        ctx.Tenants.AddRange(
            Tenant(Owner, "Dueña SAS", "900000001-1"),
            Tenant(Hija1, "Hija Uno SAS", "900000011-1"),
            Tenant(Hija2, "Hija Dos SAS", "900000012-1"),
            Tenant(OtTenant, "Tránsito", "800000001-1"));
        ctx.TransitOfficeProfiles.Add(new Flit.Infrastructure.Persistence.Entities.Admin.TransitOfficeProfile
        {
            TenantId = OtTenant, TransitOfficeId = Ot1,
        });
        await ctx.SaveChangesAsync(Ct);

        var repo = new MandateSignerRepository(ctx);
        await repo.CreateAsync(
            new CreateMandateSignerData(
                Ot1, OtTenant, "Ana", "1020304050", new string('a', 64), DateTimeOffset.UtcNow, [Owner], null, null,
                TransitOfficeIds: [Ot1, Ot2],
                OfficeCompanies: [new MandateSignerOfficeCompanies(Ot1, [Hija1, Hija2])],
                SignatureMethod: "biometria"),
            Ct);
        return ctx;
    }
}
