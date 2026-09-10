using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class MatrixChecklistItemsTests
{
    [Fact]
    public void Build_UsaNombreDelTipoEnDocumental_NoElCatalogoHardcodeado()
    {
        IReadOnlyList<ResolvedChecklistDoc> matriz =
        [
            new("paz_salvo", "Paz y Salvo de Impuestos", true, 50),
            new("compraventa", "Formato de Compraventa", true, 10),
        ];

        var items = MatrixChecklistItems.Build(TramiteTipologiaCatalog.CodigoTraspasoStandard, matriz);

        items.Should().Contain(i => i.DocTipo == "paz_salvo" && i.Label == "Paz y Salvo de Impuestos");
        items.Should().Contain(i => i.DocTipo == "compraventa" && i.Label == "Formato de Compraventa");
        items.Should().NotContain(i => i.Label == "Paz y salvo de impuestos y comparendos");
        items.Should().NotContain(i => i.Label == "Contrato de compraventa autenticado");
    }
}
