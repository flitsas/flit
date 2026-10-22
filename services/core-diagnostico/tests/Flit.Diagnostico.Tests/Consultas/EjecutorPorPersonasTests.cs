using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13792 (F05-H3), AC1-AC4 (F05-C4, F05-C5, F05-C8, F05-C10, decisión Q1 y decisión 2 del gate) — patrón «celda de persona»
/// (<see cref="EjecutorPorPersonas"/>): la celda RUNT_PROPIETARIO consulta a CADA propietario del vehículo (una llamada y un
/// intento por persona, con <c>persona_id</c>); queda COMPLETADO solo si todas responden; con alguna en error definitivo o con
/// los reintentos agotados queda en ERROR <c>PERSONAS_CON_ERROR</c> (o la causa si fallan todas) y conserva lo que respondió;
/// el reintento automático y el reproceso solo vuelven a llamar a las personas en error; y su ERROR no bloquea al vehículo.
/// PostgreSQL real, fuente RUNT fija (<see cref="RuntConPersonas"/>) y fuentes de persona programables
/// (<see cref="FuentesPersonaProgramables"/>); placas TST3## y documentos del rango ficticio 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EjecutorPorPersonasTests
/// </code>
/// </example>
public sealed class EjecutorPorPersonasTests
{
    // AC1 (F05-C4, F05-C10) — dos naturales y un NIT: dos llamadas al RUNT persona y una al RUES; COMPLETADO con tres resultados.
    [Fact]
    public async Task Completa_solo_si_todas_las_personas_responden()
    {
        var ct = TestContext.Current.CancellationToken;
        var fuentes = new FuentesPersonaProgramables();
        await using var api = await MotorPersonas.ArrancarAsync(fuentes);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        fuentes.Llamadas.Select(l => (l.Consulta, l.Numero)).Should().BeEquivalentTo(
            [("RUNT_PERSONA", RuntConPersonas.Natural1), ("RUNT_PERSONA", RuntConPersonas.Natural2), ("RUES", RuntConPersonas.Nit)]);
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        celda.TipoError.Should().BeNull();
        celda.Origen.Should().Be(OrigenCelda.Api);
        celda.DatoObtenidoEn.Should().NotBeNull();
        var personas = MotorPersonas.PersonasDelPayload(celda.Payload);
        personas.Should().HaveCount(3);
        personas.Select(p => p.GetProperty("orden").GetInt32()).Should().Equal(1, 2, 3);
        personas.Should().OnlyContain(p => p.GetProperty("estado").GetString() == "OK" && p.GetProperty("resultado").ValueKind == JsonValueKind.Object);
        personas.Select(p => p.GetProperty("consulta").GetString()).Should().Equal("RUNT_PERSONA", "RUNT_PERSONA", "RUES");
        var ids = (await MotorPersonas.PersonasAsync(empresa, celda.VehiculoId, RolPersona.Propietario, ct)).Select(p => p.Id);
        personas.Select(p => p.GetProperty("persona_id").GetGuid()).Should().Equal(ids);
    }

