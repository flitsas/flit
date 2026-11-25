using System.Security.Cryptography;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// ADR-0092 (diseño impuesto-real §1.3) — la API key del impuesto se cifra con Data Protection, propósito
/// <c>Flit.Platform.TenantProductCredential.v1</c> y subpropósitos empresa y código: ida y vuelta con los mismos datos;
/// un cifrado copiado a otra empresa o a otro código no se descifra.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>var cifrado = new DataProtectionTenantProductSecretProtector(provider).Protect(tenant, "impuesto_api_key", llave)</c>.
/// </remarks>
public sealed class DataProtectionTenantProductSecretProtectorTests
{
    private const string Llave = "llave-impuesto-CENTINELA-0123456789";
    private static readonly Guid Empresa = Guid.NewGuid();

    private readonly IDataProtectionProvider _provider = new EphemeralDataProtectionProvider();

    [Fact]
    public void IdaYVuelta_ConLaMismaEmpresaYCodigo()
    {
        var protector = new DataProtectionTenantProductSecretProtector(_provider);

        var cifrado = protector.Protect(Empresa, "impuesto_api_key", Llave);

        cifrado.Should().NotContain(Llave);
        cifrado.Length.Should().BeInRange(16, 4000);
        protector.Unprotect(Empresa, "impuesto_api_key", cifrado).Should().Be(Llave);
    }

    [Fact]
    public void LaLlaveMasLarga_CabeEnLaColumna()
    {
        var protector = new DataProtectionTenantProductSecretProtector(_provider);

        protector.Protect(Empresa, "impuesto_api_key", new string('k', 256)).Length.Should().BeInRange(16, 4000);
    }

    [Fact]
    public void CifradoDeOtraEmpresa_NoSeDescifra()
    {
        var protector = new DataProtectionTenantProductSecretProtector(_provider);
        var cifrado = protector.Protect(Empresa, "impuesto_api_key", Llave);

        var act = () => protector.Unprotect(Guid.NewGuid(), "impuesto_api_key", cifrado);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void CifradoDeOtroCodigo_NoSeDescifra()
    {
        var protector = new DataProtectionTenantProductSecretProtector(_provider);
        var cifrado = protector.Protect(Empresa, "impuesto_api_key", Llave);

        var act = () => protector.Unprotect(Empresa, "otra_llave", cifrado);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void ElPropositoEsElDelContrato()
    {
        var protector = new DataProtectionTenantProductSecretProtector(_provider);
        var cifrado = protector.Protect(Empresa, "impuesto_api_key", Llave);

        var mismoProposito = _provider.CreateProtector("Flit.Platform.TenantProductCredential.v1", Empresa.ToString("N"), "impuesto_api_key");

        mismoProposito.Unprotect(cifrado).Should().Be(Llave);
        DataProtectionTenantProductSecretProtector.Purpose.Should().Be("Flit.Platform.TenantProductCredential.v1");
    }
}
