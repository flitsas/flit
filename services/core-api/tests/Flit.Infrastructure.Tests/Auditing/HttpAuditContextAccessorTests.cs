using System.Net;
using Flit.Infrastructure.Auditing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Infrastructure.Tests.Auditing;

/// <summary>
/// RNF01 (ADR-0024): la IP auditada se resuelve con prioridad <c>X-Forwarded-For</c> (primer hop,
/// porque hay un gateway delante) sobre <c>Connection.RemoteIpAddress</c>. Sin petición HTTP
/// devuelve <c>null</c>.
/// </summary>
public sealed class HttpAuditContextAccessorTests
{
    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static HttpAuditContextAccessor Build(HttpContext? context) =>
        new(new StubHttpContextAccessor { HttpContext = context });

    [Fact]
    public void PrefersFirstHopOfForwardedFor_OverRemoteIp()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 10.0.0.1";
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        Build(http).ClientIp.Should().Be("203.0.113.7");
    }

    [Fact]
    public void FallsBackToRemoteIp_WhenNoForwardedForHeader()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.44");

        Build(http).ClientIp.Should().Be("192.0.2.44");
    }

    [Fact]
    public void ReturnsNull_WhenNoHttpContext()
    {
        Build(context: null).ClientIp.Should().BeNull();
    }
}
