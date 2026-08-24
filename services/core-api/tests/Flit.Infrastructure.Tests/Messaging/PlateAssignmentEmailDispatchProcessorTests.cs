using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>HU #11487 — worker de envío de la cola de avisos de asignación de placa.</summary>
public sealed class PlateAssignmentEmailDispatchProcessorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnvioExitoso_MarcaEnviado()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, plate: "XYZ789");
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, PlateAssignmentEmailBrand.Flit);

        await processor.ProcessPendingAsync(Ct);

        await using var verify = NewContext(dbName);
        var row = await verify.PlateAssignmentEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("enviado");
        row.ProcessedAt.Should().NotBeNull();
        sender.Messages.Should().ContainSingle();
        sender.Messages[0].TemplateKey.Should().Be(AsignacionPlacaEmailComposer.TemplateId);
        sender.Messages[0].ToEmail.Should().Be("ana@flit.test");
        sender.Messages[0].TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task VarianteCuerpo_SigueMarcaPorNit()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, plate: "XYZ789");
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, PlateAssignmentEmailBrand.Renting);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].HtmlBody.Should().Contain("Línea gratuita");
    }

    [Fact]
    public async Task FalloDeEnvio_DejaFilaPendienteConAttemptsIncrementado()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, plate: "XYZ789");
        await SeedDispatchAsync(dbName, "persona", "Ana", "ana@flit.test");

        var sender = new RecordingSender { Fail = true };
        var processor = NewProcessor(dbName, sender, PlateAssignmentEmailBrand.Flit);

        await processor.ProcessPendingAsync(Ct);

        await using var verify = NewContext(dbName);
        var row = await verify.PlateAssignmentEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("pendiente");
        row.Attempts.Should().Be(1);
        row.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task FilasOmitidas_NuncaSeReclaman()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, plate: "XYZ789");
        await using (var db = NewContext(dbName))
        {
            db.PlateAssignmentEmailDispatches.Add(NewDispatch(
                kind: "persona", name: "Hueco", email: null, status: "omitido"));
            await db.SaveChangesAsync(Ct);
        }

        var sender = new RecordingSender();
        var processor = NewProcessor(dbName, sender, PlateAssignmentEmailBrand.Flit);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task KillSwitchApagado_NoEnviaNiGastaAttempts()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, plate: "XYZ789");
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
        var processor = NewProcessor(dbName, sender, PlateAssignmentEmailBrand.Flit);

        await processor.ProcessPendingAsync(Ct);

        sender.Messages.Should().BeEmpty();
        await using var verify = NewContext(dbName);
        var row = await verify.PlateAssignmentEmailDispatches.SingleAsync(Ct);
        row.Status.Should().Be("pendiente");
        row.Attempts.Should().Be(0);
    }

    private static string NewDbName() => $"flit-plate-dispatch-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static PlateAssignmentEmailDispatchProcessor NewProcessor(
        string dbName, IEmailSender sender, PlateAssignmentEmailBrand brand)
    {
        var brandResolver = Substitute.For<IPlateAssignmentBrandResolver>();
        brandResolver.ResolveForClientTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(brand);

        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddScoped(_ => sender);
        services.AddScoped(_ => brandResolver);
        services.AddScoped<IPlateAssignmentEmailModelProjector, PlateAssignmentEmailModelProjectorService>();
        services.AddSingleton(Options.Create(new NotificationEmailAssetsOptions
        {
            BaseUrl = "https://cdn.flit.test/email-assets",
        }));

        var provider = services.BuildServiceProvider();
        return new PlateAssignmentEmailDispatchProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlateAssignmentEmailDispatchProcessor>.Instance);
    }

    private static async Task SeedInstanceAsync(string dbName, string plate)
    {
        await using var db = NewContext(dbName);
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "DSP-1",
            Status = "en_proceso",
            Plate = plate,
            PlateFlowStatus = "Asignado",
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
        db.PlateAssignmentEmailDispatches.Add(
            NewDispatch(kind, name, email, status: "pendiente", attempts));
        await db.SaveChangesAsync(Ct);
    }

    private static PlateAssignmentEmailDispatch NewDispatch(
        string kind, string name, string? email, string status, int attempts = 0) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            Plate = "XYZ789",
            Recipient = email,
            RecipientName = name,
            RecipientRole = "comprador",
            RecipientKind = kind,
            TemplateKey = AsignacionPlacaEmailComposer.TemplateId,
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
