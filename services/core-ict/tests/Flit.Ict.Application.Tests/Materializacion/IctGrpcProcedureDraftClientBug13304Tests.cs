using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Grpc.Contracts;
using Flit.Ict.Infrastructure.ExternalClients;
using FluentAssertions;
using Grpc.Core;
using GrpcStatus = Grpc.Core.Status;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Ict.Application.Tests.Materializacion;

/// <summary>
/// Bug #13304 — el cliente gRPC de ICT (1) propaga el warning de una materialización con borrador creado
/// en vez de descartarlo y (2) expone <c>UpdateCommercialAsync</c> para editar el precio de venta mientras
/// el trámite siga en borrador, devolviendo el código de core-api tal cual o <c>grpc_unavailable</c>.
/// <para>Uso de ejemplo: <c>var (ok, error) = await client.UpdateCommercialAsync(tenant, instancia, master.Id, 45_000_000m, ct);</c>
/// — <c>error == "not_draft"</c> cuando el trámite ya avanzó.</para>
/// </summary>
public sealed class IctGrpcProcedureDraftClientBug13304Tests
{
    private readonly IctOrchestration.IctOrchestrationClient _grpc = Substitute.For<IctOrchestration.IctOrchestrationClient>();

    private IctGrpcProcedureDraftClient Client() => new(
        _grpc, Substitute.For<IAttachmentDocTypeResolver>(), NullLogger<IctGrpcProcedureDraftClient>.Instance);

    private static AsyncUnaryCall<DraftReply> Call(DraftReply reply) => new(
        Task.FromResult(reply), Task.FromResult(new Metadata()), () => GrpcStatus.DefaultSuccess, () => [], () => { });

    [Fact]
    public async Task CreateDraftAsync_BorradorCreadoConWarning_DevuelveElIdYElWarning()
    {
        var id = Guid.NewGuid();
        _grpc.CreateDraftFromIctAsync(Arg.Any<CreateDraftFromIctRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Call(new DraftReply { ProcedureInstanceId = id.ToString(), Status = "borrador", ErrorCode = "actors_warning:persist_failed" }));
        var master = new ExternalIntegrationMaster { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), TransactionType = 3 };

        var result = await Client().CreateDraftAsync(master, new DraftProcedureType("TRASPASO_STANDARD", "TRASPASO", true, true), vehicle: null, TestContext.Current.CancellationToken);

        result.ProcedureInstanceId.Should().Be(id);
        result.ErrorCode.Should().Be("actors_warning:persist_failed");
    }

    [Fact]
    public async Task UpdateCommercialAsync_SinError_DevuelveOkYEnviaElValorInvariante()
    {
        UpdateDraftCommercialRequest? enviado = null;
        _grpc.UpdateDraftCommercialAsync(Arg.Do<UpdateDraftCommercialRequest>(r => enviado = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Call(new DraftReply { Status = "borrador" }));
        var tenant = Guid.NewGuid();
        var instancia = Guid.NewGuid();
        var externalRef = Guid.NewGuid();

        var (ok, error) = await Client().UpdateCommercialAsync(tenant, instancia, externalRef, 45_500_000.50m, TestContext.Current.CancellationToken);

        ok.Should().BeTrue();
        error.Should().BeNull();
        enviado!.TenantId.Should().Be(tenant.ToString());
        enviado.ProcedureInstanceId.Should().Be(instancia.ToString());
        enviado.ExternalRef.Should().Be(externalRef.ToString());
        enviado.Commercial.ValorVenta.Should().Be("45500000.50");
    }

    [Fact]
    public async Task UpdateCommercialAsync_CoreApiRechaza_DevuelveElCodigoTalCual()
    {
        _grpc.UpdateDraftCommercialAsync(Arg.Any<UpdateDraftCommercialRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Call(new DraftReply { ErrorCode = "not_draft" }));

        var (ok, error) = await Client().UpdateCommercialAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, TestContext.Current.CancellationToken);

        ok.Should().BeFalse();
        error.Should().Be("not_draft");
    }

    [Fact]
    public async Task UpdateCommercialAsync_CanalCaido_DevuelveGrpcUnavailable()
    {
        _grpc.UpdateDraftCommercialAsync(Arg.Any<UpdateDraftCommercialRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Throws(new RpcException(new GrpcStatus(StatusCode.Unavailable, "down")));

        var (ok, error) = await Client().UpdateCommercialAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, TestContext.Current.CancellationToken);

        ok.Should().BeFalse();
        error.Should().Be("grpc_unavailable");
    }
}
