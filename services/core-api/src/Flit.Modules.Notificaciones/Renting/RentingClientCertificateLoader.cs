using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// HU #11359 AC3/AC4/AC5 — carga y valida el certificado cliente (PFX) del canal Renting.
/// Se invoca desde el <c>factory</c> del <see cref="RentingClientCertificateProvider"/> registrado
/// en <c>InfrastructureExtensions.AddRentingChannel</c>.
/// <para>
/// OJO — la falla NO es al arrancar, pese a lo que decía este comentario. Verificado en DEV el
/// 2026-08-11 con un certificado ausente: el servicio levantó sano y la excepción salió en el
/// PRIMER ENVÍO del canal, desde <c>DefaultHttpClientFactory.CreateHandlerEntry</c>. El proveedor
/// se registra dentro de <c>ConfigurePrimaryHttpMessageHandler</c>, y esa fábrica resuelve el
/// handler de forma perezosa la primera vez que se pide el <see cref="System.Net.Http.HttpClient"/>
/// con nombre, así que <c>ValidateOnBuild</c> nunca llega a tocarla. Consecuencia operativa: un
/// arranque limpio NO significa que el canal esté bien configurado.
/// </para>
/// <para>
/// Ningún mensaje de excepción de esta clase incluye la passphrase, la clave de API ni ningún otro
/// secreto — solo identifica la variable de configuración incompleta o la discrepancia detectada.
/// </para>
/// </summary>
public static class RentingClientCertificateLoader
{
    public static X509Certificate2 Load(RentingChannelOptions options, ILogger logger)
    {
        // AC4 — ruta inexistente. La ruta SÍ va en el mensaje: no es un secreto, y sin ella el
        // operador no puede comprobar nada — la ruta que ve en el host puede no ser la que la app
        // abre dentro del contenedor, y ese fue exactamente el caso que costó un despliegue.
        //
        // El caso del DIRECTORIO se separa a propósito: es lo que produce un bind mount de Docker
        // cuando el .pfx NO está en el host (Docker no falla, crea un directorio vacío y lo monta),
        // y `File.Exists` lo reporta igual que "aquí no hay nada". Sin separarlos, el operador ve
        // "no se encontró el archivo", hace `ls`, encuentra algo en esa ruta y se queda sin pista.
        if (Directory.Exists(options.PfxCertificatePath))
        {
            throw new InvalidOperationException(
                "Canal Renting habilitado: la ruta configurada por RENTING_API_PFX_CERTIFICATE_PATH "
                + $"('{options.PfxCertificatePath}') es un DIRECTORIO, no un archivo. Con Docker esto "
                + "ocurre cuando el .pfx no existía en el host al montar el volumen: Docker crea un "
                + "directorio vacío en su lugar. Borre el directorio, deje el archivo en esa ruta del "
                + "host y recree el contenedor.");
        }

        if (!File.Exists(options.PfxCertificatePath))
        {
            throw new InvalidOperationException(
                "Canal Renting habilitado: no se encontró el archivo de certificado en la ruta "
                + $"configurada por RENTING_API_PFX_CERTIFICATE_PATH ('{options.PfxCertificatePath}'). "
                + "Esa ruta es la que ve el PROCESO: dentro de un contenedor es la ruta del contenedor, "
                + "no la del host.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                options.PfxCertificatePath,
                options.Passphrase,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        }
        catch (CryptographicException)
        {
            // AC4 — passphrase que no abre el archivo (o PFX corrupto). Nunca se incluye la
            // passphrase ni el mensaje técnico crudo de CryptographicException en el texto.
            throw new InvalidOperationException(
                "Canal Renting habilitado: no fue posible abrir el certificado configurado en "
                + "RENTING_API_PFX_CERTIFICATE_PATH con la passphrase de RENTING_API_PASSPHRASE. "
                + "Verifique que la ruta apunte al archivo correcto y que la passphrase sea la vigente.");
        }

        // AC5 — mTLS y login comparten la misma identidad: el Subject configurado para el login DEBE
        // coincidir con el Subject del certificado cargado. Una discrepancia (p. ej. tras renovar el
        // certificado con otro CN sin actualizar RENTING_API_LOGIN_SUBJECT) falla el arranque en vez
        // de dejar un canal que solo falla en el primer login, en producción, sin causa aparente.
        if (!string.Equals(certificate.Subject, options.LoginSubject, StringComparison.Ordinal))
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "Canal Renting habilitado: el sujeto configurado en RENTING_API_LOGIN_SUBJECT no "
                + "coincide con el Subject del certificado cargado desde "
                + "RENTING_API_PFX_CERTIFICATE_PATH. Verifique que ambos correspondan a la misma "
                + "identidad (p. ej. tras una renovación del certificado con otro CN).");
        }

        // AC3 — la traza de arranque registra la fecha de expiración del certificado cargado.
        RentingChannelLog.ClientCertificateLoaded(logger, certificate.Subject, certificate.NotAfter);

        return certificate;
    }
}

/// <summary>Logging source-generated (CA1848) del canal Renting.</summary>
internal static partial class RentingChannelLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Canal Renting: certificado cliente cargado (Subject={CertificateSubject}). Vence el {NotAfter:yyyy-MM-dd}.")]
    public static partial void ClientCertificateLoaded(ILogger logger, string certificateSubject, DateTime notAfter);
}

/// <summary>
/// Contenedor singleton del certificado cliente cargado y validado (ver
/// <see cref="RentingClientCertificateLoader"/>). Lo consumirá el handler HTTP mTLS del canal
/// Renting (registrado en <c>InfrastructureExtensions.AddRentingChannel</c>) y, más adelante, el
/// login de la HU #11360 (que reutiliza <see cref="Certificate"/> para comparar identidad).
/// </summary>
public sealed class RentingClientCertificateProvider(X509Certificate2 certificate)
{
    public X509Certificate2 Certificate { get; } = certificate;
}
