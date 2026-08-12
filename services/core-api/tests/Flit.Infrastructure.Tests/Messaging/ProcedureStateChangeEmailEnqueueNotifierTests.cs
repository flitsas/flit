using System.Text.Json;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #11465 — sink que encola despachos de correo al cambio de estado (ADR-0045).
/// Idempotencia verificada sobre la rama InMemory (Postgres UNIQUE queda en HU-A + evidencia DEV).
/// </summary>
public sealed class ProcedureStateChangeEmailEnqueueNotifierTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid OutboxId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReintentoDelOutbox_NoEncolaSegundoCorreo()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, NaturalComprador("a@flit.test", "Ana"));
        await SeedOutboxAsync(dbName);

        var sut = NewSut(dbName);
        var evt = ApprovedEvent();

        await sut.NotifyAsync(evt, Ct);
        await sut.NotifyAsync(evt, Ct);

        await using var verify = NewContext(dbName);
        var rows = await verify.ProcedureStateChangeEmailDispatches.ToListAsync(Ct);
        rows.Should().HaveCount(1);
        rows[0].Status.Should().Be("pendiente");
        rows[0].Recipient.Should().Be("a@flit.test");
    }

    [Fact]
    public async Task TraspasoJuridicaContraJuridica_ProduceCuatroCupos()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(
            dbName,
            Juridical("comprador", "c@flit.test", "rl-c@flit.test"),
            Juridical("vendedor", "v@flit.test", "rl-v@flit.test"),
            modalidad: "traspaso");
        await SeedOutboxAsync(dbName);

        await NewSut(dbName).NotifyAsync(ApprovedEvent(), Ct);

        await using var verify = NewContext(dbName);
        var rows = await verify.ProcedureStateChangeEmailDispatches.ToListAsync(Ct);
        rows.Should().HaveCount(4);
        rows.Count(r => r.RecipientKind == "empresa").Should().Be(2);
        rows.Count(r => r.RecipientKind == "representante_legal").Should().Be(2);
        rows.Select(r => r.RecipientRole).Should().BeEquivalentTo(["comprador", "comprador", "vendedor", "vendedor"]);
    }

    [Fact]
    public async Task EmpresaYRlConMismoBuzon_UnaSolaFila()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(
            dbName,
            Juridical("comprador", "mismo@flit.test", "mismo@flit.test"));
        await SeedOutboxAsync(dbName);

        await NewSut(dbName).NotifyAsync(ApprovedEvent(), Ct);

        await using var verify = NewContext(dbName);
        var rows = await verify.ProcedureStateChangeEmailDispatches.ToListAsync(Ct);
        rows.Should().ContainSingle();
        rows[0].Recipient.Should().Be("mismo@flit.test");
        rows[0].RecipientKind.Should().Be("empresa");
    }

    [Fact]
    public async Task SinNingunCorreo_EncolaOmitidosSinExcepcion()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, NaturalComprador(email: null, "Sin Mail"));
        await SeedOutboxAsync(dbName);

        var act = () => NewSut(dbName).NotifyAsync(RejectedEvent(), Ct);
        await act.Should().NotThrowAsync();

        await using var verify = NewContext(dbName);
        var rows = await verify.ProcedureStateChangeEmailDispatches.ToListAsync(Ct);
        rows.Should().ContainSingle();
        rows[0].Status.Should().Be("omitido");
        rows[0].Recipient.Should().BeNull();
        rows[0].FailureReason.Should().Contain("persona");
    }

    [Fact]
    public async Task TransicionSinPlantilla_NoEncolaNada()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName, NaturalComprador("a@flit.test", "Ana"));
        await SeedOutboxAsync(dbName);

        await NewSut(dbName).NotifyAsync(
            new ProcedureStateChangeEvent(
                TenantId, InstanceId, "preparado", "entregado",
                DateTimeOffset.UtcNow, OutboxId: OutboxId),
            Ct);

        await using var verify = NewContext(dbName);
        (await verify.ProcedureStateChangeEmailDispatches.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task FalloDelSinkCorreo_NoImpideWebhookOt_Composite()
    {
        var ot = new RecordingNotifier();
        var email = new FailingEmailSink();
        var composite = new Flit.Infrastructure.Ict.CompositeProcedureStateChangeNotifier(
            [ot, email],
            NullLogger<Flit.Infrastructure.Ict.CompositeProcedureStateChangeNotifier>.Instance);

        var act = async () => await composite.NotifyAsync(ApprovedEvent(), Ct);

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ot.Calls.Should().Be(1);
        ex.Which.InnerExceptions.Should().ContainSingle();
    }

    [Fact]
    public void BuildRows_OrdenDeterministaYColapso()
    {
        var resolution = new TramiteRecipientResolution(
            [
                new("comprador", TramiteRecipientKind.Empresa, "mismo@flit.test", "ACME"),
                new("comprador", TramiteRecipientKind.RepresentanteLegal, "mismo@flit.test", "RL"),
            ],
            []);

        var rows = ProcedureStateChangeEmailEnqueueNotifier.BuildRows(
            ApprovedEvent(), "tramites.aprobado", resolution,
            NullLogger<ProcedureStateChangeEmailEnqueueNotifier>.Instance);

        rows.Should().ContainSingle();
        rows[0].RecipientKind.Should().Be("empresa");
        rows[0].RecipientName.Should().Be("ACME");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string NewDbName() => $"flit-email-enqueue-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ProcedureStateChangeEmailEnqueueNotifier NewSut(string dbName)
    {
        var db = NewContext(dbName);
        return new ProcedureStateChangeEmailEnqueueNotifier(
            db,
            new TramiteNotificationRecipientResolver(),
            NullLogger<ProcedureStateChangeEmailEnqueueNotifier>.Instance);
    }

    private static ProcedureStateChangeEvent ApprovedEvent() => new(
        TenantId, InstanceId, "entregado", "aprobado", DateTimeOffset.UtcNow, OutboxId: OutboxId);

    private static ProcedureStateChangeEvent RejectedEvent() => new(
        TenantId, InstanceId, "entregado", "rechazado", DateTimeOffset.UtcNow, OutboxId: OutboxId);

    private static async Task SeedOutboxAsync(string dbName)
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
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedInstanceAsync(
        string dbName,
        ProcedureInstanceActor actor1,
        ProcedureInstanceActor? actor2 = null,
        string modalidad = "matricula_inicial")
    {
        await using var db = NewContext(dbName);
        var instance = new ProcedureInstance
        {
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "ENQ-1",
            Status = "entregado",
            ModalidadEntrada = modalidad,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        void AddActor(ProcedureInstanceActor actor)
        {
            actor.Id = Guid.NewGuid();
            actor.TenantId = TenantId;
            actor.ProcedureInstanceId = InstanceId;
            actor.ProcedureEntityId = Guid.NewGuid();
            actor.CreatedAt = DateTimeOffset.UtcNow;
            instance.Actors.Add(actor);
        }

        AddActor(actor1);
        if (actor2 is not null)
            AddActor(actor2);

        db.ProcedureInstances.Add(instance);
        await db.SaveChangesAsync(Ct);
    }

    private static ProcedureInstanceActor NaturalComprador(string? email, string name) => new()
    {
        ActorType = "comprador",
        DocumentType = "CC",
        DocumentNumber = "123",
        FullName = name,
        Email = email,
        PersonType = "natural",
        Metadata = "{}",
    };

    private static ProcedureInstanceActor Juridical(string role, string empresaEmail, string rlEmail) => new()
    {
        ActorType = role,
        DocumentType = "NIT",
        DocumentNumber = "900",
        FullName = role == "comprador" ? "Comprador SAS" : "Vendedor SAS",
        Email = empresaEmail,
        PersonType = "juridical",
        Metadata = JsonSerializer.Serialize(new
        {
            representanteLegal = new { NombreCompleto = $"RL {role}", Email = rlEmail },
        }),
    };

    private sealed class RecordingNotifier : IProcedureStateChangeNotifier
    {
        public int Calls { get; private set; }

        public Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEmailSink : IProcedureStateChangeNotifier
    {
        public Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("sink correo roto");
    }
}
