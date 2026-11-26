using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Liquidaciones;
using FluentAssertions;
using Xunit;
using S = Flit.Impuestos.Domain.Liquidaciones.EstadoItem;

namespace Flit.Impuestos.Tests.Dominio;

/// <summary>
/// HU #14694 (M05) — máquina de estados del ítem de liquidación (plan §3.2): estados finales, categoría del dashboard y cada
/// fila de la tabla «Desde / Evento / Hacia». Cualquier otra combinación lanza <see cref="TransicionInvalida"/>.
/// </summary>
/// <example>
/// <code>
/// var r = TransicionesItem.Aplicar(new SituacionItem(EstadoItem.Enviando), new EventoItem.Aceptado());
/// // r.Nueva.Estado == EstadoItem.Enviado
/// </code>
/// </example>
public sealed class TransicionesItemTests
{
    [Theory]
    [InlineData(S.Pendiente, false, CategoriaDashboard.Pendiente)]
    [InlineData(S.ConsultandoRunt, false, CategoriaDashboard.Pendiente)]
    [InlineData(S.ReintentoProgramado, false, CategoriaDashboard.Pendiente)]
    [InlineData(S.Enviando, false, CategoriaDashboard.Pendiente)]
    [InlineData(S.Enviado, false, CategoriaDashboard.Pendiente)]
    [InlineData(S.Incierto, false, CategoriaDashboard.Incierto)]
    [InlineData(S.InciertoRequiereDecision, true, CategoriaDashboard.Incierto)]
    [InlineData(S.DatosIncompletos, true, CategoriaDashboard.DatosIncompletos)]
    [InlineData(S.DepartamentoNoHabilitado, true, CategoriaDashboard.DepartamentoNoHabilitado)]
    [InlineData(S.Exitoso, true, CategoriaDashboard.Exitosa)]
    [InlineData(S.Error, true, CategoriaDashboard.Error)]
    [InlineData(S.SinResultadoLoteCerrado, true, CategoriaDashboard.SinResultado)]
    public void Estados_finales_y_categoria_del_dashboard(S estado, bool final, CategoriaDashboard categoria)
    {
        EstadosItem.EsFinal(estado).Should().Be(final);
        EstadosItem.Categoria(estado).Should().Be(categoria);
    }

    [Fact]
    public void Credito_asignado_consulta_el_runt()
    {
        Aplicar(S.Pendiente, new EventoItem.CreditoAsignado()).Nueva.Estado.Should().Be(S.ConsultandoRunt);
    }

    [Fact]
    public void Runt_completo_envia()
    {
        Aplicar(S.ConsultandoRunt, new EventoItem.RuntCompleto()).Nueva.Estado.Should().Be(S.Enviando);
    }

    [Fact]
    public void Runt_con_dato_faltante_queda_en_datos_incompletos_con_el_campo()
    {
        var r = Aplicar(S.ConsultandoRunt, new EventoItem.RuntDatoFaltante("documento_numero"));

        r.Nueva.Estado.Should().Be(S.DatosIncompletos);
        r.CampoFaltante.Should().Be("documento_numero");
    }

    [Fact]
    public void Departamento_fuera_del_contrato()
    {
        Aplicar(S.ConsultandoRunt, new EventoItem.DepartamentoFueraDeContrato()).Nueva.Estado.Should().Be(S.DepartamentoNoHabilitado);
    }

    [Fact]
    public void Runt_transitorio_reintenta_hasta_el_tercer_intento_y_luego_es_error_reintentable()
    {
        var uno = TransicionesItem.Aplicar(new SituacionItem(S.ConsultandoRunt), new EventoItem.RuntTransitorio());
        var dos = TransicionesItem.Aplicar(uno.Nueva with { Estado = S.ConsultandoRunt }, new EventoItem.RuntTransitorio());
        var tres = TransicionesItem.Aplicar(dos.Nueva with { Estado = S.ConsultandoRunt }, new EventoItem.RuntTransitorio());

        uno.Nueva.Should().Be(new SituacionItem(S.ReintentoProgramado, IntentosRunt: 1));
        dos.Nueva.Should().Be(new SituacionItem(S.ReintentoProgramado, IntentosRunt: 2));
        TransicionesItem.MaximoIntentos.Should().Be(3);
        tres.Nueva.Estado.Should().Be(S.Error);
        tres.Nueva.ClaseError.Should().Be(ClaseError.Reintentable);
        tres.ErrorCodigo.Should().Be(TransicionesItem.CodigoRuntNoDisponible);
    }

