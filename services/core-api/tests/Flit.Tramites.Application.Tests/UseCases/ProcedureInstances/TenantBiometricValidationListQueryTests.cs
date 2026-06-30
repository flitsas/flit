using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Validación y mapeo de los filtros de vigencia del listado transversal de validaciones (HU #10350):
/// estado de vigencia (vigente/por_vencer/vencida) + rango de fin de vigencia (expiraDesde/expiraHasta).
/// </summary>
public sealed class TenantBiometricValidationListQueryTests
{
    [Theory]
    [InlineData(BiometricVigenciaEstados.Vigente)]
    [InlineData(BiometricVigenciaEstados.PorVencer)]
    [InlineData(BiometricVigenciaEstados.Vencida)]
    public void Validate_VigenciaEstadoValido_NoError(string estado)
    {
        var query = new TenantBiometricValidationListQuery(VigenciaEstado: estado);

        query.Validate().Should().BeNull();
    }

    [Fact]
    public void Validate_VigenciaEstadoInvalido_DevuelveError()
    {
        var query = new TenantBiometricValidationListQuery(VigenciaEstado: "caducada");

        query.Validate().Should().Contain("vigenciaEstado");
    }

    [Fact]
    public void Validate_ExpiraDesdeMayorQueHasta_DevuelveError()
    {
        var query = new TenantBiometricValidationListQuery(
            ExpiraDesde: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiraHasta: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        query.Validate().Should().Contain("expiraDesde");
    }

    [Fact]
    public void ToFilter_NormalizaVigenciaEstadoAMinusculasYMapeaRango()
    {
        var desde = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var hasta = new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero);
        var query = new TenantBiometricValidationListQuery(
            VigenciaEstado: "POR_VENCER", ExpiraDesde: desde, ExpiraHasta: hasta);

        var filter = query.ToFilter();

        filter.VigenciaEstado.Should().Be(BiometricVigenciaEstados.PorVencer);
        filter.ExpiraDesde.Should().Be(desde);
        filter.ExpiraHasta.Should().Be(hasta);
        filter.HasActiveFilters.Should().BeTrue();
    }

    [Fact]
    public void Validate_VenceEnDiasNegativo_DevuelveError()
    {
        var query = new TenantBiometricValidationListQuery(VenceEnDias: -1);

        query.Validate().Should().Contain("venceEnDias");
    }

    [Fact]
    public void ToFilter_VenceEnDias_SeMapeaYMarcaActivo()
    {
        var filter = new TenantBiometricValidationListQuery(VenceEnDias: 3).ToFilter();

        filter.VenceEnDias.Should().Be(3);
        filter.HasActiveFilters.Should().BeTrue();
    }

    [Fact]
    public void ToFilter_SinFiltrosDeVigencia_NoLosMarcaActivos()
    {
        var filter = new TenantBiometricValidationListQuery().ToFilter();

        filter.VigenciaEstado.Should().BeNull();
        filter.ExpiraDesde.Should().BeNull();
        filter.ExpiraHasta.Should().BeNull();
        filter.HasActiveFilters.Should().BeFalse();
    }
}
