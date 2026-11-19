using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Endpoints del cliente (plan §6.1, §6.3; HU #14576 AC1–AC4, AC7, AC8, AC11, AC25, AC26, AC30, AC33; seguridad SP-8 con la
/// decisión E3, SP-9 y SP-15): marcar, desmarcar y leer el saneamiento de un vehículo. PostgreSQL real (<see cref="ApiDePrueba"/>).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SaneamientoEndpointsTests
/// </code>
/// </example>
public sealed class SaneamientoEndpointsTests
{
    /// <summary>Claves que puede traer el detalle del cliente (SP-15); cualquier otra falla.</summary>
    private static readonly HashSet<string> ClavesPermitidas = new(StringComparer.Ordinal)
    {
        "id", "version", "estado", "estado_etiqueta", "solicitado_en", "solicitado_por_mi", "tomado_en", "cerrado_en", "motivo_cierre",
        "motivo_anulacion", "observacion_flit", "renglones", "historial",
        "fuente", "hallazgo", "descripcion", "automatico", "resultado", "nota", "evidencia", "nombre", "tamano", "subida_en",
        "evento", "ocurrido_en", "actor", "motivo",
    };

    // AC1 + AC30 + AC25 — la bandera no depende del modo, del veredicto ni del trámite; la columna y la versión de fila cambian juntas.
    [Theory]
    [InlineData("SIMPLE", null)]
    [InlineData("CON_ANALISIS", "VIABLE")]
    [InlineData("CON_ANALISIS", "BLOQUEADO")]
    public async Task HU14576_AC1_marca_un_vehiculo_terminado_en_cualquier_modo_y_veredicto(string modo, string? veredicto)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var conAnalisis = modo == "CON_ANALISIS";
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN101"], ct,
            modo: conAnalisis ? ModoDiagnostico.ConAnalisis : ModoDiagnostico.Simple, tipoTramiteCodigo: conAnalisis ? "TRASPASO_STANDARD" : null);
        var vehiculo = lote.Vehiculos.Single().Id;
        if (veredicto is not null)
            await SaneamientoDePrueba.DictamenAsync(empresa, vehiculo, veredicto, ct);
        await using (var db = empresa.Db())
            await db.Set<VehiculoDiagnostico>().Where(v => v.Id == vehiculo)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.TramiteId, Guid.NewGuid()).SetProperty(v => v.TramiteNumero, "T-1"), ct);
        var usuario = Guid.NewGuid();

        var respuesta = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id, usuario: usuario)).PutAsync(SaneamientoDePrueba.Ruta(lote.Id, vehiculo), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await SaneamientoDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("estado").GetString().Should().Be("SOLICITADO");
        json.GetProperty("solicitado_por_mi").GetBoolean().Should().BeTrue();
        json.GetProperty("version").GetUInt32().Should().BeGreaterThan(0);
        var historial = json.GetProperty("historial").EnumerateArray().ToList();
        historial.Should().ContainSingle();
        historial[0].GetProperty("evento").GetString().Should().Be("SOLICITADO");
        historial[0].GetProperty("actor").GetString().Should().Be("COMPANIA");

        await using var lectura = empresa.Db();
        var fila = await lectura.Set<VehiculoDiagnostico>().AsNoTracking().SingleAsync(v => v.Id == vehiculo, ct);
        var cabecera = await lectura.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == lote.Id, ct);
        fila.SaneamientoEstado.Should().Be(EstadoSaneamiento.Solicitado);
        cabecera.VersionTablero.Should().BeGreaterThan(5, "la marca sube la versión del lote");
        fila.VersionTablero.Should().Be(cabecera.VersionTablero, "la fila toma la versión nueva del lote en la misma transacción");
        var guardado = await lectura.Set<SaneamientoEntidad>().AsNoTracking().SingleAsync(s => s.VehiculoId == vehiculo, ct);
        guardado.SolicitadoPor.Should().Be(usuario);
        guardado.TenantId.Should().Be(empresa.Id);
    }

    // AC2 — con una consulta en ERROR no se marca, y no se escribe nada.
    [Fact]
    public async Task HU14576_AC2_vehiculo_no_terminado_es_409_VEHICULO_NO_TERMINADO()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN102"], ct, abiertas: ["SAN102"]);

        var respuesta = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id)).PutAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("VEHICULO_NO_TERMINADO");
        await using var db = empresa.Db();
        (await db.Set<SaneamientoEntidad>().CountAsync(ct)).Should().Be(0);
        (await db.Set<EventoSaneamiento>().CountAsync(ct)).Should().Be(0);
    }

    // AC3 — idempotente en SOLICITADO, 409 con otro estado no anulado y uno nuevo tras ANULADO.
    [Fact]
    public async Task HU14576_AC3_idempotencia_unicidad_y_nueva_solicitud_tras_anular()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN103", "SAN104", "SAN105"], ct);
        var (v1, v2, v3) = (lote.Vehiculos[0].Id, lote.Vehiculos[1].Id, lote.Vehiculos[2].Id);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, v2, EstadoSaneamiento.EnGestion, ct);
        var anulado = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, v3, EstadoSaneamiento.Anulado, ct);
        using var cliente = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id));

        var primera = await cliente.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, v1), null, ct);
        var segunda = await cliente.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, v1), null, ct);
        var enGestion = await cliente.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, v2), null, ct);
        var trasAnular = await cliente.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, v3), null, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.Created);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, "repetir en SOLICITADO es idempotente");
        (await SaneamientoDePrueba.JsonAsync(segunda, ct)).GetProperty("id").GetGuid().Should().Be((await SaneamientoDePrueba.JsonAsync(primera, ct)).GetProperty("id").GetGuid());
        enGestion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SaneamientoDePrueba.CodigoAsync(enGestion, ct)).Should().Be("YA_SOLICITADO");
        trasAnular.StatusCode.Should().Be(HttpStatusCode.Created, await trasAnular.Content.ReadAsStringAsync(ct));
        (await SaneamientoDePrueba.JsonAsync(trasAnular, ct)).GetProperty("id").GetGuid().Should().NotBe(anulado.Id);

        await using var db = empresa.Db();
        (await db.Set<EventoSaneamiento>().CountAsync(e => e.Evento == TipoEventoSaneamiento.Solicitado && db.Set<SaneamientoEntidad>().Any(s => s.Id == e.SaneamientoId && s.VehiculoId == v1), ct))
            .Should().Be(1, "la repetición no agrega eventos");
        (await db.Set<SaneamientoEntidad>().CountAsync(s => s.VehiculoId == v3, ct)).Should().Be(2, "el anulado se conserva");
    }

    // AC4 — dos marcas a la vez: una 201 y la otra 409; queda uno solo.
    [Fact]
    public async Task HU14576_AC4_dos_marcas_simultaneas_una_gana_y_la_otra_es_409()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN106"], ct);
        var ruta = SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id);
        using var uno = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id));
        using var dos = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id));

        var respuestas = await Task.WhenAll(uno.PutAsync(ruta, null, ct), dos.PutAsync(ruta, null, ct));

        respuestas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await SaneamientoDePrueba.CodigoAsync(respuestas.Single(r => r.StatusCode == HttpStatusCode.Conflict), ct)).Should().Be("YA_SOLICITADO");
        await using var db = empresa.Db();
        (await db.Set<SaneamientoEntidad>().CountAsync(s => s.Estado != EstadoSaneamiento.Anulado, ct)).Should().Be(1);
    }

    // AC7 + AC30 — desmarcar en SOLICITADO (aunque lo marcó otro usuario): ANULADO por la compañía, columna a null y versión de fila.
    [Fact]
    public async Task HU14576_AC7_desmarcar_en_SOLICITADO()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN107"], ct);
        var vehiculo = lote.Vehiculos[0].Id;
        var sembrado = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, vehiculo, EstadoSaneamiento.Solicitado, ct);
        var usuario = Guid.NewGuid();

        var respuesta = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id, usuario: usuario)).DeleteAsync(SaneamientoDePrueba.Ruta(lote.Id, vehiculo), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NoContent, await respuesta.Content.ReadAsStringAsync(ct));
        await using var db = empresa.Db();
        var guardado = await db.Set<SaneamientoEntidad>().AsNoTracking().SingleAsync(s => s.Id == sembrado.Id, ct);
        guardado.Estado.Should().Be(EstadoSaneamiento.Anulado);
        guardado.AnuladoPorFlit.Should().BeFalse();
        guardado.AnuladoPor.Should().Be(usuario);
        guardado.MotivoAnulacion.Should().BeNull();
        var evento = await db.Set<EventoSaneamiento>().AsNoTracking().SingleAsync(e => e.SaneamientoId == sembrado.Id && e.Evento == TipoEventoSaneamiento.Desmarcado, ct);
        evento.EsFlit.Should().BeFalse();
        evento.EstadoAnterior.Should().Be(EstadoSaneamiento.Solicitado);
        evento.EstadoNuevo.Should().Be(EstadoSaneamiento.Anulado);
        var fila = await db.Set<VehiculoDiagnostico>().AsNoTracking().SingleAsync(v => v.Id == vehiculo, ct);
        fila.SaneamientoEstado.Should().BeNull();
        fila.VersionTablero.Should().BeGreaterThan(5);
    }

    // AC8 — fuera de SOLICITADO es 409 SANEAMIENTO_EN_GESTION; sin saneamiento no anulado, 404 SIN_SANEAMIENTO.
    [Fact]
    public async Task HU14576_AC8_desmarcar_fuera_de_SOLICITADO_o_sin_saneamiento()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN108", "SAN109", "SAN110"], ct);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[0].Id, EstadoSaneamiento.EnGestion, ct);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[1].Id, EstadoSaneamiento.Anulado, ct);
        using var cliente = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id));

        var enGestion = await cliente.DeleteAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), ct);
        var soloAnulado = await cliente.DeleteAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[1].Id), ct);
        var sinNada = await cliente.DeleteAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[2].Id), ct);

        enGestion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SaneamientoDePrueba.CodigoAsync(enGestion, ct)).Should().Be("SANEAMIENTO_EN_GESTION");
        foreach (var r in new[] { soloAnulado, sinNada })
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await SaneamientoDePrueba.CodigoAsync(r, ct)).Should().Be("SIN_SANEAMIENTO");
        }

        await using var db = empresa.Db();
        (await db.Set<SaneamientoEntidad>().SingleAsync(s => s.VehiculoId == lote.Vehiculos[0].Id, ct)).Estado.Should().Be(EstadoSaneamiento.EnGestion);
    }

    // AC11 — sin el permiso, 403 en marcar, desmarcar y masivo; desde el backoffice (?empresa=), 403 SANEAMIENTO_DESDE_BACKOFFICE.
    [Fact]
    public async Task HU14576_AC11_sin_permiso_o_desde_el_backoffice_es_403()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN111"], ct);
        var ruta = SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id);
        using var sinPermiso = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id, solicitar: false));
        using var conPermiso = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id));
        var cuerpo = JsonContent(new { vehiculo_ids = new[] { lote.Vehiculos[0].Id } });

        (await sinPermiso.PutAsync(ruta, null, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sinPermiso.DeleteAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sinPermiso.PostAsync(SaneamientoDePrueba.RutaMasivo, cuerpo, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var desdeBackoffice in new[]
                 {
                     await conPermiso.PutAsync($"{ruta}?empresa={empresa.Id}", null, ct),
                     await conPermiso.DeleteAsync($"{ruta}?empresa={empresa.Id}", ct),
                     await conPermiso.PostAsync($"{SaneamientoDePrueba.RutaMasivo}?empresa={empresa.Id}", JsonContent(new { vehiculo_ids = new[] { lote.Vehiculos[0].Id } }), ct),
                 })
        {
            desdeBackoffice.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SaneamientoDePrueba.CodigoAsync(desdeBackoffice, ct)).Should().Be("SANEAMIENTO_DESDE_BACKOFFICE");
        }

        await using var db = empresa.Db();
        (await db.Set<SaneamientoEntidad>().CountAsync(ct)).Should().Be(0);
    }

    // SP-8 con la decisión E3 — el SuperAdmin marca en la empresa de su sesión por la regla general del permiso.
    [Fact]
    public async Task SP8_E3_el_SuperAdmin_marca_en_la_empresa_de_su_sesion()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN112"], ct);
        using var superAdmin = api.Cliente(api.Token(empresa: empresa.Id, permisos: [], roles: ["SuperAdmin"]));

        var respuesta = await superAdmin.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync(ct));
        await using var db = empresa.Db();
        (await db.Set<SaneamientoEntidad>().SingleAsync(ct)).TenantId.Should().Be(empresa.Id);
    }

    // AC33 + SP-9 — otra compañía responde 404 (no 403) en marcar, desmarcar y leer; un vid de otro lote de la misma compañía, también.
    [Fact]
    public async Task HU14576_AC33_SP9_otra_compania_o_vehiculo_de_otro_lote_es_404()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var deB = await SaneamientoDePrueba.LoteAsync(empresas.B, ["SAN113"], ct);
        await SaneamientoDePrueba.SembrarAsync(empresas.B, deB.Id, deB.Vehiculos[0].Id, EstadoSaneamiento.Solicitado, ct);
        var deA = await SaneamientoDePrueba.LoteAsync(empresas.A, ["SAN114"], ct);
        var otroDeA = await SaneamientoDePrueba.LoteAsync(empresas.A, ["SAN115"], ct);
        await SaneamientoDePrueba.SembrarAsync(empresas.A, otroDeA.Id, otroDeA.Vehiculos[0].Id, EstadoSaneamiento.Solicitado, ct);
        using var cliente = api.Cliente(SaneamientoDePrueba.Token(api, empresas.A.Id));

        foreach (var ruta in new[] { SaneamientoDePrueba.Ruta(deB.Id, deB.Vehiculos[0].Id), SaneamientoDePrueba.Ruta(deA.Id, otroDeA.Vehiculos[0].Id) })
        {
            foreach (var respuesta in new[] { await cliente.PutAsync(ruta, null, ct), await cliente.DeleteAsync(ruta, ct), await cliente.GetAsync(ruta, ct) })
            {
                respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, ruta);
                (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("NO_ENCONTRADO", "la visibilidad responde antes que cualquier código de negocio (SP-9)");
            }
        }

        await using var db = empresas.B.Db();
        (await db.Set<SaneamientoEntidad>().SingleAsync(ct)).Estado.Should().Be(EstadoSaneamiento.Solicitado);
    }

    // AC26 + SP-15 — detalle del cliente: forma de §6.3, actor genérico, sin eventos de ruido, sin renglones quitados y con lista blanca de claves.
    [Fact]
    public async Task HU14576_AC26_SP15_detalle_del_cliente_con_lista_blanca_de_claves()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN116", "SAN117"], ct);
        var usuario = Guid.NewGuid();
        var saneamiento = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[0].Id, EstadoSaneamiento.EnGestion, ct, solicitante: usuario);
        var flit = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;
        var conEvidencia = new RenglonSaneamiento
        {
            TenantId = empresa.Id, SaneamientoId = saneamiento.Id, Fuente = FuenteSaneamiento.SimitPropietario, Hallazgo = HallazgoSaneamiento.Deuda,
            Automatico = true, Resultado = ResultadoRenglonSaneamiento.Saneado, Nota = "Pagado en la entidad.",
            EvidenciaReferencia = "ref-secreta-del-almacen", EvidenciaNombre = "pago.pdf", EvidenciaSha256 = new string('a', 64), EvidenciaTamano = 1234,
            EvidenciaSubidaPor = flit, EvidenciaSubidaEn = ahora, CreadoPor = flit, CreadoEn = ahora, ActualizadoPor = flit, ActualizadoEn = ahora,
        };
        var quitado = new RenglonSaneamiento
        {
            TenantId = empresa.Id, SaneamientoId = saneamiento.Id, Fuente = FuenteSaneamiento.Otro, Descripcion = "Embargo", Automatico = false,
            CreadoPor = flit, CreadoEn = ahora, QuitadoEn = ahora,
        };
        await using (var db = empresa.Db())
        {
            db.AddRange(conEvidencia, quitado);
            db.AddRange(
                Evento(empresa.Id, saneamiento.Id, TipoEventoSaneamiento.Tomado, flit, ahora, EstadoSaneamiento.Solicitado, EstadoSaneamiento.EnGestion),
                Evento(empresa.Id, saneamiento.Id, TipoEventoSaneamiento.RenglonActualizado, flit, ahora, renglon: conEvidencia),
                Evento(empresa.Id, saneamiento.Id, TipoEventoSaneamiento.ObservacionEditada, flit, ahora),
                Evento(empresa.Id, saneamiento.Id, TipoEventoSaneamiento.EvidenciaCargada, flit, ahora, renglon: conEvidencia));
            await db.SaveChangesAsync(ct);
        }

        using var cliente = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id, usuario: usuario));
        var respuesta = await cliente.GetAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), ct);
        var sinSaneamiento = await cliente.GetAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[1].Id), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        var json = JsonElement.Parse(texto);
        Claves(json).Should().BeSubsetOf(ClavesPermitidas, "el detalle del cliente nunca trae usuario_id, la referencia ni el sha (SP-15)");
        texto.Should().NotContain("ref-secreta-del-almacen").And.NotContain(flit.ToString()).And.NotContain(new string('a', 64));
        json.GetProperty("estado").GetString().Should().Be("EN_GESTION");
        json.GetProperty("solicitado_por_mi").GetBoolean().Should().BeTrue();
        json.GetProperty("version").GetUInt32().Should().BeGreaterThan(0);
        var renglones = json.GetProperty("renglones").EnumerateArray().ToList();
        renglones.Should().ContainSingle("el renglón quitado no sale (SP-2)");
        renglones[0].GetProperty("fuente").GetString().Should().Be("SIMIT_PROPIETARIO");
        renglones[0].GetProperty("hallazgo").GetString().Should().Be("DEUDA");
        renglones[0].GetProperty("resultado").GetString().Should().Be("SANEADO");
        renglones[0].GetProperty("version").GetUInt32().Should().BeGreaterThan(0);
        var evidencia = renglones[0].GetProperty("evidencia");
        evidencia.GetProperty("nombre").GetString().Should().Be("pago.pdf");
        evidencia.GetProperty("tamano").GetInt64().Should().Be(1234);
        var historial = json.GetProperty("historial").EnumerateArray().ToList();
        historial.Select(h => h.GetProperty("evento").GetString()).Should().Equal("SOLICITADO", "TOMADO", "EVIDENCIA_CARGADA");
        historial.Select(h => h.GetProperty("actor").GetString()).Should().Equal("COMPANIA", "FLIT", "FLIT");
        sinSaneamiento.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SaneamientoDePrueba.CodigoAsync(sinSaneamiento, ct)).Should().Be("SIN_SANEAMIENTO");
    }

    private static EventoSaneamiento Evento(
        Guid tenant, Guid saneamiento, TipoEventoSaneamiento tipo, Guid usuario, DateTimeOffset ahora,
        EstadoSaneamiento? anterior = null, EstadoSaneamiento? nuevo = null, RenglonSaneamiento? renglon = null) => new()
        {
            TenantId = tenant,
            SaneamientoId = saneamiento,
            Evento = tipo,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
            RenglonId = renglon?.Id,
            Fuente = renglon?.Fuente,
            UsuarioId = usuario,
            EsFlit = true,
            OcurridoEn = ahora.AddSeconds((int)tipo),
        };

    private static IEnumerable<string> Claves(JsonElement elemento) => elemento.ValueKind switch
    {
        JsonValueKind.Object => elemento.EnumerateObject().SelectMany(p => Claves(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => elemento.EnumerateArray().SelectMany(Claves),
        _ => [],
    };

    private static StringContent JsonContent(object cuerpo) =>
        new(JsonSerializer.Serialize(cuerpo), System.Text.Encoding.UTF8, "application/json");
}
