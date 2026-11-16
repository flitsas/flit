using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Reportes.Catalogo;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #14582 AC1, AC4, AC7 y AC10 (contrato §1.2/§1.5, SP-1, SP-3, SP-4, SP-5, D-3) — el documento capturado es el propietario
/// actual declarado: se guarda en <c>propietario_declarado_doc_*</c>, no alimenta al locatario y, al cerrar la celda RUNT (también
/// la reutilizada del mismo día), deja <c>cambio_propietario</c> persistido. Las lecturas (grilla de la compañía y del backoffice,
/// listado de placas y dictamen) solo leen la columna: 0 descifrados y ningún documento en la respuesta. PostgreSQL real y la
/// fuente RUNT simulada de Intempo: terminación 1 → propietario CC 9999900001; 3 → leasing, propietario NIT 999990003 y locatario
/// CC 9999900013. Documentos del rango ficticio 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CambioPropietarioTests
/// </code>
/// </example>
public sealed class CambioPropietarioTests
{
    private const string PropietarioNatural = "9999900001";
    private const string OtroDocumento = "9999900077";
    private const string LocatarioLeasing = "9999900013";

    // AC1 + AC7 + AC4 — coincide (false), no coincide (true) y sin declarado (null); el declarado queda cifrado en las columnas del
    // propietario declarado, nunca en las del locatario, y no entra como persona LOCATARIO.
    [Fact]
    public async Task HU14582_AC1_AC7_cierre_RUNT_fija_cambio_propietario_y_el_declarado_no_alimenta_locatario()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa,
            [Lotes.Fila("TST001", "CC", PropietarioNatural), Lotes.Fila("TST011", "CC", OtroDocumento), Lotes.Fila("TST021")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);

