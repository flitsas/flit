using Flit.Modules.Quipux.Application.UseCases.Cola;
using Flit.Modules.Quipux.Domain.Consola;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.Cola;

/// <summary>
/// Orquestación de los handlers de la consola de cola (HU #10774): que una transición efectiva deje
/// su rastro en la bitácora con el stage correcto, y que una fallida no escriba nada.
/// </summary>
public sealed class ColaQuipuxHandlersTests
{
    private static readonly Guid SubmissionId = Guid.NewGuid();
    private static readonly Guid TransitOfficeId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public async Task Reintentar_WhenOk_WritesReintentoManualEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Substitute.For<IQuipuxSubmissionConsoleRepository>();
        var audit = Substitute.For<IQuipuxAuditLog>();
        repo.RetryAsync(SubmissionId, TransitOfficeId, ct)
            .Returns(new QuipuxManualOpResult(QuipuxManualOpStatus.Ok, TenantId, "fallido", 5));

        var handler = new ReintentarSubmissionHandler(repo, audit, NullLogger<ReintentarSubmissionHandler>.Instance);
        var status = await handler.HandleAsync(
            new ReintentarSubmissionCommand(SubmissionId, TransitOfficeId, ActorId), ct);

        Assert.Equal(QuipuxManualOpStatus.Ok, status);
        await audit.Received(1).WriteAsync(
            TenantId, SubmissionId, QuipuxStage.ReintentoManual, QuipuxOutcome.Ok,
            Arg.Any<object?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reintentar_WhenInvalidState_DoesNotWriteEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Substitute.For<IQuipuxSubmissionConsoleRepository>();
        var audit = Substitute.For<IQuipuxAuditLog>();
        repo.RetryAsync(SubmissionId, TransitOfficeId, ct)
            .Returns(new QuipuxManualOpResult(QuipuxManualOpStatus.InvalidState, TenantId, "registrado"));

        var handler = new ReintentarSubmissionHandler(repo, audit, NullLogger<ReintentarSubmissionHandler>.Instance);
        var status = await handler.HandleAsync(
            new ReintentarSubmissionCommand(SubmissionId, TransitOfficeId, ActorId), ct);

        Assert.Equal(QuipuxManualOpStatus.InvalidState, status);
        await audit.DidNotReceive().WriteAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<object?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelar_WhenOk_WritesCanceladoManualEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Substitute.For<IQuipuxSubmissionConsoleRepository>();
        var audit = Substitute.For<IQuipuxAuditLog>();
        repo.CancelAsync(SubmissionId, TransitOfficeId, ct)
            .Returns(new QuipuxManualOpResult(QuipuxManualOpStatus.Ok, TenantId, "pendiente"));

        var handler = new CancelarSubmissionHandler(repo, audit, NullLogger<CancelarSubmissionHandler>.Instance);
        var status = await handler.HandleAsync(
            new CancelarSubmissionCommand(SubmissionId, TransitOfficeId, ActorId), ct);

        Assert.Equal(QuipuxManualOpStatus.Ok, status);
        await audit.Received(1).WriteAsync(
            TenantId, SubmissionId, QuipuxStage.CanceladoManual, QuipuxOutcome.Omitido,
            Arg.Any<object?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
