using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Flit.Diagnostico.Tests.Seguridad;

/// <summary>
/// Certificado efímero autofirmado (RSA 2048) para las llaves de Data Protection de las pruebas (HU #13751): se genera una
/// vez por proceso, se exporta a un .pfx temporal con una clave aleatoria y se borra al salir. Entra al servicio por
/// configuración, como en los ambientes (<c>Diagnostico:DataProtection:CertificadoRuta</c> y <c>CertificadoClave</c>).
/// </summary>
/// <example>
/// <code>
/// settings["Diagnostico:DataProtection:CertificadoRuta"] = CertificadoDePrueba.Ruta;
/// settings["Diagnostico:DataProtection:CertificadoClave"] = CertificadoDePrueba.Clave;
/// </code>
/// </example>
public static class CertificadoDePrueba
{
    public const string ClaveRuta = "Diagnostico:DataProtection:CertificadoRuta";
    public const string ClaveClave = "Diagnostico:DataProtection:CertificadoClave";

    private static readonly Lazy<(string Ruta, string Clave)> Generado = new(Generar);

    public static string Ruta => Generado.Value.Ruta;

    public static string Clave => Generado.Value.Clave;

    /// <summary>Un .pfx nuevo con otra clave, para probar lo que no es el certificado del servicio.</summary>
    public static (string Ruta, string Clave) Otro() => Generar();

    private static (string Ruta, string Clave) Generar()
    {
        using var rsa = RSA.Create(2048);
        var solicitud = new CertificateRequest("CN=flit-diagnostico-pruebas", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        solicitud.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: false));
        using var certificado = solicitud.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var clave = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var ruta = Path.Combine(Path.GetTempPath(), $"flit-dgn-dp-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(ruta, certificado.Export(X509ContentType.Pfx, clave));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(ruta);
        return (ruta, clave);
    }
}
