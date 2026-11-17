using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 3 (diseño §5.3 puntos 3-5 y 7, §7 segundo diagrama; ADR-0084) — el consumidor de
/// <c>diagnostico.derivaciones</c> y el reconciliador con PostgreSQL real y el doble del rpc (el evento se entrega como el SDK con
/// <see cref="DerivacionDePrueba.ConsumirAsync"/>):
/// <list type="bullet">
/// <item>Creado: el ítem queda CREADO, el vehículo guarda el trámite y, en la misma transacción, se libera el siguiente PENDIENTE de
/// la compañía (crédito).</item>
/// <item>Re-entrega de un ítem cerrado: sin efecto y sin llamar a Trámites.</item>
/// <item>Revalidación al ejecutar (4.6, AC7): si el dictamen dejó de ser VIABLE, OMITIDO NO_VIABLE sin llamar.</item>
/// <item>Transitorio: reintento programado en un scope propio (intentos + 1, <c>disponible_desde</c>) y señal de reintento; el ítem
/// sigue ENVIADO (conserva el crédito, no se libera otro). Agotado: FALLIDO TRAMITES_NO_DISPONIBLE y libera.</item>
/// <item>Rechazo de negocio: OMITIDO con el código de Trámites.</item>
/// <item>Reconciliador: un ítem ENVIADO colgado que Trámites sí creó cierra CREADO; si no lo creó, se re-publica una vez y a la
/// segunda queda FALLIDO.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConsumidorDerivacionTests
/// </code>
/// </example>
public sealed class ConsumidorDerivacionTests
{
    private static readonly Dictionary<string, string?> Credito2 = new() { [$"{OpcionesDerivacion.Seccion}:CreditoPorCompania"] = "2" };

