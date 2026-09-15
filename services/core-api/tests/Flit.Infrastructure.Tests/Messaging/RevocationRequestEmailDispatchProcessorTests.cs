using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>HU #12579 — worker de envío de la cola de correo del sub-flujo de revocatoria.</summary>
public sealed class RevocationRequestEmailDispatchProcessorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid RevocationRequestId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnvioExitoso_MarcaEnviado()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, RevocationRequestEmailMilestone.Solicitada, "Ana", "ana@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        await using var verify = NewContext(dbName);
        var row = await verify.RevocationRequestEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("enviado");
        row.ProcessedAt.Should().NotBeNull();
        sender.Messages.Should().ContainSingle();
        sender.Messages[0].TemplateKey.Should().Be(RevocationRequestNotificationEnqueuer.TemplateKey);
        sender.Messages[0].ToEmail.Should().Be("ana@flit.test");
        sender.Messages[0].TenantId.Should().Be(TenantId);
        sender.Messages[0].Subject.Should().Contain("REV-1");
    }

    [Fact]
    public async Task VarianteCuerpo_SigueCanalDelTenant()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, RevocationRequestEmailMilestone.Solicitada, "Ana", "ana@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.TenantApi);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].HtmlBody.Should().Contain("Renting Colombia");
    }

    [Fact]
    public async Task Rechazada_IncluyeMotivoEnElCuerpo()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(
            dbName, RevocationRequestEmailMilestone.Rechazada, "Ana", "ana@flit.test",
            decisionReason: "El soporte adjunto no es legible.");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].HtmlBody.Should().Contain("El soporte adjunto no es legible.");
        sender.Messages[0].TemplateKey.Should().Be(RevocationRequestNotificationEnqueuer.DecisionTemplateKeyRechazada);
    }

    [Fact]
    public async Task FalloDeEnvio_DejaFilaPendienteConAttemptsIncrementado()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, RevocationRequestEmailMilestone.Solicitada, "Ana", "ana@flit.test");

        var sender = new RecordingSender { Fail = true };
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        await using var verify = NewContext(dbName);
        var row = await verify.RevocationRequestEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("pendiente");
        row.Attempts.Should().Be(1);
        row.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task FilasOmitidas_NuncaSeReclaman()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await using (var db = NewContext(dbName))
        {
            db.RevocationRequestEmailDispatches.Add(NewDispatch(
                RevocationRequestEmailMilestone.Solicitada, "Hueco", null, status: "omitido"));
            await db.SaveChangesAsync(Ct);
        }

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task KillSwitchApagado_NoEnviaNiGastaAttempts()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, RevocationRequestEmailMilestone.Solicitada, "Ana", "ana@flit.test");
        await using (var db = NewContext(dbName))
        {
            db.TenantOperationalPolicies.Add(new Flit.Infrastructure.Persistence.Entities.Admin.TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                TramiteApprovedEmailsEnabled = false,
                TramiteRejectedEmailsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().BeEmpty();
        await using var verify = NewContext(dbName);
        var row = await verify.RevocationRequestEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("pendiente");
        row.Attempts.Should().Be(0);
    }

    private static string NewDbName() => $"flit-revocation-dispatch-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static RevocationRequestEmailDispatchProcessor NewProcessor(
        string dbName, IEmailSender sender, NotificationChannel channel)
    {
        var channelResolver = Substitute.For<INotificationChannelResolver>();
        channelResolver.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(channel);

        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddScoped(_ => sender);
        services.AddScoped(_ => channelResolver);
        services.AddSingleton(Options.Create(new NotificationEmailAssetsOptions
        {
            BaseUrl = "https://cdn.flit.test/email-assets",
        }));

        var provider = services.BuildServiceProvider();
        return new RevocationRequestEmailDispatchProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RevocationRequestEmailDispatchProcessor>.Instance);
    }

    private static async Task SeedInstanceAsync(string dbName)
    {
        await using var db = NewContext(dbName);
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "REV-1",
            Status = "aprobado",
            Plate = "ABC123",
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedDispatchAsync(
        string dbName, string milestone, string name, string email, string? decisionReason = null)
    {
        await using var db = NewContext(dbName);
        db.RevocationRequestEmailDispatches.Add(
            NewDispatch(milestone, name, email, status: "pendiente", decisionReason));
        await db.SaveChangesAsync(Ct);
    }

    private static RevocationRequestEmailDispatch NewDispatch(
        string milestone, string name, string? email, string status, string? decisionReason = null,
        int attempts = 0) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            RevocationRequestId = RevocationRequestId,
            AttemptNumber = 1,
            Milestone = milestone,
            Recipient = email,
            RecipientName = name,
            RecipientRole = "radicador",
            RecipientKind = "persona",
            TemplateKey = milestone switch
            {
                RevocationRequestEmailMilestone.Aprobada => RevocationRequestNotificationEnqueuer.DecisionTemplateKeyAprobada,
                RevocationRequestEmailMilestone.Rechazada => RevocationRequestNotificationEnqueuer.DecisionTemplateKeyRechazada,
                _ => RevocationRequestNotificationEnqueuer.TemplateKey,
            },
            Status = status,
            DecisionReason = decisionReason,
            Attempts = attempts,
            QueuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessedAt = status == "omitido" ? DateTimeOffset.UtcNow : null,
            FailureReason = status == "omitido" ? "Sin correo para la persona" : null,
        };

    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];
        public bool Fail { get; set; }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(
                Fail
                    ? EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable)
                    : EmailSendResult.Sent);
        }
    }
}
