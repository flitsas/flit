using Flit.Api.Grpc;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// Carpeta del trámite ICT: qué consolidado se le entrega al cliente según el estado del trámite y si la
/// carpeta se encola al materializar el borrador.
/// </summary>
public sealed class IctCarpetaEntregaTests
{
    [Fact]
    public void Aprobado_EntregaElDefinitivo_ConOSinPreliminar()
    {
        IctOrchestrationService.DecidirEntregaIct("aprobado", permitirPreliminar: false).Should().Be(((string?)null, true));
        IctOrchestrationService.DecidirEntregaIct("aprobado", permitirPreliminar: true).Should().Be(((string?)null, true));
    }

    [Theory]
    [InlineData("borrador")]
    [InlineData("radicado")]
    [InlineData("anulado")]
    public void SinPreliminar_ComoSiempre_SoloAprobado(string status)
    {
        IctOrchestrationService.DecidirEntregaIct(status, permitirPreliminar: false).Should().Be(("not_approved", false));
    }

    [Theory]
    [InlineData("borrador")]
    [InlineData("radicado")]
    [InlineData("rechazado")]
    public void ConPreliminar_AntesDeAprobado_EntregaLaVersionPreliminar(string status)
    {
        IctOrchestrationService.DecidirEntregaIct(status, permitirPreliminar: true).Should().Be(((string?)null, false));
    }

    [Fact]
    public void ConPreliminar_ElAnuladoNoSeEntrega()
    {
        IctOrchestrationService.DecidirEntregaIct("anulado", permitirPreliminar: true).Should().Be(("anulado", false));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void LaCarpetaAlMaterializar_EstaActivaPorDefecto_YSePuedeApagar(string? valor, bool esperado)
    {
        var datos = new Dictionary<string, string?>();
        if (valor is not null)
        {
            datos[IctOrchestrationService.CarpetaAlMaterializarKey] = valor;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(datos).Build();

        IctOrchestrationService.CarpetaAlMaterializarActiva(configuration).Should().Be(esperado);
    }
}
