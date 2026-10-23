using System.Security.Claims;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12925 — <c>POST /api/v1/dr-flit/support-cases</c> con el delegate real y el handler real; el
/// proveedor, el repositorio y el store de adjuntos sustituidos.
/// Uso de ejemplo:
/// <code>
/// var result = await DrFlitEndpoints.CreateSupportCaseAsync(ctx, tenant, body, handler, ct);
/// await result.ExecuteAsync(ctx); // 201 { caseId, caseUrl, attachmentsFailed } | 502 { code: support_unavailable }
/// </code>
/// </summary>
public sealed class DrFlitSupportCaseEndpointTests
{
    private static readonly Guid Tenant = Guid.Parse("0199a000-0000-7000-8000-0000000000aa");
    private static readonly Guid OtherTenant = Guid.Parse("0199a000-0000-7000-8000-0000000000bb");
    private static readonly Guid User = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private const string WorkItemUrl = "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_workitems/edit/13001";

    private readonly IDrFlitSupportCaseGateway _gateway = Substitute.For<IDrFlitSupportCaseGateway>();
    private readonly IDrFlitSupportCaseRepository _repository = Substitute.For<IDrFlitSupportCaseRepository>();
    private readonly IDrFlitSupportAttachmentStore _attachments = Substitute.For<IDrFlitSupportAttachmentStore>();
    private readonly IDrFlitSupportCaseSettings _settings = Substitute.For<IDrFlitSupportCaseSettings>();

    public DrFlitSupportCaseEndpointTests()
    {
        _settings.MaxAttachments.Returns(5);
        _gateway.DestinationName.Returns("FLIT - SOPORTE");
        _gateway.ResolveAffectedModule(Arg.Any<string?>()).Returns("Otros Tramites");
        _repository.InsertPendingAsync(default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(Guid.CreateVersion7());
        _attachments.GetPendingAsync(default, default, default!, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(ci => (ci.ArgAt<IReadOnlyCollection<Guid>?>(2) ?? [])
                .Select(id => new DrFlitPendingAttachment(id, "a.png", "image/png", "fm")).ToList());
        _gateway.CreateBugAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitBugCreationResult(true, 13001, WorkItemUrl, 0, null));
    }

    private CreateSupportCaseHandler Handler() =>
        new(_gateway, _repository, _attachments, _settings, TimeProvider.System, NullLogger<CreateSupportCaseHandler>.Instance);

    private static DrFlitSupportCaseRequestBody Body() => new()
    {
        Nombre = "Usuario Prueba",
        Email = "usuario.prueba@example.test",
        Telefono = "3000000000",
        Compania = "Empresa Demo S.A.S",
        Detalle = "Al subir la factura sale error",
        ResultadoEsperado = "Que la factura quede cargada",
        Frecuencia = "siempre",
        Titulo = "No puedo subir la factura",
        Prioridad = "Alta",
        AffectedModule = "/tramites",
        AttachmentIds = [],
    };

    private static DefaultHttpContext Context(bool superAdmin = false)
    {
        var claims = new List<Claim> { new("sub", User.ToString()), new(AdminAuthorization.TenantIdClaimType, Tenant.ToString()) };
        if (superAdmin)
            claims.Add(new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.SuperAdminRole));
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private async Task<(int Status, JsonElement Body)> Post(DrFlitSupportCaseRequestBody? body, bool superAdmin = false, Guid? header = null)
    {
        var ctx = Context(superAdmin);
        var result = await DrFlitEndpoints.CreateSupportCaseAsync(ctx, header ?? Tenant, body, Handler(), TestContext.Current.CancellationToken);
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (ctx.Response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task AC1_CasoRadicado_201ConElNumeroDeCaso()
    {
        var (status, body) = await Post(Body());

        status.Should().Be(StatusCodes.Status201Created);
        body.GetProperty("caseId").GetInt32().Should().Be(13001);
        body.GetProperty("attachmentsFailed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task AC1_ElCasoQuedaDelTenantDelToken_NoDelHeader()
    {
        DrFlitSupportCaseRecord? record = null;
        _repository.InsertPendingAsync(Arg.Do<DrFlitSupportCaseRecord>(r => record = r), TestContext.Current.CancellationToken).Returns(Guid.CreateVersion7());

        await Post(Body(), header: OtherTenant);

        record!.TenantId.Should().Be(Tenant);
        record.UserId.Should().Be(User);
    }

    [Fact]
    public async Task AC2_ProveedorCaido_502ConCodigoParaOfrecerCanalesEstaticos()
    {
        _gateway.CreateBugAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(DrFlitBugCreationResult.Failed("timeout"));

        var (status, body) = await Post(Body());

        status.Should().Be(StatusCodes.Status502BadGateway);
        body.GetProperty("code").GetString().Should().Be("support_unavailable");
        body.GetProperty("detail").GetString().Should().Be(CreateSupportCaseHandler.ProviderUnavailableMessage);
    }

    [Fact]
    public async Task AC3_AdjuntosParciales_AttachmentsFailedEnLaRespuesta()
    {
        _gateway.CreateBugAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(new DrFlitBugCreationResult(true, 13001, WorkItemUrl, 1, null));
        var body = Body();
        body.AttachmentIds = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];

        var (status, response) = await Post(body);

        status.Should().Be(StatusCodes.Status201Created);
        response.GetProperty("attachmentsFailed").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AC4_CaseUrlSoloParaSuperAdmin(bool superAdmin)
    {
        var (_, body) = await Post(Body(), superAdmin);

        if (superAdmin)
            body.GetProperty("caseUrl").GetString().Should().Be(WorkItemUrl);
        else
            body.GetProperty("caseUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task FormularioInvalido_400SinLlamarAlProveedor()
    {
        var body = Body();
        body.Prioridad = "Urgente";

        var (status, response) = await Post(body);

        status.Should().Be(StatusCodes.Status400BadRequest);
        response.GetProperty("detail").GetString().Should().Be("La prioridad debe ser Alta, Media o Baja.");
        await _gateway.DidNotReceiveWithAnyArgs().CreateBugAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SinBody_400()
    {
        (await Post(null)).Status.Should().Be(StatusCodes.Status400BadRequest);
    }
}
