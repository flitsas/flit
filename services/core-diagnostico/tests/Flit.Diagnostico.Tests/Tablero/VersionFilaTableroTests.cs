using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Derivacion;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #14563 (B1, ADR-0086 D-1) — versión de fila del tablero: <see cref="EscritorCeldas.SubirVersionFilaAsync"/> sube la
/// cabecera y deja <c>vehiculos.version_tablero</c> con ese valor en la misma transacción, con el vehículo seguido o no, sin
/// chocar con el <c>xmin</c>, y se serializa por la cabecera. PostgreSQL real; placas TST###.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~VersionFilaTableroTests
/// </code>
/// </example>
public sealed class VersionFilaTableroTests
{
    // AC5/AC8 — el vehículo no seguido queda con la versión de la cabecera; invariante fila <= lote.
    [Fact]
    public async Task HU14563_AC5_Subir_version_fila_sube_cabecera_y_vehiculo_no_seguido()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);

        long v;
        await using (var db = empresa.Db())
        {
            v = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var nueva = await EscritorCeldas.SubirVersionFilaAsync(db, lote.Id, lote.Vehiculos[0].Id, ct);
                await tx.CommitAsync(ct);
                return nueva;
            });
        }

        v.Should().Be(8);
        var (lote1, filas) = await LeerAsync(empresa, lote.Id, ct);
        lote1.Should().Be(8);
        filas[lote.Vehiculos[0].Id].Should().Be(8);
        filas[lote.Vehiculos[1].Id].Should().Be(0, "la otra fila no cambió");
        filas.Values.Should().OnlyContain(f => f <= lote1);
    }

    // AC9 — con el vehículo seguido, el SaveChanges posterior no lanza DbUpdateConcurrencyException.
    [Fact]
    public async Task HU14563_AC9_Vehiculo_seguido_guarda_sin_conflicto_de_xmin()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);

        await using (var db = empresa.Db())
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(x => x.Id == lote.Vehiculos[1].Id, ct);
            var v = await EscritorCeldas.SubirVersionFilaAsync(db, lote.Id, vehiculo.Id, ct);
            vehiculo.VersionTablero.Should().Be(v);
            vehiculo.TramiteId = Guid.NewGuid();
            var guardar = () => db.SaveChangesAsync(ct);
            await guardar.Should().NotThrowAsync();
            await tx.CommitAsync(ct);
        });

        var (version, filas) = await LeerAsync(empresa, lote.Id, ct);
        filas[lote.Vehiculos[1].Id].Should().Be(version).And.Be(8);
    }

    // AC7 — MarcarFila reutiliza la versión de la celda: no sube la cabecera otra vez.
    [Fact]
    public void HU14563_AC7_Marcar_fila_asigna_la_version_dada()
    {
        var vehiculo = new VehiculoDiagnostico { IdentificadorOriginal = "TST301", IdentificadorNorm = "TST301" };

        EscritorCeldas.MarcarFila(vehiculo, 12);

        vehiculo.VersionTablero.Should().Be(12);
        var bajar = () => EscritorCeldas.MarcarFila(vehiculo, 11);
        bajar.Should().Throw<InvalidOperationException>("la versión de fila es monótona");
    }

    // AC9 — dos escritores sobre el mismo lote se serializan por la cabecera: versiones distintas y sin interbloqueo.
    [Fact]
    public async Task HU14563_AC9_Escritores_concurrentes_se_serializan_por_la_cabecera()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);

        async Task<long> EscribirAsync(Guid vehiculoId)
        {
            await using var db = empresa.Db();
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var v = await EscritorCeldas.SubirVersionFilaAsync(db, lote.Id, vehiculoId, ct);
                await Task.Delay(50, ct);
                await tx.CommitAsync(ct);
                return v;
            });
        }

        var versiones = await Task.WhenAll(EscribirAsync(lote.Vehiculos[0].Id), EscribirAsync(lote.Vehiculos[1].Id));

        versiones.Should().BeEquivalentTo([8L, 9L]);
        var (version, filas) = await LeerAsync(empresa, lote.Id, ct);
        version.Should().Be(9);
        filas.Values.Should().OnlyContain(f => f <= version);
    }

    // AC5 + AC4 — un cambio de dictamen con la versión de fila viaja en el delta con veredicto, marca y versión; los conteos son los de todo el lote.
    [Fact]
    public async Task HU14563_AC5_Cambio_de_dictamen_viaja_en_el_delta_con_version_de_fila()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 7, estado: EstadoDiagnostico.Completado, modo: ModoDiagnostico.ConAnalisis,
            tipoTramiteCodigo: "TRASPASO_STANDARD", vehiculos:
            [
                new("TST311", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
                new("TST312", [new(Fuente.Runt, EstadoCelda.Completado, 2)]),
            ]);
        var vehiculo = lote.Vehiculos[0].Id;
        await using (var db = empresa.Db())
        {
            db.Add(Dictamen(vehiculo, 1, "VIABLE"));
            await db.SaveChangesAsync(ct);
        }

        var inicial = await GrillaAsync(empresa, lote.Id, 0, ct);
        inicial.GetProperty("version").GetInt64().Should().Be(7);
        inicial.GetProperty("conteos").GetProperty("listos_tramite").GetInt32().Should().Be(1);
        Fila(inicial, "TST311").GetProperty("version").GetInt64().Should().Be(0);
        Fila(inicial, "TST311").GetProperty("dictamen_ajustado").GetBoolean().Should().BeFalse();

        // Escritor de fila (como el override o el ajuste B7): primero la versión de fila, luego el dictamen.
        await using (var db = empresa.Db())
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await EscritorCeldas.SubirVersionFilaAsync(db, lote.Id, vehiculo, ct);
                var vigente = await db.Set<DictamenVehiculo>().SingleAsync(d => d.VehiculoId == vehiculo && d.Vigente, ct);
                vigente.Vigente = false;
                var ajustado = Dictamen(vehiculo, 2, "BLOQUEADO");
                ajustado.AnalisisOverride = true;
                ajustado.UsuarioOverrideId = Guid.NewGuid();
                ajustado.OverrideEn = DateTimeOffset.UtcNow;
                ajustado.AjustadoDesdeVersion = 1;
                db.Add(ajustado);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }

        var delta = await GrillaAsync(empresa, lote.Id, 7, ct);
        delta.GetProperty("version").GetInt64().Should().Be(8);
        var filas = delta.GetProperty("vehiculos").EnumerateArray().ToList();
        filas.Should().ContainSingle("solo cambió la fila de TST311");
        var fila = filas[0];
        fila.GetProperty("identificador").GetString().Should().Be("TST311");
        fila.GetProperty("veredicto").GetString().Should().Be("BLOQUEADO");
        fila.GetProperty("dictamen_ajustado").GetBoolean().Should().BeTrue();
        fila.GetProperty("version").GetInt64().Should().Be(8);
        fila.GetProperty("celdas").GetArrayLength().Should().Be(0, "no cambió ninguna celda");
        var conteos = delta.GetProperty("conteos");
        conteos.GetProperty("bloqueados").GetInt32().Should().Be(1);
        conteos.GetProperty("listos_tramite").GetInt32().Should().Be(0);

        // AC4: los conteos del delta son los de la carga completa.
        var completa = await GrillaAsync(empresa, lote.Id, 0, ct);
        completa.GetProperty("conteos").GetRawText().Should().Be(conteos.GetRawText());

        var sinCambios = await GrillaAsync(empresa, lote.Id, 8, ct);
        sinCambios.GetProperty("vehiculos").GetArrayLength().Should().Be(0);
        sinCambios.GetProperty("conteos").GetRawText().Should().Be(conteos.GetRawText());
        await InvarianteAsync(empresa, lote.Id, ct);
    }

    // AC6 — los escritores de trámite (individual y CierreItemDerivacion) suben la fila con CREADO, OMITIDO o FALLIDO.
    [Theory]
    [InlineData("CREADO")]
    [InlineData("OMITIDO")]
    [InlineData("FALLIDO")]
    public async Task HU14563_AC6_Derivacion_individual_sube_la_version_de_fila(string resultado)
    {
        var falso = new DerivacionTramitesFalso();
        switch (resultado)
        {
            case "CREADO":
                falso.Responder = _ => DerivacionTramitesFalso.Creado(Guid.NewGuid(), "TR-000321");
                break;
            case "OMITIDO":
                falso.Responder = _ => DerivacionTramitesFalso.Rechazo("OT_NO_HABILITADO", "El OT no está en la lista de la compañía.");
                break;
            default:
                falso.Falla = StatusCode.Unavailable;
                break;
        }

        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await DerivacionDePrueba.OrganismoAsync(empresa, ct, "99001");
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST321", true, "VIABLE"), ("TST322", true, "VIABLE")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        var v = (await Tablero.JsonAsync(await cliente.GetAsync(Tablero.RutaGrilla(lote.Id, desde: 0), ct), ct)).GetProperty("version").GetInt64();

        await cliente.PostAsync(DerivacionDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), null, ct);

        var delta = await Tablero.JsonAsync(await cliente.GetAsync(Tablero.RutaGrilla(lote.Id, desde: v), ct), ct);
        var fila = delta.GetProperty("vehiculos").EnumerateArray().Should().ContainSingle().Subject;
        fila.GetProperty("identificador").GetString().Should().Be("TST321");
        fila.GetProperty("version").GetInt64().Should().BeGreaterThan(v);
        fila.GetProperty("derivacion").GetProperty("estado").GetString().Should().Be(resultado);
        await InvarianteAsync(empresa, lote.Id, ct);
    }

    // AC6 — el consumidor de la derivación masiva cierra con CierreItemDerivacion y la fila viaja en el delta.
    [Fact]
    public async Task HU14563_AC6_Consumidor_de_derivacion_sube_la_version_de_fila()
    {
        var falso = new DerivacionTramitesFalso { Responder = _ => DerivacionTramitesFalso.Creado(Guid.NewGuid(), "TR-000322") };
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await DerivacionDePrueba.OrganismoAsync(empresa, ct, "99001");
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST323", true, "VIABLE")]);
        using var cliente = api.Cliente(DerivacionDePrueba.TokenDerivar(api, empresa));
        (await cliente.PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = lote.Vehiculos.Select(x => x.Id) }, ct)).EnsureSuccessStatusCode();
        var v = (await Tablero.JsonAsync(await cliente.GetAsync(Tablero.RutaGrilla(lote.Id, desde: 0), ct), ct)).GetProperty("version").GetInt64();

        var mensaje = (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Single();
        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensaje.Data, ct);

        var delta = await Tablero.JsonAsync(await cliente.GetAsync(Tablero.RutaGrilla(lote.Id, desde: v), ct), ct);
        var fila = delta.GetProperty("vehiculos").EnumerateArray().Should().ContainSingle().Subject;
        fila.GetProperty("version").GetInt64().Should().BeGreaterThan(v);
        fila.GetProperty("derivacion").GetProperty("estado").GetString().Should().Be("CREADO");
        await InvarianteAsync(empresa, lote.Id, ct);
    }

    // AC7 — la última celda cierra y emite el dictamen: la cabecera sube una sola vez y la fila queda con la versión de la celda.
    [Fact]
    public async Task HU14563_AC7_Emision_automatica_reutiliza_la_version_de_la_celda()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 5, modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_STANDARD", vehiculos:
            [new("TST331", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)])]);
        var celda = lote.Celda("TST331", Fuente.SimitVehiculo);
        var token = api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer, Permisos.ConsultasLlenar], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        const string Cuerpo = """{"version":2,"resultado":{"semaforo":"VERDE","cantidad_comparendos":0,"comparendos":[]}}""";

        var respuesta = await api.Cliente(token).PutAsync(
            $"{Program.Prefijo}/backoffice/consultas/{celda.Id}/manual?empresa={empresa.Id}",
            new StringContent(Cuerpo, Encoding.UTF8, "application/json"), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));

        await using (var db = empresa.Db())
            (await db.Set<DictamenVehiculo>().CountAsync(d => d.VehiculoId == celda.VehiculoId && d.Vigente, ct)).Should().Be(1, "el cierre emitió el dictamen");
        var (version, filas) = await LeerAsync(empresa, lote.Id, ct);
        version.Should().Be(6, "la cabecera sube una sola vez");
        filas[celda.VehiculoId].Should().Be(6, "la fila reutiliza la versión de la celda");
    }

    // AC5 — backoffice (override/ajuste B7): el contexto no tiene empresa; el escritor pasa las filas de la empresa del lote.
    [Fact]
    public async Task HU14563_AC5_Backoffice_sube_la_version_de_fila_con_la_empresa_explicita()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);

        long v;
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            v = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var nueva = await EscritorCeldas.SubirVersionFilaAsync(
                    db, lote.Id, lote.Vehiculos[0].Id, ct,
                    db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa.Id), db.ParaEditarDeLaEmpresa<VehiculoDiagnostico>(empresa.Id));
                await tx.CommitAsync(ct);
                return nueva;
            });
        }

        v.Should().Be(8);
        var (version, filas) = await LeerAsync(empresa, lote.Id, ct);
        version.Should().Be(8);
        filas[lote.Vehiculos[0].Id].Should().Be(8);
    }

    // AC9 — un conflicto real de concurrencia responde 409, nunca 500.
    [Fact]
    public async Task HU14563_AC9_Conflicto_de_concurrencia_es_409()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(mapear: app =>
            app.MapGet("/prueba/concurrencia", IResult () => throw new DbUpdateConcurrencyException("conflicto de prueba")));
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Anonimo().GetAsync("/prueba/concurrencia", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("code").GetString().Should().Be("CONFLICTO_CONCURRENCIA");
        cuerpo.GetRawText().Should().NotContain("conflicto de prueba", "el detalle de EF no viaja");
    }

    private static DictamenVehiculo Dictamen(Guid vehiculo, int version, string veredicto) => new()
    {
        VehiculoId = vehiculo,
        Version = version,
        Vigente = true,
        Veredicto = veredicto,
        Alertamientos = "[]",
        CatalogoVersion = 1,
        RedactadoPor = "PLANTILLA",
        EstadoRedaccion = "PENDIENTE",
        CreadoEn = DateTimeOffset.UtcNow,
    };

    private static async Task<JsonElement> GrillaAsync(EmpresaDePrueba empresa, Guid lote, long desde, CancellationToken ct) =>
        await Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote, desde: desde), ct), ct);

    private static JsonElement Fila(JsonElement grilla, string placa) =>
        grilla.GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("identificador").GetString() == placa);

    // AC8 — invariante de aplicación: ninguna fila supera la versión del lote.
    private static async Task InvarianteAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        var (version, filas) = await LeerAsync(empresa, lote, ct);
        filas.Values.Should().OnlyContain(f => f <= version, "vehiculos.version_tablero <= diagnosticos.version_tablero");
    }

    private static Task<LoteSembrado> SembrarAsync(EmpresaDePrueba empresa, CancellationToken ct) =>
        Tablero.SembrarAsync(empresa, ct, version: 7, vehiculos:
        [
            new("TST301", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
            new("TST302", [new(Fuente.Runt, EstadoCelda.Completado, 2)]),
        ]);

    private static async Task<(long Lote, Dictionary<Guid, long> Filas)> LeerAsync(EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var version = await db.Set<CabeceraDiagnostico>().Where(c => c.Id == lote).Select(c => c.VersionTablero).SingleAsync(ct);
        var filas = await db.Set<VehiculoDiagnostico>().Where(v => v.DiagnosticoId == lote).ToDictionaryAsync(v => v.Id, v => v.VersionTablero, ct);
        return (version, filas);
    }
}
