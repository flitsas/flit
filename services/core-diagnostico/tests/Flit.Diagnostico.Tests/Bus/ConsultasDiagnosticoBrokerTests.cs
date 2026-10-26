using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Diagnostico.Tests.Bus;

/// <summary>
/// Un RabbitMQ REAL para una prueba (ADR-0075, T12): <c>FLIT_TEST_RABBITMQ</c> (usuario administrador; en CI, el servicio del job)
/// con un vhost desechable propio, creado y borrado por la API de administración (<c>FLIT_TEST_RABBITMQ_ADMIN</c>, por defecto el
/// puerto 15672 del mismo host). Declara la topología de <c>deploy/rabbitmq/definitions.json</c> (los cinco exchanges, las tres
/// colas del diagnóstico con sus llaves y la política <c>dlq-7d</c>), porque el broker del CI no carga las definiciones y una cola
/// que nadie declaró pierde los mensajes. Para publicar como OTRO servicio crea un usuario desechable con la etiqueta
/// <c>impersonator</c> (solo puede escribir en <c>flit.consultas</c> de este vhost): el broker le acepta un <c>user_id</c> ajeno, así
/// que las pruebas publican con el <c>user_id</c> real del productor (<c>consultas</c>, <c>diagnostico</c>) sin tocar los usuarios de
/// los servicios. Sin broker la prueba se omite en local y FALLA con <c>CI=true</c>, como el fixture del SDK.
/// </summary>
/// <example>
/// <code>
/// await using var broker = await BrokerDePrueba.CrearAsync("diagnostico");
/// Assert.SkipWhen(broker.Omitir is not null, broker.Omitir ?? string.Empty);
/// await broker.PublicarAsync(resultado, userId: "consultas");
/// </code>
/// </example>
public sealed class BrokerDePrueba : IAsyncDisposable
{
    public const string ColaLote = "consultas.diagnostico";
    public const string ColaExpress = "consultas.diagnostico-express";
    public const string ColaResultados = "diagnostico.consultas-resultados";

    private static readonly string[] Exchanges = ["flit.plataforma", "flit.tramites", "flit.consultas", "flit.notificaciones", "flit.diagnostico"];

