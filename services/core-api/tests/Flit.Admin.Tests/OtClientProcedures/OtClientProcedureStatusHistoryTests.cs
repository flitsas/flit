using System.Text.Json;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #14156 — el GET de detalle OT expone <c>statusHistory</c> con actor (bug #12526) para la
/// línea de tiempo; sin inventar datos; NotFound fuera de alcance; arreglo vacío si no hay filas.
/// </summary>
public sealed class OtClientProcedureStatusHistoryTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid OtherOtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1");
    private static readonly Guid OtherTransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee2");
    private static readonly Guid TipoMatricula = Guid.Parse("11111111-1111-1111-1111-111111111191");
    private static readonly Guid ProcedureId = Guid.Parse("c0000000-0000-4000-8000-000000000101");
    private static readonly Guid ActorUserId = Guid.Parse("d0000000-0000-4000-8000-000000000201");
    private static readonly DateTimeOffset ChangedAt = new(2026, 10, 28, 15, 21, 0, TimeSpan.Zero);

    [Fact]
    public async Task AC1_DetalleOt_ConHistorial_ExponeStatusHistoryYActor()
    {
        var db = await SembrarAsync(withHistory: true, withActor: true);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        result.Procedure!.StatusHistory.Should().HaveCount(1);
        var entry = result.Procedure.StatusHistory[0];
        entry.FromStatus.Should().Be(TramiteEstado.Entregado);
        entry.ToStatus.Should().Be(TramiteEstado.Aprobado);
        entry.ChangedAt.Should().Be(ChangedAt);
        entry.Reason.Should().Be("Aprobado por OT");
        entry.ChangedByName.Should().Be("Jose Manuel Pimienta");
        entry.ChangedByEmail.Should().Be("jose.pimienta@flitsas.com");
        entry.ChangedByCompania.Should().Be("Empresa Demo FLIT");
        entry.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task AC1_DetalleOt_SerializaStatusHistoryEnCamelCase()
    {
        var db = await SembrarAsync(withHistory: true, withActor: true);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Procedure, WebJson));
        var hist = json.RootElement.GetProperty("statusHistory");
        hist.GetArrayLength().Should().Be(1);
        hist[0].GetProperty("toStatus").GetString().Should().Be(TramiteEstado.Aprobado);
        hist[0].GetProperty("changedByName").GetString().Should().Be("Jose Manuel Pimienta");
        hist[0].GetProperty("changedByEmail").GetString().Should().Be("jose.pimienta@flitsas.com");
        hist[0].GetProperty("changedByCompania").GetString().Should().Be("Empresa Demo FLIT");
    }

    [Fact]
    public async Task AC1_DetalleOt_TransicionAutomatica_SinInventarActor()
    {
        var db = await SembrarAsync(withHistory: true, withActor: false);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        var entry = result.Procedure!.StatusHistory.Should().ContainSingle().Subject;
        entry.ChangedByName.Should().BeNull();
        entry.ChangedByEmail.Should().BeNull();
        entry.ChangedByCompania.Should().BeNull();
    }

    [Fact]
    public async Task AC2_DetalleOt_TramiteFueraDeAlcance_NotFoundSinFiltrarHistorialAjeno()
    {
        var db = await SembrarAsync(withHistory: true, withActor: true);

        var result = await DetalleAsync(db, OtherOtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.NotFound);
        result.Procedure.Should().BeNull();
    }

    [Fact]
    public async Task AC3_DetalleOt_SinHistorial_StatusHistoryArregloVacio()
    {
        var db = await SembrarAsync(withHistory: false, withActor: false);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        result.Procedure!.StatusHistory.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task AC3_DetalleOt_SinHistorial_SerializaArregloVacioNoOmiteCampo()
    {
        var db = await SembrarAsync(withHistory: false, withActor: false);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Procedure, WebJson));
        json.RootElement.TryGetProperty("statusHistory", out var hist).Should().BeTrue();
        hist.ValueKind.Should().Be(JsonValueKind.Array);
        hist.GetArrayLength().Should().Be(0);
    }

    private static async Task<GetOtClientProcedureResult> DetalleAsync(
        string db,
        Guid otTenantId,
        Guid procedureId)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        return await new GetOtClientProcedureHandler(repo).HandleAsync(
            new GetOtClientProcedureQuery { OtTenantId = otTenantId, ProcedureInstanceId = procedureId },
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> SembrarAsync(bool withHistory, bool withActor)
    {
        var db = Guid.NewGuid().ToString();
        await using var ctx = NewContext(db);

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        // OT sin grant sobre el trámite: sirve para AC2 (NotFound).
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtherOtTenant,
            TransitOfficeId = OtherTransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = ClientTenant,
            Code = "cli14156",
            LegalName = "Empresa Demo FLIT",
            TaxId = "900000001",
            TenantType = "client",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = TipoMatricula,
            Code = "matricula_inicial",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
            IsActive = true,
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        if (withActor)
        {
            ctx.Users.Add(new User
            {
                Id = ActorUserId,
                Email = "jose.pimienta@flitsas.com",
                DisplayName = "Jose Manuel Pimienta",
                Status = "active",
                HomeTenantId = ClientTenant,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = ProcedureId,
            TenantId = ClientTenant,
            ProcedureTypeId = TipoMatricula,
            ReferenceNumber = "FT1-0000230",
            Consecutivo = 230,
            Status = TramiteEstado.Aprobado,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = withActor ? ActorUserId : Guid.NewGuid(),
            CreatedAt = ChangedAt.AddDays(-1),
            UpdatedAt = ChangedAt,
        });

        if (withHistory)
        {
            ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = ClientTenant,
                ProcedureInstanceId = ProcedureId,
                FromStatus = TramiteEstado.Entregado,
                ToStatus = TramiteEstado.Aprobado,
                ChangedAt = ChangedAt,
                ChangedBy = withActor ? ActorUserId : null,
                Reason = "Aprobado por OT",
                Metadata = "{}",
            });
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
