using Flit.Analytics.Application.Queries;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>
/// Uso de ejemplo:
/// var (filter, error) = ExportProceduresExcelHandler.Validate(new ExportProceduresExcelQuery(tenant, from, to, "TRASPASOS", null));
/// Cubre HU #10245: validación de rango (→ 400) y normalización de filtros del export.
/// </summary>
public sealed class ExportProceduresExcelHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    [Fact] // Happy path: normaliza categoría a minúsculas y recorta status
    public void Validate_RangoValido_NormalizaFiltros()
    {
        var (filter, error) = ExportProceduresExcelHandler.Validate(
            new ExportProceduresExcelQuery(Tenant, From, To, "TRASPASOS", " submitted "));

        error.Should().BeNull();
        filter.Should().NotBeNull();
        filter!.Category.Should().Be("traspasos");
        filter.Status.Should().Be("submitted");
        filter.TenantId.Should().Be(Tenant);
    }

    [Fact] // Edge: filtros en blanco → null (sin filtro)
    public void Validate_FiltrosEnBlanco_QuedanNulos()
    {
        var (filter, error) = ExportProceduresExcelHandler.Validate(
            new ExportProceduresExcelQuery(Tenant, From, To, "  ", null));

        error.Should().BeNull();
        filter!.Category.Should().BeNull();
        filter.Status.Should().BeNull();
    }

    [Fact] // Edge: rango inválido → invalid_range (endpoint responde 400 antes de escribir el archivo)
    public void Validate_FromPosteriorATo_DevuelveInvalidRange()
    {
        var (filter, error) = ExportProceduresExcelHandler.Validate(
            new ExportProceduresExcelQuery(Tenant, To, From, null, null));

        filter.Should().BeNull();
        error.Should().Be("invalid_range");
    }
}
