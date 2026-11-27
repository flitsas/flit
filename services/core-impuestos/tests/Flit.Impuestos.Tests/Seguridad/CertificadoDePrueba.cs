using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Flit.Impuestos.Tests.Seguridad;

/// <summary>
/// Certificado autofirmado y efímero (RSA 2048) que protege las llaves de Data Protection en las pruebas (plan §4.2). Se crea
/// una vez por proceso en un .pfx temporal con una clave aleatoria y se borra al salir. Entra por configuración, como en los
/// ambientes (<c>Impuestos:DataProtection:CertificadoRuta</c> y <c>CertificadoClave</c>).
/// </summary>
/// <example>
/// <code>
/// settings[CertificadoDePrueba.ClaveRuta] = CertificadoDePrueba.Ruta;
/// settings[CertificadoDePrueba.ClaveClave] = CertificadoDePrueba.Clave;
/// </code>
/// </example>
public static class CertificadoDePrueba
{
    public const string ClaveRuta = "Impuestos:DataProtection:CertificadoRuta";
    public const string ClaveClave = "Impuestos:DataProtection:CertificadoClave";

    private static readonly Lazy<(string Ruta, string Clave)> Unico = new(Crear);

    public static string Ruta => Unico.Value.Ruta;

    public static string Clave => Unico.Value.Clave;

    private static (string Ruta, string Clave) Crear()
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=flit-impuestos-pruebas", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        pedido.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: false));
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var clave = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var ruta = Path.Combine(Path.GetTempPath(), $"flit-imp-dp-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(ruta, certificado.Export(X509ContentType.Pfx, clave));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(ruta);
        return (ruta, clave);
    }
}
