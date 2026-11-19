using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 3 (diseño §5.1 filas 2-5, §5.3 puntos 1-2, §7 segundo diagrama; ADR-0084) — creación masiva con PostgreSQL
/// real (<see cref="ApiDePrueba"/>) y el doble del rpc:
/// <list type="bullet">
/// <item><c>POST /diagnosticos/{id}/tramites</c>: valida que los vehículos sean del lote, crea la operación MASIVA con un ítem por
/// vehículo (los no elegibles OMITIDO con su motivo de inmediato), libera solo el crédito de la compañía por la outbox (solo ids) y
/// responde 202 con <c>Location</c>; no llama a Trámites. El doble clic no duplica ítems vivos.</item>
/// <item><c>GET …/tramites/operaciones[?ultima=true]</c> y <c>GET …/operaciones/{opId}</c>: progreso con contadores propios, ítems
/// filtrados por estado y paginados, motivos agrupados; nunca llama a core-api.</item>
/// <item><c>POST …/operaciones/{opId}/reintentar-fallidas</c>: 409 con ítems en vuelo; reabre el MISMO ítem FALLIDO.</item>
/// </list>
/// Placas TST7##, documentos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DerivacionMasivaTests
/// </code>
/// </example>
public sealed class DerivacionMasivaTests
{
    private static readonly Dictionary<string, string?> Credito2 = new() { [$"{OpcionesDerivacion.Seccion}:CreditoPorCompania"] = "2" };

