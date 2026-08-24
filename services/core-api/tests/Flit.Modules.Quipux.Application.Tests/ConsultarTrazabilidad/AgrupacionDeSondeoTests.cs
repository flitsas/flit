using Flit.Modules.Quipux.Application.UseCases.ConsultarTrazabilidad;
using Flit.Modules.Quipux.Domain.LogQx;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.ConsultarTrazabilidad;

/// <summary>
/// Regla de agrupación del sondeo (HU #11787, ADR-0051 D2). Es la pieza que hace usable la pantalla
/// y también la que decide qué se le OCULTA al usuario, así que se prueba sola y con el caso real de
/// referencia: el trámite 27172 de FLIT 1.0, con 1.065 eventos para representar cinco cosas.
/// </summary>
public sealed class AgrupacionDeSondeoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 18, 17, 40, 0, TimeSpan.Zero);

    private static QuipuxEventoResumen Hito(
        string stage, string outcome = "ok", long? ms = null, int? codigo = null, int minuto = 0) =>
        new(stage, outcome, T0.AddMinutes(minuto), ms, codigo, null, null, null);

    /// <summary>Un latido: consulta correcta que no movió el trámite (estado 1 o ausente).</summary>
    private static QuipuxEventoResumen Latido(int minuto, long? ms = 400, int? estado = 1) =>
        new("consulta_respuesta", "ok", T0.AddMinutes(minuto), ms, 81, estado, null, null);

    [Fact]
    public void Las_1065_consultas_del_caso_27172_se_colapsan_en_un_solo_bloque()
    {
        var eventos = new List<QuipuxEventoResumen>
        {
            Hito("consolidado_generado", ms: 320, minuto: 0),
            Hito("s3_subido", ms: 540, minuto: 1),
            Hito("registro_respuesta", ms: 1240, codigo: 81, minuto: 2),
        };

        // Uno cada diez minutos, como el sondeo real.
        for (var i = 0; i < 1065; i++)
        {
            eventos.Add(Latido(10 + (i * 10)));
        }

        var hitos = ConsultarHitosQuipuxHandler.Agrupar(eventos);

        // Tres hitos reales + UN bloque: cuatro entradas para 1.068 eventos.
        hitos.Should().HaveCount(4);

        var bloque = hitos[^1];
        bloque.Tipo.Should().Be(QuipuxHitoTipo.Sondeo);
        bloque.Consultas.Should().Be(1065);
        bloque.OccurredAt.Should().Be(T0.AddMinutes(10));
        bloque.Hasta.Should().Be(T0.AddMinutes(10 + (1064 * 10)));
    }

    [Fact]
    public void Un_cambio_de_estado_parte_el_bloque_en_dos_y_no_esconde_la_decision()
    {
        // El riesgo real: que un rechazo quede sepultado dentro de una racha de mil latidos.
        var eventos = new List<QuipuxEventoResumen>
        {
            Latido(10),
            Latido(20),
            new("rechazado", "error_definitivo", T0.AddMinutes(30), null, 81, 3, "Ilegible", null),
            Latido(40),
        };

        var hitos = ConsultarHitosQuipuxHandler.Agrupar(eventos);

        hitos.Should().HaveCount(3);
        hitos[0].Tipo.Should().Be(QuipuxHitoTipo.Sondeo);
        hitos[0].Consultas.Should().Be(2);
        hitos[1].Tipo.Should().Be(QuipuxHitoTipo.Hito);
        hitos[1].Stage.Should().Be("rechazado");
        hitos[1].Mensaje.Should().Be("Ilegible");
        hitos[2].Tipo.Should().Be(QuipuxHitoTipo.Sondeo);
        hitos[2].Consultas.Should().Be(1);
    }

    [Theory]
    [InlineData(2)]  // aprobado
    [InlineData(3)]  // rechazado
    public void Una_consulta_que_resuelve_el_tramite_no_es_un_latido(int estadoTramite)
    {
        var eventos = new List<QuipuxEventoResumen>
        {
            Latido(10),
            Latido(20, estado: estadoTramite),
        };

        var hitos = ConsultarHitosQuipuxHandler.Agrupar(eventos);

        hitos.Should().HaveCount(2);
        hitos[0].Consultas.Should().Be(1);
        hitos[1].Tipo.Should().Be(QuipuxHitoTipo.Hito);
        hitos[1].EstadoTramite.Should().Be(estadoTramite);
    }

    [Fact]
    public void Una_consulta_que_fallo_no_es_un_latido()
    {
        var eventos = new List<QuipuxEventoResumen>
        {
            Latido(10),
            new("consulta_error", "error_transitorio", T0.AddMinutes(20), 60000, 72, null, null, null),
            Latido(30),
        };

        var hitos = ConsultarHitosQuipuxHandler.Agrupar(eventos);

        hitos.Should().HaveCount(3);
        hitos[1].Tipo.Should().Be(QuipuxHitoTipo.Hito);
        hitos[1].Codigo.Should().Be(72);
    }

    [Fact]
    public void Un_estado_ausente_cuenta_como_sin_cambios()
    {
        // Quipux omite estadoTramite mientras no resuelve; para el sondeo es lo mismo que un 1.
        var eventos = new List<QuipuxEventoResumen> { Latido(10, estado: null), Latido(20) };

        var hitos = ConsultarHitosQuipuxHandler.Agrupar(eventos);

        hitos.Should().ContainSingle().Which.Consultas.Should().Be(2);
    }

    [Fact]
    public void La_duracion_media_ignora_los_eventos_sin_duracion()
    {
        // Los eventos previos a la instrumentación no traen duración; contarlos como cero rebajaría
        // la media y mentiría sobre lo que tarda el servicio.
        var eventos = new List<QuipuxEventoResumen>
        {
            Latido(10, ms: 400),
            Latido(20, ms: null),
            Latido(30, ms: 600),
        };

        var hitos = ConsultarHitosQuipuxHandler.Agrupar(eventos);

        hitos.Should().ContainSingle().Which.DuracionMediaMs.Should().Be(500);
    }

    [Fact]
    public void Un_bloque_entero_sin_duraciones_no_inventa_una_media()
    {
        var eventos = new List<QuipuxEventoResumen> { Latido(10, ms: null), Latido(20, ms: null) };

        var hitos = ConsultarHitosQuipuxHandler.Agrupar(eventos);

        hitos.Should().ContainSingle().Which.DuracionMediaMs.Should().BeNull();
    }

    [Fact]
    public void Una_radicacion_sin_eventos_no_produce_hitos()
    {
        ConsultarHitosQuipuxHandler.Agrupar([]).Should().BeEmpty();
    }

    [Fact]
    public void Los_hitos_no_llevan_ventana_ni_conteo()
    {
        var hitos = ConsultarHitosQuipuxHandler.Agrupar([Hito("registro_enviado")]);

        var hito = hitos.Should().ContainSingle().Subject;
        hito.Tipo.Should().Be(QuipuxHitoTipo.Hito);
        hito.Hasta.Should().BeNull();
        hito.Consultas.Should().BeNull();
        hito.DuracionMediaMs.Should().BeNull();
    }

    [Fact]
    public void El_predicado_del_latido_esta_en_el_dominio_y_es_el_mismo_para_todos()
    {
        QuipuxSondeo.EsLatido(Latido(10)).Should().BeTrue();
        QuipuxSondeo.EsLatido(Latido(10, estado: 2)).Should().BeFalse();
        QuipuxSondeo.EsLatido(Hito("registro_respuesta")).Should().BeFalse();
    }
}
