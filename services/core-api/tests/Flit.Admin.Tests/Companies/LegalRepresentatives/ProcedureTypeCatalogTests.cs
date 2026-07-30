using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests del catálogo de tipos de trámite para selección en el admin (<see cref="ProcedureTypeCatalog"/>,
/// corrección del error <c>tipo_tramite_inexistente</c>): <c>ListActivePublishedAsync</c> debe devolver
/// SOLO los tipos activos y publicados con sus IDs reales del catálogo. El frontend no puede hardcodear
/// ids porque los seeds los generan con <c>uuidv7()</c> (no deterministas por BD/entorno).
/// </summary>
public sealed class ProcedureTypeCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-ptype-catalog-{Guid.NewGuid()}")
            .Options);

    private static ProcedureType Type(string code, string name, bool isActive, string status) =>
        new() { Id = Guid.NewGuid(), Code = code, Name = name, IsActive = isActive, PublicationStatus = status };

    [Fact]
    public async Task ListActivePublished_ReturnsOnlyActiveAndPublished_OrderedByName_WithRealIds()
    {
        await using var ctx = NewContext();
        var traspaso = Type("TRASPASO_STANDARD", "Traspaso", isActive: true, status: "published");
        var matricula = Type("MATRICULA_NUEVA", "Matrícula inicial", isActive: true, status: "published");
        var draft = Type("CAMBIO_COLOR", "Cambio de color", isActive: true, status: "draft");
        var inactivePublished = Type("TRASPASO_LEGACY", "Traspaso legacy", isActive: false, status: "published");
        ctx.ProcedureTypes.AddRange(traspaso, matricula, draft, inactivePublished);
        await ctx.SaveChangesAsync(Ct);

        var result = await new ProcedureTypeCatalog(ctx).ListActivePublishedAsync(Ct);

        // Solo activos + published, ordenados por nombre (Matrícula < Traspaso).
        result.Select(p => p.Code).Should().Equal("MATRICULA_NUEVA", "TRASPASO_STANDARD");
        result.Should().NotContain(p => p.Code == "CAMBIO_COLOR" || p.Code == "TRASPASO_LEGACY");

        // Proyecta el ID REAL del catálogo (no un valor hardcodeado) + código + nombre.
        var t = result.Single(p => p.Code == "TRASPASO_STANDARD");
        t.Id.Should().Be(traspaso.Id);
        t.Name.Should().Be("Traspaso");
    }

    [Fact]
    public async Task ListActivePublished_Empty_WhenNothingPublished()
    {
        await using var ctx = NewContext();
        ctx.ProcedureTypes.AddRange(
            Type("A", "A", isActive: true, status: "draft"),
            Type("B", "B", isActive: false, status: "published"));
        await ctx.SaveChangesAsync(Ct);

        var result = await new ProcedureTypeCatalog(ctx).ListActivePublishedAsync(Ct);

        result.Should().BeEmpty();
    }
}