    private readonly Uri _rabbit = new(Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/");
    private readonly HttpClient _admin;

    private BrokerDePrueba(string servicio)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        Vhost = $"prueba-t12-{servicio}-{sufijo}";
        Suplente = $"prueba-t12-suplente-{sufijo}";
        ClaveSuplente = Guid.NewGuid().ToString("N");
        var admin = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ_ADMIN") ?? $"http://{_rabbit.Host}:15672/";
        _admin = new HttpClient { BaseAddress = new Uri(admin), Timeout = TimeSpan.FromSeconds(10) };
        _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Usuario}:{Contrasena}")));
    }

    /// <summary>Motivo para omitir (sin broker en local); null si el broker está listo.</summary>
    public string? Omitir { get; private set; }

    public string Vhost { get; }

    /// <summary>Usuario desechable con <c>impersonator</c>: publica en <c>flit.consultas</c> con el <c>user_id</c> de otro servicio.</summary>
    public string Suplente { get; }

    private string ClaveSuplente { get; }

    /// <summary>Usuario administrador de las pruebas (con el que se conectan los servicios de la prueba).</summary>
    public string Usuario => Uri.UnescapeDataString(_rabbit.UserInfo.Split(':')[0]);

    private string Contrasena => Uri.UnescapeDataString(_rabbit.UserInfo.Split(':', 2)[1]);

    /// <summary>La URI del administrador en el vhost de la prueba.</summary>
    public string AmqpAdministrador => Amqp(Usuario, Contrasena);

    public static async Task<BrokerDePrueba> CrearAsync(string servicio)
    {
        var broker = new BrokerDePrueba(servicio);
        try
        {
            await broker.PutAsync($"api/vhosts/{E(broker.Vhost)}", new { });
            await broker.PutAsync($"api/permissions/{E(broker.Vhost)}/{E(broker.Usuario)}", new { configure = ".*", write = ".*", read = ".*" });
            await broker.PutAsync($"api/users/{E(broker.Suplente)}", new { password = broker.ClaveSuplente, tags = "impersonator" });
            await broker.PutAsync($"api/permissions/{E(broker.Vhost)}/{E(broker.Suplente)}", new { configure = "", write = "^flit\\.consultas$", read = "" });
            await broker.PutAsync($"api/policies/{E(broker.Vhost)}/dlq-7d", new Dictionary<string, object>
            {
                ["pattern"] = "^(consultas\\.diagnostico|consultas\\.diagnostico-express|diagnostico\\.consultas-resultados)\\.dlq$",
                ["apply-to"] = "queues",
                ["priority"] = 10,
                ["definition"] = new Dictionary<string, object> { ["message-ttl"] = 604800000 },
            });
        }
        catch (HttpRequestException ex)
        {
            await broker.DisposeAsync();
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                throw;
            return new BrokerDePrueba(servicio) { Omitir = $"RabbitMQ no alcanzable: {ex.GetType().Name}" };
        }

        try
        {
            await broker.DeclararTopologiaAsync();
        }
        catch
        {
            await broker.DisposeAsync();
            throw;
        }

        return broker;
    }

    public string Amqp(string usuario, string clave) =>
        $"amqp://{Uri.EscapeDataString(usuario)}:{Uri.EscapeDataString(clave)}@{_rabbit.Host}:{_rabbit.Port}/{Uri.EscapeDataString(Vhost)}";

    /// <summary>Publica <paramref name="sobre"/> en <c>flit.consultas</c> con <paramref name="userId"/> (por el usuario suplente).</summary>
    public Task PublicarAsync(EventEnvelope sobre, string userId) => PublicarAsync(sobre, Amqp(Suplente, ClaveSuplente), userId);

    /// <summary>Publica <paramref name="sobre"/> en <c>flit.consultas</c> como el administrador (su propio <c>user_id</c>).</summary>
    public Task PublicarComoAdministradorAsync(EventEnvelope sobre) => PublicarAsync(sobre, AmqpAdministrador, Usuario);

    /// <summary>El siguiente mensaje de <paramref name="cola"/> (null si no llega en <paramref name="espera"/>), con su <c>user_id</c> y su motivo de DLQ.</summary>
    public async Task<MensajeBroker?> TomarAsync(string cola, TimeSpan espera)
    {
        await using var conexion = await Fabrica().CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var limite = DateTime.UtcNow + espera;
        do
        {
            if (await canal.BasicGetAsync(cola, autoAck: true, TestContext.Current.CancellationToken) is { } m)
            {
                var motivo = m.BasicProperties.Headers?.TryGetValue(PlatformDeadLetters.ReasonHeader, out var v) == true && v is byte[] b ? Encoding.UTF8.GetString(b) : null;
                return new MensajeBroker(EventEnvelope.FromJson(m.Body.Span), m.RoutingKey, m.BasicProperties.UserId, motivo);
            }

            if (espera > TimeSpan.Zero)
                await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        while (DateTime.UtcNow < limite);

        return null;
    }

    /// <summary>Mensajes listos y consumidores de <paramref name="cola"/> (declaración pasiva: sin el retraso de las estadísticas).</summary>
    public async Task<(uint Mensajes, uint Consumidores)> ColaAsync(string cola)
    {
        await using var conexion = await Fabrica().CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var ok = await canal.QueueDeclarePassiveAsync(cola, TestContext.Current.CancellationToken);
        return (ok.MessageCount, ok.ConsumerCount);
    }

    public static async Task EsperarAsync(Func<Task<bool>> condicion, string que, int segundos = 30)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
                return;
            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No se cumplió a tiempo: {que}.");
    }

    /// <summary>Borra el vhost de la prueba (con sus colas y mensajes) y el usuario suplente.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _admin.DeleteAsync($"api/vhosts/{E(Vhost)}");
            await _admin.DeleteAsync($"api/users/{E(Suplente)}");
        }
        catch (HttpRequestException)
        {
            // Sin broker no hay nada que borrar.
        }

        _admin.Dispose();
    }

    private ConnectionFactory Fabrica() => new() { Uri = new Uri(AmqpAdministrador) };

    private async Task DeclararTopologiaAsync()
    {
        await using var conexion = await Fabrica().CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        foreach (var exchange in Exchanges)
            await canal.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var (cola, llave) in new[]
        {
            (ColaLote, "consultas.diagnostico.*"),
            (ColaExpress, "consultas.diagnostico-express.*"),
            (ColaResultados, ResultadoConsultaDiagnostico.Tipo),
        })
        {
            await canal.QueueDeclareAsync(cola, durable: true, exclusive: false, autoDelete: false, cancellationToken: TestContext.Current.CancellationToken);
            await canal.QueueBindAsync(cola, "flit.consultas", llave, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private static async Task PublicarAsync(EventEnvelope sobre, string uri, string userId)
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(uri) }.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), TestContext.Current.CancellationToken);
        var propiedades = new BasicProperties { UserId = userId, DeliveryMode = DeliveryModes.Persistent, ContentType = "application/json" };
        await canal.BasicPublishAsync("flit.consultas", sobre.Type, mandatory: false, propiedades, Encoding.UTF8.GetBytes(sobre.ToJson()), TestContext.Current.CancellationToken);
    }

    private async Task PutAsync(string ruta, object cuerpo) =>
        (await _admin.PutAsJsonAsync(ruta, cuerpo)).EnsureSuccessStatusCode();

    private static string E(string valor) => Uri.EscapeDataString(valor);
}

