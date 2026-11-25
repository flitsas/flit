using System.Collections.Concurrent;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Impuestos.Tests.Ejecucion;

/// <summary>
/// Entorno común de las pruebas de M09 (HU #14694): la API de verdad sobre una base efímera, el API de liquidación como
/// <see cref="ApiLiquidacionDePrueba"/>, el RUNT como <see cref="RuntDePrueba"/> y un reloj que la prueba mueve. El reconciliador
/// no corre solo: cada prueba lo llama. <see cref="ProcesarColaAsync"/> hace de broker: entrega como el SDK (bandeja en la misma
/// transacción del efecto) cada <c>impuestos.liquidacion.solicitada</c> sin publicar de la outbox y la marca publicada.
/// </summary>
/// <example>
/// <code>
/// await using var api = await EntornoEjecucion.ConApiAsync(proveedor, runt);
/// await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
/// var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["ABC123"], ct);
/// await EntornoEjecucion.DespacharAsync(api, empresa, ct);
/// await EntornoEjecucion.ProcesarColaAsync(api, ct);
/// </code>
/// </example>
internal static class EntornoEjecucion
{
    public const string UrlAvisos = "https://dev.flit.prueba/api/v1/impuestos/avisos";

    /// <summary>params_schema de Antioquia según la guía §4.</summary>
    public const string Esquema05 =
        "{\"type\":\"object\",\"required\":[\"documentType\",\"documentNumber\",\"plateNumber\",\"detail\"]," +
        "\"properties\":{\"detail\":{\"required\":[\"secretaryDepartmentCode\",\"vehicleModel\",\"secretaryCity\",\"OwnerName\"]}}}";

    public const string Esquema25 = "{\"type\":\"object\",\"properties\":{\"detail\":{\"required\":[\"secretaryDepartmentCode\"]}}}";

    public const string OrganismoMedellin = "STRIA TTO MEDELLIN";
    public const string OrganismoChia = "STRIA TTO CHIA";

