using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Lo que devolvió la fuente para UNA persona (HU #13792): el tipo de consulta (<c>RUNT_PERSONA</c>, <c>RUES</c>, …), el
/// resultado normalizado o el error tipado, y el proveedor y la transacción del intento.
/// </summary>
internal sealed record ConsultaPersona(string Consulta, object? Resultado, ErrorConsulta? Error, ServicioConsulta? Servicio, string? Transaccion)
{
    public static ConsultaPersona De<T>(string consulta, RespuestaFuente<T> respuesta)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        return new(consulta, respuesta.Error is null ? respuesta.Resultado : null, respuesta.Error, respuesta.Servicio, respuesta.TransaccionProveedorId);
    }
}

/// <summary>Resultado de una persona dentro del payload de la celda: <c>OK</c> con su resultado o <c>ERROR</c> con su tipo.</summary>
internal sealed record ResultadoPorPersona(
    Guid PersonaId, int Orden, string Consulta, string Estado, JsonElement? Resultado, string? TipoError, DateTimeOffset? ConsultadoEn);

/// <summary>Payload de una celda de persona: <c>{"personas":[...]}</c> en el orden de las personas del rol.</summary>
internal sealed record PayloadPersonas(IReadOnlyList<ResultadoPorPersona> Personas);

/// <summary>
/// Base común de las cuatro celdas de persona (HU #13792, F05-C5, F05-C10, decisión Q1 y decisión 2 del gate): RUNT_PROPIETARIO
/// y RUNT_LOCATARIO (<see cref="EjecutorCeldaRuntPersona"/>) y SIMIT_PROPIETARIO y SIMIT_LOCATARIO (<see cref="EjecutorCeldaSimitPersona"/>,
/// HU #13794). Desde ADR-0075 la celda publica UN trabajo por persona del rol, todos con el mismo <c>trabajo_id</c>
/// (<see cref="Bus.ConstructorPorPersonas"/>: el documento se descifra solo para armar la solicitud y nunca sale en logs, intento ni
/// payload), y cada respuesta llega por separado. La subclase solo dice su <see cref="Fuente"/> y cómo se lee la respuesta de una
/// persona (<see cref="Mapear"/>); el patrón vive aquí:
/// <list type="number">
/// <item>Cada respuesta deja en el payload <c>{personas:[{persona_id, orden, consulta, estado OK|ERROR, resultado?, tipo_error?,
/// consultado_en?}]}</c> (<see cref="PayloadPersonas"/>), sin nombres ni documentos, y su intento con su <c>persona_id</c>.
/// Mientras falte alguna persona del rol, la celda sigue EN_CURSO con lo que ya respondió.</item>
/// <item>Con la última: COMPLETADO si todas respondieron; si alguna falló, ERROR con <see cref="PersonasConError"/> (o el código
/// de la causa si fallan todas por la misma), conservando lo que sí respondió. Si alguna falló con un transitorio,
/// <c>tipo_error</c> lleva ese transitorio para que el consumidor decida el reintento (#13737) y
/// <see cref="ResultadoCelda.TipoErrorSinReintento"/> el código final si ya no se reintenta.</item>
/// <item>El reintento y el reproceso (#13746) solo vuelven a pedir a las personas que no respondieron OK: las que respondieron no
/// se vuelven a consumir (el publicador conserva solo ésas en el payload, <see cref="AlPublicar"/>).</item>
/// <item>Una persona sin documento consultable (NIT sin dígitos) no publica trabajo: al publicar queda en el payload en ERROR
/// definitivo <c>ESTADO_NO_OK</c>, como antes del bus, para que no bloquee el cierre con la última respuesta de las demás.</item>
/// </list>
/// Su ERROR no bloquea al vehículo (solo RUNT bloquea, <see cref="OrquestadorVehiculo"/>). La reutilización del mismo día
/// (<see cref="ReutilizacionMismoDia"/>) solo copia una celda de persona si el RUNT del vehículo vino del mismo origen.
/// </summary>
/// <example>
/// <code>
/// internal sealed class EjecutorCeldaSimitPersona(...) : EjecutorPorPersonas(reloj, log)
/// {
///     public override Fuente Fuente => fuente;
///     protected override ConsultaPersona Mapear(RespuestaConsulta r) =&gt;
///         ConsultaPersona.De("SIMIT", r.Mapear&lt;ConsultarMultasResponse, ResultadoSimit&gt;(MapeoSimit.Mapear, MapeoSimit.Error));
/// }
/// </code>
/// </example>
internal abstract partial class EjecutorPorPersonas(TimeProvider reloj, ILogger logger) : IEjecutorCelda
{
    /// <summary><c>tipo_error</c> de la celda cuando falló al menos una persona y no todas por la misma causa.</summary>
    public const string PersonasConError = "PERSONAS_CON_ERROR";

