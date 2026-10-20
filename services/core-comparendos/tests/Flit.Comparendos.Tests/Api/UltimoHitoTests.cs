using Flit.Comparendos.Api.Modulos.Bandeja;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Matriz 0.28 — etiqueta «Último hito de notificación» (§4.9): el DP y el aviso 4 también son hitos.</summary>
public sealed class UltimoHitoTests
{
    [Theory]
    [InlineData(EstadoNotificacionConductor.SinNotificar, EstadoCambioContraventor.SinSolicitar, false, EstadoGestion.SinGestion, "SinProcesar")]
    [InlineData(EstadoNotificacionConductor.SinNotificar, EstadoCambioContraventor.NoAplica, true, EstadoGestion.DpEnviado, "DpEnviado")]
    [InlineData(EstadoNotificacionConductor.SinNotificar, EstadoCambioContraventor.NoAplica, false, EstadoGestion.RespuestaOt, "DpEnviado")]
    [InlineData(EstadoNotificacionConductor.Aviso1, EstadoCambioContraventor.SinSolicitar, true, EstadoGestion.DpEnviado, "Aviso1")]
    [InlineData(EstadoNotificacionConductor.Aviso3, EstadoCambioContraventor.SinSolicitar, false, EstadoGestion.SinGestion, "Aviso3")]
    [InlineData(EstadoNotificacionConductor.Aviso2, EstadoCambioContraventor.Solicitado, false, EstadoGestion.SinGestion, "Aviso4")]
    [InlineData(EstadoNotificacionConductor.SinNotificar, EstadoCambioContraventor.AceptadoOt, false, EstadoGestion.SinGestion, "Aviso4")]
    public void Hito(EstadoNotificacionConductor aviso, EstadoCambioContraventor cambio, bool tieneDp, EstadoGestion gestion, string esperado)
    {
        var c = new Comparendo { EstadoNotificacionConductor = aviso, EstadoCambioContraventor = cambio, TieneDp = tieneDp, EstadoGestion = gestion };
        ConsultaBandeja.UltimoHito(c).Should().Be(esperado);
        ConsultaBandeja.TextoHito(esperado).Should().NotBeNullOrWhiteSpace();
    }
}