    public static Task<ApiDePrueba> ConApiAsync(ApiLiquidacionDePrueba proveedor, RuntDePrueba runt, RelojDePrueba? reloj = null, RegistroDeLogs? logs = null,
        Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder>? configurar = null) =>
        ApiDePrueba.ConBaseAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Impuestos:Api:BaseUrl"] = ClienteApiLiquidacionTests.Base,
                [GeneradorTokenAviso.ClaveUrlBase] = UrlAvisos,
                ["Impuestos:Ejecucion:Reconciliador:Activo"] = "false",
                ["Impuestos:Ejecucion:TasaPorSegundo"] = "1000",
            });
            b.Services.AddHttpClient<ClienteApiLiquidacion>().ConfigurePrimaryHttpMessageHandler(() => proveedor);
            b.Services.AddSingleton<IClienteRunt>(runt);
            if (reloj is not null)
                b.Services.AddSingleton<TimeProvider>(reloj);
            if (logs is not null)
            {
                b.Logging.AddProvider(logs);
                b.Logging.AddFilter<RegistroDeLogs>(null, LogLevel.Trace);
            }

            configurar?.Invoke(b);
        });

    /// <summary>Aprovisionamiento en <paramref name="estado"/> con su api_key (y el secreto si <paramref name="secreto"/>) y los catálogos.</summary>
    public static async Task SembrarEmpresaAsync(
        ApiDePrueba api, Guid empresa, CancellationToken ct, EstadoAprovisionamiento estado = EstadoAprovisionamiento.Listo, bool secreto = true, bool modoRespaldo = false)
    {
        await using (var db = api.Contexto(empresa))
        {
            db.Add(new AprovisionamientoEmpresa { TenantId = empresa, Estado = estado, ModoRespaldoFirma = modoRespaldo });
            await db.SaveChangesAsync(ct);
        }

        using (var scope = api.App.Services.CreateScope())
        {
            var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();
            await using var db = almacen.ContextoDe(empresa);
            almacen.Escribir(db, null, empresa, new CredencialesEnClaro(ClienteApiLiquidacionTests.ApiKey, secreto ? ClienteApiLiquidacionTests.WebhookSecret : null),
                OrigenCredencial.Alta, null);
            await db.SaveChangesAsync(ct);
        }

        await SembrarCatalogosAsync(api, ct);
    }

    /// <summary>Fuentes 05 (activa), 25 (activa) y 41 (inactiva) y los organismos de Medellín y Chía (globales, una vez por base).</summary>
    public static async Task SembrarCatalogosAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var db = api.Contexto(null);
        if (await db.Set<FuenteApi>().AnyAsync(ct))
            return;
        var ahora = DateTimeOffset.UtcNow;
        db.AddRange(
            new FuenteApi { DepartamentoCodigo = "05", Nombre = "Antioquia", Activa = true, ParamsSchema = Esquema05, SincronizadoEn = ahora },
            new FuenteApi { DepartamentoCodigo = "25", Nombre = "Cundinamarca", Activa = true, ParamsSchema = Esquema25, SincronizadoEn = ahora },
            new FuenteApi { DepartamentoCodigo = "41", Nombre = "Huila", Activa = false, ParamsSchema = "{}", SincronizadoEn = ahora });
        db.AddRange(
            new Organismo { Codigo = "05001000", Nombre = OrganismoMedellin, NombreNormalizado = OrganismoMedellin, DepartamentoCodigo = "05", Departamento = "ANTIOQUIA", Municipio = "MEDELLIN", SincronizadoEn = ahora },
            new Organismo { Codigo = "25175000", Nombre = OrganismoChia, NombreNormalizado = OrganismoChia, DepartamentoCodigo = "25", Departamento = "CUNDINAMARCA", Municipio = "CHIA", SincronizadoEn = ahora });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Un lote con un ítem por placa (en estado <paramref name="estado"/>); MASIVA si hay más de una placa.</summary>
    public static async Task<(Guid Lote, IReadOnlyList<Guid> Items)> SembrarLoteAsync(
        ApiDePrueba api, Guid empresa, IReadOnlyList<string> placas, CancellationToken ct, EstadoItem estado = EstadoItem.Pendiente,
        DateTimeOffset? creadaEn = null, TipoLiquidacion? tipo = null)
    {
        await using var db = api.Contexto(empresa);
        var lote = new Liquidacion
        {
            TenantId = empresa,
            Tipo = tipo ?? (placas.Count == 1 ? TipoLiquidacion.Individual : TipoLiquidacion.Masiva),
            Estado = EstadoLote.Creado,
            TotalItems = placas.Count,
            CPendientes = placas.Count,
            ConfigCopia = "{\"maxRegistros\":500,\"departamentosHabilitados\":null,\"concurrencia\":2}",
            CreadaPor = Guid.NewGuid(),
            CreadaEn = creadaEn ?? DateTimeOffset.UtcNow,
            NombreArchivo = placas.Count == 1 ? null : "placas.csv",
        };
        db.Add(lote);
        var items = placas.Select((p, i) => new LiquidacionItem
        {
            TenantId = empresa,
            LiquidacionId = lote.Id,
            Fila = placas.Count == 1 ? null : i + 2,
            Placa = p,
            Estado = estado,
            ActualizadoEn = DateTimeOffset.UtcNow,
        }).ToList();
        db.AddRange(items);
        await db.SaveChangesAsync(ct);
        return (lote.Id, [.. items.Select(i => i.Id)]);
    }

    public static async Task<LiquidacionItem> ItemAsync(ApiDePrueba api, Guid empresa, Guid item, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<LiquidacionItem>().AsNoTracking().SingleAsync(i => i.Id == item, ct);
    }

    public static async Task<Liquidacion> LoteAsync(ApiDePrueba api, Guid empresa, Guid lote, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<Liquidacion>().AsNoTracking().SingleAsync(l => l.Id == lote, ct);
    }

    public static async Task<AprovisionamientoEmpresa> AprovisionamientoAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct);
    }

    /// <summary>Libera el crédito de la compañía como lo hace quien encola: en su transacción, con la empresa fijada.</summary>
    public static async Task<int> DespacharAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
        var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
        var despachador = scope.ServiceProvider.GetRequiredService<DespachadorCredito>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var n = await despachador.LiberarAsync(empresa, ct);
            await tx.CommitAsync(ct);
            return n;
        });
    }

    /// <summary>Mensajes <c>impuestos.liquidacion.solicitada</c> de la outbox (publicados o no).</summary>
    public static async Task<List<OutboxMessage>> SolicitudesAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var db = api.Contexto(null);
        return await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.RoutingKey == LiquidacionSolicitada.Tipo).OrderBy(m => m.OccurredAt).ToListAsync(ct);
    }

    /// <summary>Entrega como el SDK: transacción, dedupe por la bandeja de la cola, efecto y fila de la bandeja en el mismo guardado.</summary>
    public static async Task<bool> EntregarAsync(ApiDePrueba api, EventEnvelope sobre, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
        var consumidor = scope.ServiceProvider.GetRequiredService<ConsumidorLiquidacion>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await db.Set<InboxMessage>().AnyAsync(m => m.EventId == sobre.EventId && m.Consumer == LiquidacionSolicitada.Cola, ct))
                return false;
            await consumidor.HandleAsync(sobre, sobre.DataAs<LiquidacionSolicitada>(), ct);
            db.Set<InboxMessage>().Add(new InboxMessage { EventId = sobre.EventId, Consumer = LiquidacionSolicitada.Cola, Type = sobre.Type, ProcessedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });
    }

    /// <summary>
    /// Hace de broker: entrega cada solicitud sin publicar de la outbox (en orden) y la marca publicada; repite hasta que no quedan,
    /// porque una entrega puede liberar el crédito y encolar otra. Devuelve cuántas entregó.
    /// </summary>
    public static async Task<int> ProcesarColaAsync(ApiDePrueba api, CancellationToken ct)
    {
        var entregadas = 0;
        for (var vuelta = 0; vuelta < 50; vuelta++)
        {
            var pendientes = (await SolicitudesAsync(api, ct)).Where(m => m.PublishedAt is null).ToList();
            if (pendientes.Count == 0)
                return entregadas;
            foreach (var mensaje in pendientes)
            {
                await using (var db = api.Contexto(null))
                {
                    await db.Set<OutboxMessage>().Where(m => m.Id == mensaje.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.PublishedAt, DateTimeOffset.UtcNow), ct);
                }

                await EntregarAsync(api, EventEnvelope.FromJson(System.Text.Encoding.UTF8.GetBytes(mensaje.Payload)), ct);
                entregadas++;
            }
        }

        throw new InvalidOperationException("La cola no se vació en 50 vueltas.");
    }

    public static EventEnvelope Sobre(Guid empresa, Guid lote, Guid item) => new(
        Guid.CreateVersion7(), LiquidacionSolicitada.Tipo, LiquidacionSolicitada.Version, DateTimeOffset.UtcNow, empresa, "impuestos", "prueba-m09",
        System.Text.Json.JsonSerializer.SerializeToElement(new LiquidacionSolicitada(lote, item), EventEnvelope.JsonOptions));

    public static async Task<VueltaEjecucion> ReconciliarAsync(ApiDePrueba api, CancellationToken ct) =>
        await api.App.Services.GetRequiredService<ReconciliadorEjecucion>().ReconciliarAsync(ct);

    public static int Envios(ApiLiquidacionDePrueba proveedor) =>
        proveedor.Pedidos.Count(p => p.Metodo == "POST" && p.Uri.EndsWith(ClienteApiLiquidacion.RutaConsultas, StringComparison.Ordinal));

    public static string Aceptada(string id = "8d1e0c55-0000-4000-8000-000000000001") => "{\"id\":\"" + id + "\",\"status\":\"queued\",\"source_id\":\"s-05\"}";

    /// <summary>Proveedor que acepta toda consulta (202) y responde /fuentes con 200.</summary>
    public static ApiLiquidacionDePrueba ProveedorQueAcepta() => new()
    {
        Responder = r => r.Method == HttpMethod.Post
            ? ApiLiquidacionDePrueba.Json(System.Net.HttpStatusCode.Accepted, Aceptada())
            : ApiLiquidacionDePrueba.Json(System.Net.HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200),
    };
}

