using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Api.RuntIntempo;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// core-consultas real (sin arrancar: los consumidores no escuchan el broker) sobre un Postgres efímero de
/// <c>ConnectionStrings__Core</c>, para ejecutar los efectos del bus del diagnóstico (ADR-0075) en un scope con su transacción,
/// como lo hace el consumidor del SDK. Intempo es un <see cref="HttpMessageHandler"/> falso y los proveedores de la cadena son
/// falsos: nada sale a la red. Sin Postgres la prueba FALLA (no se omite), por el contrato de la ola.
/// <para>Uso de ejemplo: <c>await anfitrion.EjecutarAsync&lt;ConsumidorTrabajosLote&gt;(sobre)</c>.</para>
/// </summary>
public class AnfitrionBus : IAsyncLifetime
{
    public const string PlacaEncontrada = "TST401";
    public const string PlacaCaida = "TST500";
    public const string BaseIntempo = "https://intempo.prueba";

    private string _database = string.Empty;

    /// <summary>Configuración extra (p. ej. simulación) antes de construir; la pone la clase de pruebas que la necesita.</summary>
    public Dictionary<string, string?> Extra { get; } = [];

    public IntempoFalsa Intempo { get; } = new();

    public Contador Llamadas { get; } = new();

    public WebApplication App { get; private set; } = null!;