        var filas = await FilasAsync(api, id, ct);
        filas["TST001"].Cambio.Should().BeFalse("el declarado es el propietario del RUNT");
        filas["TST011"].Cambio.Should().BeTrue("el declarado no es el propietario del RUNT");
        filas["TST021"].Cambio.Should().BeNull("sin declarado no hay comparación");
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        filas["TST001"].DeclaradoTipo.Should().Be("CC");
        protector.Desproteger(filas["TST001"].DeclaradoCifrado!).Should().Be(PropietarioNatural);
        filas.Values.Should().OnlyContain(f => f.LocatarioOrigen == null, "el declarado deja de alimentar RUNT_LOCATARIO / SIMIT_LOCATARIO");
        (await ContarAsync(api, "SELECT count(*) FROM diagnostico.personas_vehiculo WHERE origen = 'DECLARADO_CLIENTE'", ct))
            .Should().Be(0, "el declarado ya no entra como persona LOCATARIO");
    }

    // AC7 (SP-7, C3) — con declarado y sin locatario en el RUNT, RUNT_LOCATARIO y SIMIT_LOCATARIO quedan NA SIN_DOCUMENTO_LOCATARIO
    // sin llamar a su adaptador: no se consulta a un tercero con el documento que declaró el cliente.
    [Fact]
    public async Task HU14582_AC7_declarado_no_consulta_fuentes_del_locatario()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorNa.ArrancarAsync(b =>
        {
            new FuenteRuntEspia(orden).Registrar(b.Services);
            MotorNa.Espias(b, orden, Fuente.RuntLocatario, Fuente.SimitLocatario);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST061", "CC", OtroDocumento)],
            [Fuente.Runt], null, [Fuente.RuntLocatario, Fuente.SimitLocatario], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);

        orden.Should().Equal(["RUNT PLACA_CARRO TST061"], "las fuentes del locatario no se llaman con el declarado");
        (await Motor.CeldasAsync(api, empresa.Id, id, ct))
            .Where(c => c.Fuente is Fuente.RuntLocatario or Fuente.SimitLocatario)
            .Should().HaveCount(2).And.OnlyContain(c => c.Estado == EstadoCelda.Na && c.MotivoNa == MotivoNa.SinDocumentoLocatario);
    }

    // D-3 (H-A1=b) — LEASING: el declarado igual al locatario del RUNT no coincide con el propietario (la entidad): true. El
    // locatario sigue saliendo solo del RUNT (origen PROVEEDOR).
    [Fact]
    public async Task HU14582_D3_leasing_con_declarado_igual_al_locatario_da_cambio_true()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST003", "CC", LocatarioLeasing)], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);

        var fila = (await FilasAsync(api, id, ct))["TST003"];
        fila.Cambio.Should().BeTrue("LEASING sin trato especial: solo cuentan los propietarios");
        fila.LocatarioOrigen.Should().Be("PROVEEDOR");
    }

    // AC1 + SP-5 — reutilización del mismo día: el segundo lote copia el RUNT y recalcula con SU declarado; otra compañía con la
    // misma placa no reutiliza el RUNT de la primera y calcula el suyo.
    [Fact]
    public async Task HU14582_AC1_SP5_reutilizada_recalcula_con_el_declarado_del_destino_y_no_cruza_companias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var primero = await MotorRunt.LoteAsync(api, empresas.A, [Lotes.Fila("TST031", "CC", PropietarioNatural)], ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);
        var segundo = await OtroLoteAsync(api, empresas.A, [Lotes.Fila("TST031", "NIT", PropietarioNatural)], ct);
        var otraCompania = await MotorRunt.LoteAsync(api, empresas.B, [Lotes.Fila("TST031", "CC", PropietarioNatural)], ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);

        (await FilasAsync(api, primero, ct))["TST031"].Cambio.Should().BeFalse();
        var reutilizada = (await FilasAsync(api, segundo, ct))["TST031"];
        reutilizada.RuntReutilizado.Should().BeTrue("el RUNT de hoy de la misma compañía se reutiliza");
        reutilizada.Cambio.Should().BeTrue("D-3 (H-A2=b): NIT declarado contra la CC del RUNT no coincide");
        var ajena = (await FilasAsync(api, otraCompania, ct))["TST031"];
        ajena.RuntReutilizado.Should().BeFalse("SP-5: la reutilización no cruza compañías");
        ajena.Cambio.Should().BeFalse();
    }

    // AC10 (SP-1, SP-4) — leer la grilla (compañía y backoffice), el listado de placas y el dictamen no descifra nada y no devuelve
    // documentos: solo cambio_propietario. SP-3: ningún log lleva el número declarado.
    [Fact]
    public async Task HU14582_AC10_lecturas_solo_leen_la_columna_sin_descifrar_ni_documentos()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        ProtectorDePrueba? espia = null;
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<IProtectorDocumentoPersona>(sp =>
                espia = new ProtectorDePrueba(new ProtectorDocumentoPersona(sp.GetRequiredService<IDataProtectionProvider>())));
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST041", "CC", OtroDocumento), Lotes.Fila("TST051")], ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api), ct);
        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single(v => v.IdentificadorNorm == "TST041").Id;
        await using (var db = empresa.Db())
        {
            // El lote SIMPLE no emite dictamen: se siembra uno vigente para leer su cambio_propietario.
            db.Add(new DictamenVehiculo
            {
                VehiculoId = vehiculo,
                Version = 1,
                Vigente = true,
                Veredicto = "VIABLE",
                Alertamientos = "[]",
                CatalogoVersion = CatalogoReglasDictamen.Version,
                RedactadoPor = "PLANTILLA",
                EstadoRedaccion = "PENDIENTE",
                CreadoEn = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var backoffice = api.Cliente(api.Token(empresa: Guid.NewGuid(), permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT"));
        var antes = espia!.Llamadas;

        var grilla = await LeerAsync(cliente, $"{Program.Prefijo}/diagnosticos/{id}/grilla", ct);
        var grillaBackoffice = await LeerAsync(backoffice, $"{Program.Prefijo}/backoffice/diagnosticos/{id}/grilla?empresa={empresa.Id}", ct);
        var listado = await LeerAsync(cliente, $"{Program.Prefijo}/vehiculos", ct);
        var dictamen = await LeerAsync(cliente, $"{Program.Prefijo}/diagnosticos/{id}/vehiculos/{vehiculo}/dictamen", ct);
        var dictamenBackoffice = await LeerAsync(backoffice, $"{Program.Prefijo}/backoffice/diagnosticos/{id}/vehiculos/{vehiculo}/dictamen?empresa={empresa.Id}", ct);

        espia.Llamadas.Should().Be(antes, "SP-1: ninguna lectura descifra documentos");
        foreach (var cuerpo in new[] { grilla, grillaBackoffice, listado, dictamen, dictamenBackoffice })
        {
            cuerpo.Should().NotContain(OtroDocumento).And.NotContain("****0077").And.NotContain(PropietarioNatural).And.NotContain("****0001");
            cuerpo.Should().NotContain("documento_declarado").And.NotContain("\"propietarios\"").And.NotContain("documento_enmascarado");
        }

        CambioDe(grilla, "TST041").Should().Be(JsonValueKind.True);
        CambioDe(grilla, "TST051").Should().Be(JsonValueKind.Null, "cambio_propietario siempre presente, null sin declarado");
        CambioDe(grillaBackoffice, "TST041").Should().Be(JsonValueKind.True);
        JsonDocument.Parse(listado).RootElement.GetProperty("vehiculos").EnumerateArray()
            .Single(v => v.GetProperty("placa").GetString() == "TST041").GetProperty("cambio_propietario").ValueKind.Should().Be(JsonValueKind.True);
        JsonDocument.Parse(dictamen).RootElement.GetProperty("cambio_propietario").ValueKind.Should().Be(JsonValueKind.True);
        JsonDocument.Parse(dictamenBackoffice).RootElement.GetProperty("cambio_propietario").ValueKind.Should().Be(JsonValueKind.True);
        logs.Mensajes.Should().NotBeEmpty();
        logs.Mensajes.Should().NotContain(m => m.Contains(OtroDocumento, StringComparison.Ordinal), "SP-3: sin documentos en logs");
    }

    /// <summary>Otro lote de una empresa que ya tiene configuración: la cabecera por la API (sin volver a sembrar) y la confirmación.</summary>
    private static async Task<Guid> OtroLoteAsync(ApiDePrueba api, EmpresaDePrueba empresa, Dictionary<string, object?>[] filas, CancellationToken ct)
    {
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(
            Cabeceras.Ruta, Configuracion.ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Otro lote", "SIMPLE", null, ["RUNT"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = JsonDocument.Parse(await creada.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("id").GetGuid();
        var confirmada = await Lotes.ConfirmarAsync(cliente, id, filas, ct);
        confirmada.StatusCode.Should().Be(HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
        return id;
    }

    private static async Task<string> LeerAsync(HttpClient cliente, string ruta, CancellationToken ct)
    {
        var respuesta = await cliente.GetAsync(ruta, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, $"{ruta}: {cuerpo}");
        return cuerpo;
    }

    private static JsonValueKind CambioDe(string json, string identificador) =>
        JsonDocument.Parse(json).RootElement.GetProperty("vehiculos").EnumerateArray()
            .Single(f => f.GetProperty("identificador").GetString() == identificador).GetProperty("cambio_propietario").ValueKind;

    private sealed record FilaVehiculo(bool? Cambio, string? DeclaradoTipo, byte[]? DeclaradoCifrado, string? LocatarioOrigen, bool RuntReutilizado);

    /// <summary>Por identificador: cambio_propietario, propietario declarado, origen del locatario y si la celda RUNT se reutilizó (SQL directo).</summary>
    private static async Task<Dictionary<string, FilaVehiculo>> FilasAsync(ApiDePrueba api, Guid diagnostico, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(
            "SELECT v.identificador_norm, v.cambio_propietario, v.propietario_declarado_doc_tipo, v.propietario_declarado_doc_cifrado, " +
            "v.locatario_doc_origen, c.reutilizada_de_id IS NOT NULL FROM diagnostico.vehiculos v " +
            "JOIN diagnostico.consultas c ON c.vehiculo_id = v.id AND c.fuente = 'RUNT' WHERE v.diagnostico_id = @d", conexion);
        consulta.Parameters.AddWithValue("d", diagnostico);
        var filas = new Dictionary<string, FilaVehiculo>();
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            filas[lector.GetString(0)] = new FilaVehiculo(
                lector.IsDBNull(1) ? null : lector.GetBoolean(1),
                lector.IsDBNull(2) ? null : lector.GetString(2),
                lector.IsDBNull(3) ? null : (byte[])lector.GetValue(3),
                lector.IsDBNull(4) ? null : lector.GetString(4),
                lector.GetBoolean(5));
        }

        return filas;
    }

    private static async Task<long> ContarAsync(ApiDePrueba api, string sql, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(sql, conexion);
        return (long)(await consulta.ExecuteScalarAsync(ct))!;
    }
}