    [Fact]
    public void Placa_no_encontrada_en_el_runt_es_error_de_datos()
    {
        var r = Aplicar(S.ConsultandoRunt, new EventoItem.RuntPlacaNoEncontrada());

        r.Nueva.Estado.Should().Be(S.Error);
        r.Nueva.ClaseError.Should().Be(ClaseError.CorregirDatos);
        r.ErrorCodigo.Should().Be(TransicionesItem.CodigoRuntPlacaNoEncontrada);
    }

    [Fact]
    public void Vence_el_reintento_y_vuelve_a_pendiente()
    {
        Aplicar(S.ReintentoProgramado, new EventoItem.VenceReintento()).Nueva.Estado.Should().Be(S.Pendiente);
    }

    [Fact]
    public void Envio_aceptado_202()
    {
        Aplicar(S.Enviando, new EventoItem.Aceptado()).Nueva.Estado.Should().Be(S.Enviado);
    }

    [Theory]
    [InlineData("invalid_params", ClaseError.CorregirDatos)]
    [InlineData("portal_rejected", ClaseError.Depende)]
    [InlineData("codigo_que_no_existe", ClaseError.Depende)]
    public void Envio_rechazado_400_409_422_es_error_con_el_codigo_del_api(string codigo, ClaseError clase)
    {
        var r = Aplicar(S.Enviando, new EventoItem.Rechazado(codigo));

        r.Nueva.Estado.Should().Be(S.Error);
        r.ErrorCodigo.Should().Be(codigo);
        r.Nueva.ClaseError.Should().Be(clase);
        r.AprovisionamientoRequiereCredenciales.Should().BeFalse();
    }

    [Fact]
    public void Envio_401_es_error_y_pide_credenciales_al_aprovisionamiento()
    {
        var r = Aplicar(S.Enviando, new EventoItem.NoAutorizado());

        r.Nueva.Estado.Should().Be(S.Error);
        r.ErrorCodigo.Should().Be(TransicionesItem.CodigoCredencialRechazada);
        r.AprovisionamientoRequiereCredenciales.Should().BeTrue();
    }

    [Fact]
    public void Envio_que_no_salio_se_queda_enviando_con_el_mismo_token()
    {
        var r = Aplicar(S.Enviando, new EventoItem.NoSalio());

        r.Nueva.Estado.Should().Be(S.Enviando);
        r.TokenNuevo.Should().BeFalse();
    }

    [Fact]
    public void Timeout_o_5xx_tras_enviar_es_incierto()
    {
        Aplicar(S.Enviando, new EventoItem.FallaTrasEnviar()).Nueva.Estado.Should().Be(S.Incierto);
    }

    [Theory]
    [InlineData("succeeded", false)]
    [InlineData("partial", true)]
    public void Final_exitoso_desde_enviado(string status, bool parcial)
    {
        var r = Aplicar(S.Enviado, new EventoItem.Final(status, null));

        r.Nueva.Estado.Should().Be(S.Exitoso);
        r.Parcial.Should().Be(parcial);
        r.ResultadoTardio.Should().BeFalse();
    }

    [Fact]
    public void Final_rate_limited_reenvia_como_consulta_nueva_hasta_el_tercer_intento()
    {
        var uno = TransicionesItem.Aplicar(new SituacionItem(S.Enviado), new EventoItem.Final("failed", "rate_limited"));
        var dos = TransicionesItem.Aplicar(uno.Nueva with { Estado = S.Enviado }, new EventoItem.Final("failed", "rate_limited"));
        var tres = TransicionesItem.Aplicar(dos.Nueva with { Estado = S.Enviado }, new EventoItem.Final("failed", "rate_limited"));

        uno.Nueva.Should().Be(new SituacionItem(S.ReintentoProgramado, IntentosReenvio: 1));
        uno.TokenNuevo.Should().BeTrue("la consulta fallida no liquidó: va otra con token nuevo");
        dos.Nueva.Estado.Should().Be(S.ReintentoProgramado);
        tres.Nueva.Estado.Should().Be(S.Error);
        tres.ErrorCodigo.Should().Be("rate_limited");
        tres.Nueva.ClaseError.Should().Be(ClaseError.Reintentable);
    }