/// <summary>Doble del RUNT: responde lo que fija la prueba y registra cada llamada (empresa y placa).</summary>
internal sealed class RuntDePrueba : IClienteRunt
{
    private readonly ConcurrentQueue<(Guid Empresa, string Placa)> _llamadas = new();

    public Func<string, ConsultaRunt> Responder { get; set; } = _ => new ConsultaRunt.Encontrado(Antioquia());

    public IReadOnlyList<(Guid Empresa, string Placa)> Llamadas => [.. _llamadas];

    public Task<ConsultaRunt> ConsultarPorPlacaAsync(Guid empresa, string placa, CancellationToken ct)
    {
        _llamadas.Enqueue((empresa, placa));
        return Task.FromResult(Responder(placa));
    }

    public static DatosRunt Antioquia() => new(
        TipoDocumento: "CC", NumeroDocumento: "71706228", NombrePropietario: "PEDRO PEREZ", Modelo: "2019", Marca: "CHEVROLET",
        OrganismoNombre: EntornoEjecucion.OrganismoMedellin, Correo: "pedro@correo.example", Telefono: "3001234567",
        DireccionDepartamento: "ANTIOQUIA", DireccionCiudad: "MEDELLIN", Direccion: "CL 10 20 30");

    public static DatosRunt Cundinamarca(bool conCorreo = true, bool conTelefono = true) => Antioquia() with
    {
        OrganismoNombre = EntornoEjecucion.OrganismoChia,
        Correo = conCorreo ? "pedro@correo.example" : null,
        Telefono = conTelefono ? "3001234567" : null,
    };
}

/// <summary>
/// Reloj de prueba: arranca en la hora real truncada a microsegundos (la precisión de <c>timestamptz</c>, para comparar con lo
/// guardado) y solo avanza cuando la prueba lo mueve.
/// </summary>
internal sealed class RelojDePrueba : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = new(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Ahora;

    public void Avanzar(TimeSpan cuanto) => Ahora += cuanto;
}
