using FluentAssertions;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// N 03 (RNF01) — outbox de cambios de estado: publisher (encolar en la unidad de trabajo),
/// procesador (despacho exactamente-una-vez, reintentos, dead-letter) y concurrencia optimista
/// (row_version → DbUpdateConcurrencyException sin efectos parciales).
/// </summary>
public sealed class ProcedureStateChangeOutboxTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------
    // Publisher
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnqueueAsync_agrega_exactamente_una_fila_sin_guardar()
    {
        var dbName = NewDbName();
        await using var db = NewContext(dbName);
        var publisher = new ProcedureStateChangeOutboxPublisher(db);

        await publisher.EnqueueAsync(NewRecord(), Ct);

        // La fila queda en el change tracker (misma unidad de trabajo), NO en la base.
        db.ChangeTracker.Entries<ProcedureStateChangeOutbox>().Should().HaveCount(1);
        await using (var verify = NewContext(dbName))
        {
            (await verify.ProcedureStateChangeOutbox.CountAsync(Ct)).Should().Be(0);
        }

        // El commit del caso de uso materializa exactamente una fila pendiente.
        await db.SaveChangesAsync(Ct);
        await using (var verify = NewContext(dbName))
        {
            var rows = await verify.ProcedureStateChangeOutbox.ToListAsync(Ct);
            rows.Should().HaveCount(1);
            var row = rows[0];
            row.TenantId.Should().Be(TenantId);
            row.ProcedureInstanceId.Should().Be(InstanceId);
            row.FromStatus.Should().Be(TramiteEstado.Borrador);
            row.ToStatus.Should().Be(TramiteEstado.Preparado);
            row.Reason.Should().Be("gates ok");
            row.PublishedAt.Should().BeNull();
            row.Attempts.Should().Be(0);
        }
    }

    // ------------------------------------------------------------------
    // Procesador
    // ------------------------------------------------------------------

    [Fact]
    public async Task Procesador_despacha_exactamente_una_vez_y_sella_published_at()
    {
        var dbName = NewDbName();
        await SeedOutboxRowAsync(dbName);
        var notifier = new CountingNotifier();
        var processor = NewProcessor(dbName, notifier);

        await processor.ProcessPendingAsync(Ct);
        // Segundo ciclo: la fila ya está sellada → no debe despachar de nuevo.
        await processor.ProcessPendingAsync(Ct);

        notifier.Events.Should().HaveCount(1);
        var evt = notifier.Events[0];
        evt.TenantId.Should().Be(TenantId);
        evt.ProcedureInstanceId.Should().Be(InstanceId);
        evt.FromStatus.Should().Be(TramiteEstado.Borrador);
        evt.ToStatus.Should().Be(TramiteEstado.Preparado);
        evt.Reason.Should().Be("gates ok");
        evt.OutboxId.Should().NotBe(Guid.Empty);

        await using var verify = NewContext(dbName);
        var row = await verify.ProcedureStateChangeOutbox.SingleAsync(Ct);
        row.PublishedAt.Should().NotBeNull();
        row.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Procesador_ante_fallo_incrementa_attempts_sin_sellar()
    {
        var dbName = NewDbName();
        await SeedOutboxRowAsync(dbName);
        var notifier = new CountingNotifier { Fail = true };
        var processor = NewProcessor(dbName, notifier);

        await processor.ProcessPendingAsync(Ct);

        await using var verify = NewContext(dbName);
        var row = await verify.ProcedureStateChangeOutbox.SingleAsync(Ct);
        row.PublishedAt.Should().BeNull();
        row.Attempts.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Procesador_no_reclama_filas_en_dead_letter()
    {
        var dbName = NewDbName();
        await SeedOutboxRowAsync(dbName, attempts: ProcedureStateChangeOutbox.MaxDeliveryAttempts);
        var notifier = new CountingNotifier();
        var processor = NewProcessor(dbName, notifier);

        await processor.ProcessPendingAsync(Ct);

        notifier.Events.Should().BeEmpty("una fila con los intentos agotados queda en dead-letter");
        await using var verify = NewContext(dbName);
        var row = await verify.ProcedureStateChangeOutbox.SingleAsync(Ct);
        row.PublishedAt.Should().BeNull();
        row.Attempts.Should().Be(ProcedureStateChangeOutbox.MaxDeliveryAttempts);
    }

    // ------------------------------------------------------------------
    // Concurrencia optimista (RNF01) — carrera de dos transiciones
    // ------------------------------------------------------------------

    [Fact]
    public async Task Transiciones_concurrentes_el_perdedor_recibe_conflicto_y_gana_el_primero()
    {
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);

        // Dos "requests" cargan la MISMA versión de la instancia (row_version = 0).
        await using var winner = NewContext(dbName);
        await using var loser = NewContext(dbName);
        var winnerInstance = await winner.ProcedureInstances.SingleAsync(i => i.Id == InstanceId, Ct);
        var loserInstance = await loser.ProcedureInstances.SingleAsync(i => i.Id == InstanceId, Ct);

        // Gana el primero: transiciona en SU unidad de trabajo.
        winnerInstance.Status = "submitted";
        winnerInstance.RowVersion = 1; // simula el trigger tr_procedure_instances_row_version
        await winner.SaveChangesAsync(Ct);

        // El perdedor intenta lo suyo sobre la versión vieja (token original = 0 ≠ 1 en BD)
        // → DbUpdateConcurrencyException, que el lifecycle service traduce a 409
        // conflicto_concurrencia (contrato N 03).
        loserInstance.Status = "cancelled";
        var act = () => loser.SaveChangesAsync(Ct);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Prevalece la transición del ganador.
        await using var verify = NewContext(dbName);
        (await verify.ProcedureInstances.SingleAsync(i => i.Id == InstanceId, Ct)).Status.Should().Be("submitted");
    }

    [Fact]
    public async Task Conflicto_de_concurrencia_no_deja_efectos_parciales()
    {
        // El "sin efectos parciales" de RNF01 se apoya en que la transición completa (status +
        // historial + outbox) va en UN único SaveChanges (una transacción en PostgreSQL). Este test
        // fija ese invariante: el conflicto de row_version aborta el commit y NINGUNA de las filas
        // acompañantes queda persistida — si el publisher o el recorder llamaran SaveChanges por su
        // cuenta, las filas ya estarían en la base y este test fallaría. (El interceptor simula el
        // conflicto en el commit porque el provider InMemory no es transaccional entre entidades.)
        var dbName = NewDbName();
        await SeedInstanceAsync(dbName);

        await using var db = NewContext(dbName, new ConcurrencyConflictInterceptor());
        var instance = await db.ProcedureInstances.SingleAsync(i => i.Id == InstanceId, Ct);
        instance.Status = "cancelled";
        db.ProcedureInstanceStatusHistories.Add(NewHistory("draft", "cancelled"));
        await new ProcedureStateChangeOutboxPublisher(db)
            .EnqueueAsync(NewRecord() with { FromStatus = "draft", ToStatus = "cancelled" }, Ct);

        var act = () => db.SaveChangesAsync(Ct);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Cero efectos: ni cambio de estado, ni historial, ni outbox.
        await using var verify = NewContext(dbName);
        (await verify.ProcedureInstances.SingleAsync(i => i.Id == InstanceId, Ct)).Status.Should().Be("draft");
        (await verify.ProcedureInstanceStatusHistories.CountAsync(Ct)).Should().Be(0);
        (await verify.ProcedureStateChangeOutbox.CountAsync(Ct)).Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string NewDbName() => $"flit-n03-outbox-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName);
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new FlitDbContext(builder.Options);
    }

    private static async Task SeedInstanceAsync(string dbName)
    {
        await using var seed = NewContext(dbName);
        seed.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "N03-RACE-1",
            Status = "draft",
            CreatedByUserId = Guid.NewGuid(),
            RowVersion = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync(Ct);
    }

    /// <summary>Simula el conflicto de row_version EN el commit (antes de escribir fila alguna).</summary>
    private sealed class ConcurrencyConflictInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateConcurrencyException("Conflicto de concurrencia simulado (row_version).");
    }

    private static TramiteTransitionRecord NewRecord() => new(
        TenantId,
        InstanceId,
        TramiteEstado.Borrador,
        TramiteEstado.Preparado,
        "gates ok",
        Guid.NewGuid(),
        DateTimeOffset.UtcNow);

    private static ProcedureInstanceStatusHistory NewHistory(string from, string to) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureInstanceId = InstanceId,
        FromStatus = from,
        ToStatus = to,
        ChangedAt = DateTimeOffset.UtcNow,
    };

    private static async Task SeedOutboxRowAsync(string dbName, int attempts = 0)
    {
        await using var db = NewContext(dbName);
        db.ProcedureStateChangeOutbox.Add(new ProcedureStateChangeOutbox
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            FromStatus = TramiteEstado.Borrador,
            ToStatus = TramiteEstado.Preparado,
            Reason = "gates ok",
            OccurredAt = DateTimeOffset.UtcNow,
            PublishedAt = null,
            Attempts = attempts,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static ProcedureStateChangeOutboxProcessor NewProcessor(string dbName, IProcedureStateChangeNotifier notifier)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddScoped(_ => notifier);
        var provider = services.BuildServiceProvider();
        return new ProcedureStateChangeOutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProcedureStateChangeOutboxProcessor>.Instance);
    }

    private sealed class CountingNotifier : IProcedureStateChangeNotifier
    {
        public List<ProcedureStateChangeEvent> Events { get; } = [];
        public bool Fail { get; set; }

        public Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException("Notificación fallida (simulada).");
            Events.Add(change);
            return Task.CompletedTask;
        }
    }
}
