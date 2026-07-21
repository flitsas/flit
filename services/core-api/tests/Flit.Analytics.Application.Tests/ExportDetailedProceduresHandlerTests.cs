using Flit.Analytics.Application.Queries;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>Tests de validación del export del reporte detallado (HU #10816).</summary>
public sealed class ExportDetailedProceduresHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);
    private static readonly Guid ProcedureType = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Validate_ConFiltrosValidos_DevuelveFilter()
    {
        var (filter, error) = ExportDetailedProceduresHandler.Validate(
            new ExportDetailedProceduresQuery(Tenant, From, To, null, ProcedureType, null, "aprobado", null, null, null, null, true));

        error.Should().BeNull();
        filter.Should().NotBeNull();
        filter!.IsLeasing.Should().BeTrue();
    }

    [Fact]
    public void Validate_SoloRangoFechas_DevuelveFilter()
    {
        // Sin más filtros que el rango de fechas la exportación es válida (todos los trámites).
        var (filter, error) = ExportDetailedProceduresHandler.Validate(
            new ExportDetailedProceduresQuery(Tenant, From, To, null, null, null, null, null, null, null, null, null));

        error.Should().BeNull();
        filter.Should().NotBeNull();
    }

    [Fact]
    public void Validate_RangoInvalido_DevuelveInvalidRange()
    {
        var (filter, error) = ExportDetailedProceduresHandler.Validate(
            new ExportDetailedProceduresQuery(Tenant, To, From, null, null, null, null, null, null, null, null, null));

        filter.Should().BeNull();
        error.Should().Be("invalid_range");
    }
}
