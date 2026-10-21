using System.Reflection;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13725 (F03-H3) — fuente RUNT del vehículo (<see cref="IFuenteRunt"/>): el adaptador simulado de Intempo responde con
/// fixtures inventados según el último carácter del identificador (plan §9.1), el mapper del producto hidrata propietarios
/// y locatarios sin nombres, y por defecto (QA/PDN) responde el adaptador «no conectado». La simulación solo se habilita
/// con configuración explícita en LOCAL o DEV; en otro ambiente el servicio no arranca.
/// </summary>
/// <example>
/// <code>
/// dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~FuenteRuntSimuladaTests"
/// </code>
/// </example>
public sealed class FuenteRuntSimuladaTests
{
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-0000-0000-00000000e001");

    private static Task<ResultadoRunt> ConsultarAsync(string placa) =>
        new FuenteRuntIntempoSimulada().ConsultarVehiculoAsync(
            Empresa, new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, placa), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Persona_natural_mapea_propietario_sin_locatario()
    {
        var resultado = await ConsultarAsync("TST001");

        resultado.Exitoso.Should().BeTrue();
        var vehiculo = resultado.Vehiculo!;
        vehiculo.Placa.Should().Be("TST001", "la simulación responde por la placa consultada");
        vehiculo.Servicio.Should().Be("PARTICULAR");
        vehiculo.ServicioQueResolvio.Should().Be(FuenteRuntIntempoSimulada.Servicio);
        vehiculo.IdTransaccion.Should().NotBeNullOrWhiteSpace();
        var propietario = vehiculo.Propietarios.Should().ContainSingle().Subject;
        propietario.TipoDocumento.Should().Be("CC");
        propietario.NumeroDocumento.Should().Be("9999900001");
        propietario.TipoPropiedad.Should().Be(TipoPropiedad.Propio);
        propietario.Locatarios.Should().BeEmpty();
        vehiculo.Locatarios.Should().BeEmpty();
    }

    [Fact]
    public async Task Nit_mapea_tipo_documento_NIT()
    {
        var resultado = await ConsultarAsync("TST002");

        var propietario = resultado.Vehiculo!.Propietarios.Should().ContainSingle().Subject;
        propietario.TipoDocumento.Should().Be("NIT");
        propietario.NumeroDocumento.Should().Be("999990002");
        propietario.TipoPropiedad.Should().Be(TipoPropiedad.Propio);
    }

    // AC4 — leasing con locatario: documento del propietario y del locatario con su tipo, sin nombres.
    [Fact]
    public async Task Leasing_con_locatario_C_y_con_locatario_N_mapea_locatario()
    {
        var conC = (await ConsultarAsync("TST003")).Vehiculo!;
        var propietarioC = conC.Propietarios.Should().ContainSingle().Subject;
        propietarioC.TipoDocumento.Should().Be("NIT");
        propietarioC.NumeroDocumento.Should().Be("999990003");
        propietarioC.TipoPropiedad.Should().Be(TipoPropiedad.Leasing);
        propietarioC.Locatarios.Should().Equal(new LocatarioRunt("CC", "9999900013"));
        conC.Locatarios.Should().Equal(new LocatarioRunt("CC", "9999900013"));

        var conN = (await ConsultarAsync("TST004")).Vehiculo!;
        var propietarioN = conN.Propietarios.Should().ContainSingle().Subject;
        propietarioN.TipoPropiedad.Should().Be(TipoPropiedad.Leasing);
        propietarioN.Locatarios.Should().Equal(new LocatarioRunt("NIT", "999990014"));
        conN.Locatarios.Should().Equal(new LocatarioRunt("NIT", "999990014"));

        var json = JsonSerializer.Serialize(new[] { conC, conN });
        foreach (var nombre in FixturesSinPiiTests.NombresFicticios)
            json.Should().NotContain(nombre, "el resultado nunca trae nombres de propietarios ni locatarios");
    }

    [Fact]
    public void Resultado_no_expone_nombres()
    {
        var prohibidas = new[] { "Nombre", "Name", "RazonSocial", "Apellido" };
        var propiedades = PropiedadesPublicas(typeof(ResultadoRunt), []).ToList();

        propiedades.Should().Contain(p => p.EndsWith(".NumeroDocumento", StringComparison.Ordinal), "la reflexión recorre los tipos anidados");
        propiedades.Should().NotContain(p => prohibidas.Any(x => p.Contains(x, StringComparison.OrdinalIgnoreCase)));
    }

    // AC2 — QA/PDN: error definitivo, sin datos y sin llamar a ningún proveedor.
    [Fact]
    public async Task NoConectada_devuelve_error_definitivo_y_nunca_datos()
    {
        typeof(FuenteRuntNoConectada).GetConstructors().Should().OnlyContain(c => c.GetParameters().Length == 0,
            "sin dependencias no hay proveedor al que llamar");

        var resultado = await new FuenteRuntNoConectada().ConsultarVehiculoAsync(
            Empresa, new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, "TST001"), TestContext.Current.CancellationToken);

