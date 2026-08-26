using Flit.Ict.Domain.Enums;
using Flit.Ict.Domain.Trazabilidad;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Trazabilidad;

/// <summary>
/// HU #11816 — recorrido y tiempos por etapa.
/// </summary>
/// <remarks>
/// El caso base reproduce un trámite real observado en FLIT 1.0 (auditoría del trámite 10461), que
/// es la referencia contra la que se recuperó este cálculo: recibido 15:18:04.955, validación de
/// negocio 15:19:20.811, consulta a fuentes 15:21:31.112, borrador 15:22:29.986. Los tiempos que
/// v1 reportaba para ese trámite eran 1 min 15,8 s hasta activar y 4 min 25,0 s hasta crear.
/// </remarks>
public sealed class CalculadoraDeRecorridoTests
{
    private static readonly DateTime Recibido = new(2026, 8, 24, 15, 18, 04, 955, DateTimeKind.Utc);
    private static readonly DateTime Negocio = new(2026, 8, 24, 15, 19, 20, 811, DateTimeKind.Utc);
    private static readonly DateTime Fuentes = new(2026, 8, 24, 15, 21, 31, 112, DateTimeKind.Utc);
    private static readonly DateTime Borrador = new(2026, 8, 24, 15, 22, 29, 986, DateTimeKind.Utc);

    private static MarcasRecorrido Completo(DateTime? ahora = null) => new(
        Recibido, Negocio, Fuentes, Borrador, null,
        IctEstado.BorradorCreado, null, ahora ?? Borrador.AddMinutes(30));

    [Fact]
    public void Un_recorrido_completo_dibuja_las_cuatro_etapas_en_orden()
    {
        var (hitos, _) = CalculadoraDeRecorrido.Construir(Completo());

        hitos.Select(h => h.Etapa).Should().Equal(
            IctEstado.Recibido,
            IctEstado.EnValidacionNegocio,
            IctEstado.EnValidacionExterna,
            IctEstado.BorradorCreado);
        hitos.Should().OnlyContain(h => h.Resultado == ResultadoHito.Ok);
    }

    [Fact]
    public void Los_tiempos_entre_etapas_se_miden_contra_la_etapa_anterior()
    {
        // AC2: es el dato de la pantalla. El primer hito no tiene tramo previo contra el que medirse.
        var (hitos, _) = CalculadoraDeRecorrido.Construir(Completo());

        hitos[0].SegundosDesdeAnterior.Should().BeNull();
        hitos[1].SegundosDesdeAnterior.Should().Be(75);   // 1 min 15,8 s
        hitos[2].SegundosDesdeAnterior.Should().Be(130);  // 2 min 10,3 s
        hitos[3].SegundosDesdeAnterior.Should().Be(58);   // 58,9 s
    }

    [Fact]
    public void Los_tres_tiempos_agregados_se_miden_desde_la_recepcion()
    {
        // AC3, y es la semántica de v1: NO se encadenan entre sí. «Hasta crear el borrador» son los
        // 4 min 25 s completos desde que entró, no lo que tardó el último tramo.
        var (_, tiempos) = CalculadoraDeRecorrido.Construir(Completo());

        tiempos.SegundosHastaActivar.Should().Be(75);
        tiempos.SegundosHastaCrearBorrador.Should().Be(265);   // 4 min 25,0 s
        tiempos.SegundosTotal.Should().Be(265);
        tiempos.SegundosSinAvanzar.Should().BeNull();
    }

    [Fact]
    public void Un_tramite_terminado_no_acumula_espera_aunque_pase_el_tiempo()
    {
        // El reloj se detiene en los estados terminales: si no, un trámite cerrado hace un mes
        // aparecería como «lleva un mes esperando» y competiría por el resaltado de tramo lento.
        var (hitos, tiempos) = CalculadoraDeRecorrido.Construir(Completo(ahora: Borrador.AddDays(30)));

        tiempos.SegundosSinAvanzar.Should().BeNull();
        tiempos.SegundosTotal.Should().Be(265);
        hitos.Should().NotContain(h => h.Resultado == ResultadoHito.Espera);
    }

