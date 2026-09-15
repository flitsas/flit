using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.UseCases.RevocationRequests;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.RevocationRequests;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #12578 (Feature #12565) — «Listar solicitudes de revocatoria» del lado OT: bandeja dedicada
/// "Revocatorias" para el perfil operativo del organismo de tránsito (AC1: filtros de fecha/OT/estado;
/// AC2 de autorización lo resuelve la policy del endpoint, no este handler — mismo criterio que
/// <see cref="DecideRevocationRequestHandlerTests"/>, HU #12576).
///
/// <para>
/// EF Core InMemory con los repositorios REALES de Infrastructure (mismo patrón que
/// <see cref="DecideRevocationRequestHandlerTests"/>): ejercita también el JOIN/WHERE SQL real de
/// <c>ProcedureRevocationRequestRepository.ListForTransitOfficeAsync</c>, no solo el wiring del handler.
/// </para>
///
/// <para>
/// Uso de ejemplo:
/// <code>
/// var result = await Handler(ctx).HandleAsync(new ListOtRevocationRequestsQuery(
///     otTenantId, transitOfficeId: null, statuses: null, from: null, to: null, skip: null, take: null));
/// </code>
/// </para>
/// </summary>
public sealed class ListOtRevocationRequestsHandlerTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-bbbb-aaaa-bbbb-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-aaaa-bbbb-aaaa-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-ffff-eeee-ffff-eeeeeeeeeeee");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-2222-1111-2222-111111111111");
    private static readonly Guid Requester = Guid.Parse("33333333-4444-3333-4444-333333333333");

    // ── AC1 — filtro por estado ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_FiltraPorEstado_SoloDevuelveLasSolicitudesConEseSubEstado()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedTransitOffice(seed);
            SeedProcedure(seed, procedureId);
            SeedRevocationRequest(seed, Guid.NewGuid(), procedureId, ProcedureRevocationRequestStatus.Rechazada, attemptNumber: 1, requestedAt: DateTimeOffset.UtcNow.AddDays(-2));
            SeedRevocationRequest(seed, Guid.NewGuid(), procedureId, ProcedureRevocationRequestStatus.Solicitada, attemptNumber: 2, requestedAt: DateTimeOffset.UtcNow.AddDays(-1));
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new ListOtRevocationRequestsQuery(
            OtTenant, TransitOfficeId: null, Statuses: [ProcedureRevocationRequestStatus.Solicitada],
            RequestedFrom: null, RequestedTo: null, Skip: null, Take: null),
            TestContext.Current.CancellationToken);

        result.Total.Should().Be(1);
        result.Items.Single().Status.Should().Be(ProcedureRevocationRequestStatus.Solicitada);
    }

    // ── AC1 — filtro por rango de fecha ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_FiltraPorRangoDeFecha_ExcluyeLoFueraDelRango()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedTransitOffice(seed);
            SeedProcedure(seed, procedureId);
            SeedRevocationRequest(seed, Guid.NewGuid(), procedureId, ProcedureRevocationRequestStatus.Solicitada, attemptNumber: 1, requestedAt: DateTimeOffset.UtcNow.AddDays(-20));
            SeedRevocationRequest(seed, Guid.NewGuid(), procedureId, ProcedureRevocationRequestStatus.EnRevision, attemptNumber: 2, requestedAt: DateTimeOffset.UtcNow.AddDays(-1));
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new ListOtRevocationRequestsQuery(
            OtTenant, TransitOfficeId: null, Statuses: null,
            RequestedFrom: DateTimeOffset.UtcNow.AddDays(-5), RequestedTo: DateTimeOffset.UtcNow, Skip: null, Take: null),
            TestContext.Current.CancellationToken);

        result.Total.Should().Be(1);
        result.Items.Single().Status.Should().Be(ProcedureRevocationRequestStatus.EnRevision);
    }

    // ── Fila enriquecida — trae radicado/placa/organismo sin segunda consulta ────────────────────

    [Fact]
    public async Task HandleAsync_LaFilaTraeRadicadoPlacaYNombreDelOrganismo()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedTransitOffice(seed);
            SeedProcedure(seed, procedureId, plate: "XYZ987");
            SeedRevocationRequest(seed, Guid.NewGuid(), procedureId, ProcedureRevocationRequestStatus.Solicitada, attemptNumber: 1, requestedAt: DateTimeOffset.UtcNow);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new ListOtRevocationRequestsQuery(
            OtTenant, TransitOfficeId: null, Statuses: null, RequestedFrom: null, RequestedTo: null, Skip: null, Take: null),
            TestContext.Current.CancellationToken);

        var row = result.Items.Single();
        row.ProcedureInstanceId.Should().Be(procedureId);
        row.ReferenceNumber.Should().Be("REF-001");
        row.Placa.Should().Be("XYZ987");
        row.TransitOfficeId.Should().Be(TransitOffice);
        row.TransitOfficeName.Should().Be("OT Test");
    }

    // ── Sin organismo resoluble — degrada a página vacía, no error ───────────────────────────────

    [Fact]
    public async Task HandleAsync_SinPerfilOt_DevuelvePaginaVaciaSinLanzar()
    {
        var db = NewDbName();

        // Sin SeedOt: el tenant OT no tiene perfil configurado, ResolveTransitOfficeIdAsync no resuelve.
        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new ListOtRevocationRequestsQuery(
            OtTenant, TransitOfficeId: null, Statuses: null, RequestedFrom: null, RequestedTo: null, Skip: null, Take: null),
            TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    // ── Paginación — normalización compartida con el lado gestor ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_TakeFueraDeRango_CaeAlTope()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedOt(seed);
            SeedTransitOffice(seed);
            SeedProcedure(seed, procedureId);
            SeedRevocationRequest(seed, Guid.NewGuid(), procedureId, ProcedureRevocationRequestStatus.Solicitada, attemptNumber: 1, requestedAt: DateTimeOffset.UtcNow);
        }

        await using var ctx = NewContext(db);
        var result = await Handler(ctx).HandleAsync(new ListOtRevocationRequestsQuery(
            OtTenant, TransitOfficeId: null, Statuses: null, RequestedFrom: null, RequestedTo: null, Skip: null, Take: 9999),
            TestContext.Current.CancellationToken);

        result.Take.Should().Be(RevocationRequestListPaging.MaxTake);
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────────────────────────

    private static ListOtRevocationRequestsHandler Handler(FlitDbContext ctx)
    {
        var otRepo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var revocationRepo = new ProcedureRevocationRequestRepository(ctx);
        return new ListOtRevocationRequestsHandler(otRepo, revocationRepo);
    }

    private static void SeedOt(FlitDbContext ctx)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedTransitOffice(FlitDbContext ctx)
    {
        ctx.TransitOffices.Add(new TransitOffice
        {
            Id = TransitOffice,
            Code = "OT-TEST",
            Name = "OT Test",
            DepartmentCode = "11",
            CityCode = "11001",
            IsActive = true,
        });
        ctx.SaveChanges();
    }

    private static void SeedProcedure(FlitDbContext ctx, Guid id, string? plate = null)
    {
        if (!ctx.ProcedureTypes.Local.Any(pt => pt.Id == ProcedureType) && !ctx.ProcedureTypes.Any(pt => pt.Id == ProcedureType))
        {
            ctx.ProcedureTypes.Add(new ProcedureType
            {
                Id = ProcedureType,
                Code = "MATRICULA_NUEVA",
                Name = "Matrícula inicial",
                Family = "MATRICULAS",
                IsActive = true,
                PublicationStatus = Flit.Tramites.Domain.Enums.PublicationStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = "REF-001",
            Status = TramiteEstado.Aprobado,
            Plate = plate,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Requester,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedRevocationRequest(
        FlitDbContext ctx, Guid id, Guid procedureInstanceId, string status, int attemptNumber, DateTimeOffset requestedAt)
    {
        ctx.ProcedureRevocationRequests.Add(new Flit.Tramites.Domain.RevocationRequests.ProcedureRevocationRequest
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureInstanceId = procedureInstanceId,
            AttemptNumber = attemptNumber,
            Status = status,
            Reason = "El trámite se aprobó con datos incorrectos.",
            RequestedBy = Requester,
            RequestedAt = requestedAt,
        });
        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
