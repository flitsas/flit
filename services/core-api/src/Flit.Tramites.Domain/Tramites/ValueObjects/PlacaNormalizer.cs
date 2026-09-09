namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Forma canónica de una placa: sin espacios de borde y en MAYÚSCULAS invariantes.
///
/// <para>
/// Existe porque la misma línea (<c>Trim().ToUpperInvariant()</c>) estaba copiada en dos sitios
/// —<c>VehicleSignatureImprintRepository.NormalizePlaca</c> y
/// <c>ValidateImprintSignatureHandler.NormalizePlacaSnapshot</c>— y el historial por placa
/// (Feature #12189, HU #12192) era el tercer consumidor. Con tres copias, la primera divergencia
/// (p. ej. quitar guiones) deja dos rutas comparando placas distintas y el desajuste no se ve:
/// devuelve menos filas, no un error.
/// </para>
///
/// <para>
/// Vive en el DOMINIO de trámites y no en API/Infraestructura porque la regla es del negocio (qué
/// es "la misma placa"), no del transporte: la usan el endpoint, un repositorio y un handler, tres
/// capas que no pueden depender entre sí. Se usa <c>ToUpperInvariant</c> y no <c>ToUpper()</c> a
/// propósito: la cultura del hilo no puede decidir cómo se compara una placa.
/// </para>
/// </summary>
public static class PlacaNormalizer
{
    /// <summary>Forma canónica de una placa no nula. Usar cuando el valor ya se validó.</summary>
    public static string Normalize(string placa) => placa.Trim().ToUpperInvariant();

    /// <summary>
    /// Forma canónica, o <c>null</c> si no hay placa que normalizar (null, vacío o solo espacios).
    /// Devolver <c>null</c> —y no cadena vacía— deja que quien llama distinga "no vino placa" de
    /// "vino una placa": un filtro por cadena vacía traería TODO en vez de nada.
    /// </summary>
    public static string? NormalizeOrNull(string? placa) =>
        string.IsNullOrWhiteSpace(placa) ? null : Normalize(placa);
}
