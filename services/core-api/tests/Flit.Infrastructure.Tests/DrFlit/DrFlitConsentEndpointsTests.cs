using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.DrFlit.Application.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12931 — consentimiento obligatorio del tratamiento de datos: endpoints GET/POST /dr-flit/consent y
/// el filtro que responde 428 en el chat y en soporte sin la versión vigente aceptada.
/// Uso de ejemplo:
/// <code>
/// var result = await DrFlitEndpoints.AcceptConsentAsync(ctx, tenant, new() { Version = "2026-09-25" }, store, settings, audit, ct);
/// </code>
/// </summary>
public sealed class DrFlitConsentEndpointsTests
{
    private const string Version = "2026-09-25";
    private static readonly Guid Tenant = Guid.Parse("0199a000-0000-7000-8000-0000000000aa");
    private static readonly Guid OtherTenant = Guid.Parse("0199a000-0000-7000-8000-0000000000bb");
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-000000000001");

    private readonly IDrFlitConsentStore _store = Substitute.For<IDrFlitConsentStore>();
    private readonly IDrFlitConsentSettings _settings = Substitute.For<IDrFlitConsentSettings>();
    private readonly IAuditContextAccessor _audit = Substitute.For<IAuditContextAccessor>();

    public DrFlitConsentEndpointsTests()
    {
        _settings.CurrentVersion.Returns(Version);
        _audit.ClientIp.Returns("190.0.0.1");
    }

    private static DefaultHttpContext Context(bool withUser = true)
    {
        var claims = withUser
            ? new List<Claim> { new("sub", User.ToString()), new(AdminAuthorization.TenantIdClaimType, Tenant.ToString()) }
            : [];
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };
        ctx.Request.Headers.UserAgent = "Mozilla/5.0 (prueba)";
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<(int Status, JsonElement Body)> Execute(IResult result, DefaultHttpContext ctx)
    {
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (ctx.Response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    // ── GET /consent ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_DevuelveVersionVigenteYSiYaAcepto(bool accepted)
    {
        _store.HasAcceptedAsync(User, Version, TestContext.Current.CancellationToken).Returns(accepted);
        var ctx = Context();

        var (status, body) = await Execute(
            await DrFlitEndpoints.GetConsentAsync(ctx, _store, _settings, TestContext.Current.CancellationToken), ctx);

        status.Should().Be(StatusCodes.Status200OK);
        body.GetProperty("version").GetString().Should().Be(Version);
        body.GetProperty("accepted").GetBoolean().Should().Be(accepted);
    }

    // ── POST /consent (AC1 — evidencia) ─────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Aceptar_RegistraEvidenciaConElTenantDelToken()
    {
        DrFlitConsentAcceptance? recorded = null;
        await _store.RecordAsync(Arg.Do<DrFlitConsentAcceptance>(a => recorded = a), TestContext.Current.CancellationToken);
        var ctx = Context();

        var (status, body) = await Execute(
            await DrFlitEndpoints.AcceptConsentAsync(ctx, OtherTenant, new DrFlitConsentAcceptBody { Version = Version }, _store, _settings, _audit, TestContext.Current.CancellationToken),
            ctx);

        status.Should().Be(StatusCodes.Status201Created);
        body.GetProperty("accepted").GetBoolean().Should().BeTrue();
        recorded.Should().Be(new DrFlitConsentAcceptance(Tenant, User, Version, "190.0.0.1", "Mozilla/5.0 (prueba)"));
    }

    [Fact]
    public async Task AC1_VersionDistintaALaVigente_409SinRegistrar()
    {
        var ctx = Context();

        var (status, body) = await Execute(
            await DrFlitEndpoints.AcceptConsentAsync(ctx, Tenant, new DrFlitConsentAcceptBody { Version = "2025-01-01" }, _store, _settings, _audit, TestContext.Current.CancellationToken),
            ctx);

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("consent_version_mismatch");
        body.GetProperty("version").GetString().Should().Be(Version);
        await _store.DidNotReceiveWithAnyArgs().RecordAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Aceptar_SinVersion_400(string? version)
    {
        var ctx = Context();
        var body = version is null ? null : new DrFlitConsentAcceptBody { Version = version };

        var (status, _) = await Execute(
            await DrFlitEndpoints.AcceptConsentAsync(ctx, Tenant, body, _store, _settings, _audit, TestContext.Current.CancellationToken), ctx);

        status.Should().Be(StatusCodes.Status400BadRequest);
    }

    // ── Filtro 428 (AC3) ────────────────────────────────────────────────────────────────

    private async Task<(object? Result, bool NextCalled)> RunFilter(DefaultHttpContext ctx)
    {
        var nextCalled = false;
        var filter = new DrFlitConsentRequiredFilter(_store, _settings);
        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(ctx),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });
        return (result, nextCalled);
    }

    [Fact]
    public async Task AC3_SinAceptacion_428ConsentRequiredYNoLlegaAlEndpoint()
    {
        _store.HasAcceptedAsync(User, Version, Arg.Any<CancellationToken>()).Returns(false);
        var ctx = Context();

        var (result, nextCalled) = await RunFilter(ctx);

        nextCalled.Should().BeFalse("sin consentimiento no se llama al LLM ni a Azure DevOps");
        var (status, body) = await Execute((IResult)result!, ctx);
        status.Should().Be(StatusCodes.Status428PreconditionRequired);
        body.GetProperty("code").GetString().Should().Be("consent_required");
        body.GetProperty("version").GetString().Should().Be(Version);
    }

    [Fact]
    public async Task AC3_ConAceptacionDeLaVersionVigente_Continua()
    {
        _store.HasAcceptedAsync(User, Version, Arg.Any<CancellationToken>()).Returns(true);

        var (_, nextCalled) = await RunFilter(Context());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SinUsuario_DejaQueDecidaElEndpoint()
    {
        var (_, nextCalled) = await RunFilter(Context(withUser: false));

        nextCalled.Should().BeTrue();
        await _store.DidNotReceiveWithAnyArgs().HasAcceptedAsync(default, default!, TestContext.Current.CancellationToken);
    }
}
