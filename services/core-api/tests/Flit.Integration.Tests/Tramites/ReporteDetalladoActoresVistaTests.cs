using System.Text.Json;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Columna <c>actors</c> de <c>analytics.v_procedure_detail_report</c> (DDL 189): validación de identidad por persona.
/// Contra Postgres REAL porque lo que se prueba es el SQL de la vista (enlace actor↔validación, intentos en la
/// bitácora, rol derivado de la familia y vocabulario de salida).
/// </summary>
public sealed class ReporteDetalladoActoresVistaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("e1a00000-0000-4000-8000-0000000000aa");

    [PostgresFact]
    public async Task Traspaso_dos_vendedores_uno_rechazado_luego_aprobado()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "1001");
        await AddActorAsync(id, "vendedor", 1, "2002");
        await AddActorAsync(id, "vendedor", 2, "3003");
        var rechazada = await AddValidationAsync(id, "vendedor", "2002", "rechazado", 0,
            "{\"motivo\":\"Coincidencia facial insuficiente\"}");
        var aprobada = await AddValidationAsync(id, "vendedor", "2002", "aprobado", 1);
        await AddAuditAsync(rechazada, "send", "ok");
        await AddAuditAsync(rechazada, "webhook_received", "received");
        await AddAuditAsync(rechazada, "webhook_applied", "rechazado");
        await AddAuditAsync(aprobada, "webhook_applied", "aprobado");

        var actores = await ReadActoresAsync(id);

        actores.Select(a => (a.Rol, a.Ordinal)).Should().Equal(
            ("comprador", 1), ("vendedor", 1), ("vendedor", 2));
        var v1 = actores.Single(a => a.Rol == "vendedor" && a.Ordinal == 1);
        v1.EstadoValidacion.Should().Be("aprobado");
        v1.Intentos.Should().Be(2, "solo cuentan los resultados aplicados, no el envío ni la recepción");
        v1.MotivoRechazo.Should().Be("El rostro no coincide con el documento.");
        var v2 = actores.Single(a => a.Rol == "vendedor" && a.Ordinal == 2);
        (v2.EstadoValidacion, v2.Intentos, v2.MotivoRechazo).Should().Be(("sin_validacion", 0, null));
        actores.Single(a => a.Rol == "comprador").EstadoValidacion.Should().Be("sin_validacion");

        JsonSerializer.Serialize(actores).ToLowerInvariant()
            .Should().NotContain("kyverum").And.NotContain("manual").And.NotContain("proveedor");
    }

    [PostgresFact]
    public async Task Matricula_el_comprador_guardado_sale_como_propietario()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoMatriculaInicial);
        await AddActorAsync(id, "comprador", 1, "1001");

        var actores = await ReadActoresAsync(id);

        actores.Should().ContainSingle().Which.Rol.Should().Be("propietario");
    }

    [PostgresFact]
    public async Task Sin_bitacora_cuenta_los_attempts_de_la_fila()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "1001");
        await AddValidationAsync(id, "comprador", "1001", "rechazado", 0, null, attempts: 3);

        var actor = (await ReadActoresAsync(id)).Single();

        actor.Intentos.Should().Be(3);
        actor.MotivoRechazo.Should().Be("No se pudo verificar la identidad con las imágenes recibidas.");
    }

    [PostgresFact]
    public async Task Juridica_enlaza_por_el_documento_del_representante_legal()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "900123", personType: "juridical", rlDoc: "555");
        await AddValidationAsync(id, "comprador", "555", "aprobado", 0);

        (await ReadActoresAsync(id)).Single().EstadoValidacion.Should().Be("aprobado");
    }

    [PostgresFact]
    public async Task Dos_actores_sin_coincidencia_no_heredan_la_validacion_de_otro()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "900123", personType: "juridical");
        await AddActorAsync(id, "comprador", 2, "900456", personType: "juridical");
        await AddValidationAsync(id, "comprador", "777", "aprobado", 0, null, attempts: 2);

        var actores = await ReadActoresAsync(id);

        actores.Should().OnlyContain(a => a.EstadoValidacion == "sin_validacion" && a.Intentos == 0);
    }

    [PostgresFact]
    public async Task Un_solo_actor_sin_coincidencia_toma_las_validaciones_del_rol()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "900123", personType: "juridical");
        await AddValidationAsync(id, "comprador", "777", "enviado", 0);

        (await ReadActoresAsync(id)).Single().EstadoValidacion.Should().Be("enviado");
    }

    [PostgresFact]
    public async Task Una_validacion_de_vendedor_no_se_atribuye_al_comprador()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "1001");
        await AddValidationAsync(id, "vendedor", "1001", "aprobado", 0);

        (await ReadActoresAsync(id)).Single().EstadoValidacion.Should().Be("sin_validacion");
    }

    [PostgresFact]
    public async Task Trámite_sin_actores_devuelve_lista_vacia_y_la_consulta_de_maximos_cuenta_por_rol()
    {
        var vacio = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard, "REF-A");
        var dos = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard, "REF-B");
        await AddActorAsync(dos, "comprador", 1, "1");
        await AddActorAsync(dos, "vendedor", 1, "2");
        await AddActorAsync(dos, "vendedor", 2, "3");

        (await ReadActoresAsync(vacio)).Should().BeEmpty();

        await using var read = NewContext();
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = DetailedReportActors.MaxSql("\nFROM analytics.v_procedure_detail_report v\nWHERE v.tenant_id = @t\n");
        var p = cmd.CreateParameter();
        p.ParameterName = "t";
        p.Value = TenantSeed.LoneId;
        cmd.Parameters.Add(p);
        var max = await DetailedReportActors.ReadMaxAsync(cmd, TestContext.Current.CancellationToken);

        max.Should().Be(new MaxActoresPorRolDto(1, 2, 0));
    }

    [PostgresTheory]
    [InlineData("aprobado", 1)]
    [InlineData("rechazado", 1)]
    [InlineData("expirado", 0)]
    [InlineData("en_proceso", 0)]
    public async Task Aprobado_o_rechazado_sin_rastro_cuenta_al_menos_un_intento_los_demas_estados_el_conteo_real(
        string estado, int esperado)
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "1001");
        await AddValidationAsync(id, "comprador", "1001", estado, 0, null, attempts: 0);

        (await ReadActoresAsync(id)).Single().Intentos.Should().Be(esperado);
    }

    [PostgresFact]
    public async Task El_minimo_de_un_intento_no_cambia_los_conteos_con_bitacora()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "1001");
        var v = await AddValidationAsync(id, "comprador", "1001", "aprobado", 0);
        await AddAuditAsync(v, "webhook_applied", "aprobado");
        await AddAuditAsync(v, "webhook_applied", "aprobado");
        await AddAuditAsync(v, "webhook_applied", "aprobado");

        (await ReadActoresAsync(id)).Single().Intentos.Should().Be(3);
    }

    [PostgresFact]
    public async Task Documentos_en_blanco_nunca_coinciden_ni_entre_si()
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "");
        await AddActorAsync(id, "comprador", 2, " ");
        await AddValidationAsync(id, "comprador", "", "aprobado", 0, null, attempts: 2);

        (await ReadActoresAsync(id)).Should()
            .OnlyContain(a => a.EstadoValidacion == "sin_validacion" && a.Intentos == 0);
    }

    [PostgresTheory]
    [InlineData("manual_activo", "esperando_captura")]
    [InlineData("pendiente_revision_manual", "pendiente_revision")]
    public async Task Los_estados_del_flujo_asistido_viajan_con_codigo_sin_la_palabra_manual(string enBd, string esperado)
    {
        var id = await SeedAsync(TramiteTipologiaCatalog.CodigoTraspasoStandard);
        await AddActorAsync(id, "comprador", 1, "1001");
        await AddValidationAsync(id, "comprador", "1001", enBd, 0);

        await using var read = NewContext();
        var json = await read.Database
            .SqlQuery<string>($"SELECT actors::text AS \"Value\" FROM analytics.v_procedure_detail_report WHERE id = {id}")
            .SingleAsync(TestContext.Current.CancellationToken);

        json.Should().Contain($"\"estadoValidacion\": \"{esperado}\"");
        json.Should().NotContainEquivalentOf("manual", "ni el código de estado ni nada del JSON de la vista delata el mecanismo");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<ActorValidacionDto>> ReadActoresAsync(Guid instanceId)
    {
        await using var read = NewContext();
        var json = await read.Database
            .SqlQuery<string>($"SELECT actors::text AS \"Value\" FROM analytics.v_procedure_detail_report WHERE id = {instanceId}")
            .SingleAsync(TestContext.Current.CancellationToken);
        return DetailedReportActors.Parse(json);
    }

    private async Task<Guid> SeedAsync(string typeCode, string reference = "REF-IT")
    {
        await using var ctx = NewContext();
        if (!await ctx.Tenants.AnyAsync(t => t.Id == TenantSeed.LoneId))
        {
            ctx.Tenants.Add(TenantSeed.Lone());
            ctx.Users.Add(new User
            {
                Id = UserId, Email = "it-actores@flit.test", DisplayName = "IT Actores", Status = "active",
                HomeTenantId = TenantSeed.LoneId, CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var typeId = await ctx.ProcedureTypes.AsNoTracking().Where(t => t.Code == typeCode).Select(t => t.Id).SingleAsync();
        var id = Guid.NewGuid();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id, TenantId = TenantSeed.LoneId, ProcedureTypeId = typeId, CreatedByUserId = UserId,
            Status = TramiteEstado.Borrador, Origin = "ict", ExternalRef = "ext-" + id.ToString("N"),
            ReferenceNumber = reference + "-" + id.ToString("N")[..6], CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task AddActorAsync(Guid instanceId, string rol, int ordinal, string doc,
        string? personType = null, string? rlDoc = null)
    {
        await using var ctx = NewContext();
        var code = rol == "comprador" ? "BUYER" : "OWNER";
        var entityId = await ctx.ProcedureEntities.AsNoTracking().Where(e => e.Code == code).Select(e => e.Id).SingleAsync();
        ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(), TenantId = TenantSeed.LoneId, ProcedureInstanceId = instanceId,
            ProcedureEntityId = entityId, ActorType = rol, DocumentType = "CC", DocumentNumber = doc,
            FullName = $"{rol} {ordinal}", Ordinal = ordinal, PersonType = personType,
            Metadata = rlDoc is null
                ? "{}"
                : JsonSerializer.Serialize(new { representanteLegal = new { tipoDocumento = "CC", numeroDocumento = rlDoc, nombreCompleto = "RL" } }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Guid> AddValidationAsync(Guid instanceId, string rol, string doc, string status, int dias,
        string? detail = null, int attempts = 0)
    {
        await using var ctx = NewContext();
        var id = Guid.NewGuid();
        var t = DateTimeOffset.UtcNow.AddDays(dias - 5);
        ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = id, TenantId = TenantSeed.LoneId, ProcedureInstanceId = instanceId, PartyRole = rol,
            DocumentType = "CC", DocumentNumber = doc, Name = "Persona", Email = $"{id:N}@flit.test",
            Status = status, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            Attempts = attempts, Detail = detail, CreatedAt = t, UpdatedAt = t,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task AddAuditAsync(Guid validationId, string stage, string outcome)
    {
        await using var ctx = NewContext();
        ctx.IdentityValidationAudits.Add(new IdentityValidationAuditEvent
        {
            Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, Stage = stage, Outcome = outcome,
            TenantId = TenantSeed.LoneId, ValidationId = validationId, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }
}
