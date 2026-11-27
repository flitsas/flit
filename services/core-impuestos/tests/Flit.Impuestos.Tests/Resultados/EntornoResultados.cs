using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Avisos;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Impuestos.Tests.Resultados;

/// <summary>
/// Entorno común de las pruebas de M10 (HU #14694): la API de verdad sobre una base efímera, con el API de liquidación como
/// <see cref="ApiLiquidacionDePrueba"/>, un <see cref="RelojDePrueba"/> y el trabajador de conciliación apagado (cada prueba llama
/// la ronda a mano). Siembra lotes e ítems directo en la base (M09 se construye en paralelo) y arma avisos firmados como el
/// proveedor (guía de integración §6).
/// </summary>
/// <example>
/// <code>
/// await using var api = await EntornoResultados.ConApiAsync();
/// var empresa = await EntornoResultados.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, EntornoResultados.Secreto, ct);
/// var lote = await EntornoResultados.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
/// var item = await EntornoResultados.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");
/// </code>
/// </example>
internal static class EntornoResultados
{
    public const string Secreto = ClienteApiLiquidacionTests.WebhookSecret;
    public const string ApiKey = ClienteApiLiquidacionTests.ApiKey;

    public static Task<ApiDePrueba> ConApiAsync(ApiLiquidacionDePrueba? proveedor = null, RelojDePrueba? reloj = null, RegistroDeLogs? logs = null) =>
        ApiDePrueba.ConBaseAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Impuestos:Api:BaseUrl"] = ClienteApiLiquidacionTests.Base,
                ["Impuestos:Api:IntegrationToken"] = ClienteApiLiquidacionTests.Token,
                ["Impuestos:Conciliacion:TrabajadorActivo"] = "false",
            });
            b.Services.AddHttpClient<ClienteApiLiquidacion>().ConfigurePrimaryHttpMessageHandler(() => proveedor ?? new ApiLiquidacionDePrueba());
            if (reloj is not null)
                b.Services.AddSingleton<TimeProvider>(reloj);
            if (logs is not null)
            {
                b.Logging.AddProvider(logs);
                b.Logging.AddFilter<RegistroDeLogs>(null, LogLevel.Trace);
            }
        });

    /// <summary>Token opaco como el de M09: 32 bytes aleatorios en base64url (43 caracteres).</summary>
    public static string NuevoToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Compañía con su aprovisionamiento y su credencial (con o sin secreto de webhook).</summary>
    public static async Task<Guid> SembrarEmpresaAsync(
        ApiDePrueba api, EstadoAprovisionamiento estado, string? secreto, CancellationToken ct, Action<AprovisionamientoEmpresa>? ajustar = null)
    {
        var empresa = Guid.NewGuid();
        await using (var db = api.Contexto(empresa))
        {
            var fila = new AprovisionamientoEmpresa { TenantId = empresa, Estado = estado };
            ajustar?.Invoke(fila);
            db.Add(fila);
            await db.SaveChangesAsync(ct);
        }

        using var scope = api.App.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();
        await using var dbc = almacen.ContextoDe(empresa);
        almacen.Escribir(dbc, null, empresa, new CredencialesEnClaro(ApiKey, secreto), OrigenCredencial.Alta, null);
        await dbc.SaveChangesAsync(ct);
        return empresa;
    }

    public static async Task<Guid> SembrarLoteAsync(ApiDePrueba api, Guid empresa, EstadoLote estado, int items, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        var lote = new Liquidacion
        {
            TenantId = empresa,
            Tipo = items == 1 ? TipoLiquidacion.Individual : TipoLiquidacion.Masiva,
            Estado = estado,
            TotalItems = items,
            CPendientes = items,
            ConfigCopia = "{}",
            CreadaPor = Guid.NewGuid(),
            CreadaEn = DateTimeOffset.UtcNow,
        };
        db.Add(lote);
        await db.SaveChangesAsync(ct);
        return lote.Id;
    }

    public static async Task<LiquidacionItem> SembrarItemAsync(
        ApiDePrueba api, Guid empresa, Guid lote, EstadoItem estado, CancellationToken ct, Action<LiquidacionItem>? ajustar = null)
    {
        await using var db = api.Contexto(empresa);
        var item = new LiquidacionItem
        {
            LiquidacionId = lote,
            TenantId = empresa,
            Placa = "LHQ479",
            Estado = estado,
            ActualizadoEn = DateTimeOffset.UtcNow,
        };
        ajustar?.Invoke(item);
        db.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>Ítem con su token: guarda el hash y devuelve el token (lo que M09 pone en el callbackUrl).</summary>
    public static async Task<(LiquidacionItem Item, string Token)> SembrarItemConTokenAsync(
        ApiDePrueba api, Guid empresa, Guid lote, EstadoItem estado, string? consultaId, CancellationToken ct, Action<LiquidacionItem>? ajustar = null)
    {
        var token = NuevoToken();
        var item = await SembrarItemAsync(api, empresa, lote, estado, ct, i =>
        {
            i.TokenHash = ProcesadorAviso.HashDelToken(token);
            i.ConsultaApiId = consultaId;
            i.ConCallback = true;
            i.EnviadoEn = DateTimeOffset.UtcNow;
            ajustar?.Invoke(i);
        });
        return (item, token);
    }

    public static async Task<LiquidacionItem> ItemAsync(ApiDePrueba api, Guid empresa, Guid id, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<LiquidacionItem>().AsNoTracking().SingleAsync(i => i.Id == id, ct);
    }

    public static async Task<Liquidacion> LoteAsync(ApiDePrueba api, Guid empresa, Guid id, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<Liquidacion>().AsNoTracking().SingleAsync(l => l.Id == id, ct);
    }

    public static async Task<AprovisionamientoEmpresa> AprovisionamientoAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct);
    }

    public static async Task<List<AvisoRecibido>> AvisosAsync(ApiDePrueba api, Guid item, CancellationToken ct)
    {
        await using var db = api.Contexto(null);
        return await db.Set<AvisoRecibido>().AsNoTracking().Where(a => a.ItemId == item).ToListAsync(ct);
    }

    /// <summary>Aplica un resultado como lo hace la cola o la conciliación, en un scope propio.</summary>
    public static async Task<Aplicacion> AplicarAsync(ApiDePrueba api, Guid empresa, Guid item, string consultaJson, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var leido = LectorResultado.DesdeConsulta(consultaJson) ?? throw new InvalidOperationException("consulta de prueba ilegible");
        return await scope.ServiceProvider.GetRequiredService<AplicadorResultado>().AplicarAsync(empresa, item, leido, ct);
    }

    /// <summary>
    /// Una consulta como la devuelve <c>GET /api/v1/consultas/{id}</c> (guía §6–§7). <paramref name="totalToPay"/> y
    /// <paramref name="periodTotal"/> van tal cual al JSON: <c>"null"</c> es el null del proveedor.
    /// </summary>
    public static string Consulta(
        string id,
        string status = "succeeded",
        int attempt = 1,
        string? errorCode = null,
        string? errorMessage = null,
        string resultStatus = "con_deuda",
        string totalToPay = "771700",
        string periodTotal = "771700",
        string warnings = "[]",
        string fileId = "\"f1a2\"",
        string callbackUrl = "null",
        string fileErrors = "[]")
    {
        string result;
        if (status == "failed")
        {
            result = $$$"""{"status":"error","plateNumber":"LHQ479","department":{"code":"25","name":"CUNDINAMARCA"},"pendingPeriods":[],"totals":null,"lastPayment":null,"clearance":null,"consolidatedFileId":null,"validations":[{"field":"OwnerName","sent":"PROPIETARIO PRUEBA","portal":null,"result":"mismatch"}],"warnings":{{{warnings}}},"error":{"code":"{{{errorCode}}}","message":"{{{errorMessage}}}"}}""";
        }
        else
        {
            var periodos = resultStatus == "paz_y_salvo"
                ? "[]"
                : $$$"""[{"period":2025,"appraisal":41738000,"tax":710000,"penalty":0,"interest":249000,"discounts":224000,"previousPayments":null,"totalToPay":{{{periodTotal}}},"otherCharges":[{"description":"Costos administrativos","amount":36700}],"dueDate":"2026-11-30","paymentReference":null,"fileId":{{{fileId}}} }]""";
            result = $$$"""{"status":"{{{resultStatus}}}","plateNumber":"LHQ479","department":{"code":"25","name":"CUNDINAMARCA"},"consultedAt":"2026-11-25T15:01:35Z","vehicle":{"brand":null},"pendingPeriods":{{{periodos}}},"totals":{"tax":710000,"penalty":0,"interest":249000,"discounts":224000,"totalToPay":{{{totalToPay}}} },"lastPayment":null,"clearance":null,"consolidatedFileId":null,"validations":[{"field":"OwnerName","sent":"PROPIETARIO PRUEBA","portal":null,"result":"match"}],"warnings":{{{warnings}}},"error":null}""";
        }

        var codigo = errorCode is null ? "null" : $"\"{errorCode}\"";
        var mensaje = errorMessage is null ? "null" : $"\"{errorMessage}\"";
        return $$$"""{"id":"{{{id}}}","source_id":"s-25","source_name":"Cundinamarca","run_id":"r-1","status":"{{{status}}}","attempt":{{{attempt}}},"error_code":{{{codigo}}},"error_message":{{{mensaje}}},"file_errors":{{{fileErrors}}},"callback_url":{{{callbackUrl}}},"result":{{{result}}},"created_at":"2026-11-25T15:01:00.123456Z","started_at":"2026-11-25T15:01:01Z","finished_at":"2026-11-25T15:01:35Z"}""";
    }

    /// <summary>Consulta aún en curso (<c>result</c> null).</summary>
    public static string ConsultaEnCurso(string id, string status = "running") =>
        $$$"""{"id":"{{{id}}}","status":"{{{status}}}","attempt":1,"error_code":null,"error_message":null,"file_errors":[],"result":null,"created_at":"2026-11-25T15:01:00Z"}""";

    /// <summary>Cuerpo del aviso del webhook (guía §6) con la consulta dada.</summary>
    public static string Aviso(string consultaJson, string? deliveryId = null) =>
        $$$"""{"delivery_id":"{{{deliveryId ?? Guid.NewGuid().ToString()}}}","event":"consulta.finalizada","created_at":"2026-11-25T15:01:35.000000Z","consulta":{{{consultaJson}}} }""";

    /// <summary>
    /// El <c>POST /avisos/{token}</c> del proveedor: cuerpo crudo, <c>X-Flit-Timestamp</c> de <paramref name="ahora"/> y la firma
    /// v1 con <paramref name="secreto"/> (o <paramref name="firma"/> si se da).
    /// </summary>
    public static HttpRequestMessage PeticionAviso(
        string token, string cuerpo, string secreto, DateTimeOffset ahora, string? delivery = null, string? firma = null, string evento = "consulta.finalizada")
    {
        var bytes = Encoding.UTF8.GetBytes(cuerpo);
        var ts = ahora.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"{Program.Prefijo}/avisos/{token}") { Content = new ByteArrayContent(bytes) };
        pedido.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        pedido.Headers.Add("X-Flit-Event", evento);
        pedido.Headers.Add("X-Flit-Delivery", delivery ?? Guid.NewGuid().ToString());
        pedido.Headers.Add("X-Flit-Timestamp", ts);
        pedido.Headers.Add("X-Flit-Signature", firma ?? VerificadorFirma.Firmar(secreto, ts, bytes));
        return pedido;
    }

    /// <summary>Espera (hasta <paramref name="maximo"/>) a que se cumpla la condición; la aplicación del aviso es en segundo plano.</summary>
    public static async Task<bool> EsperarAsync(Func<Task<bool>> condicion, TimeSpan maximo, CancellationToken ct)
    {
        var limite = DateTime.UtcNow + maximo;
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
                return true;
            await Task.Delay(50, ct);
        }

        return await condicion();
    }
}

/// <summary>
/// Reloj fijo que la prueba mueve a mano (los temporizadores siguen siendo los del sistema), redondeado al microsegundo como
/// <c>timestamptz</c> de Postgres para comparar exacto lo que se guarda.
/// </summary>
public sealed class RelojDePrueba(DateTimeOffset ahora) : TimeProvider
{
    private DateTimeOffset _ahora = AlMicrosegundo(ahora);

    public DateTimeOffset Ahora
    {
        get => _ahora;
        set => _ahora = AlMicrosegundo(value);
    }

    private static DateTimeOffset AlMicrosegundo(DateTimeOffset t) => new(t.Ticks - (t.Ticks % 10), t.Offset);

    public override DateTimeOffset GetUtcNow() => Ahora;

    public void Avanzar(TimeSpan cuanto) => Ahora += cuanto;
}
