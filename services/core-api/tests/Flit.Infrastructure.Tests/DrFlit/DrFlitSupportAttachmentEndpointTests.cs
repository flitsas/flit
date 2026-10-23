using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12924 — <c>POST /api/v1/dr-flit/support-cases/attachments</c> invocando el delegate real con el
/// handler real y el store sustituido.
/// Uso de ejemplo:
/// <code>
/// var result = await DrFlitEndpoints.UploadAttachmentAsync(ctx, tenant, formFile, handler, ct);
/// await result.ExecuteAsync(ctx); // 201 { id, filename, sizeBytes }
/// </code>
/// </summary>
public sealed class DrFlitSupportAttachmentEndpointTests
{
    private static readonly Guid Tenant = Guid.Parse("0199a000-0000-7000-8000-0000000000aa");
    private static readonly Guid OtherTenant = Guid.Parse("0199a000-0000-7000-8000-0000000000bb");
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid SavedId = Guid.Parse("0199a000-0000-7000-8000-00000000f001");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];

    private readonly IDrFlitSupportAttachmentStore _store = Substitute.For<IDrFlitSupportAttachmentStore>();
    private readonly IDrFlitSupportCaseSettings _settings = Substitute.For<IDrFlitSupportCaseSettings>();

    public DrFlitSupportAttachmentEndpointTests()
    {
        _settings.MaxFileSizeBytes.Returns(20L * 1024 * 1024);
        _settings.AllowedMimeTypes.Returns(["image/png", "image/jpeg", "image/webp", "application/pdf", "text/plain"]);
        _settings.AttachmentTtl.Returns(TimeSpan.FromHours(24));
        _store.SaveAsync(default, default, default!, default!, default!, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(ci => new DrFlitStoredAttachment(SavedId, ci.ArgAt<string>(2), ci.ArgAt<Stream>(4).Length));
    }

    private UploadSupportAttachmentHandler Handler() => new(_store, _settings, TimeProvider.System);

    private static DefaultHttpContext Context()
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", User.ToString()), new Claim(AdminAuthorization.TenantIdClaimType, Tenant.ToString())], "TestAuth")),
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static FormFile File(string name, byte[] bytes, string contentType) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    private async Task<(int Status, JsonElement Body)> Post(IFormFile? file, Guid? header = null)
    {
        var ctx = Context();
        var result = await DrFlitEndpoints.UploadAttachmentAsync(ctx, header ?? Tenant, file, Handler(), TestContext.Current.CancellationToken);
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (ctx.Response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task AC1_ArchivoValido_201ConIdNombreYTamano()
    {
        var (status, body) = await Post(File("captura.png", Png, "image/png"));

        status.Should().Be(StatusCodes.Status201Created);
        body.GetProperty("id").GetGuid().Should().Be(SavedId);
        body.GetProperty("filename").GetString().Should().Be("captura.png");
        body.GetProperty("sizeBytes").GetInt64().Should().Be(Png.Length);
    }

    [Fact]
    public async Task AC1_ElAdjuntoQuedaDelTenantDelToken_NoDelHeader()
    {
        await Post(File("captura.png", Png, "image/png"), header: OtherTenant);

        await _store.Received(1).SaveAsync(
            Tenant, User, "captura.png", "image/png", Arg.Any<Stream>(), Arg.Any<DateTimeOffset>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC2_SinArchivo_400()
    {
        (await Post(null)).Status.Should().Be(StatusCodes.Status400BadRequest);
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC2_TipoNoPermitido_400()
    {
        var (status, body) = await Post(File("programa.exe", [0x4D, 0x5A], "application/octet-stream"));

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("detail").GetString().Should().Be("Tipo de archivo no permitido.");
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC2_ExcedeElTamanoConfigurado_400()
    {
        _settings.MaxFileSizeBytes.Returns(4);

        (await Post(File("captura.png", Png, "image/png"))).Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SinXTenantId_400()
    {
        var ctx = Context();
        var result = await DrFlitEndpoints.UploadAttachmentAsync(ctx, null, File("captura.png", Png, "image/png"), Handler(), TestContext.Current.CancellationToken);
        await result.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