    [Theory]
    [InlineData("owner_validation_failed", ClaseError.CorregirDatos)]
    [InlineData("portal_rejected", ClaseError.Depende)]
    [InlineData("timeout", ClaseError.Reintentable)]
    public void Final_failed_con_otro_codigo_es_error_con_su_clase(string codigo, ClaseError clase)
    {
        var r = Aplicar(S.Enviado, new EventoItem.Final("failed", codigo));

        r.Nueva.Estado.Should().Be(S.Error);
        r.ErrorCodigo.Should().Be(codigo);
        r.Nueva.ClaseError.Should().Be(clase);
    }

    [Fact]
    public void Webhook_valido_desde_incierto_adopta_la_consulta_y_aplica_el_resultado()
    {
        var r = Aplicar(S.Incierto, new EventoItem.Final("succeeded", null));

        r.Nueva.Estado.Should().Be(S.Exitoso);
        r.AdoptaConsulta.Should().BeTrue();
    }

    [Fact]
    public void Plazo_del_incierto_vencido()
    {
        var hallado = Aplicar(S.Incierto, new EventoItem.PlazoInciertoVencido(TokenHallado: true));
        var perdido = Aplicar(S.Incierto, new EventoItem.PlazoInciertoVencido(TokenHallado: false));

        hallado.Nueva.Estado.Should().Be(S.Enviado);
        hallado.AdoptaConsulta.Should().BeTrue();
        perdido.Nueva.Estado.Should().Be(S.InciertoRequiereDecision, "nunca se empareja por placa y hora");
    }

    [Fact]
    public void Relanzar_un_incierto_que_requiere_decision_crea_otra_individual_y_no_cambia_el_item()
    {
        var r = Aplicar(S.InciertoRequiereDecision, new EventoItem.Relanzar());

        r.Nueva.Estado.Should().Be(S.InciertoRequiereDecision);
        r.CrearRelanzamiento.Should().BeTrue();
    }

    [Fact]
    public void Relanzar_un_error_solo_si_es_reintentable()
    {
        var reintentable = TransicionesItem.Aplicar(new SituacionItem(S.Error, ClaseError: ClaseError.Reintentable), new EventoItem.Relanzar());
        var deDatos = () => TransicionesItem.Aplicar(new SituacionItem(S.Error, ClaseError: ClaseError.CorregirDatos), new EventoItem.Relanzar());
        var exitoso = () => TransicionesItem.Aplicar(new SituacionItem(S.Exitoso), new EventoItem.Relanzar());

        reintentable.Nueva.Estado.Should().Be(S.Error);
        reintentable.CrearRelanzamiento.Should().BeTrue();
        deDatos.Should().Throw<TransicionInvalida>().Which.Codigo.Should().Be(TransicionesItem.CodigoNoRelanzable);
        exitoso.Should().Throw<TransicionInvalida>().Which.Codigo.Should().Be(TransicionesItem.CodigoNoRelanzable);
    }

    [Fact]
    public void Relanzar_datos_incompletos_crea_otra_individual_y_no_cambia_el_item()
    {
        var r = Aplicar(S.DatosIncompletos, new EventoItem.Relanzar());

        r.Nueva.Estado.Should().Be(S.DatosIncompletos);
        r.CrearRelanzamiento.Should().BeTrue();
    }

    [Theory]
    [InlineData("EviewDetailVehicles: La placa ingresada no se encuentra inscrita en nuestra base de datos.")]
    [InlineData("LA PLACA NO SE ENCUENTRA INSCRITA")]
    public void Relanzar_un_error_de_placa_no_inscrita_del_portal_aunque_su_clase_sea_depende(string mensaje)
    {
        var r = TransicionesItem.Aplicar(
            new SituacionItem(S.Error, ClaseError: ClaseError.Depende, ErrorCodigo: "portal_rejected", ErrorMensaje: mensaje), new EventoItem.Relanzar());

        r.Nueva.Estado.Should().Be(S.Error);
        r.Nueva.ClaseError.Should().Be(ClaseError.Depende, "no hay clase nueva");
        r.CrearRelanzamiento.Should().BeTrue();
        ClasificacionErrores.EsPlacaNoInscrita("portal_rejected", mensaje).Should().BeTrue();
    }

    [Theory]
    [InlineData("portal_rejected", "El portal rechazó la consulta por mantenimiento.")]
    [InlineData("portal_rejected", null)]
    [InlineData("invalid_params", "La placa ingresada no se encuentra inscrita en nuestra base de datos.")]
    public void Un_error_depende_que_no_es_placa_no_inscrita_sigue_sin_relanzarse(string codigo, string? mensaje)
    {
        var relanzar = () => TransicionesItem.Aplicar(
            new SituacionItem(S.Error, ClaseError: ClaseError.Depende, ErrorCodigo: codigo, ErrorMensaje: mensaje), new EventoItem.Relanzar());

        relanzar.Should().Throw<TransicionInvalida>().Which.Codigo.Should().Be(TransicionesItem.CodigoNoRelanzable);
        ClasificacionErrores.EsPlacaNoInscrita(codigo, mensaje).Should().BeFalse();
    }