    [Fact]
    public void El_tramo_mas_lento_se_marca_una_sola_vez()
    {
        // AC del detalle: lo decide el servidor para que pantalla y exportación coincidan. Aquí el
        // tramo mayor es el de consulta a fuentes (130 s).
        var (hitos, _) = CalculadoraDeRecorrido.Construir(Completo());

        hitos.Where(h => h.EsTramoMasLento).Should().ContainSingle()
            .Which.Etapa.Should().Be(IctEstado.EnValidacionExterna);
    }

    [Fact]
    public void Con_un_solo_tramo_no_se_resalta_nada()
    {
        // «El más lento» de un único tramo no informa de nada; resaltarlo sería ruido. Hace falta un
        // recorrido TERMINAL para que exista un solo tramo: mientras el trámite sigue vivo, el tiempo
        // que lleva parado es un segundo tramo y entra a competir por el resaltado.
        var anulado = Recibido.AddMinutes(3);
        var marcas = new MarcasRecorrido(
            Recibido, null, null, null, anulado,
            IctEstado.Anulado, null, anulado.AddHours(2));

        var (hitos, _) = CalculadoraDeRecorrido.Construir(marcas);

        hitos.Where(h => h.SegundosDesdeAnterior is not null).Should().ContainSingle();
        hitos.Should().NotContain(h => h.EsTramoMasLento);
    }

    [Fact]
    public void Una_espera_recien_empezada_sigue_contando_como_tramo()
    {
        // Contrapunto del anterior: cero segundos de espera es un dato real («acaba de moverse»), no
        // una ausencia de tramo. Con el delta anterior ya son dos, y sí se resalta el mayor.
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, null, null, null,
            IctEstado.EnValidacionNegocio, null, Negocio);

        var (hitos, tiempos) = CalculadoraDeRecorrido.Construir(marcas);