/// <summary>Un mensaje leído del broker: el sobre, la llave, el <c>user_id</c> que validó el broker y el motivo de DLQ si lo hay.</summary>
public sealed record MensajeBroker(EventEnvelope Sobre, string RoutingKey, string? UserId, string? Motivo);

/// <summary>
/// HU #13727 (ADR-0075, T12, Q4–Q6) — del lado de core-diagnostico, con broker real: el servicio ARRANCADO (outbox publicando,
/// <see cref="ConsumidorResultados"/> escuchando <c>diagnostico.consultas-resultados</c> con <c>ProductoresPermitidos=["consultas"]</c>)
/// y un doble de core-consultas que toma los trabajos de <c>consultas.diagnostico[-express]</c>, responde con las fuentes simuladas
/// de las pruebas (<see cref="ConsultasSimuladas"/>) y publica <c>consultas.resultado.diagnostico</c> con el <c>user_id</c> real
/// <c>consultas</c>. Cubre punta a punta (la RUNT libera la diferida y el lote termina), consultas caído (el trabajo espera y se
/// procesa al volver, sin ERROR), diagnóstico caído (el resultado espera en su cola y se aplica al volver), Express en su cola
/// aparte con el lote lleno, productor no autorizado a la DLQ sin efecto y el reconciliador en pausa con consultas mudo y
/// republicando al volver. core-consultas real atendiendo estos trabajos es la otra mitad
/// (<c>Flit.Consultas.Tests.Bus.ConsultasDiagnosticoBrokerTests</c>).
/// </summary>
/// <example>
/// <code>
/// export FLIT_TEST_RABBITMQ="amqp://flit:flit-prueba@127.0.0.1:5672/"
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConsultasDiagnosticoBrokerTests
/// </code>
/// </example>
public sealed class ConsultasDiagnosticoBrokerTests
{
    [Fact]
    public async Task Punta_a_punta_la_RUNT_libera_la_diferida_y_el_lote_termina()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var broker = await BrokerDePrueba.CrearAsync("diagnostico");
        Assert.SkipWhen(broker.Omitir is not null, broker.Omitir ?? string.Empty);
        await using var api = await ArrancarAsync(broker);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var consultas = new ConsultasDelBroker(broker, api);

        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        await consultas.AtenderHastaAsync(async () => (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).All(c => c.Estado == EstadoCelda.Completado), BrokerDePrueba.ColaLote);

