using Flit.Tramites.Application.UseCases.ProcedureInstances.Notifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Notifications;

/// <summary>HU #11470 — visibilidad de despachos de correo para el gestor.</summary>
public sealed class GetNotificationDispatchesHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void MaskEmail_NoExponeLaDireccionCompleta()
    {
        var masked = GetNotificationDispatchesHandler.MaskEmail("ana.compradora@flit.test");
        masked.Should().Be("an***@***.test");
        masked.Should().NotContain("ana.compradora");
        masked.Should().NotContain("flit.test");
    }

    [Fact]
    public void MaskEmail_CupoSinCorreo_EsNull()
    {
        GetNotificationDispatchesHandler.MaskEmail(null).Should().BeNull();
        GetNotificationDispatchesHandler.MaskEmail("  ").Should().BeNull();
    }

    [Fact]
    public async Task InstanciaDeOtraCompania_DevuelveNotFound()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListEmailDispatchesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Ct)
            .Returns((IReadOnlyList<ProcedureStateChangeEmailDispatch>?)null);

        var sut = new GetNotificationDispatchesHandler(repo);
        var (result, error) = await sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task SinAvisos_DevuelveListaVacia()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListEmailDispatchesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Ct)
            .Returns(Array.Empty<ProcedureStateChangeEmailDispatch>());

        var sut = new GetNotificationDispatchesHandler(repo);
        var (result, error) = await sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Ct);

        error.Should().BeNull();
        result!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task MotivoDelHueco_EsLegibleYCorreoEnmascarado()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.ListEmailDispatchesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Ct)
            .Returns(
            [
                new ProcedureStateChangeEmailDispatch
                {
                    Id = Guid.NewGuid(),
                    RecipientRole = "comprador",
                    RecipientKind = "representante_legal",
                    Recipient = null,
                    RecipientName = "RL",
                    TemplateKey = "tramites.rechazado",
                    Status = "omitido",
                    FailureReason = "Sin correo para el representante legal",
                    Attempts = 0,
                    QueuedAt = DateTimeOffset.UtcNow,
                },
                new ProcedureStateChangeEmailDispatch
                {
                    Id = Guid.NewGuid(),
                    RecipientRole = "comprador",
                    RecipientKind = "empresa",
                    Recipient = "empresa@acme.co",
                    RecipientName = "ACME",
                    TemplateKey = "tramites.rechazado",
                    Status = "enviado",
                    Attempts = 1,
                    QueuedAt = DateTimeOffset.UtcNow,
                    ProcessedAt = DateTimeOffset.UtcNow,
                },
            ]);

        var sut = new GetNotificationDispatchesHandler(repo);
        var (result, error) = await sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Ct);

        error.Should().BeNull();
        result!.Items.Should().HaveCount(2);
        var omitido = result.Items.Single(i => i.Status == "omitido");
        omitido.FailureReason.Should().Contain("representante legal");
        omitido.RecipientMasked.Should().BeNull();

        var enviado = result.Items.Single(i => i.Status == "enviado");
        enviado.RecipientMasked.Should().Be("em***@***.co");
        enviado.RecipientMasked.Should().NotContain("empresa@acme.co");
    }
}
