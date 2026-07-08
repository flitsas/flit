using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// Huella de integridad del mandatario (ADR-0023, decisión #2): un hash determinista
/// <c>SHA-256(nombre + número de documento + fecha de registro)</c> que se estampa en cada
/// mandato generado. Se recalcula al editar los datos del mandatario (regeneración), pero
/// los mandatos ya emitidos conservan la huella con la que se generaron.
///
/// El hash <b>no</b> es un mecanismo de anonimización: el <c>documentNumber</c> es PII
/// (Ley 1581) y no debe registrarse en logs ni exponerse en mensajes de error. Aquí solo se
/// usa como insumo de la huella, dentro de este método puro.
/// </summary>
public static class MandateSignerIntegrityHash
{
    /// <summary>
    /// Calcula la huella de integridad. La fecha de registro se normaliza a UTC y se
    /// serializa en formato round-trip ("O") para que el resultado sea estable e
    /// independiente del huso horario del proceso.
    /// </summary>
    public static string Compute(string fullName, string documentNumber, DateTimeOffset registeredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        var normalizedDate = registeredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        // Separador de campo para evitar colisiones por concatenación ("ab"+"c" vs "a"+"bc").
        var payload = $"{fullName.Trim()}{documentNumber.Trim()}{normalizedDate}";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(bytes);
    }
}
