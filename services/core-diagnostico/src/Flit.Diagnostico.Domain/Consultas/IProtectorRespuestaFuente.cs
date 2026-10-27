namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Cifrado en aplicación de la respuesta COMPLETA de una fuente (HU #14087 AC8, enmienda de ADR-0075 en Propuesto): el proto-JSON
/// que llega de core-consultas, con datos personales (nombres, direcciones, contacto, histórico, terceros), se cifra antes de
/// guardarlo en <c>consultas.respuesta_fuente_cifrada</c> (<c>@pii:high</c>) y solo se descifra en memoria para el detalle de la
/// celda. Propósito propio, distinto del de los documentos de persona. Nunca se loguea.
/// </summary>
/// <example>
/// <code>
/// var cifrada = protector.Proteger(respuesta.GetRawText());
/// var texto = protector.Desproteger(cifrada); // lanza CryptographicException si no se puede descifrar
/// </code>
/// </example>
public interface IProtectorRespuestaFuente
{
    /// <summary>Cifra el texto de la respuesta.</summary>
    byte[] Proteger(string texto);

    /// <summary>Descifra lo que devolvió <see cref="Proteger"/>.</summary>
    string Desproteger(byte[] cifrado);
}