    [Fact]
    public async Task Creado_cierra_el_item_fija_el_tramite_y_libera_el_siguiente_de_la_compania()
    {
        var falso = new DerivacionTramitesFalso();
        var tramite = Guid.NewGuid();
        falso.Responder = _ => DerivacionTramitesFalso.Creado(tramite, "TR-000901");
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: Credito2);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, mensajes) = await EncolarAsync(api, ct, ("TST801", "VIABLE"), ("TST802", "VIABLE"), ("TST803", "VIABLE"));

        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct);

        var vehiculo = await DerivacionDePrueba.VehiculoAsync(empresa, lote.Vehiculos[0].Id, ct);
        (vehiculo.TramiteId, vehiculo.TramiteNumero).Should().Be((tramite, "TR-000901"));
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single().Should().Match<ItemDerivacion>(i => i.Estado == EstadoItemDerivacion.Creado && i.Intentos == 1);
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[2].Id, ct)).Single().Estado.Should().Be(EstadoItemDerivacion.Enviado, "el cierre liberó el siguiente de la compañía");
        (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Should().HaveCount(3);
        var (solicitud, metadata, _) = falso.Llamadas.Single();
        solicitud.ExternalRef.Should().Be(lote.Vehiculos[0].Id.ToString());
        solicitud.Propietario.NumeroDocumento.Should().Be("99999123", "el documento se descifra solo para el rpc");
        metadata!.GetValue("x-flit-tenant-id").Should().Be(empresa.Id.ToString());
        await using var db = empresa.Db();
        (await db.Set<OperacionDerivacion>().AsNoTracking().SingleAsync(ct)).Creadas.Should().Be(1);
    }

    [Fact]
    public async Task Reentrega_de_un_item_cerrado_no_llama_a_Tramites()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: Credito2);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, _, mensajes) = await EncolarAsync(api, ct, ("TST811", "VIABLE"));
        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct);

        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct);

        falso.Llamadas.Should().ContainSingle("la segunda entrega encuentra el ítem cerrado");
    }

    [Fact]
    public async Task Revalida_al_ejecutar_y_omite_si_el_dictamen_ya_no_es_viable()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, mensajes) = await EncolarAsync(api, ct, ("TST821", "VIABLE"));
        await using (var db = empresa.Db())
        {
            var dictamen = await db.Set<DictamenVehiculo>().SingleAsync(d => d.VehiculoId == lote.Vehiculos[0].Id && d.Vigente, ct);
            dictamen.Veredicto = "BLOQUEADO";
            await db.SaveChangesAsync(ct);
        }

        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct);

        var item = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single();
        (item.Estado, item.MotivoCodigo).Should().Be((EstadoItemDerivacion.Omitido, MotivoDerivacion.NoViable));
        falso.Llamadas.Should().BeEmpty();
    }

    // HU #14563 AC31 (ADR-0084 §4) — derivación en vuelo tras el ajuste: con crédito 1, un ítem queda ENVIADO y otro PENDIENTE. FLIT
    // ajusta los dos dictámenes a BLOQUEADO por el PUT del backoffice antes de que el consumidor los procese. El consumidor revalida
    // y deja cada ítem OMITIDO NO_VIABLE sin llamar a Trámites. La fila sube su versión con el ajuste y otra vez con el cierre del
    // ítem, y en el sondeo listos_tramite queda en 0 (bajó desde 2 antes de encolar y el OMITIDO no lo devuelve a listo).
    [Fact]
    public async Task HU14563_AC31_ajuste_a_BLOQUEADO_con_items_en_vuelo_los_deja_OMITIDO_y_la_fila_sube()
    {
        var falso = new DerivacionTramitesFalso { Responder = _ => DerivacionTramitesFalso.Creado(Guid.NewGuid(), "TR-000931") };
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: new() { [$"{OpcionesDerivacion.Seccion}:CreditoPorCompania"] = "1" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST931", true, "VIABLE"), ("TST932", true, "VIABLE")]);
        using var compania = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var antes = await TableroAyudas.JsonAsync(await compania.GetAsync(TableroAyudas.RutaGrilla(lote.Id, desde: 0), ct), ct);
        antes.GetProperty("conteos").GetProperty("listos_tramite").GetInt32().Should().Be(2);

        var encolado = await compania.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = lote.Vehiculos.Select(v => v.Id) }, ct);
        (await encolado.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("encolados").GetInt32().Should().Be(2);
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single().Estado.Should().Be(EstadoItemDerivacion.Enviado);
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[1].Id, ct)).Single().Estado.Should().Be(EstadoItemDerivacion.Pendiente);
        var enVuelo = (await TableroAyudas.JsonAsync(await compania.GetAsync(TableroAyudas.RutaGrilla(lote.Id, desde: 0), ct), ct)).GetProperty("version").GetInt64();

        using var soporte = api.Cliente(api.Token(
            empresa: Guid.CreateVersion7(), permisos: [Permisos.BackofficeVer, Permisos.DictamenOverride], roles: ["backoffice_diagnostico"],
            tipoEmpresa: UsuarioActual.TipoEmpresaFlit));
        foreach (var vehiculo in lote.Vehiculos)
        {
            using var cuerpo = new StringContent(
                """{"version_esperada":1,"veredicto":"BLOQUEADO","motivo":"Soporte confirmo el embargo con el organismo","alertamientos":[{"codigo":"AJUSTE_FLIT","texto":"Embargo confirmado por soporte","efecto":"BLOQUEA","fuente":"RUNT"}]}""",
                Encoding.UTF8, "application/json");
            var ajuste = await soporte.PutAsync(
                $"{Program.Prefijo}/backoffice/diagnosticos/{lote.Id}/vehiculos/{vehiculo.Id}/dictamen?empresa={empresa.Id}", cuerpo, ct);
            ajuste.StatusCode.Should().Be(HttpStatusCode.OK, await ajuste.Content.ReadAsStringAsync(ct));
        }

        var trasAjuste = await TableroAyudas.JsonAsync(await compania.GetAsync(TableroAyudas.RutaGrilla(lote.Id, desde: enVuelo), ct), ct);
        var filasAjuste = trasAjuste.GetProperty("vehiculos").EnumerateArray().ToList();
        filasAjuste.Should().HaveCount(2).And.OnlyContain(f => f.GetProperty("veredicto").GetString() == "BLOQUEADO" && f.GetProperty("version").GetInt64() > enVuelo);
        var versionAjuste = trasAjuste.GetProperty("version").GetInt64();

        // El consumidor procesa el ENVIADO; el cierre libera el PENDIENTE, que también llega al consumidor.
        var consumidos = new HashSet<Guid>();
        for (var vuelta = 0; vuelta < 3; vuelta++)
        {
            var pendientes = (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Where(m => consumidos.Add(m.Data.ItemId)).ToList();
            if (pendientes.Count == 0)
                break;
            foreach (var mensaje in pendientes)
                await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensaje.Data, ct);
        }

        foreach (var vehiculo in lote.Vehiculos)
        {
            var item = (await DerivacionDePrueba.ItemsAsync(empresa, vehiculo.Id, ct)).Single();
            (item.Estado, item.MotivoCodigo).Should().Be((EstadoItemDerivacion.Omitido, MotivoDerivacion.NoViable), "revalida con el dictamen ajustado");
        }

        falso.Llamadas.Should().BeEmpty("ningún ítem llega a Trámites con el dictamen bloqueado");
        var sondeo = await TableroAyudas.JsonAsync(await compania.GetAsync(TableroAyudas.RutaGrilla(lote.Id, desde: versionAjuste), ct), ct);
        var filas = sondeo.GetProperty("vehiculos").EnumerateArray().ToList();
        filas.Should().HaveCount(2).And.OnlyContain(f =>
            f.GetProperty("version").GetInt64() > versionAjuste && f.GetProperty("derivacion").GetProperty("estado").GetString() == "OMITIDO");
        sondeo.GetProperty("conteos").GetProperty("listos_tramite").GetInt32().Should().Be(0);
        sondeo.GetProperty("conteos").GetProperty("bloqueados").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Transitorio_programa_el_reintento_conserva_el_credito_y_agotado_queda_FALLIDO()
    {
        var falso = new DerivacionTramitesFalso { Falla = StatusCode.ResourceExhausted };
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: new() { [$"{OpcionesDerivacion.Seccion}:CreditoPorCompania"] = "1", [$"{OpcionesDerivacion.Seccion}:MaxIntentos"] = "2" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, mensajes) = await EncolarAsync(api, ct, ("TST831", "VIABLE"), ("TST832", "VIABLE"));
        var antes = DateTimeOffset.UtcNow;

        var reintento = async () => await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct);

        await reintento.Should().ThrowAsync<ReintentoProgramadoException>("el SDK lleva el mensaje a .retry.1");
        var item = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single();
        (item.Estado, item.Intentos).Should().Be((EstadoItemDerivacion.Enviado, (short)1), "conserva el crédito");
        item.DisponibleDesde.Should().BeOnOrAfter(antes.AddSeconds(29));
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[1].Id, ct)).Single().Estado.Should().Be(EstadoItemDerivacion.Pendiente, "no se libera otro mientras reintenta");

        await using (var db = empresa.Db())
        {
            var fila = await db.Set<ItemDerivacion>().SingleAsync(i => i.Id == item.Id, ct);
            fila.DisponibleDesde = DateTimeOffset.UtcNow.AddSeconds(-1); // la espera venció
            await db.SaveChangesAsync(ct);
        }

        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct);

        var fallido = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single();
        (fallido.Estado, fallido.MotivoCodigo, fallido.Intentos).Should().Be((EstadoItemDerivacion.Fallido, MotivoDerivacion.TramitesNoDisponible, (short)2));
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[1].Id, ct)).Single().Estado.Should().Be(EstadoItemDerivacion.Enviado, "el cierre libera el siguiente");
        falso.Llamadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Resultado_demorado_antes_de_su_espera_da_otra_vuelta_sin_llamar()
    {
        var falso = new DerivacionTramitesFalso { Falla = StatusCode.Unavailable };
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, _, mensajes) = await EncolarAsync(api, ct, ("TST841", "VIABLE"));
        await FluentActions.Awaiting(() => DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct)).Should().ThrowAsync<ReintentoProgramadoException>();

        await FluentActions.Awaiting(() => DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct)).Should().ThrowAsync<ReintentoProgramadoException>();

        falso.Llamadas.Should().ContainSingle("la espera de 30 s no venció: el mensaje vuelve a la cola de reintentos sin llamar");
    }

    [Fact]
    public async Task Rechazo_de_negocio_queda_OMITIDO_con_el_codigo_de_Tramites()
    {
        var falso = new DerivacionTramitesFalso { Responder = _ => DerivacionTramitesFalso.Rechazo(MotivoDerivacion.OtNoHabilitado, "OT no habilitado para la compañía") };
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, mensajes) = await EncolarAsync(api, ct, ("TST851", "VIABLE"));

        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensajes[0].Data, ct);

        var item = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single();
        (item.Estado, item.MotivoCodigo).Should().Be((EstadoItemDerivacion.Omitido, MotivoDerivacion.OtNoHabilitado));
        (await DerivacionDePrueba.VehiculoAsync(empresa, lote.Vehiculos[0].Id, ct)).TramiteId.Should().BeNull();
    }

    [Fact]
    public async Task Reconciliador_cierra_CREADO_lo_que_Tramites_si_creo_y_republica_una_vez_lo_demas()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: Credito2);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, _) = await EncolarAsync(api, ct, ("TST861", "VIABLE"), ("TST862", "VIABLE"));
        var creadoEnTramites = Guid.NewGuid();
        falso.PorReferencia[lote.Vehiculos[0].Id.ToString()] = (creadoEnTramites, "TR-000861");
        await ColgarAsync(empresa, ct);
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorDerivacion>();

        var primera = await reconciliador.ReconciliarAsync(ct);

        primera.Should().Be(new VueltaReconciliadorDerivacion(Revisados: 2, Creados: 1, Republicados: 1, Fallidos: 0, EnPausa: false));
        (await DerivacionDePrueba.VehiculoAsync(empresa, lote.Vehiculos[0].Id, ct)).TramiteId.Should().Be(creadoEnTramites);
        var republicado = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[1].Id, ct)).Single();
        (republicado.Estado, republicado.Republicaciones).Should().Be((EstadoItemDerivacion.Enviado, (short)1));
        (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Count(m => m.Data.ItemId == republicado.Id).Should().Be(2, "el original y la re-publicación");
        falso.Llamadas.Should().BeEmpty("el reconciliador no crea: consulta por referencia");

        await ColgarAsync(empresa, ct);
        var segunda = await reconciliador.ReconciliarAsync(ct);

        segunda.Fallidos.Should().Be(1);
        var fallido = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[1].Id, ct)).Single();
        (fallido.Estado, fallido.MotivoCodigo).Should().Be((EstadoItemDerivacion.Fallido, MotivoDerivacion.TramitesNoDisponible));
    }

    [Fact]
    public async Task Reconciliador_sin_respuesta_de_Tramites_no_republica_ni_cierra()
    {
        var falso = new DerivacionTramitesFalso { FallaReferencias = StatusCode.Unavailable };
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, _) = await EncolarAsync(api, ct, ("TST871", "VIABLE"));
        await ColgarAsync(empresa, ct);

        var vuelta = await api.App.Services.GetRequiredService<ReconciliadorDerivacion>().ReconciliarAsync(ct);

        (vuelta.Republicados, vuelta.Fallidos, vuelta.Creados).Should().Be((0, 0, 0));
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single().Republicaciones.Should().Be(0);
    }

    // HU #14563 code review menor 7 — otro proceso confirma la misma DERIVACION_TERMINADA entre la comprobación del escritor y el
    // INSERT del reconciliador: el choque real en ux_notificaciones_idempotencia (23505) pospone la empresa (como un xmin), no tumba
    // la vuelta; la siguiente vuelta cierra el ítem y queda una sola notificación.
    [Fact]
    public async Task HU14563_menor7_choque_en_ux_notificaciones_idempotencia_pospone_la_empresa_y_la_siguiente_vuelta_cierra()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, _) = await EncolarAsync(api, ct, ("TST881", "VIABLE"));
        falso.PorReferencia[lote.Vehiculos[0].Id.ToString()] = (Guid.NewGuid(), "TR-000881");
        await ColgarAsync(empresa, ct);
        OperacionDerivacion operacion;
        await using (var db = empresa.Db())
            operacion = await db.Set<OperacionDerivacion>().AsNoTracking().SingleAsync(ct);
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorDerivacion>();

        // La notificación del otro proceso queda sin confirmar: la comprobación del escritor no la ve y su INSERT espera el índice.
        await using (var otro = await Persistencia.EsquemaR8Tests.AbrirAsync(api, ct))
        {
            await using var transaccion = await otro.BeginTransactionAsync(ct);
            await Persistencia.EsquemaR8Tests.InsertarAsync(
                otro, empresa.Id, lote.Id, Flit.Diagnostico.Api.Modulos.Notificaciones.Notificacion.DerivacionTerminada, "{}", ct,
                usuario: operacion.UsuarioId, referencia: operacion.Id);
            var vuelta = reconciliador.ReconciliarAsync(ct);
            await EsperarInsertBloqueadoAsync(api, vuelta, ct);
            await transaccion.CommitAsync(ct);

            var primera = await vuelta;

            primera.Creados.Should().Be(0, "la empresa quedó para la siguiente vuelta");
        }

        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single().Estado.Should().Be(EstadoItemDerivacion.Enviado, "se revirtió la transacción");
        var segunda = await reconciliador.ReconciliarAsync(ct);
        segunda.Creados.Should().Be(1);
        (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single().Estado.Should().Be(EstadoItemDerivacion.Creado);
        await using var conexion = await Persistencia.EsquemaR8Tests.AbrirAsync(api, ct);
        (await Persistencia.EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT referencia_id::text FROM diagnostico.notificaciones WHERE diagnostico_id = '{lote.Id}' AND tipo = 'DERIVACION_TERMINADA'", ct))
            .Should().Equal(operacion.Id.ToString());
    }

    // HU #14563 code review menor 8 — orden de bloqueo determinista: el reconciliador recorre los ítems por lote y después por id,
    // aunque la base los devuelva en otro orden. La tupla del ítem de menor id se mueve al final del heap y aun así su fila sube
    // la versión primero.
    [Fact]
    public async Task HU14563_menor8_reconciliador_cierra_los_items_por_lote_y_por_id()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: Credito2);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, lote, _) = await EncolarAsync(api, ct, ("TST891", "VIABLE"), ("TST892", "VIABLE"));
        falso.PorReferencia[lote.Vehiculos[0].Id.ToString()] = (Guid.NewGuid(), "TR-000891");
        falso.PorReferencia[lote.Vehiculos[1].Id.ToString()] = (Guid.NewGuid(), "TR-000892");
        await ColgarAsync(empresa, ct);
        var items = new List<ItemDerivacion>();
        foreach (var v in lote.Vehiculos)
            items.AddRange(await DerivacionDePrueba.ItemsAsync(empresa, v.Id, ct));
        var ordenados = items.OrderBy(i => i.Id).ToList();
        await using (var conexion = await Persistencia.EsquemaR8Tests.AbrirAsync(api, ct))
        {
            await using var mover = new Npgsql.NpgsqlCommand("UPDATE diagnostico.derivaciones SET actualizado_en = actualizado_en WHERE id = @id", conexion);
            mover.Parameters.AddWithValue("id", ordenados[0].Id);
            (await mover.ExecuteNonQueryAsync(ct)).Should().Be(1);
        }

        (await api.App.Services.GetRequiredService<ReconciliadorDerivacion>().ReconciliarAsync(ct)).Creados.Should().Be(2);

        var primero = await DerivacionDePrueba.VehiculoAsync(empresa, ordenados[0].VehiculoId, ct);
        var segundo = await DerivacionDePrueba.VehiculoAsync(empresa, ordenados[1].VehiculoId, ct);
        primero.VersionTablero.Should().BeLessThan(segundo.VersionTablero, "el ítem de menor id se cierra (y bloquea su fila) primero");
    }

    /// <summary>Espera a que la vuelta quede esperando un bloqueo (el INSERT en el índice único) o termine antes, sin pasar de 15 s.</summary>
    private static async Task EsperarInsertBloqueadoAsync(ApiDePrueba api, Task vuelta, CancellationToken ct)
    {
        await using var conexion = await Persistencia.EsquemaR8Tests.AbrirAsync(api, ct);
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (!vuelta.IsCompleted && DateTime.UtcNow < limite)
        {
            await using var comando = new Npgsql.NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", conexion);
            if (Convert.ToInt64(await comando.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) > 0)
                return;
            await Task.Delay(50, ct);
        }
    }

    /// <summary>Siembra el lote, encola todos sus vehículos por el endpoint masivo y devuelve los eventos liberados.</summary>
    private static async Task<(EmpresaDePrueba Empresa, Tablero.LoteSembrado Lote, List<(Guid Empresa, Flit.Diagnostico.Api.Modulos.Derivacion.Bus.DerivacionSolicitada Data, string Payload)> Mensajes)> EncolarAsync(
        ApiDePrueba api, CancellationToken ct, params (string Placa, string Veredicto)[] vehiculos)
    {
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [.. vehiculos.Select(v => (v.Placa, true, (string?)v.Veredicto))]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var respuesta = await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = lote.Vehiculos.Select(v => v.Id) }, ct);
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("encolados").GetInt32().Should().Be(vehiculos.Length);
        return (empresa, lote, await DerivacionDePrueba.MensajesAsync(empresa, ct));
    }

    /// <summary>Deja todos los ítems ENVIADO de la empresa como colgados (enviados hace 20 min, sin espera).</summary>
    private static async Task ColgarAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        foreach (var item in await db.Set<ItemDerivacion>().Where(i => i.Estado == EstadoItemDerivacion.Enviado).ToListAsync(ct))
        {
            item.EnviadoEn = DateTimeOffset.UtcNow.AddMinutes(-20);
            item.DisponibleDesde = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
