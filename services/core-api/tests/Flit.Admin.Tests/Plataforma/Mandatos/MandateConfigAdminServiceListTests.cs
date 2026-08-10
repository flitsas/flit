using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Tests.TestDoubles;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// Listado SuperAdmin de mandatos: solo OT activos en FLIT (tenant OT + is_active).
/// </summary>
public sealed class MandateConfigAdminServiceListTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly Guid ActiveId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid InactiveId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid NoTenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task ListAsync_SoloIncluyeOtConTenantActivo()
    {
        await using var db = NewDb();
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.All.Returns(
        [
            new TransitOfficeEntry(ActiveId, "5631000", "Sabaneta", "05", "05631"),
            new TransitOfficeEntry(InactiveId, "5088000", "Bello", "05", "05088"),
            new TransitOfficeEntry(NoTenantId, "5266000", "Envigado", "05", "05266"),
        ]);

        var status = new StubTransitOfficeOperationalStatusReader()
            .Set(ActiveId, hasTenant: true, estadoActivo: true)
            .Set(InactiveId, hasTenant: true, estadoActivo: false)
            .Set(NoTenantId, hasTenant: false, estadoActivo: null);

        var service = new MandateConfigAdminService(
            db,
            catalog,
            status,
            Substitute.For<IDocumentOcrAnalyzer>(),
            Substitute.For<IMandateTemplateStorage>());

        var items = await service.ListAsync(Ct);

        items.Should().ContainSingle();
        items[0].OfficeId.Should().Be(ActiveId);
        items[0].Name.Should().Be("Sabaneta");
    }

    private static FlitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"mandate-list-{Guid.NewGuid()}")
            .Options);
}
