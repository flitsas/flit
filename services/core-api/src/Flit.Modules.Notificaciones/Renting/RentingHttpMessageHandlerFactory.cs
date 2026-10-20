using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// HU #11359 AC3 — construye el <see cref="HttpMessageHandler"/> del canal Renting con el
/// certificado cliente adjunto (mTLS). La verificación del certificado del servidor queda con el
/// comportamiento POR DEFECTO de <see cref="SocketsHttpHandler"/> (que sí valida contra la cadena
/// de confianza del sistema operativo): esta clase NUNCA toca
/// <c>SslOptions.RemoteCertificateValidationCallback</c>, ni siquiera para diagnóstico — deshabilitar
/// esa validación, aunque sea "temporalmente", está prohibido por el AC3.
/// </summary>
public static class RentingHttpMessageHandlerFactory
{
    public static HttpMessageHandler Create(X509Certificate2 clientCertificate)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions =
            {
                ClientCertificates = new X509Certificate2Collection(clientCertificate),
            },
        };

        return handler;
    }
}