    [Fact]
    public void Un_error_de_corregir_datos_sigue_sin_relanzarse()
    {
        var relanzar = () => TransicionesItem.Aplicar(
            new SituacionItem(S.Error, ClaseError: ClaseError.CorregirDatos, ErrorCodigo: "invalid_params", ErrorMensaje: "Placa inválida."), new EventoItem.Relanzar());

        relanzar.Should().Throw<TransicionInvalida>().Which.Codigo.Should().Be(TransicionesItem.CodigoNoRelanzable);
    }

    [Theory]
    [InlineData(S.Pendiente)]
    [InlineData(S.ConsultandoRunt)]
    [InlineData(S.ReintentoProgramado)]
    [InlineData(S.Enviando)]
    [InlineData(S.Enviado)]
    [InlineData(S.Incierto)]
    public void Cierre_manual_deja_sin_resultado_a_los_no_finales(S desde)
    {
        Aplicar(desde, new EventoItem.CierreManual()).Nueva.Estado.Should().Be(S.SinResultadoLoteCerrado);
    }

    [Theory]
    [InlineData(S.InciertoRequiereDecision)]
    [InlineData(S.DatosIncompletos)]
    [InlineData(S.Exitoso)]
    [InlineData(S.Error)]
    [InlineData(S.SinResultadoLoteCerrado)]
    public void Cierre_manual_no_toca_los_finales(S desde)
    {
        var act = () => Aplicar(desde, new EventoItem.CierreManual());

        act.Should().Throw<TransicionInvalida>();
    }

    [Theory]
    [InlineData("succeeded", null, S.Exitoso)]
    [InlineData("partial", null, S.Exitoso)]
    [InlineData("failed", "rate_limited", S.Error)]
    [InlineData("failed", "invalid_params", S.Error)]
    public void Resultado_tardio_sobre_un_lote_cerrado(string status, string? codigo, S hacia)
    {
        var r = Aplicar(S.SinResultadoLoteCerrado, new EventoItem.Final(status, codigo));

        r.Nueva.Estado.Should().Be(hacia);
        r.ResultadoTardio.Should().BeTrue("el lote no se reabre");
        r.TokenNuevo.Should().BeFalse("un resultado tardío no reenvía");
    }

    [Theory]
    [InlineData(S.Pendiente)]
    [InlineData(S.ConsultandoRunt)]
    [InlineData(S.Enviando)]
    [InlineData(S.Exitoso)]
    [InlineData(S.DatosIncompletos)]
    public void Resultado_final_fuera_de_enviado_incierto_o_cerrado_es_invalido(S desde)
    {
        var act = () => Aplicar(desde, new EventoItem.Final("succeeded", null));

        act.Should().Throw<TransicionInvalida>();
    }

    [Fact]
    public void Estado_final_desconocido_es_invalido()
    {
        var act = () => Aplicar(S.Enviado, new EventoItem.Final("queued", null));

        act.Should().Throw<TransicionInvalida>();
    }

    [Theory]
    [InlineData(S.Enviando, typeof(EventoItem.CreditoAsignado))]
    [InlineData(S.Pendiente, typeof(EventoItem.Aceptado))]
    [InlineData(S.Enviado, typeof(EventoItem.RuntCompleto))]
    [InlineData(S.Incierto, typeof(EventoItem.VenceReintento))]
    public void Transicion_fuera_de_la_tabla_lanza_TransicionInvalida(S desde, Type evento)
    {
        var act = () => Aplicar(desde, (EventoItem)Activator.CreateInstance(evento)!);

        act.Should().Throw<TransicionInvalida>().Which.Codigo.Should().Be(TransicionInvalida.CodigoPorDefecto);
    }

    [Theory]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, true)]
    public void Un_aviso_con_attempt_menor_que_el_maximo_no_se_aplica(int attempt, int maximo, bool aplica)
    {
        TransicionesItem.AplicaAviso(attempt, maximo).Should().Be(aplica);
    }

    private static ResultadoItem Aplicar(S desde, EventoItem evento) => TransicionesItem.Aplicar(new SituacionItem(desde), evento);
}
