using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Configuracion;

/// <summary>
/// HU #13741 (F00.2-H2), escenario 13 — <see cref="CopiaConfiguracion"/> es el valor inmutable de la configuración vigente
/// que F01 guarda en la cabecera del diagnóstico: editar después la configuración de la compañía no la cambia. No lleva
/// <c>redaccion_ia_suspendida</c> (el interruptor de emergencia actúa en el momento, no se congela).
/// </summary>
/// <example>
/// <code>
/// var copia = await CopiaConfiguracion.TomarAsync(db, ct);
/// cabecera.Configuracion = copia.ComoJson();
/// </code>
/// </example>
public sealed class CopiaConfiguracionTests
{
    // Escenario 13 — la copia conserva los valores anteriores aunque la configuración se edite después.
    [Fact]
    public async Task Copia_no_cambia_al_editar_la_configuracion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo],
            FuentesPorDefecto = [Fuente.Runt, Fuente.Avaluo],
            LimiteVehiculosLote = 250,
        }, ct);

        CopiaConfiguracion copia;
        await using (var db = empresas.A.Db())
            copia = await CopiaConfiguracion.TomarAsync(db, ct);
        var json = copia.ComoJson();

        var edicion = await api.Cliente(TokenAdmin(api, empresas.A.Id)).PutAsync(ConfiguracionEndpointsRuta,
            Cuerpo("""{ "visibilidad_usuario": "PROPIOS", "fuentes_por_defecto": ["RUNT"], "estimado_consultas": "ANTES_DE_LANZAR" }"""), ct);
        edicion.StatusCode.Should().Be(HttpStatusCode.OK, await edicion.Content.ReadAsStringAsync(ct));

        copia.FuentesHabilitadas.Should().Equal(Fuente.Runt, Fuente.Avaluo);
        copia.FuentesPorDefecto.Should().Equal(Fuente.Runt, Fuente.Avaluo);
        copia.VisibilidadUsuario.Should().Be(VisibilidadUsuario.Empresa);
        copia.EstimadoConsultas.Should().Be(EstimadoConsultas.NoMostrar);
        copia.LimiteVehiculosLote.Should().Be(250);
        copia.ComoJson().Should().Be(json, "la copia serializada tampoco cambia");

        CopiaConfiguracion nueva;
        await using (var db = empresas.A.Db())
            nueva = await CopiaConfiguracion.TomarAsync(db, ct);
        nueva.FuentesPorDefecto.Should().Equal(Fuente.Runt);
        nueva.VisibilidadUsuario.Should().Be(VisibilidadUsuario.Propios);
        nueva.Should().NotBe(copia);

        // La copia no comparte las listas de la entidad: cambiarlas en memoria tampoco la toca.
        var entidad = new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo], FuentesPorDefecto = [Fuente.Runt] };
        var deEntidad = CopiaConfiguracion.De(entidad);
        entidad.FuentesHabilitadas.Add(Fuente.SimitVehiculo);
        entidad.FuentesPorDefecto.Clear();
        entidad.LimiteVehiculosLote = 1;
        deEntidad.FuentesHabilitadas.Should().Equal(Fuente.Runt, Fuente.Avaluo);
        deEntidad.FuentesPorDefecto.Should().Equal(Fuente.Runt);
        deEntidad.LimiteVehiculosLote.Should().Be(500);

        // JSON objeto (ck_diagnosticos_configuracion_objeto) con los códigos de la base y sin el interruptor de emergencia.
        var objeto = JsonDocument.Parse(json).RootElement;
        objeto.ValueKind.Should().Be(JsonValueKind.Object);
        Codigos(objeto.GetProperty("fuentes_habilitadas")).Should().Equal("RUNT", "AVALUO");
        objeto.GetProperty("visibilidad_usuario").GetString().Should().Be("EMPRESA");
        objeto.GetProperty("estimado_consultas").GetString().Should().Be("NO_MOSTRAR");
        objeto.GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(250);
        json.Should().NotContainEquivalentOf("redaccion");
    }

    private const string ConfiguracionEndpointsRuta = Flit.Diagnostico.Api.Program.Prefijo + ConfiguracionEndpoints.Ruta;
}