    /// <summary><c>tipo_error</c> si el vehículo no tiene personas del rol (definitivo; no debería pasar tras el RUNT).</summary>
    public const string SinPersonas = "SIN_PERSONAS";

    public const string EstadoOk = "OK";
    public const string EstadoError = "ERROR";

    /// <summary>Error de una persona sin documento consultable (definitivo).</summary>
    private const string FallaInesperada = "ESTADO_NO_OK";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public abstract Fuente Fuente { get; }

    /// <summary>Tipo de consulta de una persona que no se pudo consultar (sin documento consultable).</summary>
    protected abstract string ConsultaPorDefecto { get; }

    /// <summary>Rol de las personas que consulta una fuente de persona.</summary>
    public static RolPersona RolDe(Fuente fuente) => fuente switch
    {
        Fuente.RuntPropietario or Fuente.SimitPropietario => RolPersona.Propietario,
        Fuente.RuntLocatario or Fuente.SimitLocatario => RolPersona.Locatario,
        _ => throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "No es una fuente por persona."),
    };

    /// <summary>Verdadero si la fuente se consulta por persona.</summary>
    public static bool EsPorPersona(Fuente fuente) =>
        fuente is Fuente.RuntPropietario or Fuente.RuntLocatario or Fuente.SimitPropietario or Fuente.SimitLocatario;

    /// <summary>Lo que respondió core-consultas para UNA persona, normalizado (sin nombres ni documento).</summary>
    protected abstract ConsultaPersona Mapear(RespuestaConsulta respuesta);

    /// <summary>
    /// El constructor no armó ninguna consulta: sin personas del rol, ERROR <see cref="SinPersonas"/>; si todas ya respondieron OK,
    /// COMPLETADO con lo que hay; las que no tienen un documento consultable quedan en ERROR definitivo.
    /// </summary>
    public async Task<ResultadoCelda> SinConsultaAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var personas = await DocumentosVehiculo.PersonasDelVehiculoAsync(contexto.Db, celda.TenantId, celda.VehiculoId, ct, RolDe(Fuente)).ConfigureAwait(false);
        if (personas.Count == 0)
        {
            CeldaEnError(logger, celda.Id, Fuente, SinPersonas, 0, 0);
            return ResultadoCelda.Error(SinPersonas) with { Intento = new DatosIntento(null, null, null, "El vehículo no tiene personas de ese rol.") };
        }

        var previas = await PreviasAsync(contexto.Db, celda.Id, ct).ConfigureAwait(false);
        var resultados = personas
            .Select(p => previas.TryGetValue(p.Id, out var previa) && previa.Estado == EstadoOk ? previa with { Orden = p.Orden } : SinDocumento(p))
            .ToList();
        return Cierre(celda.Id, resultados, [], reloj.GetUtcNow());
    }

    /// <summary>
    /// La respuesta de UNA persona: queda en el payload con su intento; mientras falten personas la celda sigue EN_CURSO y con la
    /// última se cierra. Una respuesta de una persona que ya no es del rol no cambia nada.
    /// </summary>
    public async Task<ResultadoCelda> AplicarAsync(ContextoCelda contexto, RespuestaConsulta respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(respuesta);
        var celda = contexto.Celda;
        var personas = await DocumentosVehiculo.PersonasDelVehiculoAsync(contexto.Db, celda.TenantId, celda.VehiculoId, ct, RolDe(Fuente)).ConfigureAwait(false);
        var previas = await PreviasAsync(contexto.Db, celda.Id, ct).ConfigureAwait(false);
        if (personas.FirstOrDefault(p => p.Id == respuesta.PersonaId) is not { } persona)
        {
            PersonaAjena(logger, celda.Id, Fuente);
            return new ResultadoCelda(EstadoCelda.EnCurso, Serializar(personas, previas), null, null, null) { IntentosPorPersona = [] };
        }

        var consulta = Mapear(respuesta);
        var error = consulta.Error ?? (consulta.Resultado is null ? new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "La fuente respondió sin resultado.") : null);
        var intento = new IntentoPersona(persona.Id, respuesta.Inicio, respuesta.Fin, error?.TipoError, new DatosIntento(null, consulta.Servicio, consulta.Transaccion, error?.Mensaje));
        previas[persona.Id] = error is null
            ? new ResultadoPorPersona(persona.Id, persona.Orden, consulta.Consulta, EstadoOk,
                JsonSerializer.SerializeToElement(consulta.Resultado!, consulta.Resultado!.GetType(), Json), null, respuesta.Fin)
            : new ResultadoPorPersona(persona.Id, persona.Orden, consulta.Consulta, EstadoError, null, error.TipoError, null);
        if (error is not null)
            PersonaEnError(logger, celda.Id, Fuente, persona.Id, error.TipoError);

        if (personas.Any(p => !previas.ContainsKey(p.Id)))
            return new ResultadoCelda(EstadoCelda.EnCurso, Serializar(personas, previas), null, null, null) { IntentosPorPersona = [intento] };

        return Cierre(celda.Id, [.. personas.Select(p => previas[p.Id] with { Orden = p.Orden })], [intento], respuesta.Fin);
    }

    /// <summary>
    /// El payload con SOLO las personas que respondieron OK (null si ninguna): lo deja el publicador al pedir de nuevo a las demás,
    /// para que la celda se cierre cuando lleguen todas las respuestas de ese trabajo.
    /// </summary>
    internal static string? SoloRespondidas(string? payload)
    {
        var ok = Leer(payload)?.Personas.Where(p => p.Estado == EstadoOk).ToList();
        return ok is { Count: > 0 } ? JsonSerializer.Serialize(new PayloadPersonas(ok), Json) : null;
    }

    /// <summary>
    /// El payload al publicar el trabajo (HU #13727): las personas del rol que ya respondieron OK y, en ERROR definitivo
    /// <c>ESTADO_NO_OK</c>, las que no tienen solicitud en <paramref name="consultadas"/> (sin documento consultable), igual que en
    /// <see cref="SinConsultaAsync"/>. Así la celda se cierra con la última respuesta de las consultadas; null si no queda ninguna.
    /// </summary>
    internal string? AlPublicar(string? payload, IReadOnlyList<PersonaVehiculo> personas, IReadOnlySet<Guid> consultadas)
    {
        ArgumentNullException.ThrowIfNull(personas);
        ArgumentNullException.ThrowIfNull(consultadas);
        var ok = Leer(payload)?.Personas.Where(p => p.Estado == EstadoOk).GroupBy(p => p.PersonaId).ToDictionary(g => g.Key, g => g.First()) ?? [];
        var resultados = personas
            .Where(p => ok.ContainsKey(p.Id) || !consultadas.Contains(p.Id))
            .Select(p => ok.TryGetValue(p.Id, out var previa) ? previa with { Orden = p.Orden } : SinDocumento(p))
            .ToList();
        return resultados.Count == 0 ? null : JsonSerializer.Serialize(new PayloadPersonas(resultados), Json);
    }

    /// <summary>
    /// Las personas del rol de la celda: las guardadas y las que la RUNT acaba de registrar en el mismo contexto y todavía no se
    /// guardaron, en su orden (lo que usan el constructor de solicitudes y el publicador).
    /// </summary>
    internal static async Task<List<PersonaVehiculo>> PersonasDelRolAsync(DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(celda);
        var rol = RolDe(celda.Fuente);
        var guardadas = await DocumentosVehiculo.PersonasDelVehiculoAsync(db, celda.TenantId, celda.VehiculoId, ct, rol).ConfigureAwait(false);
        var nuevas = db.ChangeTracker.Entries<PersonaVehiculo>()
            .Where(e => e.State == EntityState.Added && e.Entity.TenantId == celda.TenantId && e.Entity.VehiculoId == celda.VehiculoId && e.Entity.Rol == rol)
            .Select(e => e.Entity)
            .Where(n => guardadas.TrueForAll(g => g.Id != n.Id));
        return [.. guardadas.Concat(nuevas).OrderBy(p => p.Orden)];
    }

    private ResultadoPorPersona SinDocumento(PersonaVehiculo persona) =>
        new(persona.Id, persona.Orden, ConsultaPorDefecto, EstadoError, null, FallaInesperada, null);

    /// <summary>
    /// El payload copiado de otra celda con los <c>persona_id</c> traducidos a las personas del vehículo nuevo (reutilización del
    /// mismo día, HU #13792); null si alguna persona no tiene su par (entonces no se reutiliza).
    /// </summary>
    internal static string? Traducir(string payload, IReadOnlyDictionary<Guid, Guid> personas)
    {
        ArgumentNullException.ThrowIfNull(personas);
        var leido = Leer(payload);
        if (leido is null || leido.Personas.Count != personas.Count)
            return null;
        var traducidas = new List<ResultadoPorPersona>(leido.Personas.Count);
        foreach (var p in leido.Personas)
        {
            if (!personas.TryGetValue(p.PersonaId, out var nueva))
                return null;
            traducidas.Add(p with { PersonaId = nueva });
        }

        return JsonSerializer.Serialize(new PayloadPersonas(traducidas), Json);
    }

    /// <summary>El payload de una celda de persona; null si no hay o no tiene la forma esperada.</summary>
    internal static PayloadPersonas? Leer(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PayloadPersonas>(payload, Json) is { Personas: not null } leido ? leido : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>El cierre de la celda con todas sus personas: COMPLETADO, o ERROR con el transitorio para el reintento.</summary>
    private ResultadoCelda Cierre(Guid celdaId, List<ResultadoPorPersona> resultados, IReadOnlyList<IntentoPersona> intentos, DateTimeOffset ahora)
    {
        var payload = JsonSerializer.Serialize(new PayloadPersonas(resultados), Json);
        var errores = resultados.Where(r => r.Estado == EstadoError).ToList();
        if (errores.Count == 0)
        {
            CeldaCompletada(logger, celdaId, Fuente, resultados.Count, intentos.Count);
            return ResultadoCelda.Completado(payload, ahora) with { IntentosPorPersona = intentos };
        }

        var final = errores.Count == resultados.Count && errores.Select(e => e.TipoError).Distinct().Count() == 1
            ? errores[0].TipoError!
            : PersonasConError;
        var transitorio = errores.Select(e => e.TipoError).FirstOrDefault(t => ClasificadorErrores.Clasificar(t) == ClaseError.Transitorio);
        CeldaEnError(logger, celdaId, Fuente, final, errores.Count, resultados.Count);
        return new ResultadoCelda(EstadoCelda.Error, payload, null, null, null, transitorio ?? final)
        {
            IntentosPorPersona = intentos,
            TipoErrorSinReintento = transitorio is null ? null : final,
        };
    }

    private static string? Serializar(IReadOnlyList<PersonaVehiculo> personas, Dictionary<Guid, ResultadoPorPersona> previas)
    {
        var parciales = personas.Where(p => previas.ContainsKey(p.Id)).Select(p => previas[p.Id] with { Orden = p.Orden }).ToList();
        return parciales.Count == 0 ? null : JsonSerializer.Serialize(new PayloadPersonas(parciales), Json);
    }

    private static async Task<Dictionary<Guid, ResultadoPorPersona>> PreviasAsync(DiagnosticoDb db, Guid celdaId, CancellationToken ct)
    {
        var payload = await db.Set<CeldaConsulta>().AsNoTracking().Where(c => c.Id == celdaId).Select(c => c.Payload).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return Leer(payload)?.Personas.GroupBy(p => p.PersonaId).ToDictionary(g => g.Key, g => g.First()) ?? [];
    }

    [LoggerMessage(EventId = 137920, Level = LogLevel.Information, Message = "La celda {CeldaId} de {Fuente} completó con {Personas} personas ({Consultadas} consultadas en este cierre)")]
    private static partial void CeldaCompletada(ILogger logger, Guid celdaId, Fuente fuente, int personas, int consultadas);

    [LoggerMessage(EventId = 137921, Level = LogLevel.Warning, Message = "La persona {PersonaId} de la celda {CeldaId} de {Fuente} terminó en error ({TipoError})")]
    private static partial void PersonaEnError(ILogger logger, Guid celdaId, Fuente fuente, Guid personaId, string tipoError);

    [LoggerMessage(EventId = 137922, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} terminó en error ({TipoError}): {Errores} de {Personas} personas en error")]
    private static partial void CeldaEnError(ILogger logger, Guid celdaId, Fuente fuente, string tipoError, int errores, int personas);

    [LoggerMessage(EventId = 137923, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} recibió la respuesta de una persona que no es del rol: no cambia")]
    private static partial void PersonaAjena(ILogger logger, Guid celdaId, Fuente fuente);
}