    // AC2 (F05-C5) y AC4 (F05-C8) — el segundo propietario con error definitivo: ERROR PERSONAS_CON_ERROR con el primero y el
    // tercero guardados; el vehículo sigue (SIMIT_VEHICULO completa). Si fallan todas por la misma causa, queda esa causa.
    [Fact]
    public async Task Fallo_parcial_deja_ERROR_y_conserva_los_resultados_OK()
    {
        var ct = TestContext.Current.CancellationToken;
        var fuentes = new FuentesPersonaProgramables();
        fuentes.Responder(RuntConPersonas.Natural2, FuentesPersonaProgramables.Invalida);
        fuentes.Responder(RuntConPersonas.Unico, FuentesPersonaProgramables.Invalida);
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorPersonas.ArrancarAsync(fuentes, b => new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301"), Lotes.Fila("TST302")], ct, porVehiculo: [Fuente.SimitVehiculo]);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 3), ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        (celda.Estado, celda.TipoError).Should().Be((EstadoCelda.Error, EjecutorPorPersonas.PersonasConError));
        celda.IntentosAuto.Should().Be(0, "un definitivo no se reintenta");
        var personas = MotorPersonas.PersonasDelPayload(celda.Payload);
        personas.Select(p => p.GetProperty("estado").GetString()).Should().Equal("OK", "ERROR", "OK");
        personas[0].GetProperty("resultado").ValueKind.Should().Be(JsonValueKind.Object);
        personas[2].GetProperty("resultado").ValueKind.Should().Be(JsonValueKind.Object);
        personas[1].GetProperty("tipo_error").GetString().Should().Be("RESPUESTA_INVALIDA");
        personas[1].TryGetProperty("resultado", out _).Should().BeFalse();

        var todas = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST302", Fuente.RuntPropietario, ct);
        (todas.Estado, todas.TipoError).Should().Be((EstadoCelda.Error, "RESPUESTA_INVALIDA"), "si fallan todas por la misma causa queda esa causa");

        // AC4: el ERROR de la celda de persona no bloquea al vehículo.
        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST301", ct);
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct)).Should().BeEquivalentTo(
            ["RUNT COMPLETADO -", "RUNT_PROPIETARIO ERROR PERSONAS_CON_ERROR", "SIMIT_VEHICULO COMPLETADO -"]);
        orden.Should().Contain($"SIMIT_VEHICULO {vehiculo.Id}");
    }

    // AC3 (F05-C5) — un transitorio se reintenta solo para la persona en error; agotado, la celda queda en ERROR y el reproceso
    // del admin vuelve a consultar solo al segundo propietario: si responde, la celda queda COMPLETADO con los tres.
    [Fact]
    public async Task Reintento_y_reproceso_llaman_solo_a_las_personas_en_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var fuentes = new FuentesPersonaProgramables();
        fuentes.Responder(RuntConPersonas.Natural2, FuentesPersonaProgramables.Timeout, FuentesPersonaProgramables.Timeout);
        await using var api = await MotorPersonas.ArrancarAsync(fuentes, b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301")], ct);
        var procesador = Motor.Procesador(api, maxReintentos: 1);

        await MotorRunt.ProcesarTodoAsync(procesador, ct);
        var enEspera = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        (enEspera.Estado, enEspera.IntentosAuto).Should().Be((EstadoCelda.Pendiente, 1), "el TIMEOUT de una persona se reintenta con backoff");
        MotorPersonas.PersonasDelPayload(enEspera.Payload).Select(p => p.GetProperty("estado").GetString()).Should().Equal("OK", "ERROR", "OK");
        reloj.Avanzar(PoliticaReintentos.Espera(1));
        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        var agotada = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        (agotada.Estado, agotada.TipoError).Should().Be((EstadoCelda.Error, EjecutorPorPersonas.PersonasConError), "agotados los reintentos");
        fuentes.LlamadasA(RuntConPersonas.Natural1).Should().Be(1, "el reintento no vuelve a consumir a quien respondió");
        fuentes.LlamadasA(RuntConPersonas.Nit).Should().Be(1);
        fuentes.LlamadasA(RuntConPersonas.Natural2).Should().Be(2);

        var token = MotorPersonas.Admin(api, empresa);
        var respuesta = await api.Cliente(token).PostAsync($"{Program.Prefijo}/consultas/{agotada.Id}/reprocesar", null, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        fuentes.LlamadasA(RuntConPersonas.Natural1).Should().Be(1, "el reproceso tampoco vuelve a consumir a quien respondió");
        fuentes.LlamadasA(RuntConPersonas.Nit).Should().Be(1);
        fuentes.LlamadasA(RuntConPersonas.Natural2).Should().Be(3);
        var final = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        (final.Estado, final.TipoError).Should().Be((EstadoCelda.Completado, (string?)null));
        MotorPersonas.PersonasDelPayload(final.Payload).Should().HaveCount(3).And.OnlyContain(p => p.GetProperty("estado").GetString() == "OK");
        var intentos = await MotorPersonas.IntentosAsync(empresa, final.Id, ct);
        intentos.Should().HaveCount(5, "tres en la primera ejecución, uno en el reintento y uno en el reproceso");
        var ultimo = intentos[^1];
        (ultimo.Disparador, ultimo.UsuarioId, ultimo.Estado).Should().Be((DisparadorIntento.Reproceso, (Guid?)TableroAyudas.Usuario(token), ResultadoIntento.Success));
    }

    // AC1 (F05-C10) — un intento por persona con su persona_id, sin identificador (nunca un documento) y con el proveedor.
    [Fact]
    public async Task Un_intento_por_persona_con_persona_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var fuentes = new FuentesPersonaProgramables();
        fuentes.Responder(RuntConPersonas.Natural2, FuentesPersonaProgramables.Invalida);
        await using var api = await MotorPersonas.ArrancarAsync(fuentes);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        var personas = await MotorPersonas.PersonasAsync(empresa, celda.VehiculoId, RolPersona.Propietario, ct);
        var intentos = await MotorPersonas.IntentosAsync(empresa, celda.Id, ct);
        intentos.Should().HaveCount(3);
        intentos.Select(i => i.PersonaId).Should().BeEquivalentTo(personas.Select(p => (Guid?)p.Id));
        intentos.Should().OnlyContain(i => i.IdentificadorConsultado == null && i.Fuente == Fuente.RuntPropietario && i.Disparador == DisparadorIntento.Auto && !i.DesdeCache);
        var porPersona = intentos.ToDictionary(i => i.PersonaId!.Value);
        porPersona[personas[0].Id].Should().Match<IntentoConsulta>(i => i.Estado == ResultadoIntento.Success && i.Servicio == ServicioConsulta.Kyverum && i.TransaccionProveedorId != null);
        porPersona[personas[1].Id].Should().Match<IntentoConsulta>(i => i.Estado == ResultadoIntento.Error && i.TipoError == "RESPUESTA_INVALIDA" && i.MensajeError != null);
        porPersona[personas[2].Id].Should().Match<IntentoConsulta>(i => i.Estado == ResultadoIntento.Success && i.Servicio == ServicioConsulta.Verifik);
    }
}

