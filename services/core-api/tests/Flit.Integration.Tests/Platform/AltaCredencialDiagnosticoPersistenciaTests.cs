using System.Text.Json;
using Flit.Infrastructure;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Infrastructure.Platform;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #14711 (AC-DC-01, AC-DC-02, AC-DC-03; DDL 206) contra PostgreSQL real — la escritura atómica de las credenciales del alta
/// (filas, auditoría por código real y el trabajo de la outbox en UN guardado), el paso a <c>PENDIENTE</c> al encender
/// <c>diagnostico</c>, la reserva con <c>FOR UPDATE SKIP LOCKED</c> y una ronda completa del trabajador en el host de
/// core-identity (productor <c>plataforma</c>).
/// </summary>
/// <remarks>
/// Uso de ejemplo: encender <c>diagnostico</c> con <c>SetTenantProductEnabledHandler</c> y correr
/// <c>TrabajadorAltaCredencialDiagnostico.EjecutarRondaAsync</c> → <c>LISTO</c>, llave y secreto guardados y el trabajo
/// <c>impuestos.credencial_diagnostico.actualizada</c> en <c>identity.outbox</c>.
/// </remarks>
public sealed class AltaCredencialDiagnosticoPersistenciaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Llave = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string Secreto = "whsec_secreto_de_prueba_0123456789";
    private static readonly Guid Tenant = new("14711000-0000-4000-8000-0000000000c1");
    private static readonly Guid UserAdmin = new("14711000-0000-4000-8000-0000000000a1");
    private static readonly DateTimeOffset Momento = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SeedAsync(bool conAdmin = true)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at) VALUES
              ('{Tenant}','IT-14711-C','Flota Andina SAS','900123456-8','RENTING',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, home_tenant_id, created_at)
              VALUES ('{UserAdmin}','admin14711@it.local','Admin','active','{Tenant}',now());
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
              VALUES ('AdminCompany','Administrador de Compañía',true,true,'COMPANY','plataforma',NULL);
            """, cn);
        await cmd.ExecuteNonQueryAsync(Ct);
        if (conAdmin)
        {
            cmd.CommandText = $"""
                INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at)
                  SELECT '{Tenant}', '{UserAdmin}', id, now() FROM security.roles WHERE code = 'AdminCompany';
                """;
            await cmd.ExecuteNonQueryAsync(Ct);
        }
    }

    private static OutboxMessage Trabajo() => new()
    {
        Id = Guid.CreateVersion7(),
        Exchange = "flit.impuestos",
        RoutingKey = "impuestos.credencial_diagnostico.actualizada",
        Payload = "{}",
        OccurredAt = Momento,
    };

    // ── Escritura atómica del alta (AC-DC-01) ─────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task GuardarAlta_EscribeFilasAuditoriaPorCodigoYTrabajo_EnUnSoloGuardado()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new TenantProductCredentialRepository(ctx);

        await repo.GuardarAltaProveedorAsync(
            Tenant, "diagnostico",
            [new EscrituraCredencialAlta("impuesto_api_key", "cifrado-llave-0123456789"), new EscrituraCredencialAlta("impuesto_webhook_secret", "cifrado-secreto-0123456789")],
            "CREDENCIAL_OBTENIDA", null, _ => ctx.Set<OutboxMessage>().Add(Trabajo()), Ct);

        await using var lectura = NewContext();
        (await lectura.Set<TenantProductCredentialEntity>().AsNoTracking().Where(c => c.TenantId == Tenant).Select(c => c.CredentialCode).ToListAsync(Ct))
            .Should().BeEquivalentTo("impuesto_api_key", "impuesto_webhook_secret");
        var auditoria = await lectura.TenantConfigAuditLogs.AsNoTracking().Where(a => a.TenantId == Tenant).ToListAsync(Ct);
        auditoria.Select(a => a.FieldName).Should().BeEquivalentTo("impuesto_api_key", "impuesto_webhook_secret", "impuesto_alta_proveedor");
        auditoria.Single(a => a.FieldName == "impuesto_alta_proveedor").NewValue.Should().Be(JsonSerializer.Serialize("CREDENCIAL_OBTENIDA"));
        auditoria.Should().NotContain(a => (a.NewValue ?? string.Empty).Contains("cifrado", StringComparison.Ordinal)
                                           || (a.OldValue ?? string.Empty).Contains("cifrado", StringComparison.Ordinal));
        (await lectura.Set<OutboxMessage>().AsNoTracking().CountAsync(m => m.RoutingKey == "impuestos.credencial_diagnostico.actualizada", Ct)).Should().Be(1);
    }

    [PostgresFact]
    public async Task GuardarAlta_SiFallaAntesDeGuardar_NoQuedaNiFilaNiAuditoriaNiTrabajo()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new TenantProductCredentialRepository(ctx);

        var act = () => repo.GuardarAltaProveedorAsync(
            Tenant, "diagnostico", [new EscrituraCredencialAlta("impuesto_api_key", "cifrado-llave-0123456789")],
            "CREDENCIAL_OBTENIDA", null, _ => throw new InvalidOperationException("falla de prueba"), Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var lectura = NewContext();
        (await lectura.Set<TenantProductCredentialEntity>().AsNoTracking().CountAsync(c => c.TenantId == Tenant, Ct)).Should().Be(0);
        (await lectura.TenantConfigAuditLogs.AsNoTracking().CountAsync(a => a.TenantId == Tenant, Ct)).Should().Be(0);
        (await lectura.Set<OutboxMessage>().AsNoTracking().CountAsync(Ct)).Should().Be(0);
    }

    [PostgresFact]
    public async Task GuardarAlta_AlReemplazar_SubeRowVersionEnUno_LaVersionDelTrabajo()
    {
        await SeedAsync();
        await using (var ctx = NewContext())
        {
            await new TenantProductCredentialRepository(ctx).GuardarAltaProveedorAsync(
                Tenant, "diagnostico", [new EscrituraCredencialAlta("impuesto_api_key", "cifrado-llave-0123456789")], "CREDENCIAL_OBTENIDA", null, null, Ct);
        }

        var antes = await RowVersionAsync();
        await using (var ctx = NewContext())
        {
            await new TenantProductCredentialRepository(ctx).GuardarAltaProveedorAsync(
                Tenant, "diagnostico", [new EscrituraCredencialAlta("impuesto_api_key", "cifrado-llave-nueva-0123456789")], "CLAVE_ROTADA", null, null, Ct);
        }

        antes.Should().Be(0);
        (await RowVersionAsync()).Should().Be(antes + 1);
    }

    private async Task<long> RowVersionAsync()
    {
        await using var ctx = NewContext();
        return await ctx.Set<TenantProductCredentialEntity>().AsNoTracking()
            .Where(c => c.TenantId == Tenant && c.CredentialCode == "impuesto_api_key").Select(c => c.RowVersion).SingleAsync(Ct);
    }

    // ── Estado del alta automática (AC-DC-03) ─────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Pendiente_SeCreaYSeReinicia_ConVersionCreciente()
    {
        await SeedAsync();
        await using (var ctx = NewContext())
        {
            var preparacion = await new TenantProductProvisioningRepository(ctx).PrepararPendienteAltaDiagnosticoAsync(Tenant, Ct);
            preparacion.FilaExiste.Should().BeFalse();
            preparacion.Marcar(Momento);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoManualAsync(
                new EstadoAltaDiagnostico(Tenant, "ERROR", "PROVEEDOR_NO_DISPONIBLE", "x", null, 5, null, Momento.AddMinutes(1)), Ct);
        }

        await using (var ctx = NewContext())
        {
            var preparacion = await new TenantProductProvisioningRepository(ctx).PrepararPendienteAltaDiagnosticoAsync(Tenant, Ct);
            preparacion.FilaExiste.Should().BeTrue();
            preparacion.Marcar(Momento.AddMinutes(2));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var lectura = NewContext();
        var fila = await lectura.Set<TenantProductProvisioningEntity>().AsNoTracking().SingleAsync(p => p.TenantId == Tenant, Ct);
        fila.ProductCode.Should().Be("diagnostico");
        fila.Status.Should().Be("PENDIENTE");
        fila.ReasonCode.Should().BeNull();
        fila.Attempts.Should().Be(0);
        fila.NextAttemptAt.Should().BeNull();
        fila.ReportedVersion.Should().Be(3);
    }

    [PostgresFact]
    public async Task Reservar_TomaSoloLasPendientesVencidasDeDiagnostico_YLasCorreParaQueOtraRondaNoLasTome()
    {
        await SeedAsync();
        await using (var ctx = NewContext())
        {
            await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoManualAsync(
                new EstadoAltaDiagnostico(Tenant, "PENDIENTE", "PROVEEDOR_NO_DISPONIBLE", "x", null, 2, Momento.AddMinutes(-1), Momento.AddMinutes(-6)), Ct);
        }

        await using var ctx1 = NewContext();
        var primera = await new TenantProductProvisioningRepository(ctx1).ReservarPendientesAltaDiagnosticoAsync(Momento, TimeSpan.FromMinutes(10), 20, Ct);
        await using var ctx2 = NewContext();
        var segunda = await new TenantProductProvisioningRepository(ctx2).ReservarPendientesAltaDiagnosticoAsync(Momento, TimeSpan.FromMinutes(10), 20, Ct);

        primera.Should().Equal(new PendienteAltaDiagnostico(Tenant, 2, 1));
        segunda.Should().BeEmpty();
        await using var lectura = NewContext();
        (await lectura.Set<TenantProductProvisioningEntity>().AsNoTracking().SingleAsync(p => p.TenantId == Tenant, Ct))
            .NextAttemptAt.Should().Be(Momento.AddMinutes(10));
    }

    [PostgresFact]
    public async Task Reservar_SaltaLaFilaBloqueadaPorOtraReplica()
    {
        await SeedAsync();
        await using (var ctx = NewContext())
        {
            await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoManualAsync(
                new EstadoAltaDiagnostico(Tenant, "PENDIENTE", null, null, null, 0, null, Momento), Ct);
        }

        await using var otra = await Fixture.OpenConnectionAsync();
        await using var tx = await otra.BeginTransactionAsync(Ct);
        await using (var bloqueo = new NpgsqlCommand($"SELECT 1 FROM platform.tenant_product_provisioning WHERE tenant_id = '{Tenant}' FOR UPDATE", otra, tx))
            await bloqueo.ExecuteNonQueryAsync(Ct);

        await using var ctx2 = NewContext();
        var reservadas = await new TenantProductProvisioningRepository(ctx2).ReservarPendientesAltaDiagnosticoAsync(Momento, TimeSpan.FromMinutes(10), 20, Ct);

        reservadas.Should().BeEmpty();
        await tx.RollbackAsync(Ct);
    }

    // ── M-1: el guardado de la ronda va condicionado a la versión reservada ─────────────────────────────────

    private async Task<TenantProductProvisioningEntity> FilaAsync()
    {
        await using var ctx = NewContext();
        return await ctx.Set<TenantProductProvisioningEntity>().AsNoTracking().SingleAsync(p => p.TenantId == Tenant, Ct);
    }

    private async Task SembrarPendienteAsync()
    {
        await using var ctx = NewContext();
        await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoManualAsync(
            new EstadoAltaDiagnostico(Tenant, "PENDIENTE", null, null, null, 0, null, Momento.AddMinutes(-5)), Ct);
    }

    private async Task<PendienteAltaDiagnostico> ReservarAsync(DateTimeOffset ahora)
    {
        await using var ctx = NewContext();
        return (await new TenantProductProvisioningRepository(ctx).ReservarPendientesAltaDiagnosticoAsync(ahora, TimeSpan.FromMinutes(10), 20, Ct)).Single();
    }

    private async Task<bool> RegistrarAsync(EstadoAltaDiagnostico estado, long version)
    {
        await using var ctx = NewContext();
        return await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoAsync(estado, version, Ct);
    }

    [PostgresFact]
    public async Task Registrar_ConLaVersionReservada_Aplica_YSubeLaVersion()
    {
        await SeedAsync();
        await SembrarPendienteAsync();
        var reservada = await ReservarAsync(Momento);

        var aplicado = await RegistrarAsync(new EstadoAltaDiagnostico(Tenant, "LISTO", null, null, null, 0, null, Momento), reservada.Version);

        aplicado.Should().BeTrue();
        var fila = await FilaAsync();
        (fila.Status, fila.ReportedVersion).Should().Be(("LISTO", reservada.Version + 1));
    }

    [PostgresFact]
    public async Task Registrar_NoPisaUnPendienteReiniciadoDuranteLaRonda()
    {
        await SeedAsync();
        await SembrarPendienteAsync();
        var reservada = await ReservarAsync(Momento);

        // Mientras la ronda atiende, el SuperAdmin apaga y vuelve a encender diagnostico: el alta se reinicia a PENDIENTE.
        await using (var ctx = NewContext())
        {
            var preparacion = await new TenantProductProvisioningRepository(ctx).PrepararPendienteAltaDiagnosticoAsync(Tenant, Ct);
            preparacion.Marcar(Momento.AddMinutes(1));
            await ctx.SaveChangesAsync(Ct);
        }

        var aplicado = await RegistrarAsync(new EstadoAltaDiagnostico(Tenant, "LISTO", null, null, null, 0, null, Momento.AddMinutes(2)), reservada.Version);

        aplicado.Should().BeFalse("el resultado de la ronda es obsoleto");
        var fila = await FilaAsync();
        (fila.Status, fila.ReportedVersion).Should().Be(("PENDIENTE", reservada.Version + 1));
        await using (var ctx = NewContext())
        {
            (await new TenantProductProvisioningRepository(ctx).ReservarPendientesAltaDiagnosticoAsync(Momento.AddMinutes(2), TimeSpan.FromMinutes(10), 20, Ct))
                .Should().BeEmpty("la reserva de la ronda en curso sigue vigente: otra réplica no la toma a la vez");
        }
    }

    // ── N-3: el estado del botón sin cambios no sube la versión ────────────────────────────────────────────

    [PostgresFact]
    public async Task RegistrarManual_ConElMismoEstado_NoSubeLaVersion_YConOtroSi()
    {
        await SeedAsync();
        var listo = new EstadoAltaDiagnostico(Tenant, "LISTO", null, null, null, 0, null, Momento);
        await using (var ctx = NewContext())
            await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoManualAsync(listo, Ct);
        await using (var ctx = NewContext())
            await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoManualAsync(listo with { En = Momento.AddMinutes(1) }, Ct);

        (await FilaAsync()).ReportedVersion.Should().Be(1);

        await using (var ctx = NewContext())
        {
            await new TenantProductProvisioningRepository(ctx).RegistrarEstadoAltaDiagnosticoManualAsync(
                new EstadoAltaDiagnostico(Tenant, "LISTO_SIN_WEBHOOK", "SIN_WEBHOOK_SECRET", "x", "REINTENTAR", 0, null, Momento.AddMinutes(2)), Ct);
        }

        (await FilaAsync()).ReportedVersion.Should().Be(2);
    }

    // ── M-3: dos guardados a la vez ────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task GuardarAlta_ChocaConOtroGuardado_SaleComoConflicto_YElContextoQuedaLimpio()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new TenantProductCredentialRepository(ctx);

        // La otra escritura (el botón o la ronda) entra entre la lectura y el guardado de esta.
        var act = () => repo.GuardarAltaProveedorAsync(
            Tenant, "diagnostico", [new EscrituraCredencialAlta("impuesto_api_key", "cifrado-llave-esta-0123456789")], "CREDENCIAL_OBTENIDA", null,
            _ =>
            {
                using var otro = NewContext();
                new TenantProductCredentialRepository(otro).GuardarAltaProveedorAsync(
                    Tenant, "diagnostico", [new EscrituraCredencialAlta("impuesto_api_key", "cifrado-llave-otra-0123456789")], "CREDENCIAL_OBTENIDA", null, null, Ct)
                    .GetAwaiter().GetResult();
            },
            Ct);

        await act.Should().ThrowAsync<TenantProductCredentialConflictException>();
        ctx.ChangeTracker.Entries().Should().BeEmpty("lo que no se guardó no queda pendiente para el siguiente guardado");
        await repo.RegistrarAuditoriaAltaProveedorAsync(Tenant, "diagnostico", "ALTA_EN_CURSO", false, null, Ct);
    }

    // ── L-3: fila resumen sin cambios y en los fallos ──────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AuditoriaAltaProveedor_SinCambiosYFallo_DejanLaFilaResumen_SinValores()
    {
        await SeedAsync();
        await using (var ctx = NewContext())
        {
            var repo = new TenantProductCredentialRepository(ctx);
            await repo.RegistrarAuditoriaAltaProveedorAsync(Tenant, "diagnostico", "SIN_CAMBIOS", true, UserAdmin, Ct);
            await repo.RegistrarAuditoriaAltaProveedorAsync(Tenant, "diagnostico", "PROVEEDOR_NO_DISPONIBLE", false, null, Ct);
        }

        await using var lectura = NewContext();
        var filas = await lectura.TenantConfigAuditLogs.AsNoTracking().Where(a => a.TenantId == Tenant).ToListAsync(Ct);
        filas.Should().HaveCount(2).And.OnlyContain(a =>
            a.FieldName == "impuesto_alta_proveedor" && a.Operation == "obtain_from_provider" && a.OldValue == null);
        var sinCambios = filas.Single(a => a.ChangedBy == UserAdmin);
        (sinCambios.NewValue, sinCambios.Result, sinCambios.ErrorCode).Should().Be((JsonSerializer.Serialize("SIN_CAMBIOS"), "success", (string?)null));
        var fallo = filas.Single(a => a.ChangedBy == null);
        (fallo.NewValue, fallo.Result, fallo.ErrorCode).Should().Be((JsonSerializer.Serialize("PROVEEDOR_NO_DISPONIBLE"), "failure", "PROVEEDOR_NO_DISPONIBLE"));
    }

    // ── Ronda completa en el host de core-identity (AC-DC-03 + AC-DC-01) ──────────────────────────────────

    [PostgresFact]
    public async Task EncenderDiagnostico_YUnaRonda_DejanLaCredencialListaYElTrabajoParaImpuestos()
    {
        await SeedAsync();
        var proveedor = new ProveedorFijo(new RespuestaAltaProveedor(Llave, Secreto, Created: true, ApiKeyRotated: false));
        await using var sp = Servicios(proveedor);
        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SetTenantProductEnabledHandler>()
                .HandleAsync(new SetTenantProductEnabledCommand(Tenant, "diagnostico", true, null), null, Ct);
        }

        var trabajador = ActivatorUtilities.CreateInstance<TrabajadorAltaCredencialDiagnostico>(sp);
        var atendidas = await trabajador.EjecutarRondaAsync(Ct);
        var segunda = await trabajador.EjecutarRondaAsync(Ct);

        atendidas.Should().Be(1);
        segunda.Should().Be(0, "ya quedó LISTO");
        proveedor.Peticiones.Should().ContainSingle().Which.TaxId.Should().Be("900123456");
        await using var lectura = NewIdentityContext();
        var fila = await lectura.Set<TenantProductProvisioningEntity>().AsNoTracking().SingleAsync(p => p.TenantId == Tenant, Ct);
        fila.Status.Should().Be("LISTO");
        (await lectura.Set<TenantProductCredentialEntity>().AsNoTracking().Where(c => c.TenantId == Tenant).Select(c => c.CredentialCode).ToListAsync(Ct))
            .Should().BeEquivalentTo("impuesto_api_key", "impuesto_webhook_secret");
        var trabajo = await lectura.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.RoutingKey == "impuestos.credencial_diagnostico.actualizada", Ct);
        trabajo.Exchange.Should().Be("flit.impuestos");
        trabajo.Payload.Should().NotContain(Llave).And.NotContain(Secreto);
        using var sobre = JsonDocument.Parse(trabajo.Payload);
        var data = sobre.RootElement.GetProperty("data");
        data.GetProperty("tenantId").GetGuid().Should().Be(Tenant);
        data.GetProperty("productCode").GetString().Should().Be("diagnostico");
        data.GetProperty("version").GetInt64().Should().Be(0);
        sobre.RootElement.GetProperty("producer").GetString().Should().Be("plataforma");
    }

    [PostgresFact]
    public async Task SinAdminCompany_LaRondaDejaErrorSinAdmin_YNoLlamaAlProveedor()
    {
        await SeedAsync(conAdmin: false);
        var proveedor = new ProveedorFijo(new RespuestaAltaProveedor(Llave, Secreto, Created: true, ApiKeyRotated: false));
        await using var sp = Servicios(proveedor);
        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SetTenantProductEnabledHandler>()
                .HandleAsync(new SetTenantProductEnabledCommand(Tenant, "diagnostico", true, null), null, Ct);
        }

        await ActivatorUtilities.CreateInstance<TrabajadorAltaCredencialDiagnostico>(sp).EjecutarRondaAsync(Ct);

        proveedor.Peticiones.Should().BeEmpty();
        await using var lectura = NewIdentityContext();
        var fila = await lectura.Set<TenantProductProvisioningEntity>().AsNoTracking().SingleAsync(p => p.TenantId == Tenant, Ct);
        (fila.Status, fila.ReasonCode).Should().Be(("ERROR", "SIN_ADMIN"));
    }

    private ServiceProvider Servicios(IClienteAltaProveedorImpuesto proveedor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Producer"] = "plataforma",
            ["Platform:Messaging:ConnectionString"] = "amqp://nadie:nada@127.0.0.1:1/",
        }).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddScoped(_ => NewIdentityContext());
        services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddFlitOutbox<IdentityDbContext>(config);
        services.AddPlatformStores();
        services.AddSingleton(proveedor);
        services.AddSingleton(TimeProvider.System);
        return services.BuildServiceProvider();
    }

    private IdentityDbContext NewIdentityContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        NpgsqlConventions.Apply(options, Fixture.ConnectionString);
        return new IdentityDbContext(options.Options);
    }

    private sealed class ProveedorFijo(RespuestaAltaProveedor respuesta) : IClienteAltaProveedorImpuesto
    {
        public List<PeticionAltaProveedor> Peticiones { get; } = [];

        public Task<RespuestaAltaProveedor> DarDeAltaAsync(PeticionAltaProveedor peticion, CancellationToken cancellationToken = default)
        {
            Peticiones.Add(peticion);
            return Task.FromResult(respuesta);
        }
    }
}
