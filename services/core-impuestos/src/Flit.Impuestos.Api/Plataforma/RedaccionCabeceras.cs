namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// EP-CF3a: los logs del servicio no llevan el valor de <c>X-API-Key</c>, <c>Authorization</c> ni <c>X-Flit-Signature</c>.
/// <see cref="AddRedaccionCabeceras"/> lo aplica a todos los <c>HttpClient</c> del servicio (el registro de peticiones de
/// <c>IHttpClientFactory</c> escribe <see cref="Redactado"/> en su lugar); <see cref="Redactar"/> sirve a quien registre cabeceras a
/// mano (p. ej. el receptor del webhook). Las demás cabeceras se registran tal cual.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddRedaccionCabeceras();
/// RedaccionCabeceras.Redactar("X-Flit-Signature", firma); // "[REDACTADO]"
/// </code>
/// </example>
public static class RedaccionCabeceras
{
    /// <summary>Lo que se escribe en el log en lugar del valor.</summary>
    public const string Redactado = "[REDACTADO]";

    /// <summary>Cabeceras con secretos (EP-CF3a).</summary>
    public static IReadOnlyList<string> Sensibles { get; } = ["X-API-Key", "Authorization", "X-Flit-Signature"];

    private static readonly HashSet<string> Conjunto = new(Sensibles, StringComparer.OrdinalIgnoreCase);

    public static bool EsSensible(string cabecera) => Conjunto.Contains(cabecera);

    public static string Redactar(string cabecera, string valor) => EsSensible(cabecera) ? Redactado : valor;

    /// <summary>Redacta las cabeceras sensibles en el log de todo <c>HttpClient</c> creado con la fábrica del servicio.</summary>
    public static IServiceCollection AddRedaccionCabeceras(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ConfigureHttpClientDefaults(cliente => cliente.RedactLoggedHeaders(EsSensible));
        return services;
    }
}