/// <summary>
/// Fuentes de persona programables (HU #13792): implementan <see cref="IFuenteRuntPersona"/> y <see cref="IFuenteRues"/>,
/// anotan cada llamada (empresa, consulta, tipo y número) y responden OK salvo que se programe una cola de errores por número
/// de documento (la respuesta n consume el elemento n; agotada la cola, OK).
/// </summary>
public sealed class FuentesPersonaProgramables : IFuenteRuntPersona, IFuenteRues
{
    public static readonly ErrorConsulta Timeout = new("TIMEOUT", "El proveedor no respondió a tiempo.");
    public static readonly ErrorConsulta Invalida = new("RESPUESTA_INVALIDA", "La respuesta no tiene la forma esperada.");

    private readonly ConcurrentQueue<(Guid Empresa, string Consulta, string Tipo, string Numero)> _llamadas = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ErrorConsulta>> _errores = new();

    public IReadOnlyCollection<(Guid Empresa, string Consulta, string Tipo, string Numero)> Llamadas => _llamadas;

    public int LlamadasA(string numero) => _llamadas.Count(l => l.Numero == numero);

    public void Responder(string numero, params ErrorConsulta[] errores) => _errores[numero] = new ConcurrentQueue<ErrorConsulta>(errores);

    public void Registrar(IServiceCollection servicios)
    {
        servicios.AddSingleton<IFuenteRuntPersona>(this);
        servicios.AddSingleton<IFuenteRues>(this);
    }

