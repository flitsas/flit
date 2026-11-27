using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Flit.Impuestos.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Data Protection propio del servicio (plan §4.2, DA-2=b): el anillo de llaves vive en <c>impuestos.data_protection_keys</c>,
/// cifrado con un certificado que entra por configuración (<see cref="ClaveCertificadoRuta"/>, ruta de un .pfx montado como
/// secreto, y <see cref="ClaveCertificadoClave"/>), fuera de la base y del repo. Fail-closed: sin certificado, o con uno que no se
/// puede leer o sin llave privada RSA, el servicio no arranca. No hay excepción por ambiente ni modo de llaves sin proteger.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddProteccionDatos(builder.Configuration);
/// var cifrada = proteccion.ProtegerCredencial(empresa, CodigosCredencial.ApiKey, apiKey);
/// </code>
/// </example>
internal static class ProteccionDatos
{
    /// <summary>Nombre de aplicación estable: aísla el anillo de este servicio.</summary>
    public const string NombreAplicacion = "flit-core-impuestos";

    public const string ClaveCertificadoRuta = "Impuestos:DataProtection:CertificadoRuta";
    public const string ClaveCertificadoClave = "Impuestos:DataProtection:CertificadoClave";

    public static IServiceCollection AddProteccionDatos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var certificado = CargarCertificado(configuration);
        services.AddDataProtection()
            .SetApplicationName(NombreAplicacion)
            .PersistKeysToDbContext<ImpuestosDb>()
            .ProtectKeysWithCertificate(certificado);
        services.TryAddSingleton<IProteccionImpuestos, ProteccionImpuestos>();
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
        $"core-{ServicioSettings.Codigo} no puede arrancar: las llaves de Data Protection (credenciales y PII cifradas) exigen un certificado; " +
        $"{motivo}. Configure {Variable(ClaveCertificadoRuta)} (ruta del .pfx montado como secreto; en el .env IMPUESTOS_SVC_DP_CERTIFICADO_RUTA) " +
        $"y {Variable(ClaveCertificadoClave)} (IMPUESTOS_SVC_DP_CERTIFICADO_CLAVE).", causa);

    private static string Variable(string clave) => clave.Replace(":", "__", StringComparison.Ordinal);
}

/// <summary>Códigos de credencial de una compañía (un propósito por compañía y por código, DA-2=b).</summary>
public static class CodigosCredencial
{
    public const string ApiKey = "api_key";
    public const string WebhookSecret = "webhook_secret";
}

/// <summary>
/// Cifrado de lo que Impuestos guarda protegido (plan §4.2): credenciales por compañía y código
/// (<c>impuestos.credenciales.v1</c>, tenant, código), PII del propietario (<c>impuestos.pii.v1</c>) y cuerpo del resultado
/// (<c>impuestos.resultado.v1</c>). Lo descifrado solo vive en memoria y nunca se loguea.
/// </summary>
public interface IProteccionImpuestos
{
    byte[] ProtegerCredencial(Guid empresa, string codigo, string valor);

    string DesprotegerCredencial(Guid empresa, string codigo, byte[] cifrado);

    byte[] ProtegerPii(string valor);

    string DesprotegerPii(byte[] cifrado);

    byte[] ProtegerResultado(string valor);

    string DesprotegerResultado(byte[] cifrado);
}

/// <summary><see cref="IProteccionImpuestos"/> sobre la Data Protection API del servicio.</summary>
internal sealed class ProteccionImpuestos(IDataProtectionProvider provider) : IProteccionImpuestos
{
    public const string PropositoCredenciales = "impuestos.credenciales.v1";
    public const string PropositoPii = "impuestos.pii.v1";
    public const string PropositoResultado = "impuestos.resultado.v1";

    private readonly IDataProtector _pii = provider.CreateProtector(PropositoPii);
    private readonly IDataProtector _resultado = provider.CreateProtector(PropositoResultado);

    public byte[] ProtegerCredencial(Guid empresa, string codigo, string valor) => Proteger(Credencial(empresa, codigo), valor);

    public string DesprotegerCredencial(Guid empresa, string codigo, byte[] cifrado) => Desproteger(Credencial(empresa, codigo), cifrado);

    public byte[] ProtegerPii(string valor) => Proteger(_pii, valor);

    public string DesprotegerPii(byte[] cifrado) => Desproteger(_pii, cifrado);

    public byte[] ProtegerResultado(string valor) => Proteger(_resultado, valor);

    public string DesprotegerResultado(byte[] cifrado) => Desproteger(_resultado, cifrado);

    private IDataProtector Credencial(Guid empresa, string codigo)
    {
        if (empresa == Guid.Empty)
            throw new ArgumentException("La credencial exige la empresa.", nameof(empresa));
        if (codigo is not (CodigosCredencial.ApiKey or CodigosCredencial.WebhookSecret))
            throw new ArgumentException("Código de credencial desconocido.", nameof(codigo));
        return provider.CreateProtector(PropositoCredenciales, empresa.ToString("D"), codigo);
    }

    private static byte[] Proteger(IDataProtector protector, string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return protector.Protect(Encoding.UTF8.GetBytes(valor));
    }

    private static string Desproteger(IDataProtector protector, byte[] cifrado)
    {
        ArgumentNullException.ThrowIfNull(cifrado);
        return Encoding.UTF8.GetString(protector.Unprotect(cifrado));
    }
}
