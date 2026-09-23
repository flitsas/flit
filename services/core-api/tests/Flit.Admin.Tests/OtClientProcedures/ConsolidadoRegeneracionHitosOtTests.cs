using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #12796 (Épica #12760, D1) — hitos de regeneración anticipada del lado del organismo
/// (<see cref="OtClientProcedureRepository"/>): rechazar u observar un expediente ENTREGADO encola los dos
/// consolidados (AC2); asignar la placa en preasignación encola los dos (AC3); aprobar no encola (AC4); la
/// edición de campos invalida pero no encola (AC5). El encolado va DESPUÉS del commit del scope cliente y
/// lleva el tenant CLIENTE dueño del trámite, nunca el del OT.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var queue = new RecordingOtRegeneracionQueue();
/// var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), plateRepo, queue);
/// await repo.RejectAsync(otTenant, procedureId, "motivo", approver, OtTransitionSource.OtAdmin);
/// // queue.Solicitudes == [(clientTenant, procedureId, Wizard), (clientTenant, procedureId, Maestro)]
/// </code>
/// </summary>
public sealed class ConsolidadoRegeneracionHitosOtTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Approver = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<(string Db, Guid ProcedureId)> SeedAsync(string status, string? plate = null)
    {
        var db = Guid.NewGuid().ToString();
        var procedureId = Guid.NewGuid();
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
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = ProcedureType,
            Code = "MATRICULA_NUEVA",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
            // ADR-0059 — la ruta de placa (preasignacion/asignado) solo existe para tipos que piden placa.
            GateProfile = """{"entryMode":"VIN","requiresBuyer":true,"requiresPlateRequest":true}""",
            IsActive = true,
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = procedureId,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = "REF-12796",
            Status = status,
            Plate = plate,
            TransitOfficeId = TransitOffice,
            CreatedAt = DateTimeOffset.UtcNow,
            ConsolidadoMaestroVigente = true,
            ConsolidadoWizardVigente = true,
        });
        await ctx.SaveChangesAsync(Ct);
        return (db, procedureId);
    }

    private static IPlateRangeRepository PlateRepoQueReserva(bool reserva = true)
    {
        var plates = Substitute.For<IPlateRangeRepository>();
        plates.TryReservePlateAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(reserva);
        return plates;
    }

    private static OtClientProcedureRepository Repo(
        FlitDbContext ctx, IConsolidadoRegeneracionQueue? queue, IPlateRangeRepository? plates = null) =>
        new(ctx, new NullTramiteTransitionPublisher(), plates, queue);

    private static (Guid, Guid, TipoConsolidado)[] Ambos(Guid procedureId) =>
    [
        (ClientTenant, procedureId, TipoConsolidado.Wizard),
        (ClientTenant, procedureId, TipoConsolidado.Maestro),
    ];

    // ── AC2 — Decisión del OT ───────────────────────────────────────────────────────

    [Fact] // AC2 happy path — rechazar un entregado encola wizard y maestro con el tenant CLIENTE.
    public async Task AC2_RechazarEntregado_EncolaLosDosConElTenantCliente()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Entregado);
        var queue = new RecordingOtRegeneracionQueue();
        await using var ctx = NewContext(db);

        var updated = await Repo(ctx, queue).RejectAsync(
            OtTenant, procedureId, "Documentación incompleta", Approver, OtTransitionSource.OtAdmin,
            cancellationToken: Ct);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(TramiteEstado.Rechazado);
        queue.Solicitudes.Should().BeEquivalentTo(Ambos(procedureId));
        queue.Solicitudes.Should().NotContain(s => s.TenantId == OtTenant, "el tenant es el dueño del trámite");
    }

    [Fact] // AC2 — observar (rechazo con checklist subsanable) también encola los dos.
    public async Task AC2_ObservarEntregado_EncolaLosDos()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Entregado);
        var queue = new RecordingOtRegeneracionQueue();
        await using var ctx = NewContext(db);

        var updated = await Repo(ctx, queue).ObserveAsync(
            OtTenant, procedureId, "Corregir VIN",
            [new OtProcedureObservationItem { Campo = "vin", Detalle = "No coincide con la factura" }],
            Approver, OtTransitionSource.OtAdmin, cancellationToken: Ct);

        updated.Should().NotBeNull();
        queue.Solicitudes.Should().BeEquivalentTo(Ambos(procedureId));
    }

    [Fact] // AC2 edge — rechazar desde la cola de placa (preasignacion) no es un expediente entregado.
    public async Task AC2_RechazarDesdePreasignacion_NoEncola()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Preasignacion);
        var queue = new RecordingOtRegeneracionQueue();
        await using var ctx = NewContext(db);

        var updated = await Repo(ctx, queue).RejectAsync(
            OtTenant, procedureId, "Sin placa disponible", Approver, OtTransitionSource.OtAdmin,
            cancellationToken: Ct);

        updated.Should().NotBeNull();
        queue.Solicitudes.Should().BeEmpty();
    }

    [Fact] // AC2 edge — una decisión que no aplica (arista inválida) no encola.
    public async Task AC2_DecisionInvalida_NoEncola()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Borrador);
        var queue = new RecordingOtRegeneracionQueue();
        await using var ctx = NewContext(db);

        var updated = await Repo(ctx, queue).RejectAsync(
            OtTenant, procedureId, "x", Approver, OtTransitionSource.OtAdmin, cancellationToken: Ct);

        updated.Should().BeNull();
        queue.Solicitudes.Should().BeEmpty();
    }

    [Fact] // AC2 contrato — si la cola descarta (false) la decisión igual queda confirmada.
    public async Task AC2_ColaDescarta_LaDecisionNoFalla()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Entregado);
        var queue = new RecordingOtRegeneracionQueue { Acepta = false };
        await using (var ctx = NewContext(db))
        {
            var updated = await Repo(ctx, queue).RejectAsync(
                OtTenant, procedureId, "motivo", Approver, OtTransitionSource.OtAdmin, cancellationToken: Ct);
            updated.Should().NotBeNull();
        }

        await using var verify = NewContext(db);
        var p = await verify.ProcedureInstances.AsNoTracking().SingleAsync(x => x.Id == procedureId, Ct);
        p.Status.Should().Be(TramiteEstado.Rechazado);
        p.ConsolidadoWizardVigente.Should().BeFalse("la invalidación perezosa sigue intacta");
        queue.Solicitudes.Should().HaveCount(2);
    }

    // ── AC3 — Asignación de placa ───────────────────────────────────────────────────

    [Fact] // AC3 happy path — asignar la placa en preasignación encola los dos, tenant cliente.
    public async Task AC3_AsignarPlaca_EncolaLosDos()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Preasignacion);
        var queue = new RecordingOtRegeneracionQueue();
        await using var ctx = NewContext(db);

        var outcome = await Repo(ctx, queue, PlateRepoQueReserva()).AssignPlateAsync(
            OtTenant, procedureId, "ABC123", Approver, "ot_console", cancellationToken: Ct);

        outcome.Succeeded.Should().BeTrue(outcome.Failure + " " + outcome.Detail);
        outcome.Procedure!.Status.Should().Be(TramiteEstado.Asignado);
        queue.Solicitudes.Should().BeEquivalentTo(Ambos(procedureId));
    }

    [Fact] // AC3 edge — placa no disponible: la asignación falla y no encola nada.
    public async Task AC3_PlacaNoDisponible_NoEncola()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Preasignacion);
        var queue = new RecordingOtRegeneracionQueue();
        await using var ctx = NewContext(db);

        var outcome = await Repo(ctx, queue, PlateRepoQueReserva(reserva: false)).AssignPlateAsync(
            OtTenant, procedureId, "ABC123", Approver, "ot_console", cancellationToken: Ct);

        outcome.Succeeded.Should().BeFalse();
        queue.Solicitudes.Should().BeEmpty();
    }

    [Fact] // AC3 edge — fuera de preasignación no hay hito de asignación.
    public async Task AC3_FueraDePreasignacion_NoEncola()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Entregado, plate: "XYZ987");
        var queue = new RecordingOtRegeneracionQueue();
        await using var ctx = NewContext(db);

        var outcome = await Repo(ctx, queue, PlateRepoQueReserva()).AssignPlateAsync(
            OtTenant, procedureId, "ABC123", Approver, "ot_console", cancellationToken: Ct);

        outcome.Failure.Should().Be(PlateAssignmentFailure.NotPreassigned);
        queue.Solicitudes.Should().BeEmpty();
    }

    // ── AC4 — Aprobación no anticipa ────────────────────────────────────────────────

    [Fact] // AC4 — aprobar invalida como siempre pero no encola nada.
    public async Task AC4_Aprobar_NoEncola()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Entregado);
        var queue = new RecordingOtRegeneracionQueue();
        await using (var ctx = NewContext(db))
        {
            var updated = await Repo(ctx, queue).ApproveAsync(
                OtTenant, procedureId, Approver, OtTransitionSource.OtAdmin, cancellationToken: Ct);
            updated!.Status.Should().Be(TramiteEstado.Aprobado);
        }

        await using var verify = NewContext(db);
        var p = await verify.ProcedureInstances.AsNoTracking().SingleAsync(x => x.Id == procedureId, Ct);
        p.ConsolidadoMaestroVigente.Should().BeFalse();
        queue.Solicitudes.Should().BeEmpty();
    }

    // ── AC5 — Edición no anticipa ───────────────────────────────────────────────────

    [Fact] // AC5 — ediciones sucesivas de field_values invalidan (tracker del DbContext) sin encolar.
    public async Task AC5_EdicionesSucesivas_InvalidanSinEncolar()
    {
        var (db, procedureId) = await SeedAsync(TramiteEstado.Borrador);
        var queue = new RecordingOtRegeneracionQueue();

        foreach (var vin in new[] { "1HGCM82633A004352", "1HGCM82633A004353" })
        {
            await using (var ctx = NewContext(db))
            {
                // Repositorio OT vivo en el mismo scope con la cola cableada, como en producción.
                _ = Repo(ctx, queue);
                var campo = new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = ClientTenant,
                    ProcedureInstanceId = procedureId,
                    FieldKey = "vin",
                    ValueText = vin,
                    Source = "user",
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                ctx.Add(campo);
                await ctx.SaveChangesAsync(Ct);
            }

            await using var verify = NewContext(db);
            var p = await verify.ProcedureInstances.AsNoTracking().SingleAsync(x => x.Id == procedureId, Ct);
            p.ConsolidadoWizardVigente.Should().BeFalse();
            p.ConsolidadoMaestroVigente.Should().BeFalse();

            // Se vuelve a marcar vigente para que la segunda edición también tenga algo que invalidar.
            await using var reset = NewContext(db);
            var r = await reset.ProcedureInstances.SingleAsync(x => x.Id == procedureId, Ct);
            r.ConsolidadoWizardVigente = true;
            r.ConsolidadoMaestroVigente = true;
            await reset.SaveChangesAsync(Ct);
        }

        queue.Solicitudes.Should().BeEmpty();
    }
}

/// <summary>Doble de <see cref="IConsolidadoRegeneracionQueue"/> (HU #12796): registra y responde <see cref="Acepta"/>.</summary>
internal sealed class RecordingOtRegeneracionQueue : IConsolidadoRegeneracionQueue
{
    public List<(Guid TenantId, Guid ProcedureInstanceId, TipoConsolidado Documento)> Solicitudes { get; } = [];

    public bool Acepta { get; init; } = true;

    public bool Encolar(Guid tenantId, Guid procedureInstanceId, TipoConsolidado documento)
    {
        Solicitudes.Add((tenantId, procedureInstanceId, documento));
        return Acepta;
    }
}