    public Task<RespuestaFuente<ResultadoPersonaRunt>> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct)
    {
        _llamadas.Enqueue((empresa, "RUNT_PERSONA", documento.Tipo, documento.Numero));
        return Task.FromResult(Error(documento.Numero) is { } error
            ? RespuestaFuente<ResultadoPersonaRunt>.Fallo(error, ServicioConsulta.Kyverum)
            : RespuestaFuente<ResultadoPersonaRunt>.Exito(
                new ResultadoPersonaRunt(true, "ACTIVO", [new LicenciaConduccion("B1", "ACTIVA", "2030-01-01")]), ServicioConsulta.Kyverum, "TX-PERSONA"));
    }

    public Task<RespuestaFuente<ResultadoRues>> ConsultarAsync(Guid empresa, string nit, CancellationToken ct)
    {
        _llamadas.Enqueue((empresa, "RUES", "NIT", nit));
        return Task.FromResult(Error(nit) is { } error
            ? RespuestaFuente<ResultadoRues>.Fallo(error, ServicioConsulta.Verifik)
            : RespuestaFuente<ResultadoRues>.Exito(new ResultadoRues(true, "ACTIVA", "2026-03-15"), ServicioConsulta.Verifik, "TX-RUES"));
    }

    private ErrorConsulta? Error(string numero) =>
        _errores.TryGetValue(numero, out var cola) && cola.TryDequeue(out var error) ? error : null;
}

/// <summary>
/// Fuente RUNT de las pruebas de personas (HU #13792): vehículos con varias personas y documentos del rango ficticio, sin
/// nombres. TST301: dos naturales y un NIT (con dígito de verificación). TST302: un solo propietario. TST303: leasing (dueño
/// NIT, locatario CC). El VIN <see cref="VinTst301"/> devuelve la placa TST301 con OTRAS personas (RUNT consultado de nuevo).
/// <see cref="PorEmpresa"/> permite que la misma placa traiga personas distintas en cada compañía. Lo demás, la simulada de Intempo.
/// </summary>
public sealed class RuntConPersonas : IFuenteRunt
{
    public const string Natural1 = "9999900301";
    public const string Natural2 = "9999900302";
    public const string NitConDv = "999990305-1";
    public const string Nit = "999990305";
    public const string Unico = "9999900308";
    public const string DuenoLeasing = "999990306";
    public const string Locatario = "9999900303";
    public const string OtroNatural1 = "9999900321";
    public const string OtroNatural2 = "9999900322";
    public const string VinTst301 = "9ZZPRUEBA00000301";

    private readonly FuenteRuntIntempoSimulada _simulada = new();

    /// <summary>Propietarios de una placa por compañía; se consulta antes que los fijos.</summary>
    public ConcurrentDictionary<(Guid Empresa, string Placa), IReadOnlyList<PropietarioRunt>> PorEmpresa { get; } = new();

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IFuenteRunt>(this);

    public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct)
    {
        if (PorEmpresa.TryGetValue((empresa, identificador.Valor), out var propios))
            return Task.FromResult(Vehiculo(identificador.Valor, "9ZZPRUEBA00000399", propios, []));

        return identificador.Valor switch
        {
            "TST301" => Task.FromResult(Vehiculo("TST301", VinTst301,
                [Propio("CC", Natural1), Propio("CE", Natural2), Propio("NIT", NitConDv)], [])),
            "TST302" => Task.FromResult(Vehiculo("TST302", "9ZZPRUEBA00000302", [Propio("CC", Unico)], [])),
            "TST303" => Task.FromResult(Vehiculo("TST303", "9ZZPRUEBA00000303",
                [new PropietarioRunt("NIT", DuenoLeasing, TipoPropiedad.Leasing, [new LocatarioRunt("CC", Locatario)])], [new LocatarioRunt("CC", Locatario)])),
            VinTst301 => Task.FromResult(Vehiculo("TST301", VinTst301, [Propio("CC", OtroNatural1), Propio("CC", OtroNatural2)], [])),
            _ => _simulada.ConsultarVehiculoAsync(empresa, identificador, ct),
        };
    }

    public static PropietarioRunt Propio(string tipo, string numero) => new(tipo, numero, TipoPropiedad.Propio, []);

    private static ResultadoRunt Vehiculo(string placa, string vin, IReadOnlyList<PropietarioRunt> propietarios, IReadOnlyList<LocatarioRunt> locatarios) =>
        ResultadoRunt.Exito(new VehiculoRunt(
            placa, vin, "PARTICULAR", "AUTOMOVIL", "SECRETARIA DE TRANSITO DE PRUEBA", propietarios, locatarios, "INTEMPO_SIMULADO", "TX-PRUEBA-" + placa));
}

