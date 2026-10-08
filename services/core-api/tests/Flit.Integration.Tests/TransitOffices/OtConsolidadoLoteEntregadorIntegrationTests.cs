using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
using Flit.Tramites.Application.Storage;
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
using NSubstitute;
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
///   <item>AC9: de punta a punta con el carril de empaquetado REAL (#13377 + #13378): 2 partes con los nombres CF-13,
///   <c>omitidos.csv</c> por parte, <c>lote_finalizado</c> y la descarga del dueño (#13379) con <c>parte_descargada</c>.</item>
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

    private async Task SembrarAsync(int maxPdfsPorParte = 500)
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
                (id, is_active, item_slots, item_timeout_seconds, item_lease_seconds, max_item_attempts, retry_delay_seconds,
                 max_pdfs_per_part)
            VALUES (uuidv7(), true, 1, 300, 600, 3, 5, @n);
            """,
            ("n", maxPdfsPorParte));
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

    /// <summary>
    /// Carril de ítems del origen <c>ot_bandeja</c>. Con <paramref name="empaquetado"/> registra además el carril de
    /// empaquetado REAL (#13378: <see cref="ConsolidadoLoteEmpaquetado"/> + <see cref="EmpaquetarParteHandler"/> con el
    /// cifrado de la prueba); sin él, el procesador se lo salta.
    /// </summary>
    private Carril NuevoCarril(Generador generador, GuardEspia guard, Empaquetado? empaquetado = null)
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
        if (empaquetado is not null)
        {
            services.AddScoped<IConsolidadoLoteEmpaquetado>(sp => new ConsolidadoLoteEmpaquetado(sp.GetRequiredService<FlitDbContext>()));
            services.AddSingleton<IConsolidadoLoteCipher>(new ConsolidadoLoteCipher(_dataProtection));
            services.AddSingleton<IConsolidadoLoteParteStorage>(empaquetado.Partes);
            services.AddSingleton<IAttachmentStorage>(empaquetado.Adjuntos);
            services.AddSingleton(Substitute.For<IConsolidadoLoteAdjuntoActual>());
            services.AddSingleton(empaquetado.Temporales);
            services.AddScoped<EmpaquetarParteHandler>();
        }

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

    // ── AC9 ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC9 — lote OT de punta a punta con <c>max_pdfs_per_part = 1</c>: carril de ítems REAL → cierre del carril (#13377)
    /// → empaquetado y cifrado REALES (#13378) → terminal con <c>lote_finalizado</c>; y la descarga (#13379) por el
    /// ot_admin dueño escribe <c>parte_descargada</c>, mientras otro ot_admin del mismo OT recibe 404.
    /// <para>Caso de asignación que ejercita: <b>omitido sin parte → primera parte que sale</b>. X es el más reciente de
    /// la bandeja (orden «fecha desc»), así que tiene <c>position = 0</c>; con <c>item_slots = 1</c> se cierra como
    /// omitido antes que C1 y C2, y el cierre del primer incluido saca la parte 1 con su PDF y la fila de X. El caso del
    /// omitido tardío (parte extra solo con CSV) daría 3 partes y lo cubren los tests de <c>AsignadorDePartes</c>.</para>
    /// </summary>
    [PostgresFact]
    public async Task AC9_LoteOtDePuntaAPunta_DosPartesConNombres_OmitidosCsvPorParte_YLoteFinalizado()
    {
        await SembrarAsync(maxPdfsPorParte: 1);
        var c1 = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);
        var c2 = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2);
        var x = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X);
        await ExecAsync("UPDATE tramites.procedure_instances SET created_at = now() WHERE id = @x", ("x", x));
        var lote = await CrearLoteAsync(HierarchyScenario.C1, HierarchyScenario.C2, HierarchyScenario.X);
        (await ScalarAsync<int>(
                "SELECT position FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND procedure_instance_id = @x",
                ("l", lote), ("x", x)))
            .Should().Be(0, "precondición: X es el primero de la bandeja y del carril");

        // Después de crear el lote, X se borra lógicamente: «Acceso revocado».
        await using (var ctx = NewContext())
        {
            (await ctx.ProcedureInstances.SingleAsync(p => p.Id == x, Ct)).DeletedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync(Ct);
        }

        using var empaquetado = new Empaquetado();
        var generador = new Generador();
        await using (var carril = NuevoCarril(generador, new GuardEspia(), empaquetado))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(
                async () => !ConsolidadoExportStatus.EsActivo(await ScalarAsync<string>(
                    "SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote))),
                "el lote llega a un estado terminal");
            await carril.Processor.StopAsync(Ct);
        }

        (await ItemAsync(lote, x)).Should().Be("omitido||acceso_revocado|Acceso revocado|0");
        (await ScalarAsync<string>(
                """
                SELECT status || '|' || included_count || '|' || omitted_count || '|' || parts_count
                  FROM tramites.consolidado_export_batches WHERE id = @l
                """,
                ("l", lote)))
            .Should().Be($"{ConsolidadoExportStatus.CompletadoConOmitidos}|2|1|2");

        // Reparto de #13377: el primer incluido y X en la parte 1; el segundo incluido en la parte 2.
        var primero = await ScalarAsync<Guid>(
            """
            SELECT procedure_instance_id FROM tramites.consolidado_export_batch_items
             WHERE batch_id = @l AND status = 'incluido' ORDER BY processed_at, position LIMIT 1
            """,
            ("l", lote));
        var segundo = primero == c1 ? c2 : c1;
        (await ParteDelItemAsync(lote, primero)).Should().Be(1);
        (await ParteDelItemAsync(lote, x)).Should().Be(1, "el omitido sin parte va en la primera parte que sale");
        (await ParteDelItemAsync(lote, segundo)).Should().Be(2);

        // lote_finalizado: una sola fila, con parts_count = batches.parts_count = 2.
        (await ScalarAsync<string>(
                """
                SELECT count(*) || '|' || min(a.parts_count) || '|' || bool_and(a.parts_count = b.parts_count)
                  FROM tramites.consolidado_export_audit a
                  JOIN tramites.consolidado_export_batches b ON b.id = a.batch_id
                 WHERE a.batch_id = @l AND a.event = 'lote_finalizado'
                """,
                ("l", lote)))
            .Should().Be("1|2|true");

        // Nombres CF-13 en la consulta del dueño.
        var radicado = await RadicadosAsync(lote);
        var cifrador = new ConsolidadoLoteCipher(_dataProtection);
        DateTimeOffset creadoEn;
        await using (var ctx = NewContext())
        {
            var lectura = new ConsolidadoLoteLectura(ctx);
            creadoEn = (await lectura.ObtenerDelDuenoAsync(lote, UsuarioOt, Ct))!.CreatedAt;
            var consultado = await new ConsultarLoteConsolidadosHandler(lectura).PorIdAsync(new ObtenerLoteQuery(lote, UsuarioOt), Ct);
            consultado!.Partes.Select(p => p.NombreArchivo).Should().Equal(
                ConsolidadoLoteNombres.Zip(creadoEn, 1, 2), ConsolidadoLoteNombres.Zip(creadoEn, 2, 2));
            consultado.Partes.Select(p => (p.Numero, p.Pdfs, p.Omitidos)).Should().Equal((1, 1, 1), (2, 1, 0));
        }

        // Descarga por el ot_admin dueño (#13379): ZIP descifrado con su PDF y su omitidos.csv; parte_descargada en BD.
        var esperado = new Dictionary<int, (Guid Pdf, string[] Filas)>
        {
            [1] = (primero, [$"{radicado[x]};{HierarchyScenario.SharedPlate};Acceso revocado"]),
            [2] = (segundo, []),
        };
        foreach (var (numero, (pdf, filas)) in esperado)
        {
            await using var ctx = NewContext();
            var handler = new DescargarParteHandler(new ConsolidadoLoteLectura(ctx), empaquetado.Partes, cifrador);
            var r = await handler.PrepararAsync(
                new DescargarParteQuery(lote, numero, UsuarioOt, "ot_admin", System.Net.IPAddress.Parse("10.13.39.2"), "pruebas-13392"),
                Ct);
            r.Estado.Should().Be(DescargarParteEstado.Lista);
            await using var descarga = r.Descarga!;
            descarga.NombreArchivo.Should().Be(ConsolidadoLoteNombres.Zip(creadoEn, numero, 2))
                .And.MatchRegex(@"^consolidados_\d{8}_\d{4}_parte-0[12]-de-02\.zip$");

            using var claro = new MemoryStream();
            (await handler.EscribirAsync(descarga, claro, Ct)).Should().Be(descarga.BytesEnClaro);
            claro.Position = 0;
            using var zip = new ZipArchive(claro, ZipArchiveMode.Read);
            var nombrePdf = ConsolidadoLoteNombres.Pdf(radicado[pdf], HierarchyScenario.SharedPlate);
            zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo([nombrePdf, ConsolidadoLoteNombres.OmitidosCsv]);

            var csv = LeerEntrada(zip, ConsolidadoLoteNombres.OmitidosCsv);
            csv.Take(3).Should().Equal(Encoding.UTF8.Preamble.ToArray(), "UTF-8 con BOM");
            Encoding.UTF8.GetString(csv, 3, csv.Length - 3).Should().Be(
                string.Concat(new[] { OmitidosCsvWriter.Encabezado }.Concat(filas).Select(f => f + "\r\n")),
                "separador «;», cabecera radicado;placa;motivo y la fila de X solo en la parte que le corresponde");
            LeerEntrada(zip, nombrePdf).Should().Equal(
                AdjuntosEnMemoria.Contenido(generador.RutaDe(pdf)), "el PDF es el snapshot del ítem");
        }

        (await ScalarAsync<string>(
                """
                SELECT count(*) || '|' || string_agg(part_number::text, ',' ORDER BY part_number) || '|'
                       || bool_and(actor_user_id = @u AND actor_role_code = 'ot_admin')
                  FROM tramites.consolidado_export_audit WHERE batch_id = @l AND event = 'parte_descargada'
                """,
                ("l", lote), ("u", UsuarioOt)))
            .Should().Be("2|1,2|true", "una fila parte_descargada por cada descarga del dueño");

        // Otro ot_admin del mismo OT: 404, sin abrir el almacenamiento ni auditar.
        var otroOt = Guid.Parse("0e000000-0000-4000-8000-0000000133b3");
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User
            {
                Id = otroOt,
                Email = "it-ot-13392-otro@flit.test",
                DisplayName = "Otro admin OT 13392",
                Status = "active",
                HomeTenantId = O,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await ctx.SaveChangesAsync(Ct);
        }

        var aperturas = empaquetado.Partes.Aperturas;
        await using (var ctx = NewContext())
        {
            var ajeno = await new DescargarParteHandler(new ConsolidadoLoteLectura(ctx), empaquetado.Partes, cifrador)
                .PrepararAsync(new DescargarParteQuery(lote, 1, otroOt, "ot_admin"), Ct);
            ajeno.Estado.Should().Be(DescargarParteEstado.NoEncontrada);
        }

        empaquetado.Partes.Aperturas.Should().Be(aperturas, "el 404 no abre el objeto cifrado");
        (await ScalarAsync<long>(
                "SELECT count(*) FROM tramites.consolidado_export_audit WHERE batch_id = @l AND event = 'parte_descargada'",
                ("l", lote)))
            .Should().Be(2, "el 404 no audita");
    }

    private Task<short> ParteDelItemAsync(Guid lote, Guid tramite) => ScalarAsync<short>(
        "SELECT part_number FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND procedure_instance_id = @t",
        ("l", lote), ("t", tramite));

    /// <summary>Radicado congelado de cada ítem del lote (el que usan el nombre del PDF y el CSV).</summary>
    private async Task<Dictionary<Guid, string>> RadicadosAsync(Guid lote)
    {
        var r = new Dictionary<Guid, string>();
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT procedure_instance_id, reference_number FROM tramites.consolidado_export_batch_items WHERE batch_id = @l", cn);
        cmd.Parameters.AddWithValue("l", lote);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            r[reader.GetGuid(0)] = reader.GetString(1);
        return r;
    }

    private static byte[] LeerEntrada(ZipArchive zip, string nombre)
    {
        using var origen = zip.GetEntry(nombre)!.Open();
        using var ms = new MemoryStream();
        origen.CopyTo(ms);
        return ms.ToArray();
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
        public ConcurrentDictionary<Guid, string> Rutas { get; } = new();

        /// <summary>Ruta del último binario «subido» para el trámite (AC9: el PDF que debe ir en el ZIP).</summary>
        public string RutaDe(Guid tramite) => Rutas[tramite];

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
                g.Rutas[request.ProcedureInstanceId] = path;
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

    /// <summary>
    /// Dobles del carril de empaquetado (AC9): almacenamiento en memoria de las partes cifradas y de los PDF del
    /// generador, y un directorio temporal propio que se borra al final.
    /// </summary>
    private sealed class Empaquetado : IDisposable
    {
        public PartesEnMemoria Partes { get; } = new();
        public AdjuntosEnMemoria Adjuntos { get; } = new();
        public ConsolidadoLoteTemporales Temporales { get; } =
            new(Path.Combine(Path.GetTempPath(), "flit-it-13392", Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Temporales.Directorio, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }

    /// <summary>Partes cifradas subidas por el handler; cuenta las aperturas para el 404.</summary>
    private sealed class PartesEnMemoria : IConsolidadoLoteParteStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _objetos = new();
        private int _aperturas;

        public int Aperturas => Volatile.Read(ref _aperturas);

        public async Task<StoredFile> SubirAsync(Guid loteId, int partNumber, string rutaArchivoCifrado, CancellationToken ct = default)
        {
            var bytes = await File.ReadAllBytesAsync(rutaArchivoCifrado, ct);
            var ruta = $"fm/lote-13392/{loteId:N}/{partNumber}";
            _objetos[ruta] = bytes;
            return new StoredFile(ruta, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _aperturas);
            return Task.FromResult<Stream?>(_objetos.TryGetValue(storagePath, out var b) ? new MemoryStream(b, writable: false) : null);
        }

        public void Delete(string storagePath) => _objetos.TryRemove(storagePath, out _);
    }

    /// <summary>Los PDF «subidos» por <see cref="Generador"/>: contenido determinista por ruta.</summary>
    private sealed class AdjuntosEnMemoria : IAttachmentStorage
    {
        public static byte[] Contenido(string storagePath) => Encoding.ASCII.GetBytes($"%PDF-1.4 maestro {storagePath}\n%%EOF");

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(storagePath.StartsWith("fm/maestro-13392/", StringComparison.Ordinal)
                ? new MemoryStream(Contenido(storagePath), writable: false)
                : null);

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException("el empaquetado nunca sube adjuntos");

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => throw new NotSupportedException();

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
