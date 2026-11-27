using System.Text;
using System.Text.Json;
using Flit.Infrastructure;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #14694 (plan §1.1 decisión E2, D-ACT; contracts/asyncapi/impuestos-events.v1.yaml) contra PostgreSQL real y en los
/// DOS hosts del PUT: core-api (<see cref="FlitDbContext"/>, productor <c>tramites</c>, outbox <c>tramites.outbox</c>) y
/// core-identity (<see cref="IdentityDbContext"/>, productor <c>plataforma</c>, outbox <c>identity.outbox</c>). Encender o
/// apagar <c>impuestos</c> deja el trabajo <c>impuestos.producto.cambiado</c> en el MISMO guardado: sin cambio no hay
/// trabajo y un rollback del cambio lo quita. Sin broker: solo se mira la outbox.
/// <para>
/// Uso de ejemplo: <c>SetTenantProductEnabledHandler.HandleAsync(new(tenantId, "impuestos", true, null), superAdmin)</c>
/// → una fila en la outbox del host con <c>routing_key = impuestos.producto.cambiado</c> y exchange <c>flit.impuestos</c>.
/// </para>
/// </summary>
public sealed class TenantProductChangeOutboxTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid UserAdmin = new("14694300-0000-4000-8000-0000000000a1");

    private static Guid Tenant => TenantSeed.LoneId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Hosts => new() { "tramites", "plataforma" };

    // ── Montaje: un proveedor de servicios por host, con su contexto y su outbox ─────────────────────────────

    private ServiceProvider Servicios(string productor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Producer"] = productor,
            ["Platform:Messaging:ConnectionString"] = "amqp://nadie:nada@127.0.0.1:1/",
        }).Build();
        if (productor == "tramites")
        {
            services.AddScoped(_ => NewContext());
            services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<FlitDbContext>());
            services.AddFlitOutbox<FlitDbContext>(config);
        }
        else
        {
            services.AddScoped(_ => NewIdentityContext());
            services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<IdentityDbContext>());
            services.AddFlitOutbox<IdentityDbContext>(config);
        }

        services.AddPlatformStores();
        return services.BuildServiceProvider();
    }

    private IdentityDbContext NewIdentityContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        NpgsqlConventions.Apply(options, Fixture.ConnectionString);
        return new IdentityDbContext(options.Options);
    }

    private async Task<List<OutboxMessage>> OutboxAsync(string productor)
    {
        if (productor == "tramites")
        {
            await using var ctx = NewContext();
            return await ctx.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync(Ct);
        }

        await using var id = NewIdentityContext();
        return await id.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync(Ct);
    }

    private async Task SeedAsync(bool conAdminCompany = true)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.Lone());
            await ctx.SaveChangesAsync(Ct);
        }

        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            $"""
            INSERT INTO identity.users (id, email, display_name, status, home_tenant_id, created_at)
              VALUES ('{UserAdmin}','admin14694@it.local','Admin','active','{Tenant}',now());
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
              VALUES ('AdminCompany','Administrador de Compañía',true,true,'COMPANY','plataforma',NULL);
            {ActivarImpuestos}
            """,
            cn);
        await cmd.ExecuteNonQueryAsync(Ct);
        if (conAdminCompany)
        {
            cmd.CommandText = $"""
                INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id, assigned_at)
                  SELECT '{Tenant}', '{UserAdmin}', id, now() FROM security.roles WHERE code = 'AdminCompany';
                """;
            await cmd.ExecuteNonQueryAsync(Ct);
        }
    }

    // BLOQ-1 (a): el DDL 205 siembra `impuestos` inactivo; aquí se activa como en un ambiente ya encendido
    // (docs/impuestos/guia-despliegue.md) y se deja como estaba: platform.products se preserva entre pruebas.
    private const string ActivarImpuestos = "UPDATE platform.products SET status = 'active' WHERE code = 'impuestos';";

    public override async ValueTask DisposeAsync()
    {
        if (PostgresAvailability.IsAvailable && Fixture.IsInitialized)
        {
            await using var cn = await Fixture.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("UPDATE platform.products SET status = 'inactive' WHERE code = 'impuestos';", cn);
            await cmd.ExecuteNonQueryAsync();
        }

        await base.DisposeAsync();
    }

    private static async Task<TenantProductChange> CambiarAsync(ServiceProvider sp, string producto, bool enabled)
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SetTenantProductEnabledHandler>()
            .HandleAsync(new SetTenantProductEnabledCommand(Tenant, producto, enabled, null), null, Ct);
    }

    private async Task<bool> HayFilaImpuestosAsync()
    {
        await using var check = NewContext();
        return await check.Set<TenantProductEntity>().AnyAsync(r => r.TenantId == Tenant && r.ProductCode == "impuestos", Ct);
    }

    private static JsonElement Data(OutboxMessage m) => JsonDocument.Parse(m.Payload).RootElement.GetProperty("data");

    // ── Encender, repetir y apagar ────────────────────────────────────────────────────────────────────────

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task EncenderImpuestos_EncolaElTrabajoEnLaOutboxDelHost(string productor)
    {
        await SeedAsync();
        await using var sp = Servicios(productor);

        var change = await CambiarAsync(sp, "impuestos", enabled: true);

        var m = (await OutboxAsync(productor)).Should().ContainSingle().Subject;
        m.Exchange.Should().Be("flit.impuestos");
        m.RoutingKey.Should().Be("impuestos.producto.cambiado");
        var sobre = EventEnvelope.FromJson(Encoding.UTF8.GetBytes(m.Payload));
        sobre.Producer.Should().Be(productor);
        sobre.TenantId.Should().Be(Tenant);
        sobre.Version.Should().Be(1);
        var data = Data(m);
        data.GetProperty("tenantId").GetGuid().Should().Be(Tenant);
        data.GetProperty("productCode").GetString().Should().Be("impuestos");
        data.GetProperty("enabled").GetBoolean().Should().BeTrue();
        data.GetProperty("changedAt").GetDateTimeOffset().Should().BeCloseTo(change.Current.UpdatedAt, TimeSpan.FromMilliseconds(1));
        data.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("tenantId", "productCode", "enabled", "changedAt");
    }

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task RepetirElMismoValor_NoEncolaOtroTrabajo(string productor)
    {
        await SeedAsync();
        await using var sp = Servicios(productor);

        await CambiarAsync(sp, "impuestos", enabled: true);
        (await CambiarAsync(sp, "impuestos", enabled: true)).Changed.Should().BeFalse();

        (await OutboxAsync(productor)).Should().ContainSingle();
    }

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task Apagar_EncolaEnabledFalse_SinExigirAdminCompany(string productor)
    {
        await SeedAsync();
        await using var sp = Servicios(productor);
        await CambiarAsync(sp, "impuestos", enabled: true);
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await using var cmd = new NpgsqlCommand("UPDATE security.user_role_assignments SET deleted_at = now()", cn);
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        await CambiarAsync(sp, "impuestos", enabled: false);

        var outbox = await OutboxAsync(productor);
        outbox.Should().HaveCount(2);
        Data(outbox[1]).GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task ProductoSinAprovisionamiento_NoEncolaTrabajo(string productor)
    {
        await SeedAsync();
        await using var sp = Servicios(productor);

        (await CambiarAsync(sp, "comparendos", enabled: true)).Changed.Should().BeTrue();

        (await OutboxAsync(productor)).Should().BeEmpty();
    }

    // ── Atomicidad: el trabajo se confirma o se revierte con la fila ───────────────────────────────────────

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task RollbackDeLaTransaccion_QuitaLaFilaYElTrabajo(string productor)
    {
        await SeedAsync();
        await using var sp = Servicios(productor);

        await using (var scope = sp.CreateAsyncScope())
        {
            // IdentityDbContext usa la estrategia de reintentos de Npgsql: la transacción manual va dentro de ella.
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(Ct);
                (await scope.ServiceProvider.GetRequiredService<SetTenantProductEnabledHandler>()
                    .HandleAsync(new SetTenantProductEnabledCommand(Tenant, "impuestos", true, null), null, Ct)).Changed.Should().BeTrue();
                await tx.RollbackAsync(Ct);
            });
        }

        (await OutboxAsync(productor)).Should().BeEmpty();
        (await HayFilaImpuestosAsync()).Should().BeFalse();
    }

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task GuardadoRechazadoPorElMotor_NoDejaTrabajo(string productor)
    {
        await SeedAsync();
        await using var sp = Servicios(productor);

        await using (var scope = sp.CreateAsyncScope())
        {
            var jobs = scope.ServiceProvider.GetRequiredService<ITenantProductChangeJobs>();
            var act = () => scope.ServiceProvider.GetRequiredService<ITenantProductRepository>()
                .SetAsync(Guid.NewGuid(), "impuestos", true, null, null, c => jobs.EncolarCambio(c.Current), Ct);
            await act.Should().ThrowAsync<DbUpdateException>("la empresa no existe: la FK rechaza el guardado");
        }

        (await OutboxAsync(productor)).Should().BeEmpty();
    }

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task SinAdminCompanyActivo_Rechaza_YNoGuardaNiEncola(string productor)
    {
        await SeedAsync(conAdminCompany: false);
        await using var sp = Servicios(productor);

        var act = () => CambiarAsync(sp, "impuestos", enabled: true);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProvisioningPrerequisiteMissing);
        (await OutboxAsync(productor)).Should().BeEmpty();
        (await HayFilaImpuestosAsync()).Should().BeFalse();
    }

    // ── Reintentar (plan §1.2 decisión R1) ───────────────────────────────────────────────────────────────

    [PostgresTheory]
    [MemberData(nameof(Hosts))]
    public async Task Reintentar_EncolaElTrabajoConQuienLoPidio(string productor)
    {
        await SeedAsync();
        await using var sp = Servicios(productor);
        await CambiarAsync(sp, "impuestos", enabled: true);
        var superAdmin = Guid.NewGuid();

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RetryProvisioningHandler>()
                .HandleAsync(new RetryProvisioningCommand(Tenant, "impuestos"), superAdmin, Ct);
        }

        var outbox = await OutboxAsync(productor);
        outbox.Should().HaveCount(2);
        var m = outbox[1];
        m.Exchange.Should().Be("flit.impuestos");
        m.RoutingKey.Should().Be("impuestos.aprovisionamiento.reintentar");
        var data = Data(m);
        data.GetProperty("tenantId").GetGuid().Should().Be(Tenant);
        data.GetProperty("solicitadoPor").GetGuid().Should().Be(superAdmin);
        data.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("tenantId", "solicitadoPor");
    }
}