    public virtual async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        App = Construir(Extra);
        await Program.MigrateAsync(App);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
        await db.Database.EnsureDeletedAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Otro core-consultas sobre la misma base con configuración propia (lo construye y lo devuelve sin arrancar).</summary>
    public WebApplication Construir(IReadOnlyDictionary<string, string?> extra) => Program.Build([], b =>
    {
        b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = _database,
            ["Platform:ServiceClient:ClientSecret"] = "secreto",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            ["ImprontaRunt:ApiKey"] = "llave-de-prueba",
            ["Verifik:BearerToken"] = "llave-verifik-de-prueba",
            [IntempoOpciones.VariableBaseUrl] = BaseIntempo,
            [IntempoOpciones.VariableTimeout] = "5",
            [$"{OpcionesConsumidoresDiagnostico.Seccion}:TiempoMaximoSegundos"] = "2",
            [ServicioSettings.GrpcPortKey] = "5994",
        });
        b.Configuration.AddInMemoryCollection(extra);
    }, s =>
    {
        s.AddHttpClient(ProveedorRuntIntempo.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Intempo);
        foreach (var clave in new[] { FinesProviderResolver.FlitFines, FinesProviderResolver.VerifikSimit, "verifik_rues", "falso_conductor" })
            s.AddTransient<IConsultationProvider>(_ => new Falso(clave, Llamadas));
        s.AddTransient<IConsultationProvider>(_ => new Explota(Llamadas));
        s.AddTransient<IConsultationProvider>(_ => new Lento(Llamadas));
    });

    /// <summary>El efecto de <typeparamref name="TConsumidor"/> en un scope con su transacción, como el consumidor del SDK.</summary>
    public async Task EjecutarAsync<TConsumidor>(EventEnvelope sobre, bool confirmar = true)
        where TConsumidor : IEventConsumer<TrabajoConsultaDiagnostico>
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var consumidor = scope.ServiceProvider.GetRequiredService<TConsumidor>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await consumidor.HandleAsync(sobre, sobre.DataAs<TrabajoConsultaDiagnostico>(), ct);
            await db.SaveChangesAsync(ct);
            if (confirmar)
                await tx.CommitAsync(ct);
            else
                await tx.RollbackAsync(ct);
        });
    }

    /// <summary>Un trabajo de <paramref name="rpc"/> de Diagnóstico para <paramref name="empresa"/>, como lo deja su outbox.</summary>
    public static EventEnvelope Trabajo(Guid empresa, string fuente, string rpc, IMessage solicitud, bool esExpress = false, string producer = "diagnostico") =>
        Trabajo(empresa, fuente, rpc, JsonFormatter.Default.Format(solicitud), esExpress, producer);

    public static EventEnvelope Trabajo(Guid empresa, string fuente, string rpc, string solicitudJson, bool esExpress = false, string producer = "diagnostico")
    {
        using var solicitud = JsonDocument.Parse(solicitudJson);
        var datos = new TrabajoConsultaDiagnostico(Guid.CreateVersion7(), Guid.CreateVersion7(), null, fuente, rpc, solicitud.RootElement.Clone());
        return new EventEnvelope(Guid.CreateVersion7(), $"{(esExpress ? TrabajoConsultaDiagnostico.PrefijoExpress : TrabajoConsultaDiagnostico.Prefijo)}.{fuente}",
            1, DateTimeOffset.UtcNow, empresa, producer, $"corr-{Guid.NewGuid():N}"[..20], JsonSerializer.SerializeToElement(datos, EventEnvelope.JsonOptions));
    }

    /// <summary>Los resultados publicados (outbox) del trabajo de <paramref name="sobre"/>.</summary>
    public async Task<List<EventEnvelope>> ResultadosAsync(EventEnvelope sobre)
    {
        var trabajoId = sobre.Data.GetProperty("trabajoId").GetString()!;
        await using var scope = App.Services.CreateAsyncScope();
        var filas = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.RoutingKey == ResultadoConsultaDiagnostico.Tipo && m.Payload.Contains(trabajoId))
            .ToListAsync(TestContext.Current.CancellationToken);
        foreach (var fila in filas)
            fila.Exchange.Should().Be("flit.consultas", "el resultado va al exchange propio de Consultas");
        return [.. filas.Select(f => EventEnvelope.FromJson(Encoding.UTF8.GetBytes(f.Payload)))];
    }

    public async Task<List<ConsumoConsulta>> ConsumosAsync(Guid empresa)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Consumos.AsNoTracking()
            .Where(c => c.TenantId == empresa).ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task SembrarEmpresaAsync(Guid empresa, string fuenteMultas, string cadenas = """{"conductor":{"primary":"falso_conductor","fallback":[]}}""")
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        db.ConfiguracionEmpresas.Add(new ConfiguracionEmpresa { TenantId = empresa, CadenasJson = cadenas, FuenteMultas = fuenteMultas, ActualizadoEn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Llamadas a los proveedores falsos, por clave.</summary>
    public sealed class Contador
    {
        private readonly Dictionary<string, int> _porClave = [];

        public int De(string clave)
        {
            lock (_porClave)
                return _porClave.GetValueOrDefault(clave);
        }

        internal void Sumar(string clave)
        {
            lock (_porClave)
                _porClave[clave] = _porClave.GetValueOrDefault(clave) + 1;
        }
    }

    /// <summary>Proveedor falso: verde, con un campo que dice quién respondió y otro con un nombre (que la minimización quita).</summary>
    private sealed class Falso(string key, Contador llamadas) : IConsultationProvider
    {
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            llamadas.Sumar(key);
            return Task.FromResult(new ConsultationResult(key, "green",
                [new ConsultationCheck("conductor_identidad", "Persona en RUNT", "ok", key, null)],
                [new HydratedField("respondio", key, null), new HydratedField("person_full_name", "NOMBRE FICTICIO PRUEBA", null)]));
        }
    }

    /// <summary>Falla inesperada con un mensaje que nunca debe viajar.</summary>
    private sealed class Explota(Contador llamadas) : IConsultationProvider
    {
        public const string Clave = "falso_explota";

        public string Key => Clave;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            llamadas.Sumar(Clave);
            throw new InvalidOperationException("detalle interno con DOCUMENTO 9999900011");
        }
    }

    /// <summary>No responde hasta que lo cancelen.</summary>
    private sealed class Lento(Contador llamadas) : IConsultationProvider
    {
        public const string Clave = "falso_lento";

        public string Key => Clave;

        public async Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            llamadas.Sumar(Clave);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("inalcanzable");
        }
    }

    /// <summary>Intempo falsa: registra la URL y responde según la placa (datos inventados).</summary>
    public sealed class IntempoFalsa : HttpMessageHandler
    {
        private const string Crudo = """{"noPlaca":"TST401","noVin":"9ZZPRUEBA00000401","claseVehiculo":"AUTOMOVIL","marca":"MARCA PRUEBA"}""";

        public List<string> Pedidos { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            lock (Pedidos)
                Pedidos.Add(url);
            static HttpResponseMessage R(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(url.EndsWith("Plate=" + PlacaCaida, StringComparison.Ordinal) ? R(HttpStatusCode.InternalServerError, "caído") : R(HttpStatusCode.OK, Crudo));
        }
    }
}
