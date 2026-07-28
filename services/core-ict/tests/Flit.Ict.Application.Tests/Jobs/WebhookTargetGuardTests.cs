using Flit.Ict.Infrastructure.Jobs;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

/// <summary>
/// Guard anti-SSRF del target_url de los webhooks. Con IPs LITERALES no hay resolución DNS, así que los
/// casos son deterministas (no dependen del entorno).
/// </summary>
public sealed class WebhookTargetGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://8.8.8.8/webhook")]
    [InlineData("https://1.1.1.1/callback")]
    [InlineData("https://93.184.216.34/hook")] // rango público
    public async Task Destino_publico_es_permitido(string url)
    {
        (await WebhookTargetGuard.IsPublicHttpTargetAsync(url, Ct)).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://127.0.0.1/x")]          // loopback
    [InlineData("http://10.1.2.3/x")]           // 10/8
    [InlineData("http://172.16.5.9/x")]         // 172.16/12
    [InlineData("http://172.31.255.1/x")]       // 172.16/12 (borde alto)
    [InlineData("http://192.168.0.10/x")]       // 192.168/16
    [InlineData("http://169.254.169.254/latest/meta-data")] // IP de metadata de la nube
    [InlineData("http://100.64.0.1/x")]         // 100.64/10 CGNAT
    [InlineData("http://[::1]/x")]              // loopback IPv6
    public async Task Destino_interno_o_privado_se_bloquea(string url)
    {
        (await WebhookTargetGuard.IsPublicHttpTargetAsync(url, Ct)).Should().BeFalse();
    }

    [Theory]
    [InlineData("ftp://8.8.8.8/x")]     // esquema no http(s)
    [InlineData("file:///etc/passwd")]  // esquema no http(s)
    [InlineData("no-es-una-url")]       // no parseable
    [InlineData("")]                    // vacío
    [InlineData("/relativa")]           // no absoluta
    public async Task Esquema_invalido_o_url_mala_se_bloquea(string url)
    {
        (await WebhookTargetGuard.IsPublicHttpTargetAsync(url, Ct)).Should().BeFalse();
    }
}
