using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Derivacion.Bus;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 3 (diseño §5.1 filas 2-5, §5.3 «Configuración», §8) — lo del masivo que se decide ANTES de tocar la base, con
/// la API real sin Postgres (<see cref="ApiDePrueba.SinBaseAsync"/>): las rutas no existen con la derivación apagada; 403 sin
/// <c>diagnostico.derivacion.crear</c> (masivo y reintentar) o sin <c>diagnostico.diagnosticos.ver</c> (progreso); 403 desde el
/// contexto de backoffice; 400 con la lista vacía, mayor que <c>MaxItemsPorOperacion</c> o un cuerpo que no es JSON; las claves del
/// masivo por defecto y su validación; y el consumidor y el reconciliador solo se registran con la derivación Y el consumidor
/// encendidos (apagado heredado de <c>Habilitada</c>). El reconciliador no toca la base con el freno abierto.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DerivacionMasivaSinBaseTests
/// </code>
/// </example>
public sealed class DerivacionMasivaSinBaseTests
{
    private static readonly Guid Lote = Guid.NewGuid();
    private static readonly Guid Operacion = Guid.NewGuid();

    [Fact]
    public async Task Con_la_derivacion_apagada_masivo_progreso_y_reintentar_no_existen()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var ct = TestContext.Current.CancellationToken;
        using var cliente = api.Cliente(api.Token(permisos: [Permisos.DerivacionCrear, Permisos.DiagnosticosVer]));

