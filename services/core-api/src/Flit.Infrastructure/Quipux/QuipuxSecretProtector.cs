using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// Cifra/descifra los secretos de <c>admin.quipux_settings</c> (<c>password_enc</c> y
/// <c>aws_secret_access_key_enc</c>). En BD NUNCA se guardan en claro: el repositorio de settings cifra
/// al escribir y descifra al leer, y <see cref="Flit.Modules.Quipux.Domain.Configuracion.QuipuxSettings"/>
/// solo expone el valor descifrado en memoria.
/// <para>
/// Vive en Infrastructure (y no en Domain/Application) porque es un detalle de persistencia: el Domain
/// declara que los secretos "vienen descifrados" y no le importa con qué.
/// </para>
/// </summary>
internal interface IQuipuxSecretProtector
{
    /// <summary>Cifra un secreto en claro para persistirlo (<c>password_enc</c> / <c>aws_secret_access_key_enc</c>).</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Descifra un secreto persistido. Lanza <see cref="CryptographicException"/> si el keyring no puede
    /// descifrarlo — ver las notas de <see cref="DataProtectionQuipuxSecretProtector.Unprotect"/> sobre
    /// cómo debe degradar el llamador.
    /// </summary>
    string Unprotect(string ciphertext);
}

/// <summary>
/// <see cref="IQuipuxSecretProtector"/> sobre la Data Protection API, con un propósito PROPIO y distinto
/// del webhook de Kyverum (<c>DataProtectionWebhookSecretProtector</c>): purposes distintos aíslan los
/// secretos entre sí, de modo que un ciphertext de un contexto no se puede descifrar en el otro aunque
/// compartan keyring.
/// <para>
/// <b>Por qué esto funciona tras un reinicio:</b> <c>InfrastructureExtensions</c> persiste el keyring en
/// Postgres (<c>PersistKeysToDbContext&lt;FlitDbContext&gt;</c>) y fija un <c>ApplicationName</c> estable
/// (<c>flit-core-api</c>). Sin eso las llaves viven en el filesystem efímero del contenedor: cada
/// restart o réplica generaría un keyring nuevo y los secretos ya cifrados quedarían indescifrables
/// (es exactamente el fallo que se corrigió para el webhook de Kyverum).
/// </para>
/// </summary>
internal sealed class DataProtectionQuipuxSecretProtector(
    IDataProtectionProvider provider,
    ILogger<DataProtectionQuipuxSecretProtector> logger) : IQuipuxSecretProtector
{
    /// <summary>
    /// Propósito dedicado a los secretos de configuración de Quipux. NO reutilizar el del webhook
    /// (<c>Flit.Kyverum.WebhookSecret.v1</c>). Cambiar este string invalida todo lo ya cifrado.
    /// </summary>
    private const string Purpose = "Flit.Quipux.Settings.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    /// <summary>
    /// Descifra el secreto persistido.
    /// <para>
    /// <b>Keyring rotado/perdido:</b> si Data Protection no puede descifrar (llave revocada, purpose o
    /// ApplicationName distinto, fila cifrada con un keyring anterior) lanza
    /// <see cref="CryptographicException"/>. Aquí se registra el diagnóstico —sin el ciphertext ni el
    /// secreto— y se relanza tal cual, deliberadamente: el protector no puede saber qué es "degradar"
    /// en cada contexto, así que la decisión es del llamador, igual que en el precedente
    /// <c>KyverumWebhookHandler</c> (que ante un secreto indescifrable NO devuelve 500, sino que
    /// reconcilia consultando al proveedor).
    /// </para>
    /// <para>
    /// <b>Contrato para el repositorio de settings:</b> capturar <see cref="CryptographicException"/> y
    /// dejar el secreto vacío en vez de propagar. Con <c>Password</c> o <c>AwsSecretAccessKey</c> vacíos,
    /// <c>QuipuxSettings.EstaCompleta()</c> devuelve false y los workers quedan inertes (no-op observable)
    /// en lugar de tumbar el ciclo o radicar con credenciales rotas. La cura es re-guardar los secretos
    /// desde el admin, que los vuelve a cifrar con el keyring vigente.
    /// </para>
    /// </summary>
    public string Unprotect(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException ex)
        {
            // Solo el tipo de excepción: ni el ciphertext ni el secreto pueden llegar al log.
            QuipuxSecretProtectorLog.DescifradoFallido(logger, ex.GetType().Name);
            throw;
        }
    }
}

/// <summary>Logs del protector (source-generated, CA1848). Nunca incluyen secretos ni ciphertext.</summary>
internal static partial class QuipuxSecretProtectorLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux: no se pudo descifrar un secreto de admin.quipux_settings ({ErrorType}). "
            + "Keyring de Data Protection rotado o perdido: re-guarde la configuración desde el admin "
            + "para volver a cifrar los secretos con el keyring vigente.")]
    public static partial void DescifradoFallido(ILogger logger, string errorType);
}
