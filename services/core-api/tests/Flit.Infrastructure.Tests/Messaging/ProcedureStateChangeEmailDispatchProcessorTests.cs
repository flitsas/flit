using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
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
    public async Task EmpresaYRepresentanteLegal_Reciben_CorreosPropios_ConSuSaludo()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "empresa", "Empresa SAS", "empresa@flit.test");
        await SeedDispatchAsync(dbName, "representante_legal", "Rep Legal", "rl@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);
        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().HaveCount(2);
        var empresa = sender.Messages.Single(m => m.ToEmail == "empresa@flit.test");
        var rl = sender.Messages.Single(m => m.ToEmail == "rl@flit.test");

        // A la persona jurídica se le escribe como tal; a su representante, por su nombre.
        empresa.HtmlBody.Should().Contain("Estimados señores").And.Contain("Empresa SAS");
        empresa.HtmlBody.Should().NotContain("Rep Legal");
        rl.HtmlBody.Should().Contain("Estimado/a Señor/a").And.Contain("Rep Legal");
        empresa.BccEmails.Should().BeEmpty();
        rl.BccEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task Traspaso_Comprador_y_Vendedor_Reciben_CorreoPropio_ConSuNombre()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, traspaso: true);
        await SeedDispatchAsync(dbName, "persona", "Ana Compradora", "ana@flit.test", role: "comprador");
        await SeedDispatchAsync(dbName, "persona", "Beto Vendedor", "beto@flit.test", role: "vendedor");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);
        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().HaveCount(2);
        var comprador = sender.Messages.Single(m => m.ToEmail == "ana@flit.test");
        var vendedor = sender.Messages.Single(m => m.ToEmail == "beto@flit.test");

        // El reporte de QA: el vendedor recibía el correo dirigido al comprador.
        comprador.HtmlBody.Should().Contain("Estimado/a Señor/a <strong style=\"color:#2F6FED\">Ana Compradora</strong>");
        vendedor.HtmlBody.Should().Contain("Estimado/a Señor/a <strong style=\"color:#2F6FED\">Beto Vendedor</strong>");

        // Nadie va en copia oculta de nadie: son envíos independientes.
        comprador.BccEmails.Should().BeEmpty();
        vendedor.BccEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task Traspaso_CargaElTipo_YMuestraLaLineaDelVendedor()
    {
        // Regresión: ADR-0050 pasó a derivar la familia de la navegación ProcedureType, que esta
        // consulta del worker no cargaba. Llegaba null, todo traspaso se componía como si no lo
        // fuera y la línea «Vendedor:» desaparecía del bloque de detalles.
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, traspaso: true);
        await SeedDispatchAsync(dbName, "persona", "Ana Compradora", "ana@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);
        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].HtmlBody.Should().Contain("Vendedor:").And.Contain("Beto Vendedor");
    }

    [Fact]
    public async Task Gestor_ViajaEnCopiaOcultaDelCompradorYNoRecibeCorreoPropio()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, traspaso: true);
        await SeedDispatchAsync(dbName, "persona", "Ana Compradora", "ana@flit.test", role: "comprador");
        await SeedDispatchAsync(dbName, "persona", "Beto Vendedor", "beto@flit.test", role: "vendedor");
        await SeedDispatchAsync(dbName, "persona", "Dag Gestor", "dag@flit.test", role: "radicador");
        await SeedDispatchAsync(
            dbName, "persona", "avisos@empresa.test", "avisos@empresa.test",
            role: "configuracion_empresa");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);
        await processor.ProcessPendingAsync(Ct);

        // Un correo por parte; gestor y correo extra no son parte y no abren un envío propio.
        sender.Messages.Should().HaveCount(2);
        var comprador = sender.Messages.Single(m => m.ToEmail == "ana@flit.test");
        var vendedor = sender.Messages.Single(m => m.ToEmail == "beto@flit.test");

        comprador.BccEmails.Should().BeEquivalentTo(["dag@flit.test", "avisos@empresa.test"]);
        vendedor.BccEmails.Should().BeEmpty();

        // Las cuatro filas se cierran, aunque solo hubo dos envíos.
        await using var verify = NewContext(dbName);
        var rows = await verify.ProcedureStateChangeEmailDispatches.ToListAsync(Ct);
        rows.Should().HaveCount(4);
        rows.Should().OnlyContain(r => r.Status == "enviado");
    }

    [Fact]
    public async Task SinPartesConCorreo_ElGestorRecibeUnUnicoCorreoSinPersonalizar()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedDispatchAsync(dbName, "persona", "Dag Gestor", "dag@flit.test", role: "radicador");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);
        await processor.ProcessPendingAsync(Ct);

        // Comportamiento previo a separar: sin parte a la que dirigirse, se conserva el comprador.
        sender.Messages.Should().ContainSingle();
        sender.Messages[0].ToEmail.Should().Be("dag@flit.test");
        sender.Messages[0].HtmlBody.Should().Contain("Ana Compradora");
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
                TramiteApprovedEmailsEnabled = false,
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
                TramiteApprovedEmailsEnabled = false,
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
            policy.TramiteApprovedEmailsEnabled = true;
            await db.SaveChangesAsync(Ct);
        }

        await processor.ProcessPendingAsync(Ct);
        sender.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task Rechazado_IncluyeCausalesYObservacionDelUltimoEvento()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedRejectionAsync(
            dbName,
            older: ("Fotos borrosas", "Evento viejo"),
            latest: (["Improntas no coinciden", "Documentos ilegibles"], "Adjuntar SOAT vigente."));
        await SeedDispatchAsync(
            dbName, "persona", "Ana", "ana@flit.test",
            templateKey: "tramites.rechazado");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().ContainSingle();
        var html = sender.Messages[0].HtmlBody;
        html.Should().Contain("Motivo de rechazo");
        html.Should().Contain("Documentos ilegibles; Improntas no coinciden");
        html.Should().Contain("Adjuntar SOAT vigente.");
        html.Should().NotContain("Fotos borrosas");
        html.Should().NotContain("Evento viejo");
        sender.Messages[0].TemplateKey.Should().Be("tramites.rechazado");
    }

    [Fact]
    public async Task Rechazado_SinCausales_SoloObservacion()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);
        await SeedRejectionAsync(
            dbName,
            older: null,
            latest: ([], "Rechazo de secretaría sin catálogo."));
        await SeedDispatchAsync(
            dbName, "persona", "Ana", "ana@flit.test",
            templateKey: "tramites.rechazado");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, NotificationChannel.FlitSmtp);

        await processor.ProcessPendingAsync(Ct);

        var html = sender.Messages[0].HtmlBody;
        html.Should().NotContain("Motivo de rechazo");
        html.Should().Contain("Observación");
        html.Should().Contain("Rechazo de secretaría sin catálogo.");
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

    private static async Task SeedInstanceAsync(string dbName, bool traspaso = false)
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
            ProcedureType = traspaso
                ? ProcedureTypeFixture.Traspaso
                : ProcedureTypeFixture.Matricula,
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "DSP-1",
            Status = "aprobado",
            Plate = "ABC123",
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

        if (traspaso)
        {
            db.ProcedureInstanceActors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                ProcedureEntityId = Guid.NewGuid(),
                ActorType = "vendedor",
                DocumentType = "CC",
                DocumentNumber = "2",
                FullName = "Beto Vendedor",
                Email = "beto@flit.test",
                PersonType = "natural",
                Metadata = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedDispatchAsync(
        string dbName, string kind, string name, string email, int attempts = 0,
        string templateKey = "tramites.aprobado", string role = "comprador")
    {
        await using var db = NewContext(dbName);
        db.ProcedureStateChangeEmailDispatches.Add(
            NewDispatch(kind, name, email, status: "pendiente", attempts, templateKey, role));
        await db.SaveChangesAsync(Ct);
    }

    private static ProcedureStateChangeEmailDispatch NewDispatch(
        string kind, string name, string? email, string status, int attempts = 0,
        string templateKey = "tramites.aprobado", string role = "comprador") => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            OutboxId = OutboxId,
            ProcedureInstanceId = InstanceId,
            Recipient = email,
            RecipientName = name,
            RecipientRole = role,
            RecipientKind = kind,
            TemplateKey = templateKey,
            Status = status,
            Attempts = attempts,
            QueuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessedAt = status == "omitido" ? DateTimeOffset.UtcNow : null,
            FailureReason = status == "omitido" ? "Sin correo para la persona" : null,
        };

    private static async Task SeedRejectionAsync(
        string dbName,
        (string Causal, string Observacion)? older,
        (IReadOnlyList<string> Causales, string Observacion) latest)
    {
        await using var db = NewContext(dbName);
        var now = DateTimeOffset.UtcNow;

        if (older is { } oldEvent)
        {
            var oldHistoryId = Guid.NewGuid();
            db.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
            {
                Id = oldHistoryId,
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                FromStatus = TramiteEstado.Entregado,
                ToStatus = TramiteEstado.Rechazado,
                ChangedAt = now.AddDays(-2),
                Reason = oldEvent.Observacion,
                Metadata = "{}",
            });
            var oldReasonId = Guid.NewGuid();
            db.RejectionReasons.Add(new RejectionReason
            {
                Id = oldReasonId,
                Code = "OLD",
                Description = oldEvent.Causal,
                Family = "MATRICULAS",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = now,
            });
            db.ProcedureInstanceRejectionReasons.Add(new ProcedureInstanceRejectionReason
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                StatusHistoryId = oldHistoryId,
                RejectionReasonId = oldReasonId,
                CreatedAt = now.AddDays(-2),
            });
        }

        var historyId = Guid.NewGuid();
        db.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = historyId,
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            FromStatus = TramiteEstado.Entregado,
            ToStatus = TramiteEstado.Rechazado,
            ChangedAt = now.AddMinutes(-5),
            Reason = latest.Observacion,
            Metadata = "{}",
        });

        for (var i = 0; i < latest.Causales.Count; i++)
        {
            var reasonId = Guid.NewGuid();
            db.RejectionReasons.Add(new RejectionReason
            {
                Id = reasonId,
                Code = $"NEW{i}",
                Description = latest.Causales[i],
                Family = "MATRICULAS",
                SortOrder = 10 + i,
                IsActive = true,
                CreatedAt = now,
            });
            db.ProcedureInstanceRejectionReasons.Add(new ProcedureInstanceRejectionReason
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = InstanceId,
                StatusHistoryId = historyId,
                RejectionReasonId = reasonId,
                CreatedAt = now.AddMinutes(-5),
            });
        }

        await db.SaveChangesAsync(Ct);
    }

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
