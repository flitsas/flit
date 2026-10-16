using System.Text;
using System.Text.Json;
using Flit.Infrastructure.FlitoSignals;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Notificaciones.Webhooks;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13624 (Feature #13617, Épica #13614) contra Postgres real con todas las migraciones: el despachador convierte
/// cada aviso maduro de <c>integrations.flito_sync_signals</c> en un trabajo <c>notificaciones.webhook.send</c> firmado
/// en <c>tramites.outbox</c> (outbox del SDK), en la misma transacción en que sella el aviso. El publicador del SDK y
/// core-notificaciones no corren aquí: se verifica lo que queda en la outbox.
/// </summary>
public sealed class FlitoSignalDispatcherPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Destino = "https://dev.operaciones.flitsas.online/api/flito/sync/flit2/avisos";
    private const string Clave = "clave-ficticia-de-prueba-0123456789abcdef";
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000013624");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013624");
    private static readonly Guid Radicado = new("5a5a5a5a-0003-4000-8000-000000013624");
    private static readonly Guid Borrador = new("5a5a5a5a-0004-4000-8000-000000013624");

    [PostgresFact]
    public async Task AC1_UnTramiteRadicadoDejaElTrabajoEnLaOutboxYSellaElAviso()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await using var sp = Servicios();

        (await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken)).Should().BeGreaterThan(0);

        var trabajos = await TrabajosAsync();
        trabajos.Should().ContainSingle();
        var (sobre, trabajo) = trabajos[0];
        sobre.Type.Should().Be(TrabajoWebhook.Tipo);
        sobre.Producer.Should().Be("tramites");
        sobre.TenantId.Should().Be(Cliente);
        trabajo.Url.Should().Be(Destino);
        trabajo.Origen.Should().Be("flito");
        trabajo.Evento.Should().Be("tramite.cambio");

        var aviso = await EstadoAvisoAsync(Radicado);
        aviso.Pendiente.Should().BeFalse();
        aviso.Encolado.Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC2_ElCuerpoTraeSoloLosSieteCamposYLaFirmaValidaConLaClave()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await using var sp = Servicios();

        await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken);

        var (_, trabajo) = (await TrabajosAsync()).Single();
        using var cuerpo = JsonDocument.Parse(trabajo.Cuerpo);
        cuerpo.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["eventId", "tipo", "id", "syncVersion", "fechaUltimoCambio", "eliminado", "tenantId"]);
        cuerpo.RootElement.GetProperty("tipo").GetString().Should().Be("tramite.cambio");
        cuerpo.RootElement.GetProperty("id").GetGuid().Should().Be(Radicado);
        cuerpo.RootElement.GetProperty("syncVersion").GetInt64().Should().Be(await VersionAsync(Radicado));
        cuerpo.RootElement.GetProperty("eliminado").GetBoolean().Should().BeFalse();
        cuerpo.RootElement.GetProperty("tenantId").GetGuid().Should().Be(Cliente);
        cuerpo.RootElement.GetProperty("eventId").GetGuid().Version.Should().Be(7, "eventId es UUIDv7");

        var esperada = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Clave), Encoding.UTF8.GetBytes(trabajo.Cuerpo))).ToLowerInvariant();
        trabajo.Cabeceras["X-Webhook-Signature"].Should().Be($"sha256={esperada}");
        trabajo.Cabeceras["X-Correlation-Id"].Should().Be(cuerpo.RootElement.GetProperty("eventId").GetString());
        trabajo.Cuerpo.Should().NotContain(Clave);
    }

    [PostgresFact]
    public async Task AC3_UnCambioDentroDeLaVentanaDeEstabilidadNoSeTomaEnEseCiclo()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await using var sp = Servicios(lag: TimeSpan.FromMinutes(5));

        (await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        (await TrabajosAsync()).Should().BeEmpty();
        (await EstadoAvisoAsync(Radicado)).Pendiente.Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC4_UnBorradorNuncaRadicadoSeCierraSinDejarTrabajo()
    {
        await SembrarAsync();
        await using var sp = Servicios();

        await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken);

        (await TrabajosAsync()).Should().BeEmpty();
        var aviso = await EstadoAvisoAsync(Borrador);
        aviso.Pendiente.Should().BeFalse("el aviso se cierra; lo reabre el próximo cambio");
        aviso.Encolado.Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC5_UnTramiteRadicadoYLuegoEliminadoViajaComoTombstone()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await EjecutarAsync($"UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = '{Radicado}'");
        await using var sp = Servicios();

        await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken);

        var (_, trabajo) = (await TrabajosAsync()).Single();
        using var cuerpo = JsonDocument.Parse(trabajo.Cuerpo);
        cuerpo.RootElement.GetProperty("eliminado").GetBoolean().Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC6_DosReplicasALaVezEncolanCadaVersionUnaSolaVez()
    {
        await SembrarAsync();
        var ids = new List<Guid> { Radicado };
        for (var n = 10; n < 30; n++)
        {
            var id = Guid.Parse($"5a5a5a5a-00{n:D2}-4000-8000-000000013624");
            await SembrarTramiteAsync(id, n);
            ids.Add(id);
        }

        foreach (var id in ids)
        {
            await RadicarAsync(id);
        }

        await using var sp = Servicios(lote: 5);
        var a = Despachador(sp);
        var b = Despachador(sp);
        var ct = TestContext.Current.CancellationToken;
        for (var ronda = 0; ronda < 10; ronda++)
        {
            await Task.WhenAll(a.ProcesarPendientesAsync(ct), b.ProcesarPendientesAsync(ct));
        }

        var porTramite = (await TrabajosAsync())
            .Select(t => JsonDocument.Parse(t.Trabajo.Cuerpo).RootElement.GetProperty("id").GetGuid())
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());
        porTramite.Keys.Should().BeEquivalentTo(ids);
        porTramite.Values.Should().OnlyContain(n => n == 1);
    }

    [PostgresFact]
    public async Task AC6_ApagadoNoEncolaNadaNiFalla()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await using var sp = Servicios(encendido: false);

        (await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        (await TrabajosAsync()).Should().BeEmpty();
        (await EstadoAvisoAsync(Radicado)).Pendiente.Should().BeTrue("al encender, el aviso sale");
    }

    [PostgresFact]
    public async Task AC6_SinBusDeTramitesNoEncolaNiFalla()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await using var sp = Servicios(conBus: false);

        (await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        (await EstadoAvisoAsync(Radicado)).Pendiente.Should().BeTrue();
    }

    [PostgresFact]
    public async Task ElAvisoYElTrabajoSeConfirmanJuntos_SiFallaLaOutboxElAvisoSiguePendiente()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await using var sp = Servicios(outboxQueFalla: true);

        (await Despachador(sp).ProcesarPendientesAsync(TestContext.Current.CancellationToken))
            .Should().Be(1, "solo cuenta el borrador, que se cierra sin enviar; el aviso que falla se aparta sin frenar el ciclo");

        (await TrabajosAsync()).Should().BeEmpty();
        (await EstadoAvisoAsync(Radicado)).Pendiente.Should().BeTrue("la transacción se revirtió completa");
    }

    [PostgresFact]
    public async Task UnCambioDespuesDelDespachoGeneraOtroAvisoConLaVersionNueva()
    {
        await SembrarAsync();
        await RadicarAsync(Radicado);
        await using var sp = Servicios();
        var d = Despachador(sp);
        await d.ProcesarPendientesAsync(TestContext.Current.CancellationToken);

        await EjecutarAsync($"UPDATE tramites.procedure_instances SET status = status WHERE id = '{Radicado}'");
        await d.ProcesarPendientesAsync(TestContext.Current.CancellationToken);

        var versiones = (await TrabajosAsync())
            .Select(t => JsonDocument.Parse(t.Trabajo.Cuerpo).RootElement)
            .Where(e => e.GetProperty("id").GetGuid() == Radicado)
            .Select(e => e.GetProperty("syncVersion").GetInt64())
            .ToList();
        versiones.Should().HaveCount(2);
        versiones[1].Should().BeGreaterThan(versiones[0]);
        versiones[1].Should().Be(await VersionAsync(Radicado));
    }

    [PostgresFact]
    public async Task UnaTransaccionMasAntiguaAbiertaRetieneElAvisoComoElFeed()
    {
        await SembrarAsync();
        var ct = TestContext.Current.CancellationToken;

        // L: cualquier transacción con id asignado que sigue abierta. El feed no entrega cambios confirmados después de
        // que L empezó (pg_snapshot_xmin); el aviso tampoco puede salir antes.
        await using var connL = await Fixture.OpenConnectionAsync();
        await using var txL = await connL.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("SELECT pg_current_xact_id()", connL, txL))
        {
            await cmd.ExecuteScalarAsync(ct);
        }

        await RadicarAsync(Radicado);
        await using var sp = Servicios();
        var d = Despachador(sp);

        await d.ProcesarPendientesAsync(ct);
        (await TrabajosAsync()).Should().BeEmpty("con L abierta el cambio todavía no está en el feed");
        (await EstadoAvisoAsync(Radicado)).Pendiente.Should().BeTrue();

        await txL.CommitAsync(ct);
        await d.ProcesarPendientesAsync(ct);
        (await TrabajosAsync()).Should().ContainSingle();
    }

    [PostgresFact]
    public async Task ConEscritoresConcurrentesLaUltimaVersionDeCadaTramiteRadicadoSeAvisa()
    {
        await SembrarAsync();
        var ids = new List<Guid>();
        for (var n = 40; n < 52; n++)
        {
            var id = Guid.Parse($"5a5a5a5a-00{n:D2}-4000-8000-000000013624");
            await SembrarTramiteAsync(id, n);
            ids.Add(id);
        }

        await using var sp = Servicios(lote: 5);
        var d = Despachador(sp);
        using var fin = new CancellationTokenSource();
        var despachador = Task.Run(async () =>
        {
            while (!fin.IsCancellationRequested)
            {
                await d.ProcesarPendientesAsync(CancellationToken.None);
                await Task.Delay(10);
            }
        });

        // Escritores: transacciones que radican o cambian un trámite y confirman tras una pausa, mientras el
        // despachador reclama. Varios toman la versión y confirman fuera de orden.
        var escritores = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            var azar = new Random(13624 + w);
            for (var m = 0; m < 15; m++)
            {
                var id = ids[azar.Next(ids.Count)];
                await using var conn = await Fixture.OpenConnectionAsync();
                await using var tx = await conn.BeginTransactionAsync();
                var sql = azar.Next(2) == 0
                    ? $"INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES ('{Cliente}', '{id}', 'entregado')"
                    : $"UPDATE tramites.procedure_instances SET status = status WHERE id = '{id}'";
                await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                await using (var pausa = new NpgsqlCommand("SELECT pg_sleep(@s)", conn, tx))
                {
                    pausa.Parameters.AddWithValue("s", azar.Next(0, 80) / 1000.0);
                    await pausa.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
        })).ToList();

        await Task.WhenAll(escritores);
        await fin.CancelAsync();
        await despachador;
        while (await d.ProcesarPendientesAsync(TestContext.Current.CancellationToken) > 0)
        {
        }

        var ultimoAvisado = (await TrabajosAsync())
            .Select(t => JsonDocument.Parse(t.Trabajo.Cuerpo).RootElement)
            .GroupBy(e => e.GetProperty("id").GetGuid())
            .ToDictionary(g => g.Key, g => g.Max(e => e.GetProperty("syncVersion").GetInt64()));
        foreach (var id in ids)
        {
            var radicado = await EscalarAsync<long>(
                $"SELECT count(*) FROM tramites.procedure_instance_status_history WHERE procedure_instance_id = '{id}' AND to_status = 'entregado'") > 0;
            if (radicado)
            {
                ultimoAvisado.Should().ContainKey(id, "un trámite radicado tiene que haberse avisado");
                ultimoAvisado[id].Should().Be(await VersionAsync(id), $"la última versión de {id} tiene que haberse avisado");
            }
            else
            {
                ultimoAvisado.Should().NotContainKey(id, "un borrador nunca radicado no se avisa");
            }
        }

        (await EscalarAsync<long>("SELECT count(*) FROM integrations.flito_sync_signals WHERE pendiente_desde IS NOT NULL"))
            .Should().Be(0, "no queda nada pendiente");
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static FlitoSignalDispatcher Despachador(ServiceProvider sp) =>
        new(sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<FlitoSignalsOptions>>(),
            NullLogger<FlitoSignalDispatcher>.Instance);

    private ServiceProvider Servicios(
        TimeSpan? lag = null, int lote = 50, bool encendido = true, bool conBus = true, bool outboxQueFalla = false)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://sin-broker:sin-clave@127.0.0.1:1/flit",
            ["Integrations:FlitoSignals:Enabled"] = encendido ? "true" : "false",
            ["Integrations:FlitoSignals:TargetUrl"] = Destino,
            ["Integrations:FlitoSignals:SigningKey"] = Clave,
            ["Integrations:FlitoSignals:StabilityLag"] = (lag ?? TimeSpan.Zero).ToString(),
            ["Integrations:FlitoSignals:BatchSize"] = lote.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Fixture.CreateDbContext());
        services.Configure<FlitoSignalsOptions>(config.GetSection(FlitoSignalsOptions.SectionName));
        if (conBus)
        {
            services.AddFlitOutbox<FlitDbContext>(config);
        }

        if (outboxQueFalla)
        {
            services.AddScoped<IPlatformOutbox, OutboxQueFalla>();
        }

        return services.BuildServiceProvider();
    }

    private sealed class OutboxQueFalla : IPlatformOutbox
    {
        public EventEnvelope Enqueue<TData>(string type, int version, Guid tenantId, TData data) => throw new InvalidOperationException("outbox caída");

        public EventEnvelope EnqueueJob<TData>(string type, int version, Guid tenantId, TData data) => throw new InvalidOperationException("outbox caída");
    }

    private async Task<List<(EventEnvelope Sobre, TrabajoWebhook Trabajo)>> TrabajosAsync()
    {
        await using var ctx = NewContext();
        var filas = await ctx.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.RoutingKey == TrabajoWebhook.Tipo)
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(TestContext.Current.CancellationToken);
        return filas.Select(f =>
        {
            var sobre = EventEnvelope.FromJson(Encoding.UTF8.GetBytes(f.Payload));
            return (sobre, sobre.DataAs<TrabajoWebhook>());
        }).ToList();
    }

    private sealed record EstadoAviso(bool Pendiente, bool Encolado);

    private async Task<EstadoAviso> EstadoAvisoAsync(Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT pendiente_desde IS NOT NULL, encolado_en IS NOT NULL FROM integrations.flito_sync_signals WHERE procedure_instance_id = @x", conn);
        cmd.Parameters.AddWithValue("x", id);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await r.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue($"el trámite {id} tiene aviso");
        return new EstadoAviso(r.GetBoolean(0), r.GetBoolean(1));
    }

    private async Task<long> VersionAsync(Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT sync_version FROM tramites.procedure_instances WHERE id = @x", conn);
        cmd.Parameters.AddWithValue("x", id);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Radica el trámite: una fila de historial en <c>entregado</c> (la regla «radicado al menos una vez» del feed).</summary>
    private Task RadicarAsync(Guid id) =>
        EjecutarAsync(
            $"INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES ('{Cliente}', '{id}', 'entregado')");

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-13624", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13624@flit.test",
                DisplayName = "Gestor 13624",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await SembrarTramiteAsync(Radicado, 1);
        await SembrarTramiteAsync(Borrador, 2);
    }

    private async Task SembrarTramiteAsync(Guid id, int n)
    {
        await using var ctx = NewContext();
        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = Cliente,
            ProcedureTypeId = tipo,
            ReferenceNumber = $"IT13624-{n}",
            Status = TramiteEstado.Borrador,
            Vin = $"VIN13624{n:D9}",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
