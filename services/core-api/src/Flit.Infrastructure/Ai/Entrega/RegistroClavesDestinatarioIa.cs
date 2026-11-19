using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ai.Entrega;

/// <summary>
/// Claves públicas de los destinatarios de la llave IA (ADR-0091 §2.5-6), por audiencia y <c>kid</c>:
/// <c>Ai:Entrega:&lt;Audiencia&gt;:Kid</c> + <c>ClavePublica</c> (actual) y <c>KidSiguiente</c> + <c>ClavePublicaSiguiente</c>
/// (rotación sin corte). La clave es base64 de DER SPKI en una línea (o PEM). La pública no es secreta, pero solo se acepta
/// RSA ≥ 3072 (SP-9): lo demás se descarta con un Warning que nombra la audiencia y el kid, nunca el valor. Guarda el DER y
/// crea un <see cref="RSA"/> por uso (las instancias no son seguras entre hilos).
/// </summary>
internal sealed partial class RegistroClavesDestinatarioIa
{
    /// <summary>Audiencias con destinatario (minúsculas, como entran al AAD) y su sección de configuración.</summary>
    private static readonly Dictionary<string, string> SeccionPorAudiencia = new(StringComparer.Ordinal)
    {
        ["diagnostico"] = "Diagnostico",
    };

    private readonly Dictionary<(string Audiencia, string Kid), byte[]> _claves = [];

    public RegistroClavesDestinatarioIa(IConfiguration configuration, ILogger<RegistroClavesDestinatarioIa> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        foreach (var (audiencia, seccion) in SeccionPorAudiencia)
        {
            var config = configuration.GetSection($"Ai:Entrega:{seccion}");
            Registrar(logger, audiencia, config["Kid"], config["ClavePublica"]);
            Registrar(logger, audiencia, config["KidSiguiente"], config["ClavePublicaSiguiente"]);
        }
    }

    public bool Conoce(string audiencia, string kid) => _claves.ContainsKey((audiencia, kid));

    /// <summary>Una instancia nueva con la pública del <paramref name="kid"/>, o <c>null</c>. El llamante la libera.</summary>
    public RSA? Crear(string audiencia, string kid)
    {
        if (!_claves.TryGetValue((audiencia, kid), out var der))
            return null;
        var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(der, out _);
        return rsa;
    }

    private void Registrar(ILogger logger, string audiencia, string? kid, string? valor)
    {
        if (string.IsNullOrWhiteSpace(kid) && string.IsNullOrWhiteSpace(valor))
            return;
        kid = kid?.Trim();
        if (!AadLlaveIa.KidValido(kid))
        {
            LogClaveDescartada(logger, audiencia, "(inválido)", "kid");
            return;
        }

        var der = Importar(valor, out var motivo);
        if (der is null)
        {
            LogClaveDescartada(logger, audiencia, kid!, motivo);
            return;
        }

        _claves[(audiencia, kid!)] = der;
    }

    private static byte[]? Importar(string? valor, out string motivo)
    {
        motivo = "ausente";
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        try
        {
            using var rsa = RSA.Create();
            var texto = valor.Trim();
            if (texto.StartsWith("-----BEGIN", StringComparison.Ordinal))
                rsa.ImportFromPem(texto);
            else
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(texto), out _);

            if (rsa.KeySize < SelladorLlaveIa.MinBitsRsa)
            {
                motivo = "rsa_" + rsa.KeySize.ToString(CultureInfo.InvariantCulture) + "_bits";
                return null;
            }

            return rsa.ExportSubjectPublicKeyInfo();
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            motivo = "formato";
            return null;
        }
    }

    [LoggerMessage(EventId = 7301, EventName = "ia.clave_destinatario.descartada", Level = LogLevel.Warning,
        Message = "ia.clave_destinatario.descartada audiencia={Audiencia} kid={Kid} motivo={Motivo}")]
    private static partial void LogClaveDescartada(ILogger logger, string audiencia, string kid, string motivo);
}
