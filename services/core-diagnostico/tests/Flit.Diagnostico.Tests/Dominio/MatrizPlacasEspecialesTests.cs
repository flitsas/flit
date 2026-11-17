using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Dictamen;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// Punto 7 (ola 2026-11-11), AC2 — matriz automatizada de placas especiales por clase: carro particular, carro de servicio
/// público, moto, motocarro, maquinaria y remolque. Por fila: la placa se valida y clasifica igual en la carga masiva y en
/// la captura express (<see cref="ValidadorFilaVehiculo"/>), qué fuentes aplican o quedan en NA
/// (<see cref="ReglasAplicabilidad"/>) y qué reglas del dictamen pueden disparar. Las expectativas de SOAT/RTM son las de
/// <see cref="MatrizClaseVehiculo"/> (Bug RTM/SOAT), no se duplican aquí. Placas inventadas.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --filter FullyQualifiedName~MatrizPlacasEspecialesTests
/// </code>
/// </example>
public sealed class MatrizPlacasEspecialesTests
{
    /// <summary>Placa inventada y tipo de identificador esperado de cada fila de <see cref="MatrizClaseVehiculo"/>.</summary>
    private static readonly Dictionary<string, (string Placa, TipoIdentificador Tipo)> Placas = new(StringComparer.Ordinal)
    {
        ["carro particular"] = ("abc-123", TipoIdentificador.PlacaCarro),
        ["carro de servicio público"] = ("XYZ 987", TipoIdentificador.PlacaCarro),
        ["moto"] = ("abc12d", TipoIdentificador.PlacaMoto),
        ["motocarro"] = ("123-abc", TipoIdentificador.PlacaMotocarro),
        ["maquinaria"] = ("MC.029.554", TipoIdentificador.PlacaMaquinaria),
        ["remolque"] = ("s 54321", TipoIdentificador.PlacaRemolque),
    };

    private static readonly Fuente[] Fuentes = Enum.GetValues<Fuente>();

    public static TheoryData<string> Clases()
    {
        var datos = new TheoryData<string>();
        foreach (var fila in MatrizClaseVehiculo.Filas)
            datos.Add(fila.Nombre);
        return datos;
    }

    [Fact]
    public void La_matriz_cubre_las_seis_clases_del_punto_7()
    {
        Placas.Keys.Should().BeEquivalentTo(MatrizClaseVehiculo.Filas.Select(f => f.Nombre));
    }

    // ---- Validación: la carga masiva y el express aceptan la placa y la clasifican ----

    [Theory]
    [MemberData(nameof(Clases))]
    public void La_placa_de_cada_clase_se_acepta_en_la_carga_y_en_el_express(string nombre)
    {
        var (placa, tipo) = Placas[nombre];

        var fila = ValidadorFilaVehiculo.Validar(placa, null, null);

        fila.EsValida.Should().BeTrue($"{nombre}: {placa}");
        fila.Tipo.Should().Be(tipo);
        fila.Normalizado.Should().MatchRegex("^[A-Z0-9]+$");
        ValidadorIdentificador.Validar(placa).Tipo.Should().Be(tipo);
    }

    [Fact]
    public void El_motocarro_se_guarda_como_PLACA_MOTOCARRO_y_cabe_en_la_columna()
    {
        var codigo = CodigosDeEnum.Codigo(TipoIdentificador.PlacaMotocarro);

        codigo.Should().Be("PLACA_MOTOCARRO");
        codigo.Length.Should().BeLessThanOrEqualTo(16, "tipo_identificador es varchar(16)");
        CodigosDeEnum.ListaSql<TipoIdentificador>().Should().Contain("'PLACA_MOTOCARRO'");
    }

    [Theory]
    [InlineData("1234ABC")]
    [InlineData("12ABC")]
    [InlineData("123ABCD")]
    public void Un_motocarro_mal_escrito_es_invalido_en_la_carga(string placa)
    {
        var fila = ValidadorFilaVehiculo.Validar(placa, null, null);

        fila.EsValida.Should().BeFalse();
        fila.Tipo.Should().BeNull();
    }

    // ---- Fuentes que aplican o quedan en NA por clase ----

    [Theory]
    [MemberData(nameof(Clases))]
    public void Fuentes_que_aplican_por_clase(string nombre)
    {
        var fila = MatrizClaseVehiculo.Fila(nombre);
        var hechos = new HechosAplicabilidad(fila.ServicioRunt, "05", true, OrigenDocumento.DeclaradoCliente);
        var publico = ClasificadorVehiculoDictamen.Servicio(fila.ServicioRunt) == ServicioVehiculoDictamen.Publico;

        foreach (var fuente in Fuentes)
        {
            var esperado = fuente == Fuente.ImpuestoDepartamental && publico ? MotivoNa.ServicioPublico : (MotivoNa?)null;
            ReglasAplicabilidad.Evaluar(fuente, hechos).Should().Be(esperado, $"{nombre} / {fuente}");
        }
    }

    [Fact]
    public void El_motocarro_consulta_las_mismas_fuentes_que_la_moto()
    {
        var moto = MatrizClaseVehiculo.Fila("moto");
        var motocarro = MatrizClaseVehiculo.Fila("motocarro");

        foreach (var fuente in Fuentes)
        {
            ReglasAplicabilidad.Evaluar(fuente, new HechosAplicabilidad(motocarro.ServicioRunt, "05", true, null))
                .Should().Be(ReglasAplicabilidad.Evaluar(fuente, new HechosAplicabilidad(moto.ServicioRunt, "05", true, null)), fuente.ToString());
        }
    }

    // ---- Reglas del dictamen que pueden disparar por clase ----

    [Theory]
    [MemberData(nameof(Clases))]
    public void Reglas_de_SOAT_y_RTM_que_disparan_con_seis_anios_y_ambos_vencidos(string nombre)
    {
        var fila = MatrizClaseVehiculo.Fila(nombre);
        var esperadas = new List<string>();
        if (fila.CodigoSoat is { } soat)
            esperadas.Add(soat);
        if (fila.AniosRtm is not null)
            esperadas.Add("RTM_NO_VIGENTE");

        var resultado = EvaluadorDictamen.Evaluar(
            MatrizClaseVehiculoDictamenTests.Hechos("TRASPASO_STANDARD", fila, 6, soat: "NO VIGENTE", rtm: "NO VIGENTE"));

        resultado.Reglas.Select(r => r.Codigo).Should().Equal(esperadas);
        var bloquea = fila.EfectoSoat == "BLOQUEA" || fila.AniosRtm is not null;
        resultado.Veredicto.Should().Be(bloquea ? "BLOQUEADO" : "VIABLE");
    }
}
