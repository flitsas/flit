using Flit.Admin.Application.DocumentRequirements.PreviewInformativos;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirements;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.DocumentRequirements;

public sealed class PreviewDocumentosInformativosHandlerTests
{
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocA = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");

    [Fact]
    public async Task TipoDesconocido_ReportaQueNoExiste()
    {
        // ADR-0050 — se acepta cualquier `code` del catálogo, así que un valor desconocido ya no es
        // "modalidad inválida" sino un tipo que no existe. El diagnóstico es más preciso: antes
        // cualquier trámite fuera de las dos modalidades se rechazaba por el vocabulario, no por el
        // dato.
        var catalog = Substitute.For<IProcedureTypeCatalog>();
        catalog.ListActivePublishedAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureTypeCatalogItem>());
        var handler = new PreviewDocumentosInformativosHandler(
            catalog,
            Substitute.For<IResolvedDocumentMatrixResolver>());

        var result = await handler.HandleAsync(
            new PreviewDocumentosInformativosQuery { Modalidad = "NO_EXISTE" },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PreviewDocumentosInformativosOutcome.ProcedureTypeNotFound);
    }

    [Fact]
    public async Task SinModalidad_SigueSiendoInvalida()
    {
        var catalog = Substitute.For<IProcedureTypeCatalog>();
        catalog.ListActivePublishedAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureTypeCatalogItem>());
        var handler = new PreviewDocumentosInformativosHandler(
            catalog,
            Substitute.For<IResolvedDocumentMatrixResolver>());

        var result = await handler.HandleAsync(
            new PreviewDocumentosInformativosQuery { Modalidad = "  " },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PreviewDocumentosInformativosOutcome.ModalidadInvalida);
    }

    [Fact]
    public async Task ProcedureTypeNotFound_WhenCatalogEmpty()
    {
        var catalog = Substitute.For<IProcedureTypeCatalog>();
        catalog.ListActivePublishedAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureTypeCatalogItem>());

        var handler = new PreviewDocumentosInformativosHandler(
            catalog,
            Substitute.For<IResolvedDocumentMatrixResolver>());

        var result = await handler.HandleAsync(
            new PreviewDocumentosInformativosQuery { Modalidad = "matricula_inicial" },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PreviewDocumentosInformativosOutcome.ProcedureTypeNotFound);
    }

    [Fact]
    public async Task Resolved_MapsMatriculaAndReturnsOrderedItems()
    {
        var catalog = Substitute.For<IProcedureTypeCatalog>();
        catalog.ListActivePublishedAsync(Arg.Any<CancellationToken>())
            .Returns([new ProcedureTypeCatalogItem(ProcedureTypeId, "MATRICULA_NUEVA", "Matrícula")]);

        var resolver = Substitute.For<IResolvedDocumentMatrixResolver>();
        resolver.ResolveAsync(ProcedureTypeId, null, Arg.Any<CancellationToken>())
            .Returns([
                new ResolvedDocumentMatrixItem
                {
                    DocumentTypeId = DocA,
                    Codigo = "FACTURA",
                    Nombre = "Factura",
                    Obligatorio = true,
                    OrdenResuelto = 1,
                    NivelAplicado = "DEFAULT",
                },
            ]);

        var handler = new PreviewDocumentosInformativosHandler(catalog, resolver);

        var result = await handler.HandleAsync(
            new PreviewDocumentosInformativosQuery { Modalidad = "matricula_inicial" },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PreviewDocumentosInformativosOutcome.Resolved);
        result.TipologiaCodigo.Should().Be("MATRICULA_NUEVA");
        result.ProcedureTypeId.Should().Be(ProcedureTypeId);
        result.Items.Should().ContainSingle();
        result.Items[0].Codigo.Should().Be("FACTURA");
        result.Items[0].Obligatorio.Should().BeTrue();
    }

    [Fact]
    public async Task Resolved_MapsTraspasoToTraspasoStandard()
    {
        var catalog = Substitute.For<IProcedureTypeCatalog>();
        catalog.ListActivePublishedAsync(Arg.Any<CancellationToken>())
            .Returns([new ProcedureTypeCatalogItem(ProcedureTypeId, "TRASPASO_STANDARD", "Traspaso")]);

        var resolver = Substitute.For<IResolvedDocumentMatrixResolver>();
        resolver.ResolveAsync(ProcedureTypeId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var handler = new PreviewDocumentosInformativosHandler(catalog, resolver);
        var ot = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

        var result = await handler.HandleAsync(
            new PreviewDocumentosInformativosQuery { Modalidad = "traspaso", TransitOfficeId = ot },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PreviewDocumentosInformativosOutcome.Resolved);
        result.TipologiaCodigo.Should().Be("TRASPASO_STANDARD");
        await resolver.Received(1).ResolveAsync(ProcedureTypeId, ot, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcedureTypeNotFound_WhenCatalogHasWrongCodes()
    {
        var catalog = Substitute.For<IProcedureTypeCatalog>();
        // Catálogo con tipología runtime (incorrecta) en vez del code canónico.
        catalog.ListActivePublishedAsync(Arg.Any<CancellationToken>())
            .Returns([new ProcedureTypeCatalogItem(ProcedureTypeId, "matricula_inicial", "Matrícula")]);

        var handler = new PreviewDocumentosInformativosHandler(
            catalog,
            Substitute.For<IResolvedDocumentMatrixResolver>());

        var result = await handler.HandleAsync(
            new PreviewDocumentosInformativosQuery { Modalidad = "matricula_inicial" },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(PreviewDocumentosInformativosOutcome.ProcedureTypeNotFound);
    }
}
