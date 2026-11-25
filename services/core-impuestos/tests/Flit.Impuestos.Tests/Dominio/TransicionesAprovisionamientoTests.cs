using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Aprovisionamiento;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;
using V = Flit.Impuestos.Domain.Aprovisionamiento.EventoAprovisionamiento;

namespace Flit.Impuestos.Tests.Dominio;

/// <summary>
/// HU #14694 (M05, F1a-CF10) — máquina de estados del aprovisionamiento por compañía (plan §3.1). Cada fila de la tabla
/// «Desde / Evento / Hacia» es un caso; cualquier otra combinación lanza <see cref="TransicionInvalida"/>.
/// </summary>
/// <example>
/// <code>
/// var r = TransicionesAprovisionamiento.Aplicar(SituacionAprovisionamiento.SinFila, EventoAprovisionamiento.ProductoHabilitado);
/// // r.Nueva.Estado == EstadoAprovisionamiento.Pendiente
/// </code>
/// </example>
public sealed class TransicionesAprovisionamientoTests
{
    private static SituacionAprovisionamiento En(E estado, E? previo = null, int intentos = 0, bool firma = false) =>
        new(estado, previo, intentos, firma);

    [Fact]
    public void Sin_fila_y_habilitado_pasa_a_pendiente()
    {
        var r = TransicionesAprovisionamiento.Aplicar(SituacionAprovisionamiento.SinFila, V.ProductoHabilitado);

        r.Nueva.Estado.Should().Be(E.Pendiente);
        r.Nueva.IntentosAutomaticos.Should().Be(0);
        r.Alerta.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(E.Pendiente)]
    [InlineData(E.Error)]
    [InlineData(E.RequiereCredenciales)]
    public void Suspendido_sin_estado_listo_previo_y_habilitado_vuelve_a_pendiente(E? previo)
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(E.Suspendido, previo), V.ProductoHabilitado);

        r.Nueva.Estado.Should().Be(E.Pendiente, "sin estado listo previo se evalúa de nuevo");
        r.Nueva.EstadoPrevio.Should().BeNull();
    }

    [Theory]
    [InlineData(E.Listo)]
    [InlineData(E.ListoSinWebhook)]
    public void Suspendido_con_estado_listo_previo_y_habilitado_recupera_el_previo_sin_alta(E previo)
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(E.Suspendido, previo, firma: true), V.ProductoHabilitado);

        r.Nueva.Estado.Should().Be(previo, "D-DESACT: reactivar no repite el alta");
        r.Nueva.EstadoPrevio.Should().BeNull();
        r.Nueva.FirmaConfirmada.Should().BeTrue("la firma confirmada antes de suspender se conserva");
    }

    [Theory]
    [InlineData(E.Pendiente)]
    [InlineData(E.Listo)]
    [InlineData(E.ListoSinWebhook)]
    [InlineData(E.Error)]
    [InlineData(E.Incierto)]
    [InlineData(E.RequiereCredenciales)]
    [InlineData(E.RequiereRevisionNit)]
    public void Cualquiera_distinto_de_suspendido_y_deshabilitado_suspende_guardando_el_previo(E desde)
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(desde), V.ProductoDeshabilitado);

        r.Nueva.Estado.Should().Be(E.Suspendido);
        r.Nueva.EstadoPrevio.Should().Be(desde);
    }

    [Theory]
    [InlineData(V.CredencialPropiaValidaConSecreto, E.Listo)]
    [InlineData(V.CredencialPropiaValidaSinSecreto, E.ListoSinWebhook)]
    [InlineData(V.CopiaDiagnosticoValida, E.ListoSinWebhook)]
    [InlineData(V.CredencialRechazada, E.RequiereCredenciales)]
    [InlineData(V.NitNoValido, E.RequiereRevisionNit)]
    [InlineData(V.Alta201, E.Listo)]
    [InlineData(V.Alta409, E.RequiereCredenciales)]
    [InlineData(V.Incierto, E.Incierto)]
    public void Desde_pendiente(V evento, E hacia)
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(E.Pendiente, intentos: 2), evento);

        r.Nueva.Estado.Should().Be(hacia);
        r.Nueva.FirmaConfirmada.Should().BeFalse("la firma queda sin confirmar hasta el primer webhook válido");
        r.Nueva.TipoError.Should().BeNull();
    }

    [Fact]
    public void Alta_con_datos_invalidos_es_error_de_datos_sin_alerta()
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(E.Pendiente), V.AltaDatosInvalidos);

        r.Nueva.Estado.Should().Be(E.Error);
        r.Nueva.TipoError.Should().Be(TipoErrorAprovisionamiento.Datos);
        r.Alerta.Should().BeFalse();
    }

    [Fact]
    public void Alta_401_o_404_es_error_de_ambiente_con_alerta()
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(E.Pendiente), V.AltaAmbienteInvalido);

        r.Nueva.Estado.Should().Be(E.Error);
        r.Nueva.TipoError.Should().Be(TipoErrorAprovisionamiento.Ambiente);
        r.Alerta.Should().BeTrue();
    }

    [Fact]
    public void No_salio_reintenta_hasta_cinco_veces_y_luego_es_error_de_ambiente()
    {
        var situacion = En(E.Pendiente);
        for (var i = 1; i <= TransicionesAprovisionamiento.MaximoIntentosAutomaticos; i++)
        {
            var r = TransicionesAprovisionamiento.Aplicar(situacion, V.NoSalio);
            r.Nueva.Estado.Should().Be(E.Pendiente, $"reintento automático {i}");
            r.Nueva.IntentosAutomaticos.Should().Be(i);
            situacion = r.Nueva;
        }

        var agotado = TransicionesAprovisionamiento.Aplicar(situacion, V.NoSalio);

        TransicionesAprovisionamiento.MaximoIntentosAutomaticos.Should().Be(5);
        agotado.Nueva.Estado.Should().Be(E.Error);
        agotado.Nueva.TipoError.Should().Be(TipoErrorAprovisionamiento.Ambiente);
    }

    [Theory]
    [InlineData(E.Error)]
    [InlineData(E.Incierto)]
    [InlineData(E.RequiereRevisionNit)]
    public void Reintentar_vuelve_a_pendiente_y_reinicia_los_intentos(E desde)
    {
        var r = TransicionesAprovisionamiento.Aplicar(new SituacionAprovisionamiento(desde, null, 5, false, TipoErrorAprovisionamiento.Ambiente), V.Reintentar);

        r.Nueva.Estado.Should().Be(E.Pendiente);
        r.Nueva.IntentosAutomaticos.Should().Be(0);
        r.Nueva.TipoError.Should().BeNull("al salir de ERROR el tipo de error se limpia");
    }

    [Theory]
    [InlineData(E.RequiereCredenciales, V.CargaManualConSecreto, E.Listo)]
    [InlineData(E.RequiereCredenciales, V.CargaManualSinSecreto, E.ListoSinWebhook)]
    [InlineData(E.ListoSinWebhook, V.CargaManualConSecreto, E.Listo)]
    [InlineData(E.ListoSinWebhook, V.CargaManualSinSecreto, E.ListoSinWebhook)]
    public void Carga_manual_valida(E desde, V evento, E hacia)
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(desde, firma: true), evento);

        r.Nueva.Estado.Should().Be(hacia);
        r.Nueva.FirmaConfirmada.Should().BeFalse("un secreto nuevo vuelve a quedar sin confirmar");
    }

    [Theory]
    [InlineData(E.Listo)]
    [InlineData(E.ListoSinWebhook)]
    public void Un_401_en_uso_pide_credenciales_con_alerta(E desde)
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(desde, firma: true), V.NoAutorizadoEnUso);

        r.Nueva.Estado.Should().Be(E.RequiereCredenciales);
        r.Alerta.Should().BeTrue();
    }

    [Fact]
    public void Primer_webhook_valido_confirma_la_firma()
    {
        var r = TransicionesAprovisionamiento.Aplicar(En(E.Listo), V.PrimerWebhookValido);

        r.Nueva.Estado.Should().Be(E.Listo);
        r.Nueva.FirmaConfirmada.Should().BeTrue();
    }

    [Theory]
    [InlineData(E.Listo, V.ProductoHabilitado)]
    [InlineData(E.Suspendido, V.ProductoDeshabilitado)]
    [InlineData(E.Listo, V.Alta201)]
    [InlineData(E.Error, V.Alta201)]
    [InlineData(E.Pendiente, V.Reintentar)]
    [InlineData(E.Listo, V.Reintentar)]
    [InlineData(E.Pendiente, V.CargaManualConSecreto)]
    [InlineData(E.Error, V.NoAutorizadoEnUso)]
    [InlineData(E.ListoSinWebhook, V.PrimerWebhookValido)]
    [InlineData(E.Suspendido, V.NoSalio)]
    public void Transicion_fuera_de_la_tabla_lanza_TransicionInvalida(E desde, V evento)
    {
        var act = () => TransicionesAprovisionamiento.Aplicar(En(desde), evento);

        act.Should().Throw<TransicionInvalida>().Which.Codigo.Should().Be(TransicionInvalida.CodigoPorDefecto);
    }

    [Fact]
    public void Sin_fila_solo_admite_la_habilitacion()
    {
        foreach (var evento in Enum.GetValues<V>().Where(e => e != V.ProductoHabilitado))
        {
            var act = () => TransicionesAprovisionamiento.Aplicar(SituacionAprovisionamiento.SinFila, evento);
            act.Should().Throw<TransicionInvalida>($"sin fila no aplica {evento}");
        }
    }

    [Theory]
    [InlineData(E.Listo, true, true)]
    [InlineData(E.ListoSinWebhook, true, false)]
    [InlineData(E.Pendiente, false, false)]
    [InlineData(E.Error, false, false)]
    [InlineData(E.Incierto, false, false)]
    [InlineData(E.RequiereCredenciales, false, false)]
    [InlineData(E.RequiereRevisionNit, false, false)]
    [InlineData(E.Suspendido, false, false)]
    public void Liquida_solo_en_estados_listos_y_con_callback_solo_en_listo(E estado, bool liquida, bool callback)
    {
        TransicionesAprovisionamiento.Liquida(estado).Should().Be(liquida);
        TransicionesAprovisionamiento.EnviaCallback(estado).Should().Be(callback);
    }
}
