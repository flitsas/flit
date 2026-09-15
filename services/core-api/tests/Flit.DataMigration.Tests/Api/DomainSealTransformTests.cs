extern alias gw;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;
using DomainSealOptions = gw::Flit.Gateway.Configuration.DomainSealOptions;
using DomainSealTransform = gw::Flit.Gateway.Transforms.DomainSealTransform;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// HU #12417 AC1/AC2/AC5 (ADR-0060 D2) — <see cref="DomainSealTransform"/> sin levantar YARP: se
/// ejercita <see cref="DomainSealTransform.ResolveSealedHost"/> directamente (mismo cálculo que usa
/// el <c>RequestTransform</c> registrado vía <c>AddTransforms&lt;DomainSealTransform&gt;</c>).
/// Uso de ejemplo:
/// <code>
/// var host = DomainSealTransform.ResolveSealedHost(httpContext, new DomainSealOptions());
/// </code>
/// No existe <c>Flit.Gateway.Tests</c> (delta-hechos-post-adr.md hecho 4): vive junto a
/// <c>GatewayRutaMigracionTests</c>/<c>GatewayRutaPublicaTests</c>.
/// </summary>
public sealed class DomainSealTransformTests
{
    private static DefaultHttpContext BuildContext(
        string host,
        string? remoteIp = "203.0.113.10",
        string? incomingSeal = null,
        string? forwardedHost = null)
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

        return context;
    }

    [Fact]
    public void SinSelloEntrante_SellaConElHostReal()
    {
        var context = BuildContext("cliente.movilidadandina.com");

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions());

        result.Should().Be("cliente.movilidadandina.com");
    }

    [Fact]
    public void ClienteEnviaSelloPropio_SeDescartaYSeUsaElHostReal()
    {
        // AC1 — cualquier valor de X-Flit-Domain que traiga el cliente se descarta antes de sellar.
        var context = BuildContext("red-a.com", incomingSeal: "otro-valor-inventado.com");

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions());

        result.Should().Be("red-a.com");
    }

    [Fact]
    public void Suplantacion_ElSelloReflejaElHostRealNoElDeOtraRed()
    {
        // AC5 — prueba negativa: cliente envía X-Flit-Domain con el valor del dominio de OTRA red,
        // por Host: red-a.com. El sello final debe ser red-a.com, nunca el de la red suplantada.
        var context = BuildContext("red-a.com", incomingSeal: "red-b.com");

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions());

        result.Should().Be("red-a.com");
        result.Should().NotBe("red-b.com");
    }

    [Fact]
    public void NormalizaMayusculasYEspacios()
    {
        var context = BuildContext("  Cliente.Movilidadandina.COM  ".Trim());

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions());

        result.Should().Be("cliente.movilidadandina.com");
    }

    [Fact]
    public void RedInternaDeConfianzaConSelloEntrante_ConservaElSelloNormalizado()
    {
        // delta-hechos-post-adr.md hecho 7 — el frontend interno (red Docker) envía el sello
        // explícito; SOLO se acepta si la IP remota cae en DomainSeal:InternalAllowedNetworks.
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.18.0.0/16"] };
        var context = BuildContext(
            "gateway-interno.local",
            remoteIp: "172.18.0.42",
            incomingSeal: "Movilidadandina.COM");

        var result = DomainSealTransform.ResolveSealedHost(context, options);

        result.Should().Be("movilidadandina.com");
    }

    [Fact]
    public void IpFueraDeLaRedInternaConfigurada_IgnoraElSelloEntrante()
    {
        var options = new DomainSealOptions { InternalAllowedNetworks = ["172.18.0.0/16"] };
        var context = BuildContext(
            "cliente-publico.com",
            remoteIp: "198.51.100.7",
            incomingSeal: "otra-cosa.com");

        var result = DomainSealTransform.ResolveSealedHost(context, options);

        result.Should().Be("cliente-publico.com");
    }

    [Fact]
    public void SinRedesInternasConfiguradas_NuncaConfiaEnElSelloEntrante()
    {
        // Fail-closed: DomainSeal:InternalAllowedNetworks vacío (default) ⇒ SIEMPRE se sobrescribe,
        // incluso si por error la IP remota fuera la misma que se usaría en Docker.
        var context = BuildContext("cliente.com", remoteIp: "172.18.0.42", incomingSeal: "otra-red.com");

        var result = DomainSealTransform.ResolveSealedHost(context, new DomainSealOptions());

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
            forwardedHost: "suplantado.com");

        var result = DomainSealTransform.ResolveSealedHost(context, options);

        result.Should().Be("cliente.com");
        result.Should().NotBe("suplantado.com");
    }
}
