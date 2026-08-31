using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #11701 — preview sintético: 400/404 sin pegarle a PostgreSQL.</summary>
public sealed class PreviewFurHandlerTests
{
    private readonly IProcedureTypeRepository _types = Substitute.For<IProcedureTypeRepository>();
    private readonly IFurDocumentGenerator _generator = new MockFurDocumentGenerator();
    private readonly IFurTemplateResolver _resolver = Substitute.For<IFurTemplateResolver>();
    private readonly PreviewFurHandler _handler;

    public PreviewFurHandlerTests()
    {
        _resolver.ResolveMatchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FurClassificationMatch(FurTemplateFormat.Automotor, "AUTOMOVIL"));
        _handler = new PreviewFurHandler(_types, _generator, _resolver);
    }

    [Fact]
    public async Task MissingProcedureTypeId_ReturnsBadRequest()
    {
        var result = await _handler.HandleAsync(
            new PreviewFurRequest(null, "natural", "natural", "carro"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.BadRequest);
        result.Error.Should().Be("procedure_type_id_requerido");
    }

    [Fact]
    public async Task InvalidVehicle_ReturnsBadRequest()
    {
        var result = await _handler.HandleAsync(
            new PreviewFurRequest(Guid.NewGuid(), "natural", "natural", "barco"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.BadRequest);
        result.Error.Should().Be("vehicle_kind_invalido");
    }

    [Fact]
    public async Task UnknownType_ReturnsNotFound()
    {
        _types.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureType?)null);

        var result = await _handler.HandleAsync(
            new PreviewFurRequest(Guid.NewGuid(), "natural", "natural", "carro"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.NotFound);
        result.Document.Should().BeNull();
    }

    [Fact]
    public async Task KnownType_ReturnsGeneratedDocument()
    {
        var id = Guid.NewGuid();
        _types.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = id,
            Code = "MATRICULA_NUEVA",
            Family = "MATRICULAS",
            Name = "Matrícula inicial",
        });

        var result = await _handler.HandleAsync(
            new PreviewFurRequest(id, "natural", "natural", "carro"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.Ok);
        result.Document.Should().NotBeNull();
        result.Document!.Tipo.Should().Be("fur");
    }

    [Fact]
    public async Task InvalidPrenda_ReturnsBadRequest()
    {
        var result = await _handler.HandleAsync(
            new PreviewFurRequest(Guid.NewGuid(), "natural", "natural", "carro", Prenda: "hipoteca"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.BadRequest);
        result.Error.Should().Be("prenda_invalida");
    }

    [Fact]
    public async Task FillAll_OmitsProcedureTypeAndReturnsDocument()
    {
        var result = await _handler.HandleAsync(
            new PreviewFurRequest(null, null, null, null, FillAll: true),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.Ok);
        result.Document.Should().NotBeNull();
        result.Document!.Filename.Should().Contain("FILLALL");
        await _types.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FillAll_WithTemplateFormatMaquinaria_ReturnsDocument()
    {
        var result = await _handler.HandleAsync(
            new PreviewFurRequest(null, null, null, null, FillAll: true, TemplateFormat: "MAQUINARIA"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.Ok);
        result.Document.Should().NotBeNull();
    }

    [Fact]
    public async Task FillAll_InvalidTemplateFormat_ReturnsBadRequest()
    {
        var result = await _handler.HandleAsync(
            new PreviewFurRequest(null, null, null, null, FillAll: true, TemplateFormat: "AUTOMOTRIZ"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.BadRequest);
        result.Error.Should().Be("template_format_invalido");
    }

    [Fact]
    public async Task VehicleClass_UsesCatalogClassification()
    {
        var id = Guid.NewGuid();
        _types.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = id,
            Code = "MATRICULA_NUEVA",
            Family = "MATRICULAS",
            Name = "Matrícula inicial",
        });
        _resolver.ResolveMatchAsync("EXCAVADORA", Arg.Any<CancellationToken>())
            .Returns(new FurClassificationMatch(FurTemplateFormat.Maquinaria, "CONSTRUCCION"));

        var result = await _handler.HandleAsync(
            new PreviewFurRequest(id, "natural", "natural", null, VehicleClass: "EXCAVADORA"),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(PreviewFurStatus.Ok);
        result.Document.Should().NotBeNull();
        await _resolver.Received(1).ResolveMatchAsync("EXCAVADORA", Arg.Any<CancellationToken>());
    }
}
