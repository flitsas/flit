using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Personas;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// ASP.NET Data Protection del servicio (HU #13751, adelanta lo que pedía #13730): el anillo de llaves vive en
/// <c>diagnostico.data_protection_keys</c> (HU #13735) pero cifrado con un certificado que entra por configuración
/// (<see cref="ClaveCertificadoRuta"/>, ruta de un .pfx montado como secreto, y <see cref="ClaveCertificadoClave"/>): la
/// llave que protege los documentos queda fuera de la base (decisión del usuario 2026-10-21). Fail-closed: sin certificado,
/// o con uno que no se puede leer, sin llave privada o sin RSA, el servicio no arranca. No hay excepción por ambiente (los
/// tres corren como Development) ni modo de llaves sin proteger.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddProteccionDocumentos(builder.Configuration);
/// var cifrado = protector.Proteger("1020304050"); // IProtectorDocumentoPersona
/// </code>
/// </example>
internal static class ProteccionDocumentos
{
    /// <summary>Nombre de aplicación estable: aísla el anillo de este servicio y sobrevive a cambios de ruta o de versión.</summary>
    public const string NombreAplicacion = "flit-core-diagnostico";

    public const string ClaveCertificadoRuta = "Diagnostico:DataProtection:CertificadoRuta";
    public const string ClaveCertificadoClave = "Diagnostico:DataProtection:CertificadoClave";

    public static IServiceCollection AddProteccionDocumentos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var certificado = CargarCertificado(configuration);
        services.AddDataProtection()
            .SetApplicationName(NombreAplicacion)
            .PersistKeysToDbContext<DiagnosticoDb>()
            .ProtectKeysWithCertificate(certificado);
        services.TryAddSingleton<IProtectorDocumentoPersona, ProtectorDocumentoPersona>();
        return services;
    }

    /// <summary>Carga el .pfx de la configuración; cualquier problema detiene el arranque con un mensaje que no revela la clave.</summary>
    internal static X509Certificate2 CargarCertificado(IConfiguration configuration)
    {
        var ruta = configuration[ClaveCertificadoRuta];
        var clave = configuration[ClaveCertificadoClave];
        if (string.IsNullOrWhiteSpace(ruta) || string.IsNullOrWhiteSpace(clave))
            throw NoArranca("falta la ruta del certificado o su clave");

        X509Certificate2 certificado;
        try
        {
            certificado = X509CertificateLoader.LoadPkcs12FromFile(ruta, clave, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw NoArranca($"no se pudo leer el .pfx ({ex.GetType().Name}: revise la ruta del secreto montado y la clave)", ex);
        }

        using var rsa = certificado.GetRSAPrivateKey();
        if (rsa is null)
        {
            certificado.Dispose();
            throw NoArranca("el certificado no trae una llave privada RSA");
        }

        return certificado;
    }

    private static InvalidOperationException NoArranca(string motivo, Exception? causa = null) => new(
        $"core-{ServicioSettings.Codigo} no puede arrancar: las llaves de Data Protection (documentos de persona cifrados) exigen un certificado; " +
        $"{motivo}. Configure {Variable(ClaveCertificadoRuta)} (ruta del .pfx montado como secreto) y {Variable(ClaveCertificadoClave)}. " +
        "Ver services/core-diagnostico/README.md.", causa);

    private static string Variable(string clave) => clave.Replace(":", "__", StringComparison.Ordinal);
}

/// <summary>
/// <see cref="IProtectorDocumentoPersona"/> sobre la Data Protection API (patrón <c>DataProtectionWebhookSecretProtector</c>
/// de core-api) con el propósito <see cref="Proposito"/>: el número del documento se cifra antes de guardarlo en
/// <c>*_doc_cifrado</c> y solo se descifra en memoria. Nunca se loguea.
/// </summary>
internal sealed class ProtectorDocumentoPersona : IProtectorDocumentoPersona
{
    public const string Proposito = "Flit.Diagnostico.DocumentoPersona.v1";

    private readonly IDataProtector _protector;

    public ProtectorDocumentoPersona(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Proposito);
    }

    public byte[] Proteger(string numero)
    {
        ArgumentNullException.ThrowIfNull(numero);
        return _protector.Protect(Encoding.UTF8.GetBytes(numero));
    }

    public string Desproteger(byte[] cifrado)
    {
        ArgumentNullException.ThrowIfNull(cifrado);
        return Encoding.UTF8.GetString(_protector.Unprotect(cifrado));
    }
}
