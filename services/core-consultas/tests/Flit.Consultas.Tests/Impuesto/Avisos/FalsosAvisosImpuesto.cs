using System.Text;
using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Api.Impuesto.Avisos;

namespace Flit.Consultas.Tests.Impuesto.Avisos;

/// <summary>
/// Dobles del aviso del impuesto (HU #14711). Datos sintéticos: ningún secreto, placa, documento ni nombre real.
/// <para>Uso de ejemplo: <c>var cuerpo = AvisosDePrueba.Cuerpo("ext-1", attempt: 2);</c> y <c>AvisosDePrueba.Firmar(secreto, ts, cuerpo)</c>.</para>
/// </summary>
internal static class AvisosDePrueba
{
    public const string Secreto = "whsec_prueba_ACTUAL_000000000000000000";
    public const string SecretoAnterior = "whsec_prueba_ANTERIOR_0000000000000000";
    public static readonly byte[] Llave = Encoding.UTF8.GetBytes("llave-de-prueba-del-token-0123456789-abcdef");

    public static byte[] Cuerpo(string idExterno, int attempt, string status = "succeeded", string delivery = "d-1", string evento = "consulta.finalizada") =>
        Encoding.UTF8.GetBytes(
            "{\"delivery_id\":\"" + delivery + "\",\"event\":\"" + evento + "\",\"created_at\":\"2026-11-19T15:01:35Z\",\"consulta\":"
            + Consulta(idExterno, attempt, status) + "}");

    public static string Consulta(string idExterno, int attempt, string status = "succeeded") =>
        "{\"id\":\"" + idExterno + "\",\"status\":\"" + status + "\",\"attempt\":" + attempt
        + ",\"error_code\":null,\"result\":{\"status\":\"paz_y_salvo\",\"totals\":{\"tax\":0,\"penalty\":0,\"interest\":0,\"discounts\":0,\"totalToPay\":0},"
        + "\"pendingPeriods\":[],\"validations\":[],\"warnings\":[]}}";

    /// <summary>Firma calculada aquí, sin el código de producción: hex de HMAC-SHA256(secreto, "{ts}.{cuerpo}").</summary>
    public static string Firmar(string secreto, string timestamp, byte[] cuerpo)
    {
        var mensaje = Encoding.UTF8.GetBytes(timestamp + ".").Concat(cuerpo).ToArray();
        return "v1=" + Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), mensaje));
    }

    public static string Ts(DateTimeOffset ahora) => ahora.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Secretos por empresa; <see cref="Siguiente"/> es lo que devuelve un <c>forzar</c> (secreto rotado).</summary>
internal sealed class FuenteSecretoFalsa : IFuenteSecretoWebhookImpuesto
{
    public Dictionary<Guid, SecretoWebhookImpuesto> PorEmpresa { get; } = [];

    public SecretoWebhookImpuesto? Siguiente { get; set; }

    public Exception? Lanzar { get; set; }

    public List<(Guid Tenant, bool Forzar)> Pedidos { get; } = [];

    public Task<SecretoWebhookImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct)
    {
        Pedidos.Add((tenantId, forzar));
        if (Lanzar is not null)
            throw Lanzar;
        if (forzar && Siguiente is not null)
            PorEmpresa[tenantId] = Siguiente;
        return Task.FromResult(PorEmpresa.TryGetValue(tenantId, out var s) ? s : SecretoWebhookImpuesto.SinConfigurar);
    }
}

/// <summary>
/// Registro en memoria: deduplica por (empresa, entrega) y lee el estado de la consulta del <see cref="SeguimientoEnMemoria"/> (el
/// mismo que usa el proveedor), con la empresa de cada trabajo en <see cref="Empresas"/>.
/// </summary>
internal sealed class RegistroEnMemoria(SeguimientoEnMemoria seguimiento) : IRegistroAvisosImpuesto
{
    private readonly HashSet<(Guid, string)> _entregas = [];

    public Dictionary<Guid, Guid> Empresas { get; } = [];

    public List<(Guid Tenant, string Delivery, string IdExterno, int Attempt)> Registradas { get; } = [];

    /// <summary>Se lanza una sola vez en el próximo <see cref="EstadoConsultaAsync"/> (base caída) y se limpia.</summary>
    public Exception? LanzarEnEstado { get; set; }

    /// <summary>Se lanza en cada <see cref="RegistrarEntregaAsync"/> mientras no sea null.</summary>
    public Exception? LanzarEnRegistro { get; set; }

    public Task<bool> RegistrarEntregaAsync(Guid tenantId, string deliveryId, string idExterno, int attempt, CancellationToken ct)
    {
        if (LanzarEnRegistro is not null)
            throw LanzarEnRegistro;
        var nueva = _entregas.Add((tenantId, deliveryId));
        if (nueva)
            Registradas.Add((tenantId, deliveryId, idExterno, attempt));
        return Task.FromResult(nueva);
    }

    public Task<EstadoConsultaAviso> EstadoConsultaAsync(Guid tenantId, string idExterno, CancellationToken ct)
    {
        if (LanzarEnEstado is { } fallo)
        {
            LanzarEnEstado = null;
            throw fallo;
        }

        foreach (var (trabajo, empresa) in Empresas)
        {
            if (empresa != tenantId || seguimiento.Fila(trabajo) is not { } fila || fila.IdExterno != idExterno)
                continue;
            return Task.FromResult(fila.Estado is "reserved" or "queued" or "running" ? EstadoConsultaAviso.Abierta : EstadoConsultaAviso.Cerrada);
        }

        return Task.FromResult(EstadoConsultaAviso.NoEncontrada);
    }

    public Task<int> PurgarAsync(DateTimeOffset antesDe, CancellationToken ct) => Task.FromResult(0);
}

/// <summary>Permite <c>new TransporteGuionado { Estados = { … } }</c> sobre las colas del doble.</summary>
internal static class ColasDePrueba
{
    public static void Add<T>(this Queue<T> cola, T item) => cola.Enqueue(item);
}
