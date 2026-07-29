using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Flit.DataMigration.Api.Authorization;

/// <summary>
/// El SHA-256 de la llave del API, calculado una sola vez al arrancar.
/// <para>
/// Se guarda el hash y no la llave para que la comparación sea de longitud fija: comparar las
/// cadenas en crudo filtra la longitud del secreto por tiempo de respuesta.
/// </para>
/// <para>
/// <b>Fail-closed</b>, el mismo idioma que <c>ApiSecurityExtensions.cs</c> usa con el token de
/// servicio de ICT: si no hay llave configurada se genera una ALEATORIA de 48 bytes, así que el
/// host levanta pero nada valida. Dejar la puerta abierta cuando falta la configuración sería
/// exactamente lo contrario de lo que necesita un API que puede reescribir trámites.
/// </para>
/// </summary>
internal sealed class MigracionApiKey
{
    internal byte[] Hash { get; }

    /// <summary>Cierto si el host arrancó SIN llave, es decir, con la puerta cerrada del todo.</summary>
    internal bool NotConfigured { get; }

    public MigracionApiKey(IOptions<MigracionApiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var key = options.Value.ApiKey;
        NotConfigured = string.IsNullOrWhiteSpace(key);
        Hash = NotConfigured
            ? SHA256.HashData(RandomNumberGenerator.GetBytes(48))
            : SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }

    internal bool Matches(string? provided) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty)), Hash);
}