        (await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(Lote), new { vehiculo_ids = new[] { Guid.NewGuid() } }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.GetAsync($"{DerivacionDePrueba.RutaOperaciones(Lote)}?ultima=true", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.GetAsync(DerivacionDePrueba.RutaOperacion(Lote, Operacion), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.PostAsync(DerivacionDePrueba.RutaReintentar(Lote, Operacion), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sin_permiso_de_derivar_masivo_y_reintentar_son_403_y_el_progreso_exige_ver()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: Encendida());
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        using var soloVer = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear]));
        using var soloDerivar = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.DerivacionCrear]));

        (await soloVer.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(Lote), new { vehiculo_ids = new[] { Guid.NewGuid() } }, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await soloVer.PostAsync(DerivacionDePrueba.RutaReintentar(Lote, Operacion), null, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await soloDerivar.GetAsync(DerivacionDePrueba.RutaOperacion(Lote, Operacion), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await soloDerivar.GetAsync(DerivacionDePrueba.RutaOperaciones(Lote), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Desde_el_contexto_de_backoffice_masivo_y_reintentar_son_403()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: Encendida());
        var ct = TestContext.Current.CancellationToken;
        using var cliente = api.Cliente(api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.DerivacionCrear, Permisos.BackofficeVer], roles: ["admin_diagnostico"], tipoEmpresa: "FLIT"));
        var empresa = Guid.NewGuid();

        var masivo = await cliente.PostAsJsonAsync($"{DerivacionDePrueba.RutaMasivo(Lote)}?empresa={empresa}", new { vehiculo_ids = new[] { Guid.NewGuid() } }, ct);
        var reintentar = await cliente.PostAsync($"{DerivacionDePrueba.RutaReintentar(Lote, Operacion)}?empresa={empresa}", null, ct);

        foreach (var respuesta in new[] { masivo, reintentar })
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(DerivacionEndpoints.CodigoDesdeBackoffice);
        }
    }

    [Theory]
    [InlineData("""{"vehiculo_ids":[]}""", DerivacionMasiva.CodigoVehiculosRequeridos)]
    [InlineData("""{}""", DerivacionMasiva.CodigoVehiculosRequeridos)]
    [InlineData("""{"vehiculo_ids":["0199f3a1-0000-7000-8000-000000000001","0199f3a1-0000-7000-8000-000000000002","0199f3a1-0000-7000-8000-000000000003","0199f3a1-0000-7000-8000-000000000004"]}""", DerivacionMasiva.CodigoDemasiadosVehiculos)]
    [InlineData("""{"vehiculo_ids": no""", DerivacionEndpoints.CodigoCuerpoInvalido)]
    public async Task Lista_vacia_mayor_al_maximo_o_cuerpo_invalido_es_400_sin_tocar_la_base(string cuerpo, string codigo)
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: Encendida(new() { [$"{OpcionesDerivacion.Seccion}:MaxItemsPorOperacion"] = "3" }));
        var ct = TestContext.Current.CancellationToken;
        using var cliente = api.Cliente(api.Token(permisos: [Permisos.DerivacionCrear]));

        var respuesta = await cliente.PostAsync(DerivacionDePrueba.RutaMasivo(Lote), new StringContent(cuerpo, Encoding.UTF8, "application/json"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await respuesta.Content.ReadAsStringAsync(ct));
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(codigo);
    }

    [Fact]
    public void Claves_del_masivo_por_defecto_y_su_validacion()
    {
        var o = new OpcionesDerivacion();
        (o.ConsumidorHabilitado, o.Concurrencia, o.Prefetch, o.CreditoPorCompania, o.TasaPorSegundo).Should().Be((true, (ushort)4, (ushort)8, 2, 10));
        (o.PausaTrasFallos, o.PausaSegundos, o.PlazoRpcSegundos, o.MaxIntentos, o.MaxItemsPorOperacion).Should().Be((5, 30, 15, 3, 5000));
        (o.Reconciliador.Intervalo, o.Reconciliador.Margen, o.Reconciliador.Maximo).Should().Be((TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(15), 500));
        OpcionesDerivacion.Validar(o).Should().BeTrue();

        foreach (var mala in new[]
                 {
                     new OpcionesDerivacion { CreditoPorCompania = 0 },
                     new OpcionesDerivacion { CreditoPorCompania = 5 }, // > Concurrencia (4)
                     new OpcionesDerivacion { Concurrencia = 0 },
                     new OpcionesDerivacion { Concurrencia = 8, Prefetch = 4 },
                     new OpcionesDerivacion { TasaPorSegundo = 0 },
                     new OpcionesDerivacion { PausaTrasFallos = 0 },
                     new OpcionesDerivacion { PausaSegundos = 0 },
                     new OpcionesDerivacion { MaxIntentos = 0 },
                     new OpcionesDerivacion { MaxIntentos = 4 }, // más que las esperas del SDK
                     new OpcionesDerivacion { MaxItemsPorOperacion = 0 },
                     new OpcionesDerivacion { Reconciliador = new() { Intervalo = TimeSpan.Zero } },
                     new OpcionesDerivacion { Reconciliador = new() { Margen = TimeSpan.FromSeconds(30) } },
                     new OpcionesDerivacion { Reconciliador = new() { Maximo = 0 } },
                 })
        {
            OpcionesDerivacion.Errores(mala).Should().NotBeEmpty();
            OpcionesDerivacion.Validar(mala).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Consumidor_y_reconciliador_solo_con_la_derivacion_y_el_consumidor_encendidos()
    {
        await using (var encendida = await ApiDePrueba.SinBaseAsync(configurar: Encendida(new() { [$"{OpcionesDerivacion.Seccion}:ConsumidorHabilitado"] = "true" })))
        {
            var hospedados = encendida.App.Services.GetServices<IHostedService>().ToList();
            hospedados.Should().Contain(h => h is ReconciliadorDerivacion);
            hospedados.Should().Contain(h => h.GetType().GenericTypeArguments.Contains(typeof(ConsumidorDerivacion)), "el consumidor de diagnostico.derivaciones");
        }

        await using (var sinConsumidor = await ApiDePrueba.SinBaseAsync(configurar: Encendida()))
        {
            var hospedados = sinConsumidor.App.Services.GetServices<IHostedService>().ToList();
            hospedados.Should().NotContain(h => h is ReconciliadorDerivacion, "las pruebas apagan el consumidor (ConsumidorHabilitado=false)");
            hospedados.Should().NotContain(h => h.GetType().GenericTypeArguments.Contains(typeof(ConsumidorDerivacion)));
        }

        await using var apagada = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [$"{OpcionesDerivacion.Seccion}:ConsumidorHabilitado"] = "true" }));
        apagada.App.Services.GetServices<IHostedService>().Should().NotContain(h => h is ReconciliadorDerivacion || h.GetType().GenericTypeArguments.Contains(typeof(ConsumidorDerivacion)),
            "sin Habilitada no se registra nada del masivo");
    }

    [Fact]
    public async Task El_reconciliador_no_toca_la_base_con_el_freno_abierto()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: Encendida());
        var freno = api.App.Services.GetRequiredService<FrenoTramites>();
        for (var i = 0; i < api.App.Services.GetRequiredService<IOptions<OpcionesDerivacion>>().Value.PausaTrasFallos; i++)
            freno.RegistrarFalloTransitorio();

        var vuelta = await api.App.Services.GetRequiredService<ReconciliadorDerivacion>().ReconciliarAsync(TestContext.Current.CancellationToken);

        vuelta.Should().Be(new VueltaReconciliadorDerivacion(Revisados: 0, Creados: 0, Republicados: 0, Fallidos: 0, EnPausa: true));
    }

    private static Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder> Encendida(Dictionary<string, string?>? extra = null) => b =>
    {
        var configuracion = new Dictionary<string, string?> { [$"{OpcionesDerivacion.Seccion}:Habilitada"] = "true" };
        foreach (var (clave, valor) in extra ?? [])
            configuracion[clave] = valor;
        b.Configuration.AddInMemoryCollection(configuracion);
        b.Services.AddSingleton<DerivacionTramitesService.DerivacionTramitesServiceClient>(new DerivacionTramitesFalso());
        b.Services.AddScoped<IClienteDerivacionTramites, ClienteDerivacionTramitesGrpc>();
    };
}
