using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>HU #11467 — worker de envío de la cola de avisos de cambio de estado.</summary>
public sealed class ProcedureStateChangeEmailDispatchProcessorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid OutboxId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NombreDestinatario_DistingueEmpresaDeRepresentanteLegal()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "empresa", "Empresa SAS", "empresa@flit.test");
        await SeedDispatchAsync(dbName, "representante_legal", "Rep Legal", "rl@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);
        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().HaveCount(2);
        sender.Messages.Select(m => m.ToName).Should().BeEquivalentTo(["Empresa SAS", "Rep Legal"]);
    }

    [Fact]
    public async Task VarianteCuerpo_SigueCanalDelTenant()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.TenantApi);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().ContainSingle();
        // Renting: saludo nominal / Compra Tu Usado; FLIT no incluye ese copy.
        sender.Messages[0].HtmlBody.Should().Contain("Es un gusto saludarte");
    }

    [Fact]
    public async Task FalloDeEnvio_DejaFilaPendienteConAttemptsIncrementado()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");

        var sender = new RecordingSender { Fail = true };
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        await using var verify = NewContext(dbName);
        var row = await verify.ProcedureStateChangeEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("pendiente");
        row.Attempts.Should().Be(1);
        row.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task AgotarIntentos_DejaFallidoYNoSeReclamaMas()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(
            dbName, "persona", "Ana", "ana@flit.test",
            attempts: ProcedureStateChangeEmailDispatch.MaxDeliveryAttempts - 1);

        var sender = new RecordingSender { Fail = true };
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);
        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().HaveCount(1, "el segundo ciclo no debe reclamar filas fallidas");

        await using var verify = NewContext(dbName);
        var row = await verify.ProcedureStateChangeEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("fallido");
        row.Attempts.Should().Be(ProcedureStateChangeEmailDispatch.MaxDeliveryAttempts);
        row.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FilasOmitidas_NuncaSeReclaman()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await using (var db = NewContext(dbName))
        {
            db.ProcedureStateChangeEmailDispatches.Add(NewDispatch(
                kind: "persona", name: "Hueco", email: null, status: "omitido"));
            await db.SaveChangesAsync(Ct);
        }

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task EnvioExitoso_MarcaEnviado()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        await using var verify = NewContext(dbName);
        var row = await verify.ProcedureStateChangeEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("enviado");
        row.ProcessedAt.Should().NotBeNull();
        sender.Messages[0].TemplateKey.Should().Be("tramites.aprobado");
        sender.Messages[0].ToEmail.Should().Be("ana@flit.test");
    }

    [Fact]
    public async Task KillSwitchApagado_NoEnviaNiGastaAttempts()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");
        await using (var db = NewContext(dbName))
        {
            db.TenantOperationalPolicies.Add(new Flit.Infrastructure.Persistence.Entities.Admin.TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                TramiteStateEmailsEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().BeEmpty();
        await using var verify = NewContext(dbName);
        var row = await verify.ProcedureStateChangeEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("pendiente");
        row.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task KillSwitchReanudado_EnviaLoAcumulado()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");
        await using (var db = NewContext(dbName))
        {
            db.TenantOperationalPolicies.Add(new Flit.Infrastructure.Persistence.Entities.Admin.TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                TramiteStateEmailsEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);
        await processor.ProcessPendingAsync(Ct);
        sender.Messages.Should().BeEmpty();

        await using (var db = NewContext(dbName))
        {
            var policy = await db.TenantOperationalPolicies.SingleAsync(Ct);
            policy.TramiteStateEmailsEnabled = true;
            await db.SaveChangesAsync(Ct);
        }

        await processor.ProcessPendingAsync(Ct);
        sender.Messages.Should().ContainSingle();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string NewDbName() => $"flit-email-dispatch-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ProcedureStateChangeEmailDispatchProcessor NewProcessor(
        string dbName, IEmailSender sender, NotificationChannel channel)
    {
        var channelResolver = Substitute.For<INotificationChannelResolver>();
        channelResolver.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(channel);

        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddScoped(_ => sender);
        services.AddScoped(_ => channelResolver);
        services.AddSingleton(Options.Create(new NotificationEmailAssetsOptions
        {
            BaseUrl = "https://cdn.flit.test/email-assets",
        }));

        var provider = services.BuildServiceProvider();
        return new ProcedureStateChangeEmailDispatchProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProcedureStateChangeEmailDispatchProcessor>.Instance);
    }

    private static async Task SeedInstanceAsync(string dbName)
    {
        await using var db = NewContext(dbName);
        db.ProcedureStateChangeOutbox.Add(new ProcedureStateChangeOutbox
        {
            Id = OutboxId,
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            FromStatus = "entregado",
            ToStatus = "aprobado",
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "DSP-1",
            Status = "aprobado",
            Plate = "ABC123",
            ModalidadEntrada = "matricula_inicial",
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Actors =
            {
                new ProcedureInstanceActor
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    ProcedureInstanceId = InstanceId,
                    ProcedureEntityId = Guid.NewGuid(),
                    ActorType = "comprador",
                    DocumentType = "CC",
                    DocumentNumber = "1",
                    FullName = "Ana Compradora",
                    Email = "ana@flit.test",
                    PersonType = "natural",
                    Metadata = "{}",
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            },
            FieldValues =
            {
                new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    ProcedureInstanceId = InstanceId,
                    FieldKey = "transit_office_name",
                    ValueText = "OT Funza",
                    Source = "user",
                },
                new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    ProcedureInstanceId = InstanceId,
                    FieldKey = "transit_office_city",
                    ValueText = "FUNZA",
                    Source = "user",
                },
            },
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedDispatchAsync(
        string dbName, string kind, string name, string email, int attempts = 0)
    {
        await using var db = NewContext(dbName);
        db.ProcedureStateChangeEmailDispatches.Add(
            NewDispatch(kind, name, email, status: "pendiente", attempts));
        await db.SaveChangesAsync(Ct);
    }

    private static ProcedureStateChangeEmailDispatch NewDispatch(
        string kind, string name, string? email, string status, int attempts = 0) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            OutboxId = OutboxId,
            ProcedureInstanceId = InstanceId,
            Recipient = email,
            RecipientName = name,
            RecipientRole = "comprador",
            RecipientKind = kind,
            TemplateKey = "tramites.aprobado",
            Status = status,
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
