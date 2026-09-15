extern alias gw;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;
using DomainSealOptions = gw::Flit.Gateway.Configuration.DomainSealOptions;
using InternalApiOptions = gw::Flit.Gateway.Configuration.InternalApiOptions;
using DomainSealTransform = gw::Flit.Gateway.Transforms.DomainSealTransform;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// HU #12417 AC1/AC2/AC5 (ADR-0060 D2) + follow-up de seguridad (delta-hechos-post-adr.md hecho 24)
/// — <see cref="DomainSealTransform"/> sin levantar YARP: se ejercita
/// <see cref="DomainSealTransform.ResolveSealedHost"/> directamente (mismo cálculo que usa el
/// <c>RequestTransform</c> registrado vía <c>AddTransforms&lt;DomainSealTransform&gt;</c>).
/// Uso de ejemplo:
/// <code>
/// var host = DomainSealTransform.ResolveSealedHost(httpContext, new DomainSealOptions(), new InternalApiOptions());
/// </code>
/// No existe <c>Flit.Gateway.Tests</c> (delta-hechos-post-adr.md hecho 4): vive junto a
/// <c>GatewayRutaMigracionTests</c>/<c>GatewayRutaPublicaTests</c>.
///
/// Follow-up de seguridad (hecho 24): con <c>docker-proxy</c>/DNAT, una conexión externa puede
/// llegar al contenedor con IP del gateway del bridge (dentro del CIDR interno configurado). El
/// CIDR solo() ya NO basta — se exige también <c>X-Internal-Key</c> == <c>Internal:ApiKey</c>.
/// </summary>
public sealed class DomainSealTransformTests
{
    private const string InternalKeyHeader = "X-Internal-Key";
    private const string ValidApiKey = "clave-interna-de-prueba";

    private static DefaultHttpContext BuildContext(
        string host,
        string? remoteIp = "203.0.113.10",
        string? incomingSeal = null,
        string? forwardedHost = null,
        string? internalKeyHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        if (remoteIp is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        }

        if (incomingSeal is not null)
        {
            context.Request.Headers[DomainSealTransform.HeaderName] = incomingSeal;
        }

        if (forwardedHost is not null)
        {
            context.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        }

        if (internalKeyHeader is not null)
        {
            context.Request.Headers[InternalKeyHeader] = internalKeyHeader;
        }

        return context;
    }

    private static InternalApiOptions WithApiKey(string apiKey) => new() { ApiKey = apiKey };

    [Fact]
    public void SinSelloEntrante_SellaConElHostReal()
    {
        var context = BuildContext("cliente.movilidadandina.com");

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions(), new InternalApiOptions());