        resultado.Exitoso.Should().BeFalse();
        resultado.Vehiculo.Should().BeNull();
        resultado.Error.Should().Be(TipoErrorFuente.NoConectado);
        resultado.EsTransitorio.Should().BeFalse("no se reintenta lo que no está conectado");
    }

    [Fact]
    public async Task Por_defecto_el_servicio_usa_la_fuente_no_conectada()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();

        api.App.Services.GetRequiredService<IFuenteRunt>().Should().BeOfType<FuenteRuntNoConectada>();
    }

    [Theory]
    [InlineData("DEV")]
    [InlineData("local")]
    public async Task Simulacion_habilitada_en_DEV_o_LOCAL_usa_la_fuente_simulada(string ambiente)
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Modulos.ClaveSimulacionHabilitada] = "true",
            [Modulos.ClaveAmbiente] = ambiente,
        }));

        api.App.Services.GetRequiredService<IFuenteRunt>().Should().BeOfType<FuenteRuntIntempoSimulada>();
    }

    // AC3 (negativo) — simulación fuera de DEV: el arranque falla nombrando la configuración.
    [Theory]
    [InlineData(null)]
    [InlineData("QA")]
    [InlineData("PDN")]
    [InlineData("Development")]
    public async Task Simulacion_habilitada_fuera_de_Development_impide_arrancar(string? ambiente)
    {
        var arrancar = async () =>
        {
            await using var api = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Modulos.ClaveSimulacionHabilitada] = "true",
                [Modulos.ClaveAmbiente] = ambiente,
            }));
        };

        var error = (await arrancar.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("Diagnostico__Simulacion__Habilitada").And.Contain("Diagnostico__Ambiente");
    }

    [Theory]
    [InlineData("TST005", "PUBLICO", null)]
    [InlineData("TST006", "PARTICULAR", "SECRETARIA DE TRANSITO DE PRUEBA SIN DEPARTAMENTO")]
    public async Task Escenarios_de_servicio_publico_y_organismo_no_soportado(string placa, string servicio, string? organismo)
    {
        var vehiculo = (await ConsultarAsync(placa)).Vehiculo!;

        vehiculo.Servicio.Should().Be(servicio);
        if (organismo is not null)
            vehiculo.Organismo.Should().Be(organismo);
    }

    [Fact]
    public async Task Escenarios_de_error_transitorio_y_definitivo()
    {
        var timeout = await ConsultarAsync("TST007");
        timeout.Exitoso.Should().BeFalse();
        timeout.Error.Should().Be(TipoErrorFuente.Timeout);
        timeout.EsTransitorio.Should().BeTrue();

        var invalida = await ConsultarAsync("TST008");
        invalida.Exitoso.Should().BeFalse();
        invalida.Error.Should().Be(TipoErrorFuente.RespuestaInvalida);
        invalida.EsTransitorio.Should().BeFalse();
    }

    [Fact]
    public async Task Consulta_por_VIN_devuelve_el_VIN_consultado()
    {
        var resultado = await new FuenteRuntIntempoSimulada().ConsultarVehiculoAsync(
            Empresa, new IdentificadorVehiculo(TipoIdentificador.Vin, "9ZZPRUEBA00000003"), TestContext.Current.CancellationToken);

        resultado.Vehiculo!.Vin.Should().Be("9ZZPRUEBA00000003");
        resultado.Vehiculo.Propietarios.Should().ContainSingle().Which.TipoPropiedad.Should().Be(TipoPropiedad.Leasing);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"codigoResultado":"Exitoso"}""")]
    [InlineData("""{"codigoResultado":"Error","informacionGeneral":{"noPlaca":"TST001"}}""")]
    public void Mapper_con_respuesta_mal_formada_o_de_error_devuelve_RespuestaInvalida(string json)
    {
        var resultado = MapperIntempo.Mapear(json, "INTEMPO");

        resultado.Error.Should().Be(TipoErrorFuente.RespuestaInvalida);
        resultado.Vehiculo.Should().BeNull();
    }

    private static IEnumerable<string> PropiedadesPublicas(Type tipo, HashSet<Type> vistos)
    {
        if (!vistos.Add(tipo))
            yield break;
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return $"{tipo.Name}.{p.Name}";
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (t.IsGenericType && t.GetGenericArguments() is [var elemento])
                t = elemento;
            if (t.Namespace == typeof(ResultadoRunt).Namespace && t.IsClass)
            {
                foreach (var anidada in PropiedadesPublicas(t, vistos))
                    yield return anidada;
            }
        }
    }
}
