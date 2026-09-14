using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Application.BulkTramites.Parsing;
using Flit.Tramites.Application.BulkTramites.SubmitBatch;
using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>Orquestación de la carga de un lote (HU #12522, AC1/AC2/AC3): parser mockeado, repositorio mockeado.</summary>
public sealed class SubmitBulkTramitesBatchHandlerTests
{
    private readonly IBulkTramitesXlsxParser _parser = Substitute.For<IBulkTramitesXlsxParser>();
    private readonly IBulkTramitesBatchRepository _repository = Substitute.For<IBulkTramitesBatchRepository>();
    private readonly SubmitBulkTramitesBatchHandler _handler;

    public SubmitBulkTramitesBatchHandlerTests()
    {
        _handler = new SubmitBulkTramitesBatchHandler(_parser, _repository, TimeProvider.System);
    }

    private static SubmitBulkTramitesBatchCommand Command(BulkTramitesTemplateType tipo = BulkTramitesTemplateType.Matricula) =>
        new(Guid.NewGuid(), Guid.NewGuid(), tipo, "lote.xlsx", new MemoryStream());

    [Fact]
    public async Task ArchivoValido_PersisteElLoteYSusFilas_YRetornaAccepted()
    {
        var filaBuena = new BulkTramitesParsedRow(1, new Dictionary<string, string?> { ["placa"] = "ABC123" }, null);
        var filaConError = new BulkTramitesParsedRow(
            2, new Dictionary<string, string?> { ["placa"] = "DEF456" }, "porcentajes_no_suman_100");

        _parser.Parse(Arg.Any<BulkTramitesTemplateType>(), Arg.Any<Stream>())
            .Returns(new BulkTramitesParseResult(null, [filaBuena, filaConError]));

        BulkTramitesBatch? guardado = null;
        _repository.AddAsync(Arg.Do<BulkTramitesBatch>(b => guardado = b), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var resultado = await _handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        resultado.Outcome.Should().Be(SubmitBulkTramitesBatchOutcome.Accepted);
        resultado.TotalRows.Should().Be(2);
        resultado.RowsWithStructuralErrors.Should().Be(1);

        guardado.Should().NotBeNull();
        guardado!.Rows.Should().HaveCount(2);
        guardado.Rows.Single(r => r.RowNumber == 2).StructuralErrorCode.Should().Be("porcentajes_no_suman_100");
        guardado.Status.Should().Be(BulkTramitesBatchStatus.Queued);

        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArchivoConEstructuraInvalida_NoPersisteNada_YRetornaElMotivo()
    {
        _parser.Parse(Arg.Any<BulkTramitesTemplateType>(), Arg.Any<Stream>())
            .Returns(BulkTramitesParseResult.Rejected(BulkTramitesFileError.TemplateInvalid));

        var resultado = await _handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        resultado.Outcome.Should().Be(SubmitBulkTramitesBatchOutcome.Rejected);
        resultado.Error.Should().Be(BulkTramitesFileError.TemplateInvalid);

        await _repository.DidNotReceive().AddAsync(Arg.Any<BulkTramitesBatch>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArchivoSinFilasDeDatos_SeRechazaComoVacio()
    {
        _parser.Parse(Arg.Any<BulkTramitesTemplateType>(), Arg.Any<Stream>())
            .Returns(new BulkTramitesParseResult(null, []));

        var resultado = await _handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        resultado.Outcome.Should().Be(SubmitBulkTramitesBatchOutcome.Rejected);
        resultado.Error.Should().Be("empty_file");
    }
}
