using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;

/// <summary>
/// Por qué no hay credencial (ADR-0091 §3.2). <see cref="NoConfigurado"/> saca el dictamen de la cola (plantilla);
/// <see cref="Tecnica"/> (core-api caído, sobre inválido, reloj) se reintenta con el tope de la cola. Nunca se confunden (H8).
/// </summary>
public enum FallaCredencial
{
    Ninguna,
    NoConfigurado,
    Tecnica,
}

/// <summary>
/// Proveedor, modelo y llave con los que se llama al modelo. SP-18: clase (no <c>record</c>, cuyo <c>ToString</c> imprimiría la llave),
/// <see cref="ToString"/> redactado y la llave <c>internal</c> + <see cref="JsonIgnoreAttribute"/>. Solo vive en memoria del proceso.
/// </summary>
public sealed class CredencialRedactor
{
    public CredencialRedactor(string proveedor, string modelo, string apiKey, long rowVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proveedor);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        Proveedor = proveedor;
        Modelo = modelo;
        ApiKey = apiKey;
        RowVersion = rowVersion;
    }

    public string Proveedor { get; }

    public string Modelo { get; }

    /// <summary>La llave en claro. Solo la lee el proveedor para el encabezado <c>x-api-key</c>.</summary>
    [JsonIgnore]
    internal string ApiKey { get; }

    public long RowVersion { get; }

    public override string ToString() => $"CredencialRedactor(proveedor={Proveedor}, modelo={Modelo}, row_version={RowVersion}, llave=[REDACTADA])";
}

/// <summary>
/// Resultado de pedir la credencial: una credencial o el motivo de la falla. <see cref="DeCache"/> dice si la credencial salió de la
/// caché (no se pidió en esta llamada): solo esa merece pedir otra y reintentar tras un 401/403 del proveedor (contrato §2.6, CR-7).
/// </summary>
public sealed class ResultadoCredencial
{
    private ResultadoCredencial(CredencialRedactor? credencial, FallaCredencial falla, bool deCache = false)
    {
        Credencial = credencial;
        Falla = falla;
        DeCache = deCache;
    }

    public static ResultadoCredencial NoConfigurado { get; } = new(null, FallaCredencial.NoConfigurado);

    public static ResultadoCredencial Tecnica { get; } = new(null, FallaCredencial.Tecnica);

    public CredencialRedactor? Credencial { get; }

    public FallaCredencial Falla { get; }

    /// <summary>True si la credencial salió de la caché y no se pidió en esta llamada.</summary>
    public bool DeCache { get; }

    public static ResultadoCredencial Con(CredencialRedactor credencial)
    {
        ArgumentNullException.ThrowIfNull(credencial);
        return new(credencial, FallaCredencial.Ninguna);
    }

    /// <summary>La misma credencial marcada como salida de la caché (las fallas no cambian).</summary>
    public ResultadoCredencial DesdeCache() => Credencial is null || DeCache ? this : new(Credencial, Falla, deCache: true);

    public override string ToString() => Credencial is { } c ? c.ToString() : $"ResultadoCredencial({Falla})";
}

/// <summary>De dónde sale la credencial del redactor: la llave local del desarrollador o la configuración de IA de la plataforma.</summary>
public interface IFuenteCredencialRedactor
{
    Task<ResultadoCredencial> ObtenerAsync(CancellationToken ct);

    /// <summary>Descarta la credencial guardada (p. ej. tras un 401/403 del proveedor): la siguiente llamada la vuelve a pedir.</summary>
    void Invalidar();
}

/// <summary><c>Diagnostico:Ia:Origen=local</c>: la llave y el modelo de <c>Diagnostico:Anthropic</c>, como siempre.</summary>
public sealed class FuenteCredencialLocal(IOptions<OpcionesAnthropic> opciones) : IFuenteCredencialRedactor
{
    public Task<ResultadoCredencial> ObtenerAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        var llave = o.ApiKey?.Trim();
        return Task.FromResult(string.IsNullOrEmpty(llave) || string.IsNullOrWhiteSpace(o.Modelo)
            ? ResultadoCredencial.NoConfigurado
            : ResultadoCredencial.Con(new CredencialRedactor("anthropic", o.Modelo, llave, 0)));
    }

    public void Invalidar()
    {
    }
}
