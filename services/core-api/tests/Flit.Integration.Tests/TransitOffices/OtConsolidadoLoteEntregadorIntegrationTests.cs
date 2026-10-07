using System.Collections.Concurrent;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.OtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// HU #13392 (Épica #13216, Feature #13308) — el lote de maestros de la bandeja OT procesado por el carril REAL
/// (<see cref="ConsolidadoLoteProcessor"/> + <see cref="ProcesarItemLoteHandler"/> + <see cref="OtConsolidadoLoteEntregador"/>)
/// contra PostgreSQL, con el acceso de la bandeja y el scope de la compañía cliente REALES
/// (<see cref="OtClientProcedureRepository"/> vía <see cref="OtClientProcedureConsolidadoContext"/>). El lote lo crea el
/// mismo handler que usa <c>POST /api/v1/admin/ot/consolidados/lotes</c> (#13391). Son dobles: la revalidación del
/// solicitante (la real está en <c>ConsolidadoLoteAccessCheckerTests</c>), el guard de Quipux (registra el tenant) y el
/// generador del maestro, que aplica la regla del dominio «si existe se toma; si no, se sube el binario, se inserta la
/// fila y se difiere la compensación» igual que <c>ConsolidadoReemplazoSeguro</c>.
/// <list type="bullet">
///   <item>AC2: la generación corre en una transacción abierta con el GUC <c>app.current_tenant_id</c> = compañía cliente,
///   después del guard con el tenant OT; un maestro por trámite.</item>
///   <item>AC5 + AC7: el trámite que cambió de organismo o tuvo borrado lógico se omite «Acceso revocado»; el de un
///   cliente cuyo grant se revocó sigue incluido (la bandeja de #12350 no filtra por grant).</item>
///   <item>AC8: almacenamiento no disponible ⇒ rollback de la transacción del cliente, compensación del binario nuevo e
///   ítem en <c>pendiente</c> con reintento; tras reiniciar el proceso queda un solo maestro.</item>
/// </list>
/// <para>Uso de ejemplo: <c>await using var carril = NuevoCarril(new Generador()); await carril.Processor.StartAsync(ct);</c></para>
/// </summary>
public sealed class OtConsolidadoLoteEntregadorIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid O = HierarchyScenario.O;
    private static readonly Guid UsuarioOt = Guid.Parse("0e000000-0000-4000-8000-0000000133b2");
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(30);

    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Siembra ─────────────────────────────────────────────────────────────────────────────

    private async Task SembrarAsync()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User
            {
                Id = UsuarioOt,
                Email = "it-ot-13392@flit.test",
                DisplayName = "Admin OT 13392",
                Status = "active",
                HomeTenantId = O,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await ExecAsync(
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings
                (id, is_active, item_slots, item_timeout_seconds, item_lease_seconds, max_item_attempts, retry_delay_seconds)
            VALUES (uuidv7(), true, 1, 300, 600, 3, 5);
            """);
    }

    /// <summary>Lote <c>ot_bandeja</c> del ot_admin con el handler y el resolver reales de #13391 (modo ids).</summary>
    private async Task<Guid> CrearLoteAsync(params Guid[] tenantsCliente)
    {
        await using var ctx = NewContext();
        var bandeja = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        var organismo = await bandeja.ResolveTransitOfficeIdAsync(O, null, Ct);
        var handler = new CrearLoteConsolidadosHandler(
            new ConsolidadoLoteRepository(ctx),
            new LoteSeleccionResolverPorOrigen([new OtBandejaSeleccionResolver(bandeja)]),
            new ConsolidadoLoteCipher(_dataProtection));
        var r = await handler.HandleAsync(new CrearLoteConsolidadosCommand
        {
            Origen = ConsolidadoExportOrigin.OtBandeja,
            TenantId = O,
            OtTransitOfficeId = organismo,
            UsuarioId = UsuarioOt,
            RolCodigo = "ot_admin",
            TipoDocumento = ConsolidadoExportDocumentType.ConsolidadoMaestro,
            Seleccion = new SeleccionPorIds(tenantsCliente.Select(HierarchyScenario.DeliveredProcedureOf).ToList()),
            ConfirmaEfectos = true,
            ClientIp = System.Net.IPAddress.Parse("10.13.39.2"),
            UserAgent = "pruebas-13392",
        }, Ct);
        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(tenantsCliente.Length);
        return r.Lote.Id;
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private Task<long> CerradosAsync(Guid lote) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND status IN ('incluido', 'omitido')",
        ("l", lote));

    private Task<long> MaestrosAsync(Guid tramite) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @t AND tipo = 'consolidado_maestro'",
        ("t", tramite));

    /// <summary><c>status|delivery_mode|omission_code|omission_reason|attempts</c> del ítem del trámite.</summary>
    private Task<string> ItemAsync(Guid lote, Guid tramite) => ScalarAsync<string>(
        """
        SELECT status || '|' || coalesce(delivery_mode, '') || '|' || coalesce(omission_code, '') || '|'
               || coalesce(omission_reason, '') || '|' || attempts
          FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND procedure_instance_id = @t
        """,
        ("l", lote), ("t", tramite));

    private static async Task EsperarAsync(Func<Task<bool>> condicion, string porque)
    {
        var limite = DateTime.UtcNow + Espera;
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
                return;
            await Task.Delay(50, Ct);
        }

        (await condicion()).Should().BeTrue(porque);
    }

    // ── Carril con el origen ot_bandeja ─────────────────────────────────────────────────────

    private Carril NuevoCarril(Generador generador, GuardEspia guard)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Fixture.CreateDbContext());
        services.AddScoped<IConsolidadoLoteRepository>(sp => new ConsolidadoLoteRepository(sp.GetRequiredService<FlitDbContext>()));
        services.AddScoped<IConsolidadoLoteItemProceso>(sp => new ConsolidadoLoteItemProceso(sp.GetRequiredService<FlitDbContext>()));
        services.AddSingleton<IConsolidadoLoteAccessChecker>(new SolicitanteConAcceso());
        services.AddScoped<IOtClientProcedureRepository>(sp =>
            new OtClientProcedureRepository(sp.GetRequiredService<FlitDbContext>(), new NullTramiteTransitionPublisher()));
        services.AddSingleton<IResolvedDocumentMatrixResolver>(new MatrizVacia());
        services.AddScoped<IOtClientProcedureConsolidadoContext, OtClientProcedureConsolidadoContext>();
        services.AddSingleton<IQuipuxReadOnlyGuard>(guard);
        services.AddScoped<ILoteItemEntregador>(sp => generador.Para(sp.GetRequiredService<FlitDbContext>()));
        services.AddScoped<ILoteItemOrigen, OtConsolidadoLoteEntregador>();
        services.AddScoped<LoteItemOrigenPorOrigen>();
        services.AddScoped(sp => new ProcesarItemLoteHandler(
            sp.GetRequiredService<LoteItemOrigenPorOrigen>(),
            sp.GetRequiredService<IConsolidadoLoteItemProceso>(),
            sp.GetRequiredService<ILogger<ProcesarItemLoteHandler>>()));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var processor = new ConsolidadoLoteProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConsolidadoLoteProcessor>.Instance,
            new ConsolidadoLoteProcessorOptions(TimeSpan.FromMilliseconds(100), TimeSpan.Zero),
            TimeProvider.System);
        return new Carril(provider, processor);
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_SinMaestro_GeneraEnUnaTransaccionConElTenantDelCliente_TrasElGuardConElTenantOt_UnMaestroPorTramite()
    {
        await SembrarAsync();
        var lote = await CrearLoteAsync(HierarchyScenario.C1, HierarchyScenario.C2);

        // Arrastre #13391: el lote actual del ot_admin se encuentra por su sub, aunque su tenant sea el del OT.
        await using (var ctx = NewContext())
            (await new ConsolidadoLoteRepository(ctx).ObtenerLoteActivoIdAsync(UsuarioOt, Ct)).Should().Be(lote);

        var orden = new ConcurrentQueue<string>();
        var generador = new Generador(orden);
        var guard = new GuardEspia(orden);
        await using (var carril = NuevoCarril(generador, guard))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(async () => await CerradosAsync(lote) == 2, "el carril cierra los dos ítems");
            await carril.Processor.StopAsync(Ct);
        }

        foreach (var cliente in new[] { HierarchyScenario.C1, HierarchyScenario.C2 })
        {
            var tramite = HierarchyScenario.DeliveredProcedureOf(cliente);
            generador.Contextos.Should().ContainSingle(c => c.Tramite == tramite).Which.Should().Be(
                new ContextoGeneracion(tramite, cliente, cliente.ToString(), EnTransaccion: true),
                "la petición, el GUC de la transacción y la compañía son los del cliente, nunca los del OT");
            (await MaestrosAsync(tramite)).Should().Be(1, "exactamente un maestro nuevo");
            (await ItemAsync(lote, tramite)).Should().Be("incluido|generado|||0");
        }

        guard.Llamadas.Should().HaveCount(2).And.OnlyContain(
            l => l.TenantId == O && l.Accion == OtConsolidadoLoteEntregador.AccionQuipux, "el guard usa el tenant OT del lote");
        orden.Should().Equal(["guard", "generar", "guard", "generar"], "con item_slots = 1: primero el guard, después la generación");
    }

    // ── AC5 + AC7 ───────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_AC7_MovidoDeOrganismoOBorrado_AccesoRevocado_YConGrantRevocado_SigueIncluido()
    {
        await SembrarAsync();
        var lote = await CrearLoteAsync(HierarchyScenario.C1, HierarchyScenario.C2, HierarchyScenario.X);
        var conGrantRevocado = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);
        var movido = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2);
        var borrado = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X);

        // Después de crear el lote: C1 pierde su grant hacia Ot1, el trámite de C2 pasa a Ot2 y el de X se borra.
        await using (var ctx = NewContext())
        {
            foreach (var grant in await ctx.TenantTransitOfficeGrants.Where(g => g.TenantId == HierarchyScenario.C1).ToListAsync(Ct))
                grant.IsEnabled = false;
            (await ctx.ProcedureInstances.SingleAsync(p => p.Id == movido, Ct)).TransitOfficeId = HierarchyScenario.Ot2;
            (await ctx.ProcedureInstances.SingleAsync(p => p.Id == borrado, Ct)).DeletedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync(Ct);
        }

        var generador = new Generador();
        await using (var carril = NuevoCarril(generador, new GuardEspia()))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(async () => await CerradosAsync(lote) == 3, "el carril cierra los tres ítems");
            await carril.Processor.StopAsync(Ct);
        }

        (await ItemAsync(lote, conGrantRevocado)).Should().Be("incluido|generado|||0", "D-FB1: sigue en la bandeja");
        (await ItemAsync(lote, movido)).Should().Be("omitido||acceso_revocado|Acceso revocado|0");
        (await ItemAsync(lote, borrado)).Should().Be("omitido||acceso_revocado|Acceso revocado|0");
        generador.Llamadas.Keys.Should().BeEquivalentTo([conGrantRevocado], "los que salieron de la bandeja no llegan al entregador");
        (await MaestrosAsync(movido)).Should().Be(0);
        (await MaestrosAsync(borrado)).Should().Be(0);
    }

    // ── AC8 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_AlmacenamientoNoDisponible_RevierteYCompensa_QuedaPendienteConReintento_YTrasReiniciarUnSoloMaestro()
    {
        await SembrarAsync();
        var lote = await CrearLoteAsync(HierarchyScenario.C1);
        var tramite = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);

        // 1.ª instancia: el binario se sube y la fila se guarda, pero el almacenamiento falla después.
        var caido = new Generador { FallarAlmacenamiento = true };
        await using (var carril = NuevoCarril(caido, new GuardEspia()))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(
                async () => await ItemAsync(lote, tramite) == "pendiente||||1",
                "el fallo técnico devuelve el ítem a pendiente con un intento");
            await carril.Processor.StopAsync(Ct);
        }

        (await MaestrosAsync(tramite)).Should().Be(0, "la transacción del cliente se revirtió: la fila no quedó");
        caido.Subidos.Should().ContainSingle();
        caido.Compensados.Should().Equal(caido.Subidos, "la compensación diferida borra el binario nuevo al revertir");
        (await ScalarAsync<bool>(
                "SELECT next_attempt_at > now() FROM tramites.consolidado_export_batch_items WHERE batch_id = @l",
                ("l", lote)))
            .Should().BeTrue("el reintento queda programado (retry_delay_seconds)");

        // Reinicio del proceso: el reintento vence y otra instancia lo toma.
        await ExecAsync(
            "UPDATE tramites.consolidado_export_batch_items SET next_attempt_at = now() WHERE batch_id = @l",
            ("l", lote));
        var reinicio = new Generador();
        await using (var carril = NuevoCarril(reinicio, new GuardEspia()))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(async () => await CerradosAsync(lote) == 1, "el reintento cierra el ítem");
            await carril.Processor.StopAsync(Ct);
        }

        (await MaestrosAsync(tramite)).Should().Be(1, "un solo maestro tras el reinicio");
        (await ItemAsync(lote, tramite)).Should().Be("incluido|generado|||1", "attempts conserva el fallo técnico");
        reinicio.Compensados.Should().BeEmpty();
    }

    // ── Dobles ──────────────────────────────────────────────────────────────────────────────

    private sealed record ContextoGeneracion(Guid Tramite, Guid TenantPeticion, string? GucTenant, bool EnTransaccion);

    private sealed record LlamadaGuard(Guid TenantId, string Accion);

    private sealed class Carril(ServiceProvider provider, ConsolidadoLoteProcessor processor) : IAsyncDisposable
    {
        public ConsolidadoLoteProcessor Processor { get; } = processor;

        public async ValueTask DisposeAsync()
        {
            Processor.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class SolicitanteConAcceso : IConsolidadoLoteAccessChecker
    {
        public Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class MatrizVacia : IResolvedDocumentMatrixResolver
    {
        public Task<IReadOnlyList<ResolvedDocumentMatrixItem>> ResolveAsync(
            Guid procedureTypeId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResolvedDocumentMatrixItem>>([]);
    }

    /// <summary>Guard de Quipux que siempre permite (Q11) y anota el tenant y el orden.</summary>
    private sealed class GuardEspia(ConcurrentQueue<string>? orden = null) : IQuipuxReadOnlyGuard
    {
        public ConcurrentQueue<LlamadaGuard> Llamadas { get; } = new();

        public Task<QuipuxReadOnlyResult> ValidateActionAsync(Guid tenantId, string action, CancellationToken cancellationToken = default)
        {
            Llamadas.Enqueue(new LlamadaGuard(tenantId, action));
            orden?.Enqueue("guard");
            return Task.FromResult(QuipuxReadOnlyResult.Allowed());
        }
    }

    /// <summary>
    /// Generador del maestro con la regla del dominio y la compensación diferida de <c>ConsolidadoReemplazoSeguro</c>:
    /// si el trámite tiene maestro se toma (<c>existente</c>); si no, pasa el gancho, «sube» el binario, inserta la fila
    /// en la transacción del scope y difiere el borrado del binario para el caso de rollback.
    /// </summary>
    private sealed class Generador(ConcurrentQueue<string>? orden = null)
    {
        public bool FallarAlmacenamiento { get; init; }
        public ConcurrentDictionary<Guid, int> Llamadas { get; } = new();
        public ConcurrentQueue<ContextoGeneracion> Contextos { get; } = new();
        public ConcurrentQueue<string> Subidos { get; } = new();
        public ConcurrentQueue<string> Compensados { get; } = new();

        public ConcurrentQueue<string>? Orden { get; } = orden;

        public ILoteItemEntregador Para(FlitDbContext db) => new Instancia(this, db);

        private sealed class Instancia(Generador g, FlitDbContext db) : ILoteItemEntregador
        {
            public async Task<LoteItemEntregaResult> EntregarAsync(LoteItemEntregaRequest request, CancellationToken ct = default)
            {
                g.Llamadas.AddOrUpdate(request.ProcedureInstanceId, 1, (_, n) => n + 1);
                var tx = db.Database.CurrentTransaction;

                var existente = await db.ProcedureInstanceAttachments.AsNoTracking()
                    .Where(a => a.ProcedureInstanceId == request.ProcedureInstanceId && a.Tipo == "consolidado_maestro")
                    .FirstOrDefaultAsync(ct);
                if (existente is not null)
                    return LoteItemEntregaResult.Incluido(Adjunto(existente), LoteDeliveryMode.Existente);

                if (request.AntesDeGenerar is { } gancho && await gancho(ct) is { } motivo)
                    return LoteItemEntregaResult.Omitido(motivo);

                string? guc;
                await using (var cmd = db.Database.GetDbConnection().CreateCommand())
                {
                    cmd.Transaction = tx?.GetDbTransaction();
                    cmd.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
                    guc = await cmd.ExecuteScalarAsync(ct) as string;
                }

                g.Orden?.Enqueue("generar");
                g.Contextos.Enqueue(new ContextoGeneracion(request.ProcedureInstanceId, request.TenantId, guc, tx is not null));

                var path = $"fm/maestro-13392/{Guid.NewGuid():N}";
                g.Subidos.Enqueue(path);
                var nuevo = new ProcedureInstanceAttachment
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = request.TenantId,
                    ProcedureInstanceId = request.ProcedureInstanceId,
                    Tipo = "consolidado_maestro",
                    Filename = "consolidado_maestro.pdf",
                    Mimetype = "application/pdf",
                    SizeBytes = 300,
                    Sha256 = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()),
                    StoragePath = path,
                    Source = "system",
                    UploadedAt = DateTimeOffset.UtcNow,
                };
                db.ProcedureInstanceAttachments.Add(nuevo);
                await db.SaveChangesAsync(ct);
                db.AccionesPostTransaccion.TryDiferir(
                        tx?.TransactionId,
                        alConfirmar: null,
                        alRevertir: () =>
                        {
                            g.Compensados.Enqueue(path);
                            return Task.CompletedTask;
                        })
                    .Should().BeTrue("la generación corre dentro de la transacción gestionada del scope del cliente");

                return g.FallarAlmacenamiento
                    ? LoteItemEntregaResult.Fallo("storage_unavailable")
                    : LoteItemEntregaResult.Incluido(Adjunto(nuevo), LoteDeliveryMode.Generado);
            }

            private static LoteItemAdjunto Adjunto(ProcedureInstanceAttachment a) =>
                new(a.Id, a.StoragePath, a.SizeBytes, a.Sha256, a.Filename);
        }
    }
}
