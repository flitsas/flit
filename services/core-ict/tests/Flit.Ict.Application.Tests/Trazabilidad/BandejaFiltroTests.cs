using Flit.Ict.Domain.Enums;
using Flit.Ict.Domain.Trazabilidad;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Trazabilidad;

/// <summary>
/// HU #11815 — normalización del campo «placas o VIN» y vocabulario de estados de la bandeja.
/// </summary>
public sealed class BandejaFiltroTests
{
    [Fact]
    public void Parse_admite_varias_placas_separadas_por_coma()
    {
        // AC2: el analista pega las placas que le manda el cliente y espera encontrar ambos trámites.
        var terminos = PlacaVinFiltro.Parse("NPT415, LTS304");

        terminos.Should().Equal("NPT415", "LTS304");
    }

    [Theory]
    [InlineData("NPT415;LTS304")]
    [InlineData("NPT415 LTS304")]
    [InlineData("NPT415\nLTS304")]
    [InlineData("  npt415 ,  lts304  ")]
    public void Parse_tolera_los_separadores_y_el_formato_que_llega_pegado(string entrada)
    {
        // Lo que llega por correo o por WhatsApp viene con cualquier separador y en cualquier caja.
        // Obligar a un formato concreto convierte la búsqueda en una fuente de falsos negativos.
        PlacaVinFiltro.Parse(entrada).Should().Equal("NPT415", "LTS304");
    }

    [Fact]
    public void Parse_mezcla_placas_y_vin_en_el_mismo_campo()
    {
        // El campo es uno solo a propósito: quien busca no sabe ni le importa cuál de los dos tiene.
        var terminos = PlacaVinFiltro.Parse("NPT415, JALFVR347V7000402");

        terminos.Should().Equal("NPT415", "JALFVR347V7000402");
    }

    [Fact]
    public void Parse_elimina_duplicados_conservando_el_orden()
    {
        // Pegar la misma placa dos veces no debe multiplicar las comparaciones de la consulta.
        PlacaVinFiltro.Parse("NPT415, npt415, LTS304").Should().Equal("NPT415", "LTS304");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ; ")]
    public void Parse_devuelve_vacio_cuando_no_hay_nada_que_buscar(string? entrada)
    {
        // Vacío y no null: quien lo consume no tiene que distinguir entre «no vino» y «vino en blanco»,
        // y el repositorio traduce lista vacía a «sin filtro de placa».
        PlacaVinFiltro.Parse(entrada).Should().BeEmpty();
    }

    [Fact]
    public void Parse_corta_en_el_tope_de_terminos()
    {
        // Una pegada accidental de mil placas convertiría la bandeja en un escaneo secuencial.
        var entrada = string.Join(",", Enumerable.Range(1, 200).Select(i => $"AAA{i:D3}"));

        PlacaVinFiltro.Parse(entrada).Should().HaveCount(PlacaVinFiltro.MaximoTerminos);
    }

    [Fact]
    public void La_bandeja_expone_exactamente_los_siete_estados_del_vocabulario()
    {
        // AC4: siete contadores, ni uno más ni uno menos. Si alguien añade un estado a IctEstado sin
        // pasarlo por aquí, la tira de la bandeja dejaría de sumar el total.
        TrazabilidadEstados.Todos.Should().Equal(
            IctEstado.Recibido,
            IctEstado.EnValidacionNegocio,
            IctEstado.EnValidacionExterna,
            IctEstado.Procesado,
            IctEstado.BorradorCreado,
            IctEstado.ConNovedades,
            IctEstado.Anulado);
    }

    [Theory]
    [InlineData("con_novedades", true)]
    [InlineData("borrador_creado", true)]
    [InlineData("CON_NOVEDADES", false)]
    [InlineData("inventado", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsValido_solo_acepta_el_vocabulario_exacto(string? estado, bool esperado)
    {
        // El estado llega en la URL. Uno desconocido se descarta (la bandeja sale sin filtrar) en vez
        // de traducirse a «ninguna coincidencia», que se vería como una bandeja rota.
        TrazabilidadEstados.EsValido(estado).Should().Be(esperado);
    }

    [Fact]
    public void Solo_borrador_creado_y_anulado_detienen_el_reloj_de_espera()
    {
        // El tiempo en espera mide cuánto lleva el trámite SIN AVANZAR. En los estados terminales deja
        // de tener sentido y se informa vacío, no cero: cero se leería como «acaba de moverse».
        foreach (var estado in TrazabilidadEstados.Todos)
        {
            TrazabilidadEstados.EsTerminal(estado).Should().Be(
                estado is IctEstado.BorradorCreado or IctEstado.Anulado,
                because: $"«{estado}» decide si el reloj de espera sigue corriendo");
        }
    }
}