        tiempos.SegundosSinAvanzar.Should().Be(0);
        hitos.Single(h => h.EsTramoMasLento).Etapa.Should().Be(IctEstado.EnValidacionNegocio);
    }

    [Fact]
    public void Las_etapas_no_alcanzadas_se_informan_como_pendientes_y_no_se_ocultan()
    {
        // Que falte una etapa es información: dice cuánto camino queda. Ocultarla dejaría al analista
        // sin saber que el trámite ni siquiera llegó a consultar fuentes.
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, null, null, null,
            IctEstado.EnValidacionNegocio, null, Negocio.AddMinutes(5));

        var (hitos, _) = CalculadoraDeRecorrido.Construir(marcas);

        hitos.Where(h => h.Resultado == ResultadoHito.Pendiente)
            .Select(h => h.Etapa)
            .Should().Equal(IctEstado.EnValidacionExterna, IctEstado.BorradorCreado);
        hitos.Should().OnlyContain(h => h.Titulo.Length > 0);
    }

    [Fact]
    public void La_novedad_se_cuelga_de_la_ultima_etapa_alcanzada()
    {
        // AC4: pintarla suelta al pie obligaría a adivinar en qué punto se rompió el trámite.
        const string mensaje = "El código de organismo de tránsito no tiene un valor válido o no está activo.";
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, null, null, null,
            IctEstado.ConNovedades, mensaje, Negocio.AddHours(4));

        var (hitos, _) = CalculadoraDeRecorrido.Construir(marcas);

        var fallida = hitos.Single(h => h.Resultado == ResultadoHito.Error);
        fallida.Etapa.Should().Be(IctEstado.EnValidacionNegocio);
        fallida.Mensaje.Should().Be(mensaje);
        // El mensaje no se repite en las etapas que sí salieron bien.
        hitos.Where(h => h.Resultado != ResultadoHito.Error)
            .Should().OnlyContain(h => h.Mensaje == null);
    }

    [Fact]
    public void La_novedad_tras_consultar_fuentes_se_cuelga_de_esa_etapa_y_no_de_la_anterior()
    {
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, Fuentes, null, null,
            IctEstado.ConNovedades, "RUNT sin respuesta", Fuentes.AddHours(1));

        var (hitos, _) = CalculadoraDeRecorrido.Construir(marcas);

        hitos.Single(h => h.Resultado == ResultadoHito.Error)
            .Etapa.Should().Be(IctEstado.EnValidacionExterna);
    }

    [Fact]
    public void Un_tramite_detenido_cierra_con_el_tiempo_que_lleva_sin_avanzar()
    {
        // Es la cifra que convierte «está en validación» en «lleva cuatro horas en validación», y no
        // aparece en ningún delta porque no hay etapa siguiente contra la que medirla.
        var ahora = Negocio.AddHours(4).AddMinutes(12);
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, null, null, null,
            IctEstado.ConNovedades, "novedad", ahora);

        var (hitos, tiempos) = CalculadoraDeRecorrido.Construir(marcas);

        var espera = hitos[^1];
        espera.Resultado.Should().Be(ResultadoHito.Espera);
        espera.Ocurrido.Should().BeNull("aún no ha ocurrido nada; es tiempo transcurrido, no una marca");
        espera.SegundosDesdeAnterior.Should().Be(4 * 3600 + 12 * 60);
        tiempos.SegundosSinAvanzar.Should().Be(4 * 3600 + 12 * 60);
    }

    [Fact]
    public void La_espera_larga_gana_el_resaltado_a_los_tramos_ya_cerrados()
    {
        // Si un trámite lleva cuatro horas parado, ESE es el tramo lento, no el minuto que tardó la
        // validación de negocio en resolverse.
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, null, null, null,
            IctEstado.ConNovedades, "novedad", Negocio.AddHours(4));

        var (hitos, _) = CalculadoraDeRecorrido.Construir(marcas);

        hitos.Single(h => h.EsTramoMasLento).Resultado.Should().Be(ResultadoHito.Espera);
    }

    [Fact]
    public void La_anulacion_sustituye_al_borrador_porque_son_desenlaces_excluyentes()
    {
        var anulado = Negocio.AddMinutes(2);
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, null, null, anulado,
            IctEstado.Anulado, null, anulado.AddDays(1));

        var (hitos, tiempos) = CalculadoraDeRecorrido.Construir(marcas);

        hitos.Should().NotContain(h => h.Etapa == IctEstado.BorradorCreado);
        var cierre = hitos.Single(h => h.Etapa == IctEstado.Anulado);
        cierre.Resultado.Should().Be(ResultadoHito.Anulado);
        cierre.Ocurrido.Should().Be(anulado);
        // Terminal: el reloj de espera no sigue corriendo tras la anulación.
        tiempos.SegundosSinAvanzar.Should().BeNull();
        tiempos.SegundosHastaCrearBorrador.Should().BeNull();
    }

    [Fact]
    public void Un_tramite_recien_recibido_solo_tiene_la_primera_etapa_y_su_espera()
    {
        var ahora = Recibido.AddMinutes(48);
        var marcas = new MarcasRecorrido(
            Recibido, null, null, null, null,
            IctEstado.Recibido, null, ahora);

        var (hitos, tiempos) = CalculadoraDeRecorrido.Construir(marcas);

        hitos[0].Ocurrido.Should().Be(Recibido);
        hitos.Count(h => h.Resultado == ResultadoHito.Pendiente).Should().Be(3);
        tiempos.SegundosSinAvanzar.Should().Be(48 * 60);
        tiempos.SegundosHastaActivar.Should().BeNull();
        tiempos.SegundosTotal.Should().Be(48 * 60);
    }

    [Fact]
    public void Un_reloj_atrasado_no_produce_esperas_negativas()
    {
        // Desfase de reloj entre la base y la aplicación, o una marca escrita con adelanto: una espera
        // negativa se leería como un trámite que avanza hacia atrás.
        var marcas = new MarcasRecorrido(
            Recibido, Negocio, null, null, null,
            IctEstado.EnValidacionNegocio, null, Negocio.AddSeconds(-30));

        var (_, tiempos) = CalculadoraDeRecorrido.Construir(marcas);

        tiempos.SegundosSinAvanzar.Should().Be(0);
    }
}
