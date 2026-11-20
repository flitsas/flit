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

    // ── HU #14173 — modificaciones de datos campo a campo ──────────────────────────────────────

    [Fact] // Test de mapeo: cuando hay una modificación escrita en borrador, aparece en Modificaciones
    public async Task AC4_DetalleOt_ConModificacionDatos_ExponeModificaciones()
    {
        // Seeded with a dato_modificado event (written during borrador phase, before submittedAt)
        var db = await SembrarAsync(withHistory: false, withActor: true, withModificacion: true);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        result.Procedure!.Modificaciones.Should().HaveCount(1);
        var mod = result.Procedure.Modificaciones[0];
        mod.Etiqueta.Should().Be("Nombre propietario");
        mod.Campo.Should().Be("nombre_propietario");
        mod.Anterior.Should().Be("Juan Perez");
        mod.Nuevo.Should().Be("María García");
        // El tiempo se conserva tal como se guardó (UTC); no se convierte a America/Bogota.
        mod.ChangedAt.Should().Be(ChangedAt.AddDays(-1));
        mod.ActorNombre.Should().Be("Jose Manuel Pimienta");
        mod.ActorCompania.Should().Be("Empresa Demo FLIT");
    }

    [Fact] // Sin modificaciones → arreglo vacío (no null)
    public async Task AC4_DetalleOt_SinModificaciones_ArregloVacio()
    {
        var db = await SembrarAsync(withHistory: false, withActor: false);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        result.Procedure!.Modificaciones.Should().NotBeNull().And.BeEmpty();
    }

    // ── HU #14176 — cambios de estado visibles en Modificaciones (detalle OT) ─────────────────

    [Fact] // AC1: una transición de status history aparece en Modificaciones con Etiqueta="Estado"
    public async Task HU14176_AC1_TransicionEstado_ApareceEnModificaciones()
    {
        var db = await SembrarAsync(withHistory: true, withActor: true);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        // StatusHistory (panel de estados) sigue con su fila
        result.Procedure!.StatusHistory.Should().ContainSingle();
        // Modificaciones ahora incluye también la transición de estado
        var estadoMod = result.Procedure.Modificaciones
            .Should().Contain(m => m.Campo == "estado").Subject;
        estadoMod.Etiqueta.Should().Be("Estado");
        estadoMod.Anterior.Should().Be(TramiteEstado.Entregado);
        estadoMod.Nuevo.Should().Be(TramiteEstado.Aprobado);
        estadoMod.ChangedAt.Should().Be(ChangedAt);
        estadoMod.ActorNombre.Should().Be("Jose Manuel Pimienta");
        estadoMod.ActorCompania.Should().Be("Empresa Demo FLIT");
    }

    [Fact] // AC2: el cambio de estado aparece UNA sola vez en Modificaciones aunque Items también tenga la fila
    public async Task HU14176_AC2_CambioEstado_ApareceUnaVezEnModificaciones_AunqueItemsTambienLoTiene()
    {
        // withHistory=true → una fila en StatusHistory (panel de estados) Y proyectada en Modificaciones
        var db = await SembrarAsync(withHistory: true, withActor: true);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        // Items (panel de estados) tiene la fila
        result.Procedure!.StatusHistory.Should().ContainSingle();
        // Modificaciones tiene exactamente UNA entrada de estado (no duplicada)
        result.Procedure.Modificaciones
            .Where(m => m.Campo == "estado")
            .Should().ContainSingle("la proyección de status history no debe duplicarse en Modificaciones");
    }

    [Fact] // AC2: dato_modificado con campo "estado" y mismo anterior/nuevo no produce duplicado
    public async Task HU14176_AC2_DatoModificadoEstadoDuplicado_ApareceUnaVezEnModificaciones()
    {
        var db = await SembrarConEstadoDuplicadoAsync();

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        // EXACTAMENTE una entrada con campo "estado" en Modificaciones (el dato_modificado se descartó)
        result.Procedure!.Modificaciones
            .Where(m => m.Campo == "estado")
            .Should().ContainSingle();
    }

    // ── HU #14180 — actos ya guardados en la misma línea de Modificaciones (detalle OT) ────────

    [Fact] // AC1: reasignación, reenvío y revocatoria se ven en Modificaciones con su etiqueta
    public async Task HU14180_AC1_ActosYaGuardados_AparecenEnModificacionesConEtiqueta()
    {
        var db = await SembrarConEventosAsync(
        [
            Evento("reasignar_gestor_admin", ChangedAt, """{"previous_assigned_to_user_id":"11111111-1111-1111-1111-111111111111","new_assigned_to_user_id":"22222222-2222-2222-2222-222222222222"}"""),
            Evento("reenvio_validacion_admin", ChangedAt.AddMinutes(-1), """{"party_role":"propietario","correo_anterior":"viejo@flit.io","correo_destino":"nuevo@flit.io","email_actualizado":true}"""),
            Evento("revocatoria_solicitada", ChangedAt.AddMinutes(-2), """{"attempt_number":1,"reason":"El propietario desistió"}"""),
            Evento("revocatoria_aprobada", ChangedAt.AddMinutes(-3), """{"attempt_number":1,"decision_reason":"Procede la revocatoria"}"""),
            Evento("revocatoria_rechazada", ChangedAt.AddMinutes(-4), """{"attempt_number":2,"decision_reason":null}"""),
        ]);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        result.Procedure!.StatusHistory.Should().BeEmpty();
        var mods = result.Procedure.Modificaciones;
        mods.Should().HaveCount(5);

        var reasignar = mods.Should().ContainSingle(m => m.Campo == "reasignacion_gestor").Subject;
        reasignar.Etiqueta.Should().Be("Reasignación de gestor");
        reasignar.Anterior.Should().BeEmpty();
        reasignar.Nuevo.Should().Be("Reasignación de gestor");
        reasignar.ChangedAt.Should().Be(ChangedAt);
        reasignar.ChangedAt.Offset.Should().Be(TimeSpan.Zero);
        reasignar.ActorNombre.Should().Be("Jose Manuel Pimienta");

        var reenvio = mods.Should().ContainSingle(m => m.Campo == "reenvio_validacion").Subject;
        reenvio.Etiqueta.Should().Be("Reenvío de validación");
        reenvio.Anterior.Should().BeEmpty();
        reenvio.Nuevo.Should().Be("Reenvío de validación");
        reenvio.Nuevo.Should().NotContain("@");
        reenvio.Anterior.Should().NotContain("@");

        var solicitada = mods.Should().ContainSingle(m => m.Etiqueta == "Solicitud de revocatoria").Subject;
        solicitada.Campo.Should().Be("revocatoria");
        solicitada.Anterior.Should().BeEmpty();
        solicitada.Nuevo.Should().Be("El propietario desistió");

        var aprobada = mods.Should().ContainSingle(m => m.Etiqueta == "Revocatoria aprobada").Subject;
        aprobada.Campo.Should().Be("revocatoria");
        aprobada.Nuevo.Should().Be("Procede la revocatoria");

        var rechazada = mods.Should().ContainSingle(m => m.Etiqueta == "Revocatoria rechazada").Subject;
        rechazada.Campo.Should().Be("revocatoria");
        rechazada.Anterior.Should().BeEmpty();
        rechazada.Nuevo.Should().Be("Revocatoria rechazada");
    }

    [Fact] // AC1 actor: consulta_runt e identidad sin usuario; otro campo no inventa nombre
    public async Task HU14180_AC1_DatoModificadoSinUsuario_ActorDeSistemaSoloEnRuntEIdentidad()
    {
        var db = await SembrarConEventosAsync(
        [
            Evento("dato_modificado", ChangedAt, """{"etiqueta":"Consulta RUNT","campo":"consulta_runt","anterior":"","nuevo":"Sin alertas"}""", sinUsuario: true),
            Evento("dato_modificado", ChangedAt.AddMinutes(-1), """{"etiqueta":"Identidad","campo":"identidad","anterior":"","nuevo":"Validada"}""", sinUsuario: true),
            Evento("dato_modificado", ChangedAt.AddMinutes(-2), """{"etiqueta":"Placa","campo":"placa","anterior":"ABC","nuevo":"XYZ"}""", sinUsuario: true),
            Evento("dato_modificado", ChangedAt.AddMinutes(-3), """{"etiqueta":"Consulta RUNT","campo":"consulta_runt","anterior":"","nuevo":"Con gestor"}"""),
        ]);

        var result = await DetalleAsync(db, OtTenant, ProcedureId);

        var mods = result.Procedure!.Modificaciones;
        var runt = mods.Should().ContainSingle(m => m.Campo == "consulta_runt" && m.Nuevo == "Sin alertas").Subject;
        runt.ActorNombre.Should().Be("Sistema · RUNT");
        runt.ActorCompania.Should().BeNull();
        runt.ChangedAt.Offset.Should().Be(TimeSpan.Zero);

        var identidad = mods.Should().ContainSingle(m => m.Campo == "identidad").Subject;
        identidad.ActorNombre.Should().Be("Sistema · Validación de identidad");
        identidad.ActorCompania.Should().BeNull();

        var placa = mods.Should().ContainSingle(m => m.Campo == "placa").Subject;
        placa.ActorNombre.Should().BeNull();
        placa.ActorCompania.Should().BeNull();

        var runtConUsuario = mods.Should().ContainSingle(m => m.Campo == "consulta_runt" && m.Nuevo == "Con gestor").Subject;
        runtConUsuario.ActorNombre.Should().Be("Jose Manuel Pimienta");
        runtConUsuario.ActorCompania.Should().Be("Empresa Demo FLIT");
    }

    [Fact] // AC2: el acto y un dato_modificado con el mismo campo y el mismo nuevo salen una sola vez
    public async Task HU14180_AC2_ActoYDatoModificadoMismoCampoYNuevo_ApareceUnaVez_YConsultarOtraVezNoDuplica()
    {
        var db = await SembrarConEventosAsync(
        [
            Evento("reasignar_gestor_admin", ChangedAt, """{"previous_assigned_to_user_id":"11111111-1111-1111-1111-111111111111","new_assigned_to_user_id":"22222222-2222-2222-2222-222222222222"}"""),
            Evento("dato_modificado", ChangedAt.AddMinutes(-30), """{"etiqueta":"Reasignación de gestor","campo":"reasignacion_gestor","anterior":"","nuevo":"Reasignación de gestor"}"""),
            Evento("reasignar_gestor_admin", ChangedAt.AddHours(-5), """{"previous_assigned_to_user_id":"11111111-1111-1111-1111-111111111111","new_assigned_to_user_id":"33333333-3333-3333-3333-333333333333"}"""),
        ]);

        var first = await DetalleAsync(db, OtTenant, ProcedureId);
        var second = await DetalleAsync(db, OtTenant, ProcedureId);

        var mods = first.Procedure!.Modificaciones.Where(m => m.Campo == "reasignacion_gestor").ToList();
        mods.Should().HaveCount(2);
        mods.Select(m => m.ChangedAt).Should().BeEquivalentTo(new[] { ChangedAt, ChangedAt.AddHours(-5) });
        second.Procedure!.Modificaciones.Where(m => m.Campo == "reasignacion_gestor").Should().HaveCount(2);
    }

    private static ProcedureInstanceEvent Evento(string tipo, DateTimeOffset at, string payload, bool sinUsuario = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = ProcedureId,
            Tipo = tipo,
            Payload = payload,
            CreatedAt = at,
            CreatedBy = sinUsuario ? null : ActorUserId,
        };

    /// <summary>
    /// Siembra un trámite con una fila de status history (Entregado → Aprobado) Y un evento
    /// dato_modificado con campo "estado" y el mismo anterior/nuevo (simula la situación que
    /// HU #14176 debe deduplicar).
    /// </summary>
    private static async Task<string> SembrarConEstadoDuplicadoAsync()
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
            Code = "cli14176dup",
            LegalName = "Empresa Demo FLIT",
            TaxId = "900000099",
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
        ctx.Users.Add(new User
        {
            Id = ActorUserId,
            Email = "jose.pimienta@flitsas.com",
            DisplayName = "Jose Manuel Pimienta",
            Status = "active",
            HomeTenantId = ClientTenant,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = ProcedureId,
            TenantId = ClientTenant,
            ProcedureTypeId = TipoMatricula,
            ReferenceNumber = "FT1-0000231",
            Consecutivo = 231,
            Status = TramiteEstado.Aprobado,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = ActorUserId,
            CreatedAt = ChangedAt.AddDays(-1),
            UpdatedAt = ChangedAt,
        });
        // Fila de status history: Entregado → Aprobado
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = ProcedureId,
            FromStatus = TramiteEstado.Entregado,
            ToStatus = TramiteEstado.Aprobado,
            ChangedAt = ChangedAt,
            ChangedBy = ActorUserId,
            Reason = "Aprobado por OT",
            Metadata = "{}",
        });
        // dato_modificado con campo "estado" y MISMO anterior/nuevo → debe ser descartado (dedup)
        ctx.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            ProcedureInstanceId = ProcedureId,
            Tipo = "dato_modificado",
            Payload = $$$"""{"etiqueta":"Estado","campo":"estado","anterior":"{{{TramiteEstado.Entregado}}}","nuevo":"{{{TramiteEstado.Aprobado}}}"}""",
            CreatedAt = ChangedAt.AddSeconds(-1),
            CreatedBy = ActorUserId,
        });

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
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

    private static async Task<string> SembrarAsync(bool withHistory, bool withActor, bool withModificacion = false)
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

        if (withModificacion)
        {
            // Evento escrito durante la fase de borrador (ChangedAt.AddDays(-1) < SubmittedAt).
            // Incluye filas de borrador: no filtramos por estado del trámite.
            ctx.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = ClientTenant,
                ProcedureInstanceId = ProcedureId,
                Tipo = "dato_modificado",
                Payload = """{"etiqueta":"Nombre propietario","campo":"nombre_propietario","anterior":"Juan Perez","nuevo":"María García"}""",
                CreatedAt = ChangedAt.AddDays(-1),
                CreatedBy = withActor ? ActorUserId : null,
            });
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static async Task<string> SembrarConEventosAsync(IReadOnlyList<ProcedureInstanceEvent> events)
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
            Code = "cli14180",
            LegalName = "Empresa Demo FLIT",
            TaxId = "900000180",
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
        ctx.Users.Add(new User
        {
            Id = ActorUserId,
            Email = "jose.pimienta@flitsas.com",
            DisplayName = "Jose Manuel Pimienta",
            Status = "active",
            HomeTenantId = ClientTenant,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = ProcedureId,
            TenantId = ClientTenant,
            ProcedureTypeId = TipoMatricula,
            ReferenceNumber = "FT1-0000232",
            Consecutivo = 232,
            Status = TramiteEstado.Aprobado,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = ActorUserId,
            CreatedAt = ChangedAt.AddDays(-1),
            UpdatedAt = ChangedAt,
        });
        ctx.ProcedureInstanceEvents.AddRange(events);

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