/// <summary>Ayudas de las pruebas de las celdas de persona (HU #13792).</summary>
internal static class MotorPersonas
{
    /// <summary>API con <see cref="RuntConPersonas"/> y las fuentes de persona dadas (o las que registre <paramref name="configurar"/>).</summary>
    public static Task<ApiDePrueba> ArrancarAsync(FuentesPersonaProgramables? fuentes, Action<WebApplicationBuilder>? configurar = null, RuntConPersonas? runt = null) =>
        MotorRunt.ArrancarAsync(b =>
        {
            (runt ?? new RuntConPersonas()).Registrar(b.Services);
            fuentes?.Registrar(b.Services);
            configurar?.Invoke(b);
        });

    /// <summary>Lote con RUNT (y <paramref name="porVehiculo"/>), RUNT_PROPIETARIO y, si se pide, RUNT_LOCATARIO.</summary>
    public static Task<Guid> LoteAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Dictionary<string, object?>[] filas, CancellationToken ct, Fuente[]? porVehiculo = null, bool conLocatario = false) =>
        MotorNa.LoteAsync(api, empresa, filas, [Fuente.Runt, .. porVehiculo ?? []], [Fuente.RuntPropietario], conLocatario ? [Fuente.RuntLocatario] : null, ct);

    public static async Task<CeldaConsulta> CeldaAsync(ApiDePrueba api, EmpresaDePrueba empresa, Guid lote, string identificador, Fuente fuente, CancellationToken ct)
    {
        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, identificador, ct);
        await using var db = empresa.Db();
        return await db.Set<CeldaConsulta>().AsNoTracking().SingleAsync(c => c.VehiculoId == vehiculo.Id && c.Fuente == fuente, ct);
    }

    public static async Task<List<PersonaVehiculo>> PersonasAsync(EmpresaDePrueba empresa, Guid vehiculo, RolPersona rol, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<PersonaVehiculo>().AsNoTracking()
            .Where(p => p.VehiculoId == vehiculo && p.Rol == rol).OrderBy(p => p.Orden).ToListAsync(ct);
    }

    public static async Task<List<IntentoConsulta>> IntentosAsync(EmpresaDePrueba empresa, Guid celda, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<IntentoConsulta>().AsNoTracking().Where(i => i.ConsultaId == celda).OrderBy(i => i.Inicio).ThenBy(i => i.Id).ToListAsync(ct);
    }

    /// <summary>Los elementos de <c>personas</c> del payload, en orden.</summary>
    public static List<JsonElement> PersonasDelPayload(string? payload)
    {
        payload.Should().NotBeNull("la celda de persona guarda lo que respondió");
        return [.. JsonDocument.Parse(payload!).RootElement.GetProperty("personas").EnumerateArray().Select(e => e.Clone())];
    }

    /// <summary>Admin de la compañía con permiso de reproceso (§6.2).</summary>
    public static string Admin(ApiDePrueba api, EmpresaDePrueba empresa) => api.Token(
        empresa: empresa.Id, roles: ["admin_diagnostico"],
        permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear, Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar]);
}