    [Fact]
    public async Task Encola_los_elegibles_omite_los_no_elegibles_y_libera_solo_el_credito()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: Credito2);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST701", true, "VIABLE"), ("TST702", true, "VIABLE"), ("TST703", true, "VIABLE"), ("TST704", true, "BLOQUEADO")]);
        var ids = lote.Vehiculos.Select(v => v.Id).ToArray();
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));

        var respuesta = await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = ids }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        var operacion = cuerpo.GetProperty("operacion_id").GetGuid();
        (cuerpo.GetProperty("total").GetInt32(), cuerpo.GetProperty("encolados").GetInt32()).Should().Be((4, 3));
        var noElegible = cuerpo.GetProperty("no_elegibles").EnumerateArray().Should().ContainSingle().Subject;
        noElegible.GetProperty("vehiculo_id").GetGuid().Should().Be(ids[3]);
        noElegible.GetProperty("motivo_codigo").GetString().Should().Be(MotivoDerivacion.NoViable);
        respuesta.Headers.Location!.ToString().Should().EndWith(DerivacionDePrueba.RutaOperacion(lote.Id, operacion));

        await using var db = empresa.Db();
        var items = await db.Set<ItemDerivacion>().AsNoTracking().Where(i => i.OperacionId == operacion).OrderBy(i => i.Orden).ToListAsync(ct);
        items.Select(i => (i.VehiculoId, i.Estado)).Should().Equal(
            (ids[0], EstadoItemDerivacion.Enviado), (ids[1], EstadoItemDerivacion.Enviado), (ids[2], EstadoItemDerivacion.Pendiente), (ids[3], EstadoItemDerivacion.Omitido));
        items[3].MotivoCodigo.Should().Be(MotivoDerivacion.NoViable);
        var op = await db.Set<OperacionDerivacion>().AsNoTracking().SingleAsync(o => o.Id == operacion, ct);
        (op.Tipo, op.Estado, op.Total, op.Omitidas, op.TipoTramiteCodigo).Should().Be((TipoOperacionDerivacion.Masiva, EstadoOperacionDerivacion.EnCurso, 4, 1, "TRASPASO_STANDARD"));
        var mensajes = await DerivacionDePrueba.MensajesAsync(empresa, ct);
        mensajes.Select(m => m.Data.ItemId).Should().Equal(items[0].Id, items[1].Id);
        mensajes.Should().OnlyContain(m => !m.Payload.Contains("TST70", StringComparison.Ordinal) && !m.Payload.Contains("99999123", StringComparison.Ordinal), "el evento solo lleva ids");
        falso.Llamadas.Should().BeEmpty("el masivo no llama a Trámites en la petición");
    }

    [Fact]
    public async Task Doble_clic_no_duplica_items_vivos()
    {
        await using var api = await DerivacionDePrueba.ApiAsync(new DerivacionTramitesFalso(), extra: Credito2);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST711", true, "VIABLE"), ("TST712", true, "VIABLE"), ("TST713", true, "VIABLE")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var cuerpo = new { vehiculo_ids = lote.Vehiculos.Select(v => v.Id) };

        var respuestas = await Task.WhenAll(
            cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), cuerpo, ct), cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), cuerpo, ct));

        respuestas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        var encolados = new List<int>();
        foreach (var r in respuestas)
            encolados.Add((await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("encolados").GetInt32());
        encolados.Should().BeEquivalentTo([3, 0], "el segundo clic encuentra los ítems vivos del primero (EN_CURSO)");
        await using var db = empresa.Db();
        (await db.Set<ItemDerivacion>().CountAsync(i => i.Estado == EstadoItemDerivacion.Pendiente || i.Estado == EstadoItemDerivacion.Enviado, ct))
            .Should().Be(3, "un ítem vivo por vehículo (ux_derivaciones_vehiculo_vivo)");
    }

    [Fact]
    public async Task Vehiculo_de_otro_lote_es_400_y_no_crea_nada()
    {
        await using var api = await DerivacionDePrueba.ApiAsync(new DerivacionTramitesFalso());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var lote = await DerivacionDePrueba.SembrarAsync(empresas.A, ct, vehiculos: [("TST721", true, "VIABLE")]);
        var otro = await DerivacionDePrueba.SembrarAsync(empresas.B, ct, vehiculos: [("TST722", true, "VIABLE")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresas.A));

        var respuesta = await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = new[] { lote.Vehiculos[0].Id, otro.Vehiculos[0].Id } }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(DerivacionMasiva.CodigoVehiculoFueraDelLote);
        await using var db = empresas.A.Db();
        (await db.Set<OperacionDerivacion>().CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task Lote_simple_exige_el_tipo_y_lo_valida_contra_el_catalogo()
    {
        await using var api = await DerivacionDePrueba.ApiAsync(new DerivacionTramitesFalso());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, modo: ModoDiagnostico.Simple, vehiculos: [("TST731", true, null)]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var ids = new[] { lote.Vehiculos[0].Id };

        var sinTipo = await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = ids }, ct);
        var invalido = await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = ids, tipo_tramite_codigo = "NO_EXISTE" }, ct);
        var valido = await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = ids, tipo_tramite_codigo = "TRASPASO_UNILATERAL" }, ct);

        sinTipo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sinTipo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(DerivacionIndividual.CodigoTipoRequerido);
        invalido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        valido.StatusCode.Should().Be(HttpStatusCode.Accepted, await valido.Content.ReadAsStringAsync(ct));
        await using var db = empresa.Db();
        (await db.Set<OperacionDerivacion>().AsNoTracking().SingleAsync(ct)).TipoTramiteCodigo.Should().Be("TRASPASO_UNILATERAL");
    }

    [Fact]
    public async Task Todos_no_elegibles_la_operacion_termina_de_inmediato()
    {
        await using var api = await DerivacionDePrueba.ApiAsync(new DerivacionTramitesFalso());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST741", false, null), ("TST742", true, "BLOQUEADO")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));

        var respuesta = await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = lote.Vehiculos.Select(v => v.Id) }, ct);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("encolados").GetInt32().Should().Be(0);
        cuerpo.GetProperty("no_elegibles").EnumerateArray().Select(n => n.GetProperty("motivo_codigo").GetString())
            .Should().BeEquivalentTo([MotivoDerivacion.NoTerminado, MotivoDerivacion.NoViable]);
        await using var db = empresa.Db();
        var op = await db.Set<OperacionDerivacion>().AsNoTracking().SingleAsync(ct);
        (op.Estado, op.Omitidas, op.TerminadaEn is not null).Should().Be((EstadoOperacionDerivacion.Terminada, 2, true));
    }

    [Fact]
    public async Task Progreso_con_contadores_propios_items_filtrados_paginados_y_motivos()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: Credito2);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST751", true, "VIABLE"), ("TST752", true, "VIABLE"), ("TST753", true, "VIABLE"), ("TST754", true, "BLOQUEADO")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var alta = await (await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = lote.Vehiculos.Select(v => v.Id) }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var operacion = alta.GetProperty("operacion_id").GetGuid();
        var primero = (await DerivacionDePrueba.MensajesAsync(empresa, ct))[0].Data;
        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, primero, ct);

        var progreso = await cliente.GetFromJsonAsync<JsonElement>(DerivacionDePrueba.RutaOperacion(lote.Id, operacion), ct);
        var omitidos = await cliente.GetFromJsonAsync<JsonElement>($"{DerivacionDePrueba.RutaOperacion(lote.Id, operacion)}?estado=OMITIDO&pagina=1", ct);
        var ultima = await cliente.GetFromJsonAsync<JsonElement>($"{DerivacionDePrueba.RutaOperaciones(lote.Id)}?ultima=true", ct);

        (progreso.GetProperty("estado").GetString(), progreso.GetProperty("total").GetInt32(), progreso.GetProperty("creadas").GetInt32(),
            progreso.GetProperty("omitidas").GetInt32(), progreso.GetProperty("fallidas").GetInt32(), progreso.GetProperty("pendientes").GetInt32())
            .Should().Be(("EN_CURSO", 4, 1, 1, 0, 2));
        progreso.GetProperty("items").GetArrayLength().Should().Be(4);
        var creado = progreso.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("estado").GetString() == "CREADO");
        creado.GetProperty("placa").GetString().Should().Be("TST751");
        creado.GetProperty("tramite_numero").GetString().Should().Be("TR-000123");
        var omitido = omitidos.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        (omitido.GetProperty("motivo_codigo").GetString(), omitido.GetProperty("motivo").GetString()).Should().Be((MotivoDerivacion.NoViable, MotivoDerivacion.Mensaje(MotivoDerivacion.NoViable)));
        omitidos.GetProperty("motivos").EnumerateArray().Should().ContainSingle(m => m.GetProperty("motivo_codigo").GetString() == MotivoDerivacion.NoViable && m.GetProperty("cantidad").GetInt32() == 1);
        ultima.EnumerateArray().Should().ContainSingle().Which.GetProperty("operacion_id").GetGuid().Should().Be(operacion);
        falso.Llamadas.Should().ContainSingle("solo el consumidor llamó; el progreso no toca core-api");
        (await cliente.GetAsync($"{DerivacionDePrueba.RutaOperacion(lote.Id, operacion)}?estado=OTRO", ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reintentar_fallidas_es_409_con_items_en_vuelo_y_reabre_el_mismo_item()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: new() { [$"{OpcionesDerivacion.Seccion}:CreditoPorCompania"] = "2", [$"{OpcionesDerivacion.Seccion}:MaxIntentos"] = "1" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST761", true, "VIABLE"), ("TST762", true, "VIABLE")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var operacion = (await (await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = lote.Vehiculos.Select(v => v.Id) }, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("operacion_id").GetGuid();
        var mensajes = await DerivacionDePrueba.MensajesAsync(empresa, ct);

        (await cliente.PostAsync(DerivacionDePrueba.RutaReintentar(lote.Id, operacion), null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict, "hay ítems en vuelo");

        falso.Falla = global::Grpc.Core.StatusCode.Unavailable;
        foreach (var m in mensajes)
            await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, m.Data, ct); // MaxIntentos 1: FALLIDO a la primera
        falso.Falla = null;
        var fallido = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Single();
        fallido.Estado.Should().Be(EstadoItemDerivacion.Fallido);

        var respuesta = await cliente.PostAsync(DerivacionDePrueba.RutaReintentar(lote.Id, operacion), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("reencolados").GetInt32().Should().Be(2);
        var reabierto = (await DerivacionDePrueba.ItemsAsync(empresa, lote.Vehiculos[0].Id, ct)).Should().ContainSingle("reabre el MISMO ítem").Subject;
        (reabierto.Id, reabierto.Estado, reabierto.Intentos, reabierto.MotivoCodigo).Should().Be((fallido.Id, EstadoItemDerivacion.Enviado, (short)0, (string?)null));
        await using var db = empresa.Db();
        var op = await db.Set<OperacionDerivacion>().AsNoTracking().SingleAsync(o => o.Id == operacion, ct);
        (op.Estado, op.Fallidas, op.TerminadaEn).Should().Be((EstadoOperacionDerivacion.EnCurso, 0, (DateTimeOffset?)null));
    }

    // Revisión de seguridad 2026-11-11: el borrador de un ítem reintentado queda a nombre de quien pidió el reintento, no del
    // usuario que lanzó la operación original (que pudo perder el permiso o ya no estar en la compañía).
    [Fact]
    public async Task Reintentar_fallidas_crea_los_borradores_a_nombre_de_quien_reintenta()
    {
        var falso = new DerivacionTramitesFalso();
        await using var api = await DerivacionDePrueba.ApiAsync(falso, extra: new() { [$"{OpcionesDerivacion.Seccion}:MaxIntentos"] = "1" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST781", true, "VIABLE")]);
        var (quienLanza, quienReintenta) = (Guid.NewGuid(), Guid.NewGuid());
        string Token(Guid usuario) => api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear, Permisos.DerivacionCrear], roles: ["usuario_diagnostico"], sujeto: usuario.ToString());
        using var lanza = api.Cliente(Token(quienLanza));
        var operacion = (await (await lanza.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = new[] { lote.Vehiculos[0].Id } }, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("operacion_id").GetGuid();
        falso.Falla = global::Grpc.Core.StatusCode.Unavailable;
        foreach (var m in await DerivacionDePrueba.MensajesAsync(empresa, ct))
            await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, m.Data, ct); // MaxIntentos 1: FALLIDO a la primera
        falso.Falla = null;
        falso.Llamadas.Should().ContainSingle().Which.Solicitud.UsuarioId.Should().Be(quienLanza.ToString(), "el primer intento es de quien lanzó");
        var primeros = (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Count;

        using var reintenta = api.Cliente(Token(quienReintenta));
        (await reintenta.PostAsync(DerivacionDePrueba.RutaReintentar(lote.Id, operacion), null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var nuevos = (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Skip(primeros).ToList();
        nuevos.Should().ContainSingle("el reintento re-encola el ítem");
        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, nuevos[0].Data, ct);

        falso.Llamadas.Should().HaveCount(2);
        falso.Llamadas.Last().Solicitud.UsuarioId.Should().Be(quienReintenta.ToString(), "el borrador reintentado es de quien reintentó");
        await using var db = empresa.Db();
        (await db.Set<OperacionDerivacion>().AsNoTracking().SingleAsync(o => o.Id == operacion, ct)).UsuarioId.Should().Be(quienLanza, "la operación conserva a quien la lanzó");
    }

    [Fact]
    public async Task Progreso_de_un_lote_de_otra_compania_es_404()
    {
        await using var api = await DerivacionDePrueba.ApiAsync(new DerivacionTramitesFalso());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var deB = await DerivacionDePrueba.SembrarAsync(empresas.B, ct, vehiculos: [("TST771", true, "VIABLE")]);
        var operacion = (await (await api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresas.B)).PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(deB.Id), new { vehiculo_ids = new[] { deB.Vehiculos[0].Id } }, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("operacion_id").GetGuid();
        using var deA = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresas.A));

        (await deA.GetAsync(DerivacionDePrueba.RutaOperacion(deB.Id, operacion), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await deA.PostAsync(DerivacionDePrueba.RutaReintentar(deB.Id, operacion), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
