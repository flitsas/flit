using Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;
using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Services;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.ProcedureSnapshots;

/// <summary>
/// Alta de trámite con snapshot documental (HU #10197) — AC1 (snapshot generado y
/// persistido junto al trámite) y AC3 (un fallo al escribir el snapshot revierte toda la
/// operación: 0 trámites). Ejercita handler + repositorio EF reales sobre InMemory.
/// </summary>
public sealed class CreateProcedureInstanceHandlerTests
{
    private static readonly Guid ProcedureTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClienteId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid DocA = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid DocB = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---------- AC1: snapshot generado automáticamente al crear el trámite ----------

    [Fact]
    public async Task AC1_Create_GeneratesAndPersistsSnapshot_WithResolvedMatrix()
    {
        var db = NewDbName();
        await SeedAsync(db);
        await using (var seed = NewContext(db))
        {
            // RF22 — el OT es el único nivel que reordena: override OT sobre DocA → orden 1 nivel
            // OT; DocB queda DEFAULT (20).
            seed.DocumentOrderOverrides.Add(new DocumentOrderOverride
            {
                Id = Guid.NewGuid(),
                ProcedureTypeId = ProcedureTypeId,
                DocumentTypeId = DocA,
                ScopeType = "OT",
                ScopeRefId = TransitOfficeId,
                SortOrder = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CreateProcedureInstanceResult result;
        await using (var act = NewContext(db))
        {
            result = await Handler(act).HandleAsync(new CreateProcedureInstanceCommand
            {
                ActorUserId = Actor,
                Request = new CreateProcedureInstanceRequest(
                    ProcedureTypeId, ClienteId, TransitOfficeId, "TR-2026-0001", null),
            }, TestContext.Current.CancellationToken);
        }

        result.Outcome.Should().Be(CreateProcedureInstanceOutcome.Created);
        result.Response.Should().NotBeNull();
        result.Response!.ReferenceNumber.Should().Be("TR-2026-0001");
        result.Response.Status.Should().Be("borrador");
        result.Response.DocumentCount.Should().Be(2);

        await using var verify = NewContext(db);
        var instance = await verify.ProcedureInstances.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        instance.TenantId.Should().Be(ClienteId);
        instance.ProcedureTypeId.Should().Be(ProcedureTypeId);
        instance.TransitOfficeId.Should().Be(TransitOfficeId);
        instance.CreatedByUserId.Should().Be(Actor);
        instance.Status.Should().Be("borrador");

        var snapshot = await verify.ProcedureDocumentSnapshots.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        snapshot.ProcedureInstanceId.Should().Be(instance.Id);
        snapshot.TenantId.Should().Be(ClienteId);
        snapshot.SnapshotBy.Should().Be(Actor);

        var payload = SnapshotJson.Deserialize(snapshot.Snapshot);
        payload.Should().NotBeNull();
        payload!.ProcedureTypeId.Should().Be(ProcedureTypeId);
        payload.ClienteId.Should().Be(ClienteId);
        payload.Documentos.Should().HaveCount(2);
        payload.Documentos.Single(d => d.DocumentTypeId == DocA).NivelAplicado.Should().Be("OT");
        payload.Documentos.Single(d => d.DocumentTypeId == DocA).OrdenResuelto.Should().Be((short)1);
        payload.Documentos.Single(d => d.DocumentTypeId == DocB).NivelAplicado.Should().Be("DEFAULT");
    }

    [Fact]
    public async Task AC1_Create_ProcedureWithoutDocuments_PersistsSnapshotWithEmptyMatrix()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            seed.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "X", Name = "X", IsActive = true });
            seed.Tenants.Add(new Tenant { Id = ClienteId, Code = "C1", LegalName = "Cliente 1" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var act = NewContext(db);
        var result = await Handler(act).HandleAsync(new CreateProcedureInstanceCommand
        {
            ActorUserId = Actor,
            Request = new CreateProcedureInstanceRequest(ProcedureTypeId, ClienteId, null, "TR-EMPTY", null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureInstanceOutcome.Created);
        result.Response!.DocumentCount.Should().Be(0);

        await using var verify = NewContext(db);
        var snapshot = await verify.ProcedureDocumentSnapshots.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        SnapshotJson.Deserialize(snapshot.Snapshot)!.Documentos.Should().BeEmpty();
    }

    // ---------- Reglas de negocio ----------

    [Fact]
    public async Task Create_NonExistentProcedureType_Returns404_AndPersistsNothing()
    {
        var db = NewDbName();
        await SeedAsync(db);

        await using var act = NewContext(db);
        var result = await Handler(act).HandleAsync(new CreateProcedureInstanceCommand
        {
            ActorUserId = Actor,
            Request = new CreateProcedureInstanceRequest(Guid.NewGuid(), ClienteId, null, "TR-X", null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureInstanceOutcome.ProcedureTypeNotFound);
        (await act.ProcedureInstances.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Create_NonExistentTenant_Returns404()
    {
        var db = NewDbName();
        await SeedAsync(db);

        await using var act = NewContext(db);
        var result = await Handler(act).HandleAsync(new CreateProcedureInstanceCommand
        {
            ActorUserId = Actor,
            Request = new CreateProcedureInstanceRequest(ProcedureTypeId, Guid.NewGuid(), null, "TR-X", null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureInstanceOutcome.TenantNotFound);
    }

    [Fact]
    public async Task Create_DuplicateReferenceNumberForTenant_Returns422()
    {
        var db = NewDbName();
        await SeedAsync(db);
        await using (var seed = NewContext(db))
        {
            seed.ProcedureInstances.Add(new ProcedureInstance
            {
                ProcedureType = ProcedureTypeFixture.Matricula,
                Id = Guid.NewGuid(),
                TenantId = ClienteId,
                ProcedureTypeId = ProcedureTypeId,
                ReferenceNumber = "TR-DUP",
                Status = "borrador",
                CreatedByUserId = Actor,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var act = NewContext(db);
        var result = await Handler(act).HandleAsync(new CreateProcedureInstanceCommand
        {
            ActorUserId = Actor,
            Request = new CreateProcedureInstanceRequest(ProcedureTypeId, ClienteId, null, "TR-DUP", null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureInstanceOutcome.ValidationFailed);
        (await act.ProcedureInstances.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingReferenceNumber_Returns422(string? referenceNumber)
    {
        var db = NewDbName();
        await SeedAsync(db);

        await using var act = NewContext(db);
        var result = await Handler(act).HandleAsync(new CreateProcedureInstanceCommand
        {
            ActorUserId = Actor,
            Request = new CreateProcedureInstanceRequest(ProcedureTypeId, ClienteId, null, referenceNumber, null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureInstanceOutcome.ValidationFailed);
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await act.ProcedureInstances.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Create_WithoutActorOrCreatedBy_Returns422()
    {
        var db = NewDbName();
        await SeedAsync(db);

        await using var act = NewContext(db);
        var result = await Handler(act).HandleAsync(new CreateProcedureInstanceCommand
        {
            ActorUserId = null,
            Request = new CreateProcedureInstanceRequest(ProcedureTypeId, ClienteId, null, "TR-NOUSER", null),
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateProcedureInstanceOutcome.ValidationFailed);
    }

    // ---------- AC3: fallo al generar el snapshot revierte la creación ----------

    [Fact]
    public async Task AC3_SnapshotWriteFailure_RollsBack_NoInstancePersisted()
    {
        var db = NewDbName();
        await SeedAsync(db);

        // Contexto cuyo SaveChangesAsync lanza: simula el timeout/fallo al persistir el
        // snapshot. Como trámite + snapshot se guardan en un único SaveChanges, nada se
        // materializa.
        await using (var failing = NewContext(db, new ThrowingSaveChangesInterceptor()))
        {
            var result = await Handler(failing).HandleAsync(new CreateProcedureInstanceCommand
            {
                ActorUserId = Actor,
                Request = new CreateProcedureInstanceRequest(
                    ProcedureTypeId, ClienteId, TransitOfficeId, "TR-FAIL", null),
            }, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(CreateProcedureInstanceOutcome.Failed);
            result.Error.Should().NotBeNullOrWhiteSpace();
        }

        await using var verify = NewContext(db);
        (await verify.ProcedureInstances.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        (await verify.ProcedureDocumentSnapshots.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // ---------- Helpers ----------

    private static CreateProcedureInstanceHandler Handler(FlitDbContext ctx) =>
        new(
            new ProcedureTypeCatalog(ctx),
            new TenantCatalog(ctx),
            new ResolvedDocumentMatrixResolver(ctx),
            new AdminProcedureInstanceRepository(ctx));

    private static string NewDbName() => $"flit-tramite-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new FlitDbContext(builder.Options);
    }

    /// <summary>Siembra trámite, cliente (tenant), usuario y dos documentos (DocA=10, DocB=20).</summary>
    private static async Task SeedAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        ctx.ProcedureTypes.Add(new ProcedureType { Id = ProcedureTypeId, Code = "TRASPASO", Name = "Traspaso", IsActive = true });
        ctx.Tenants.Add(new Tenant { Id = ClienteId, Code = "C1", LegalName = "Cliente 1" });
        ctx.Users.Add(new User { Id = Actor, Email = "operador@cliente.co", DisplayName = "Operador", Status = "active", CreatedAt = DateTimeOffset.UtcNow });
        ctx.DocumentTypes.AddRange(
            new DocumentType { Id = DocA, Code = "DOCA", Name = "Documento A", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new DocumentType { Id = DocB, Code = "DOCB", Name = "Documento B", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        ctx.ProcedureDocumentRequirements.AddRange(
            new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocA, IsMandatory = true, DefaultSortOrder = 10, CreatedAt = DateTimeOffset.UtcNow },
            new ProcedureDocumentRequirement { Id = Guid.NewGuid(), ProcedureTypeId = ProcedureTypeId, DocumentTypeId = DocB, IsMandatory = false, DefaultSortOrder = 20, CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Interceptor que aborta cualquier <c>SaveChanges</c> para simular el fallo de BD (AC3).</summary>
    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fallo simulado al escribir el snapshot documental.");

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result) =>
            throw new InvalidOperationException("Fallo simulado al escribir el snapshot documental.");
    }
}