        result.Should().Be("cliente.movilidadandina.com");
    }

    [Fact]
    public void ClienteEnviaSelloPropio_SeDescartaYSeUsaElHostReal()
    {
        // AC1 — cualquier valor de X-Flit-Domain que traiga el cliente se descarta antes de sellar.
        var context = BuildContext("red-a.com", incomingSeal: "otro-valor-inventado.com");

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions(), new InternalApiOptions());

        result.Should().Be("red-a.com");
    }

    [Fact]
    public void Suplantacion_ElSelloReflejaElHostRealNoElDeOtraRed()
    {
        // AC5 — prueba negativa: cliente envía X-Flit-Domain con el valor del dominio de OTRA red,
        // por Host: red-a.com. El sello final debe ser red-a.com, nunca el de la red suplantada.
        var context = BuildContext("red-a.com", incomingSeal: "red-b.com");

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions(), new InternalApiOptions());

        result.Should().Be("red-a.com");
        result.Should().NotBe("red-b.com");
    }

    [Fact]
    public void NormalizaMayusculasYEspacios()
    {
        var context = BuildContext("  Cliente.Movilidadandina.COM  ".Trim());

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions(), new InternalApiOptions());

        result.Should().Be("cliente.movilidadandina.com");
    }

    [Fact]
    public void RedInternaDeConfianzaConSelloEntranteYClaveValida_ConservaElSelloNormalizado()
    {
        // delta-hechos-post-adr.md hecho 7/24 — el frontend interno (red Docker) envía el sello
        // explícito; SOLO se acepta si la IP remota cae en DomainSeal:InternalAllowedNetworks Y
        // además trae X-Internal-Key correcta.
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.18.0.0/16"] };
        var context = BuildContext(
            "gateway-interno.local",
            remoteIp: "172.18.0.42",
            incomingSeal: "Movilidadandina.COM",
            internalKeyHeader: ValidApiKey);

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("movilidadandina.com");
    }

    [Fact]
    public void IpFueraDeLaRedInternaConfigurada_IgnoraElSelloEntrante()
    {
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.18.0.0/16"] };
        var context = BuildContext(
            "cliente-publico.com",
            remoteIp: "198.51.100.7",
            incomingSeal: "otra-cosa.com",
            internalKeyHeader: ValidApiKey);

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("cliente-publico.com");
    }

    [Fact]
    public void SinRedesInternasConfiguradas_NuncaConfiaEnElSelloEntrante()
    {
        // Fail-closed: DomainSeal:InternalAllowedNetworks vacío (default) ⇒ SIEMPRE se sobrescribe,
        // incluso si por error la IP remota fuera la misma que se usaría en Docker.
        var context = BuildContext(
            "cliente.com",
            remoteIp: "172.18.0.42",
            incomingSeal: "otra-red.com",
            internalKeyHeader: ValidApiKey);

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions(), WithApiKey(ValidApiKey));

        result.Should().Be("cliente.com");
    }

    [Fact]
    public void IgnoraXForwardedHostAunDesdeUnaIpDeConfianza()
    {
        // Decisión de diseño (HU #12417): el nginx externo del borde preserva Host end-to-end
        // (#12421), así que el Gateway NUNCA confía en X-Forwarded-Host — ni siquiera desde una red
        // marcada como interna — solo evalúa la excepción documentada para X-Flit-Domain.
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.18.0.0/16"] };
        var context = BuildContext(
            "cliente.com",
            remoteIp: "172.18.0.42",
            forwardedHost: "suplantado.com",
            internalKeyHeader: ValidApiKey);

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("cliente.com");
        result.Should().NotBe("suplantado.com");
    }

    // --- Follow-up de seguridad #12417 (hecho 24): CIDR y clave interna, ambos obligatorios ---

    [Fact]
    public void IpInternaSinClave_DescartaElSelloEntrante()
    {
        // Sin X-Internal-Key: aunque la IP remota caiga en el CIDR interno (posible DNAT/docker-proxy
        // desde un cliente público), el sello entrante se descarta.
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.28.40.0/24"] };
        var context = BuildContext(
            "cliente-publico.com",
            remoteIp: "172.28.40.1",
            incomingSeal: "red-suplantada.com");

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("cliente-publico.com");
        result.Should().NotBe("red-suplantada.com");
    }

    [Fact]
    public void IpInternaConClaveIncorrecta_DescartaElSelloEntrante()
    {
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.28.40.0/24"] };
        var context = BuildContext(
            "cliente-publico.com",
            remoteIp: "172.28.40.1",
            incomingSeal: "red-suplantada.com",
            internalKeyHeader: "clave-incorrecta");

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("cliente-publico.com");
    }

    [Fact]
    public void IpInternaConClaveDeLongitudDistinta_DescartaElSelloEntrante()
    {
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.28.40.0/24"] };
        var context = BuildContext(
            "cliente-publico.com",
            remoteIp: "172.28.40.1",
            incomingSeal: "red-suplantada.com",
            internalKeyHeader: "corta");

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("cliente-publico.com");
    }

    [Fact]
    public void IpInternaConClaveCorrecta_ConservaElSelloEntrante()
    {
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.28.40.0/24"] };
        var context = BuildContext(
            "gateway-interno.local",
            remoteIp: "172.28.40.5",
            incomingSeal: "frontend-interno.com",
            internalKeyHeader: ValidApiKey);

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("frontend-interno.com");
    }

    [Fact]
    public void IpExternaConClaveCorrecta_DescartaElSelloEntrante()
    {
        // La clave sola no basta: si la IP remota no cae en el CIDR interno, se descarta igual.
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.28.40.0/24"] };
        var context = BuildContext(
            "cliente-publico.com",
            remoteIp: "198.51.100.7",
            incomingSeal: "otra-red.com",
            internalKeyHeader: ValidApiKey);

        var result = DomainSealTransform.ResolveSealedHost(context, options, WithApiKey(ValidApiKey));

        result.Should().Be("cliente-publico.com");
    }

    [Fact]
    public void ClaveConfiguradaVacia_NuncaConfiaEnElSelloEntranteAunqueLaCabeceraVenga()
    {
        // Internal:ApiKey vacío (default) ⇒ fail-closed: nunca se confía, aunque el cliente mande
        // una cabecera X-Internal-Key (con cualquier valor, incluido vacío) y la IP sea interna.
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.28.40.0/24"] };
        var context = BuildContext(
            "cliente-publico.com",
            remoteIp: "172.28.40.5",
            incomingSeal: "frontend-interno.com",
            internalKeyHeader: string.Empty);

        var result = DomainSealTransform.ResolveSealedHost(context, options, new InternalApiOptions());

        result.Should().Be("cliente-publico.com");
    }
}