        consultas.Atendidos.Select(a => a.Sobre.Data.GetProperty("fuente").GetString()).Should().Equal(["runt", "simit_vehiculo"], "la SIMIT sale cuando la RUNT cierra");
        consultas.Atendidos.Should().OnlyContain(a => a.Sobre.Producer == "diagnostico" && a.UserId == broker.Usuario && a.Sobre.TenantId == empresa.Id,
            "el user_id lo pone el publicador del SDK con el usuario de su conexión");
        await BrokerDePrueba.EsperarAsync(
            async () => await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.outbox WHERE routing_key = 'diagnostico.lote.terminado' AND published_at IS NOT NULL", ct) == 1,
            "diagnostico.lote.terminado publicado en flit.diagnostico");
        (await broker.ColaAsync(BrokerDePrueba.ColaResultados)).Mensajes.Should().Be(0);
    }

    [Fact]
    public async Task Con_consultas_caido_el_trabajo_espera_en_la_cola_y_se_procesa_al_volver_sin_ERROR()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var broker = await BrokerDePrueba.CrearAsync("diagnostico");
        Assert.SkipWhen(broker.Omitir is not null, broker.Omitir ?? string.Empty);
        await using var api = await ArrancarAsync(broker);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST002")], ct);
        await BrokerDePrueba.EsperarAsync(async () => (await broker.ColaAsync(BrokerDePrueba.ColaLote)).Mensajes == 1, "el trabajo en consultas.diagnostico");
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        (await broker.ColaAsync(BrokerDePrueba.ColaLote)).Should().Be((1u, 0u), "espera, sin duplicarse, a que vuelva core-consultas");
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Should().ContainSingle().Which.Estado.Should().Be(EstadoCelda.EnCurso);

        var consultas = new ConsultasDelBroker(broker, api);
        await consultas.AtenderHastaAsync(async () => (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado == EstadoCelda.Completado, BrokerDePrueba.ColaLote);
        consultas.Atendidos.Should().ContainSingle();
    }

    [Fact]
    public async Task Con_diagnostico_caido_el_resultado_espera_en_su_cola_y_se_aplica_al_volver()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var broker = await BrokerDePrueba.CrearAsync("diagnostico");
        Assert.SkipWhen(broker.Omitir is not null, broker.Omitir ?? string.Empty);
        await using var api = await ArrancarAsync(broker);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var consumidor = ConsumidorDeResultados(api);
        await consumidor.StopAsync(ct);
        await BrokerDePrueba.EsperarAsync(async () => (await broker.ColaAsync(BrokerDePrueba.ColaResultados)).Consumidores == 0, "el consumidor de resultados apagado");

        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST003")], ct);
        var consultas = new ConsultasDelBroker(broker, api);
        await consultas.AtenderHastaAsync(async () => (await broker.ColaAsync(BrokerDePrueba.ColaResultados)).Mensajes == 1, BrokerDePrueba.ColaLote);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.EnCurso, "el resultado espera en diagnostico.consultas-resultados");

        await consumidor.StartAsync(ct);
        await BrokerDePrueba.EsperarAsync(async () => (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado == EstadoCelda.Completado, "la celda cerrada al volver");
        (await broker.ColaAsync(BrokerDePrueba.ColaResultados)).Mensajes.Should().Be(0);
    }

    [Fact]
    public async Task El_Express_va_por_su_cola_y_no_espera_al_lote_lleno()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var broker = await BrokerDePrueba.CrearAsync("diagnostico");
        Assert.SkipWhen(broker.Omitir is not null, broker.Omitir ?? string.Empty);
        await using var api = await ArrancarAsync(broker);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, Motor.Placas(5, desde: 11), ct);
        await BrokerDePrueba.EsperarAsync(async () => (await broker.ColaAsync(BrokerDePrueba.ColaLote)).Mensajes == 5, "los cinco trabajos del lote en su cola");

        var express = await CrearExpressAsync(api, empresa, "TST101", ct);
        var consultas = new ConsultasDelBroker(broker, api);
        await consultas.AtenderHastaAsync(async () => (await Motor.CeldasAsync(api, empresa.Id, express, ct)).Single().Estado == EstadoCelda.Completado, BrokerDePrueba.ColaExpress);

        consultas.Atendidos.Should().ContainSingle().Which.RoutingKey.Should().Be("consultas.diagnostico-express.runt");
        (await broker.ColaAsync(BrokerDePrueba.ColaLote)).Mensajes.Should().Be(5, "el lote sigue esperando su turno");
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Should().HaveCount(5).And.OnlyContain(c => c.Estado == EstadoCelda.EnCurso);
    }

    [Fact]
    public async Task Un_resultado_de_un_productor_no_autorizado_va_a_la_DLQ_sin_efecto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var broker = await BrokerDePrueba.CrearAsync("diagnostico");
        Assert.SkipWhen(broker.Omitir is not null, broker.Omitir ?? string.Empty);
        await using var api = await ArrancarAsync(broker);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST004")], ct);
        var consultas = new ConsultasDelBroker(broker, api);
        var trabajo = await consultas.TomarAsync(BrokerDePrueba.ColaLote);
        var resultado = await consultas.ResultadoAsync(trabajo, ct);

        await broker.PublicarComoAdministradorAsync(resultado);
        await broker.PublicarAsync(resultado with { EventId = Guid.CreateVersion7(), Producer = "tramites" }, userId: "tramites");

        var muertos = new List<MensajeBroker>();
        for (var i = 0; i < 2; i++)
            muertos.Add(await broker.TomarAsync(BrokerDePrueba.ColaResultados + ".dlq", TimeSpan.FromSeconds(15)) ?? throw new TimeoutException("sin mensaje en la DLQ"));
        muertos.Should().OnlyContain(m => m.Motivo == VerificacionProductor.MotivoNoAutorizado);
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.EnCurso, "sin efecto en la celda");
        (await Motor.EscalarAsync(api, $"SELECT count(*) FROM diagnostico.inbox WHERE consumer = '{OpcionesBus.Cola}'", ct)).Should().Be(0);

        await broker.PublicarAsync(resultado, userId: OpcionesBus.Productor);
        await BrokerDePrueba.EsperarAsync(async () => (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado == EstadoCelda.Completado,
            "el mismo resultado del productor consultas sí cierra la celda");
    }

    [Fact]
    public async Task El_reconciliador_no_republica_con_consultas_mudo_y_republica_al_volver()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var broker = await BrokerDePrueba.CrearAsync("diagnostico");
        Assert.SkipWhen(broker.Omitir is not null, broker.Omitir ?? string.Empty);
        var reloj = new RelojManual();
        await using var api = await ArrancarAsync(broker, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST005"), Lotes.Fila("TST006")], ct);
        await BrokerDePrueba.EsperarAsync(async () => (await broker.ColaAsync(BrokerDePrueba.ColaLote)).Mensajes == 2, "los dos trabajos en la cola");
        var consultas = new ConsultasDelBroker(broker, api);
        var perdido = await consultas.TomarAsync(BrokerDePrueba.ColaLote); // se pierde: nadie lo responde

        reloj.Avanzar(TimeSpan.FromMinutes(31));
        var mudo = await reconciliador.ReconciliarAsync(ct);

        (mudo.Revisadas, mudo.EnPausa, mudo.Republicadas, mudo.Cerradas).Should().Be((2, 2, 0, 0), "sin resultados en la ventana, consultas está caído");
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        (await broker.ColaAsync(BrokerDePrueba.ColaLote)).Mensajes.Should().Be(1, "no duplica el trabajo que espera");

        var celdaPerdida = perdido.Sobre.Data.GetProperty("consultaId").GetGuid();
        await consultas.AtenderHastaAsync(
            async () => (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Count(c => c.Estado == EstadoCelda.Completado) == 1, BrokerDePrueba.ColaLote);
        var vivo = await reconciliador.ReconciliarAsync(ct);

        (vivo.Revisadas, vivo.Republicadas, vivo.EnPausa).Should().Be((1, 1, 0), "con resultados fluyendo, la huérfana se republica una vez");
        await consultas.AtenderHastaAsync(async () => (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).All(c => c.Estado == EstadoCelda.Completado), BrokerDePrueba.ColaLote);
        var republicado = consultas.Atendidos.Last().Sobre.DataAs<TrabajoConsultaDiagnostico>();
        republicado.ConsultaId.Should().Be(celdaPerdida);
        republicado.TrabajoId.Should().NotBe(perdido.Sobre.Data.GetProperty("trabajoId").GetGuid(), "trabajo_id nuevo");
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single(c => c.Id == celdaPerdida).Republicaciones.Should().Be(1);
    }

    /// <summary>core-diagnostico con el broker de la prueba, el consumidor de resultados encendido y el reconciliador sin vueltas solas.</summary>
    private static Task<ApiDePrueba> ArrancarAsync(BrokerDePrueba broker, RelojManual? reloj = null) =>
        MotorRunt.ArrancarAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:Messaging:ConnectionString"] = broker.AmqpAdministrador,
                ["Platform:Messaging:PollInterval"] = "00:00:00.200",
                ["Diagnostico:Consultas:Bus:ConsumidorHabilitado"] = "true",
                // Las pruebas llaman al reconciliador directo: su vuelta automática no llega durante la prueba.
                ["Diagnostico:Consultas:Reconciliador:Intervalo"] = "01:00:00",
            });
            b.Services.AddSingleton<IFuenteSimit>(new FuenteSimitSimulada());
            if (reloj is not null)
                b.Services.AddSingleton<TimeProvider>(reloj);
        });

    private static IHostedService ConsumidorDeResultados(ApiDePrueba api) =>
        api.App.Services.GetServices<IHostedService>().Single(s => s.GetType().Name.StartsWith("PlatformConsumer", StringComparison.Ordinal)
            && s.GetType().GetGenericArguments()[1] == typeof(ConsumidorResultados));

    /// <summary>Un Express de <paramref name="placa"/> con RUNT (la empresa ya tiene su configuración).</summary>
    private static async Task<Guid> CrearExpressAsync(ApiDePrueba api, EmpresaDePrueba empresa, string placa, CancellationToken ct)
    {
        // La configuración de la empresa ya la sembró el lote de la prueba.
        var cuerpo = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["identificador"] = placa,
            ["modo"] = "SIMPLE",
            ["tipo_tramite_codigo"] = null,
            ["alcance"] = new Dictionary<string, string[]> { ["por_vehiculo"] = ["RUNT"], ["por_propietario"] = [], ["por_locatario"] = [] },
            ["tipo_documento_locatario"] = null,
            ["documento_locatario"] = null,
        });
        var respuesta = await api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id)).PostAsync(Program.Prefijo + "/express", Configuracion.ConfiguracionOperativaTests.Cuerpo(cuerpo), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Doble de core-consultas por el broker: toma los trabajos de una cola, los responde con <see cref="ConsultasSimuladas"/> y
    /// publica el resultado con el <c>user_id</c> real <see cref="OpcionesBus.Productor"/>.
    /// </summary>
    private sealed class ConsultasDelBroker(BrokerDePrueba broker, ApiDePrueba api)
    {
        public List<MensajeBroker> Atendidos { get; } = [];

        public async Task<MensajeBroker> TomarAsync(string cola) =>
            await broker.TomarAsync(cola, TimeSpan.FromSeconds(15)) ?? throw new TimeoutException($"sin trabajo en {cola}");

        public async Task<EventEnvelope> ResultadoAsync(MensajeBroker trabajo, CancellationToken ct)
        {
            var atendido = new TrabajoAtendido(trabajo.Sobre.EventId, trabajo.RoutingKey, trabajo.Sobre);
            return new BusDePrueba(api).Resultado(atendido, await ConsultasSimuladas.ResponderAsync(api, atendido, ct));
        }

        /// <summary>Atiende los trabajos de <paramref name="cola"/> hasta que se cumpla <paramref name="listo"/>.</summary>
        public async Task AtenderHastaAsync(Func<Task<bool>> listo, string cola)
        {
            var ct = TestContext.Current.CancellationToken;
            await BrokerDePrueba.EsperarAsync(async () =>
            {
                while (await broker.TomarAsync(cola, TimeSpan.Zero) is { } trabajo)
                {
                    Atendidos.Add(trabajo);
                    await broker.PublicarAsync(await ResultadoAsync(trabajo, ct), userId: OpcionesBus.Productor);
                }

                return await listo();
            }, $"atender {cola}");
        }
    }
}
