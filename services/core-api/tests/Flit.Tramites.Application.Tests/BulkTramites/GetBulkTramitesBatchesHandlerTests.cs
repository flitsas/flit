using Flit.Tramites.Application.BulkTramites.Query;
using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>
/// Proyección del resumen del lote (HU #12524). Nació por un rótulo visto en vivo: las filas
/// rechazadas al subir el archivo salían «En cola» para siempre, porque nunca reciben outcome.
/// </summary>
public sealed class GetBulkTramitesBatchesHandlerTests
{
    private readonly IBulkTramitesBatchRepository _repository = Substitute.For<IBulkTramitesBatchRepository>();

    [Fact]
    public async Task FilaConErrorEstructural_SeProyectaComoNoCreada_ConSuMotivo()
    {
        var tenant = Guid.NewGuid();
        var batch = new BulkTramitesBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            CreatedByUserId = Guid.NewGuid(),
            TemplateType = "traspaso",
            Status = BulkTramitesBatchStatus.Completed,
            SourceFilename = "lote.xlsx",
            TotalRows = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            Rows =
            [
                new BulkTramitesBatchRow
                {
                    Id = Guid.NewGuid(), RowNumber = 1, ValuesJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
                    StructuralErrorCode = "porcentajes_no_suman_100",
                },
                new BulkTramitesBatchRow
                {
                    Id = Guid.NewGuid(), RowNumber = 2, ValuesJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
                    Outcome = BulkTramitesRowOutcome.Created, ProcedureInstanceId = Guid.NewGuid(),
                },
            ],
        };
        _repository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        var detalle = await new GetBulkTramitesBatchesHandler(_repository)
            .GetAsync(tenant, batch.Id, TestContext.Current.CancellationToken);

        var rechazada = detalle!.Rows.Single(r => r.RowNumber == 1);
        rechazada.Outcome.Should().Be(BulkTramitesRowOutcome.NotCreated);
        rechazada.Motivo.Should().Be("porcentajes_no_suman_100");
        detalle.Batch.Counts.NotCreated.Should().Be(1);
        detalle.Batch.Counts.Pendientes.Should().Be(0);
    }
}
