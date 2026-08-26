using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Quipux.Domain.Consola;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Quipux;

/// <summary>
/// Consola de cola Quipux (HU #10774) sobre <see cref="DbQuipuxSubmissionConsoleRepository"/> con EF
/// InMemory: listado filtrado por secretaría destino + orden desc, y las transiciones manuales
/// (re-encolar un <c>fallido</c>, cancelar un <c>pendiente</c>) con sus guards de estado, pertenencia
/// y unicidad.
/// </summary>
public sealed class DbQuipuxSubmissionConsoleRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TransitOfficeId = Guid.NewGuid();
    private static readonly Guid OtherOfficeId = Guid.NewGuid();
    private static readonly Guid ProcedureTypeId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task SeedCatalogAsync(FlitDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = TenantId, LegalName = "Renting del Café S.A.S." });
        db.ProcedureTypes.Add(new ProcedureType {
        Family = "MATRICULAS", Id = ProcedureTypeId, Name = "Traspaso" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Crea trámite + submission enlazados a la secretaría <paramref name="officeId"/>.</summary>
    private static async Task<Guid> SeedSubmissionAsync(
        FlitDbContext db,
        string status,
        Guid officeId,
        int attempts = 0,
        DateTimeOffset? createdAt = null,
        string reference = "TRM-2026-000001")
    {
        var procedureId = Guid.NewGuid();
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = procedureId,
            TenantId = TenantId,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = reference,
            TransitOfficeId = officeId,
            CreatedAt = createdAt ?? Base,
        });

        var submissionId = Guid.NewGuid();
        db.QuipuxSubmissions.Add(new QuipuxSubmission
        {
            Id = submissionId,
            TenantId = TenantId,
            ProcedureInstanceId = procedureId,
            DocumentName = $"FLIT_{reference}",
            AttachmentId = Guid.NewGuid(),
            QuipuxProcedureType = 16,
            QuipuxRequirementType = 51,
            DivipoCode = "05001",
            Status = status,
            Attempts = attempts,
            CreatedAt = createdAt ?? Base,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return submissionId;
    }

    [Fact]
    public async Task List_FiltersByTransitOffice_AndOrdersNewestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(List_FiltersByTransitOffice_AndOrdersNewestFirst));
        await SeedCatalogAsync(db);
        await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Pendiente, TransitOfficeId,
            createdAt: Base, reference: "TRM-A");
        await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Aprobado, TransitOfficeId,
            createdAt: Base.AddHours(2), reference: "TRM-B");
        // Otra secretaría: no debe aparecer.
        await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Pendiente, OtherOfficeId,
            reference: "TRM-OTHER");

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var page = await repo.ListByTransitOfficeAsync(TransitOfficeId, 1, 20, ct);

        page.TotalCount.Should().Be(2);
        page.Items.Should().HaveCount(2);
        page.Items[0].ReferenceNumber.Should().Be("TRM-B"); // más nuevo primero
        page.Items[1].ReferenceNumber.Should().Be("TRM-A");
        page.Items[0].ProcedureTypeName.Should().Be("Traspaso");
        page.Items[0].ClientTenantName.Should().Be("Renting del Café S.A.S.");
    }

    [Fact]
    public async Task List_Paginates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(List_Paginates));
        await SeedCatalogAsync(db);
        for (var i = 0; i < 3; i++)
        {
            await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Pendiente, TransitOfficeId,
                createdAt: Base.AddMinutes(i), reference: $"TRM-{i}");
        }

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var page = await repo.ListByTransitOfficeAsync(TransitOfficeId, 2, 2, ct);

        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(1); // segunda página con pageSize 2 → 1 elemento
    }

    [Fact]
    public async Task Retry_FromFallido_ReturnsOk_AndResetsToPendiente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Retry_FromFallido_ReturnsOk_AndResetsToPendiente));
        await SeedCatalogAsync(db);
        var id = await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Fallido, TransitOfficeId, attempts: 5);

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var result = await repo.RetryAsync(id, TransitOfficeId, ct);

        result.Status.Should().Be(QuipuxManualOpStatus.Ok);
        result.PreviousStatus.Should().Be(QuipuxSubmissionEstado.Fallido);
        result.PreviousAttempts.Should().Be(5);

        var row = await db.QuipuxSubmissions.AsNoTracking().FirstAsync(s => s.Id == id, ct);
        row.Status.Should().Be(QuipuxSubmissionEstado.Pendiente);
        row.Attempts.Should().Be(0);
        row.ClaimedAt.Should().BeNull();
        row.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task Retry_WhenNotFallido_ReturnsInvalidState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Retry_WhenNotFallido_ReturnsInvalidState));
        await SeedCatalogAsync(db);
        var id = await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Registrado, TransitOfficeId);

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var result = await repo.RetryAsync(id, TransitOfficeId, ct);

        result.Status.Should().Be(QuipuxManualOpStatus.InvalidState);
    }

    [Fact]
    public async Task Retry_WhenAnotherActiveExists_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Retry_WhenAnotherActiveExists_ReturnsConflict));
        await SeedCatalogAsync(db);

        // Un trámite con una submission fallida y OTRA activa (pendiente) sobre el mismo trámite.
        var procedureId = Guid.NewGuid();
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = procedureId,
            TenantId = TenantId,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = "TRM-DUP",
            TransitOfficeId = TransitOfficeId,
            CreatedAt = Base,
        });
        var fallidoId = Guid.NewGuid();
        db.QuipuxSubmissions.Add(new QuipuxSubmission
        {
            Id = fallidoId,
            TenantId = TenantId,
            ProcedureInstanceId = procedureId,
            DocumentName = "FLIT_TRM-DUP_old",
            AttachmentId = Guid.NewGuid(),
            QuipuxProcedureType = 16,
            QuipuxRequirementType = 51,
            DivipoCode = "05001",
            Status = QuipuxSubmissionEstado.Fallido,
            Attempts = 5,
            CreatedAt = Base,
        });
        db.QuipuxSubmissions.Add(new QuipuxSubmission
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = procedureId,
            DocumentName = "FLIT_TRM-DUP_new",
            AttachmentId = Guid.NewGuid(),
            QuipuxProcedureType = 16,
            QuipuxRequirementType = 51,
            DivipoCode = "05001",
            Status = QuipuxSubmissionEstado.Pendiente,
            Attempts = 0,
            CreatedAt = Base.AddMinutes(5),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var result = await repo.RetryAsync(fallidoId, TransitOfficeId, ct);

        result.Status.Should().Be(QuipuxManualOpStatus.Conflict);
    }

    [Fact]
    public async Task Retry_WhenSubmissionBelongsToAnotherOffice_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Retry_WhenSubmissionBelongsToAnotherOffice_ReturnsNotFound));
        await SeedCatalogAsync(db);
        var id = await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Fallido, OtherOfficeId);

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var result = await repo.RetryAsync(id, TransitOfficeId, ct);

        result.Status.Should().Be(QuipuxManualOpStatus.NotFound);
    }

    [Fact]
    public async Task Cancel_FromPendiente_ReturnsOk_AndMovesToFallido()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Cancel_FromPendiente_ReturnsOk_AndMovesToFallido));
        await SeedCatalogAsync(db);
        var id = await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Pendiente, TransitOfficeId);

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var result = await repo.CancelAsync(id, TransitOfficeId, ct);

        result.Status.Should().Be(QuipuxManualOpStatus.Ok);
        result.PreviousStatus.Should().Be(QuipuxSubmissionEstado.Pendiente);

        var row = await db.QuipuxSubmissions.AsNoTracking().FirstAsync(s => s.Id == id, ct);
        row.Status.Should().Be(QuipuxSubmissionEstado.Fallido);
        row.CompletedAt.Should().NotBeNull();
        row.RejectionReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Cancel_WhenNotPendiente_ReturnsInvalidState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Cancel_WhenNotPendiente_ReturnsInvalidState));
        await SeedCatalogAsync(db);
        var id = await SeedSubmissionAsync(db, QuipuxSubmissionEstado.Registrado, TransitOfficeId);

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var result = await repo.CancelAsync(id, TransitOfficeId, ct);

        result.Status.Should().Be(QuipuxManualOpStatus.InvalidState);
    }

    [Fact]
    public async Task Cancel_MissingSubmission_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Cancel_MissingSubmission_ReturnsNotFound));
        await SeedCatalogAsync(db);

        var repo = new DbQuipuxSubmissionConsoleRepository(db);
        var result = await repo.CancelAsync(Guid.NewGuid(), TransitOfficeId, ct);

        result.Status.Should().Be(QuipuxManualOpStatus.NotFound);
    }
}
