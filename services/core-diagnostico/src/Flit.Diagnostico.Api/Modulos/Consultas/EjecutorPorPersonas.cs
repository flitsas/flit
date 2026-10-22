using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
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
/// y RUNT_LOCATARIO (<see cref="EjecutorCeldaRuntPersona"/>) y SIMIT_PROPIETARIO y SIMIT_LOCATARIO (<see cref="EjecutorCeldaSimitPersona"/>, HU #13794). La subclase solo
/// dice su <see cref="Fuente"/> y cómo consulta a una persona (<see cref="ConsultarAsync"/>); el patrón vive aquí:
/// <list type="number">
/// <item>Lee las personas del rol en <c>personas_vehiculo</c> (<see cref="DocumentosVehiculo.PersonasDelVehiculoAsync"/>, filtro
/// de empresa explícito) y hace UNA llamada por persona, descifrando su documento justo antes de llamar
/// (<see cref="IProtectorDocumentoPersona"/>); el documento en claro no sale de esa llamada: ni logs, ni intento, ni payload.</item>
/// <item>Guarda en el payload <c>{personas:[{persona_id, orden, consulta, estado OK|ERROR, resultado?, tipo_error?,
/// consultado_en?}]}</c> (<see cref="PayloadPersonas"/>), sin nombres ni documentos.</item>
/// <item>COMPLETADO solo si todas responden. Si alguna falla, ERROR con <see cref="PersonasConError"/> (o el código de la causa
/// si fallan todas por la misma), conservando lo que sí respondió. Si alguna falló con un transitorio, <c>tipo_error</c> lleva
/// ese transitorio para que el procesador decida el backoff (#13737) y <see cref="ResultadoCelda.TipoErrorSinReintento"/> el
/// código final si ya no se reintenta.</item>
/// <item>El reintento automático y el reproceso (#13746) leen el payload que dejó la ejecución anterior y vuelven a llamar
/// SOLO a las personas en ERROR (o que no estaban): las que respondieron no se vuelven a consumir.</item>
/// <item>Un intento por persona consultada (<see cref="ResultadoCelda.IntentosPorPersona"/>), con su <c>persona_id</c> e
/// <c>identificador_consultado=null</c>.</item>
/// </list>
/// Su ERROR no bloquea al vehículo (solo RUNT bloquea, <see cref="OrquestadorVehiculo"/>). La reutilización del mismo día
/// (<see cref="ReutilizacionMismoDia"/>) solo copia una celda de persona si el RUNT del vehículo vino del mismo origen.
/// </summary>
/// <example>
/// <code>
/// internal sealed class EjecutorCeldaSimitPersona(...) : EjecutorPorPersonas(protector, reloj, log)
/// {
///     public override Fuente Fuente => fuente;
///     protected override async Task&lt;ConsultaPersona&gt; ConsultarAsync(Guid empresa, DocumentoPersona doc, CancellationToken ct) =&gt;
///         ConsultaPersona.De("SIMIT", await simit.ConsultarAsync(empresa, SolicitudSimit.PorDocumento(doc), ct));
/// }
/// </code>
/// </example>
internal abstract partial class EjecutorPorPersonas(IProtectorDocumentoPersona protector, TimeProvider reloj, ILogger logger) : IEjecutorCelda
{
    /// <summary><c>tipo_error</c> de la celda cuando falló al menos una persona y no todas por la misma causa.</summary>
    public const string PersonasConError = "PERSONAS_CON_ERROR";

    /// <summary><c>tipo_error</c> si el vehículo no tiene personas del rol (definitivo; no debería pasar tras el RUNT).</summary>
    public const string SinPersonas = "SIN_PERSONAS";

    public const string EstadoOk = "OK";
    public const string EstadoError = "ERROR";

    /// <summary>Error de una fuente que falló sin un error tipado (definitivo).</summary>
    private const string FallaInesperada = "ESTADO_NO_OK";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public abstract Fuente Fuente { get; }

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

    /// <summary>Consulta a UNA persona con su documento en claro; no debe guardarlo ni registrarlo.</summary>
    protected abstract Task<ConsultaPersona> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct);

    /// <summary>Tipo de consulta que corresponde al documento (<c>RUNT_PERSONA</c>, <c>RUES</c>, …), sin llamar a nadie.</summary>
    protected abstract string ConsultaDe(DocumentoPersona documento);

    public async Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var personas = await DocumentosVehiculo.PersonasDelVehiculoAsync(contexto.Db, celda.TenantId, celda.VehiculoId, ct, RolDe(Fuente)).ConfigureAwait(false);
        if (personas.Count == 0)
        {
            CeldaEnError(logger, celda.Id, Fuente, SinPersonas, 0, 0);
            return ResultadoCelda.Error(SinPersonas) with { Intento = new DatosIntento(null, null, null, "El vehículo no tiene personas de ese rol.") };
        }

        // Reintento o reproceso: lo que ya respondió en la ejecución anterior se conserva sin volver a consumir.
        var previas = await PreviasAsync(contexto.Db, celda.Id, ct).ConfigureAwait(false);
        var resultados = new List<ResultadoPorPersona>(personas.Count);
        var intentos = new List<IntentoPersona>();
        foreach (var persona in personas)
        {
            if (previas.TryGetValue(persona.Id, out var previa) && previa.Estado == EstadoOk)
            {
                resultados.Add(previa with { Orden = persona.Orden });
                continue;
            }

            var inicio = reloj.GetUtcNow();
            var consulta = await ConsultarPersonaAsync(celda.TenantId, persona, ct).ConfigureAwait(false);
            var fin = reloj.GetUtcNow();
            var error = consulta.Error ?? (consulta.Resultado is null ? new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "La fuente respondió sin resultado.") : null);
            intentos.Add(new IntentoPersona(persona.Id, inicio, fin, error?.TipoError, new DatosIntento(null, consulta.Servicio, consulta.Transaccion, error?.Mensaje)));
            resultados.Add(error is null
                ? new ResultadoPorPersona(persona.Id, persona.Orden, consulta.Consulta, EstadoOk,
                    JsonSerializer.SerializeToElement(consulta.Resultado!, consulta.Resultado!.GetType(), Json), null, fin)
                : new ResultadoPorPersona(persona.Id, persona.Orden, consulta.Consulta, EstadoError, null, error.TipoError, null));
            if (error is not null)
                PersonaEnError(logger, celda.Id, Fuente, persona.Id, error.TipoError);
        }

        var payload = JsonSerializer.Serialize(new PayloadPersonas(resultados), Json);
        var errores = resultados.Where(r => r.Estado == EstadoError).ToList();
        if (errores.Count == 0)
        {
            CeldaCompletada(logger, celda.Id, Fuente, resultados.Count, intentos.Count);
            return ResultadoCelda.Completado(payload, reloj.GetUtcNow()) with { IntentosPorPersona = intentos };
        }

        var final = errores.Count == resultados.Count && errores.Select(e => e.TipoError).Distinct().Count() == 1
            ? errores[0].TipoError!
            : PersonasConError;
        var transitorio = errores.Select(e => e.TipoError).FirstOrDefault(t => ClasificadorErrores.Clasificar(t) == ClaseError.Transitorio);
        CeldaEnError(logger, celda.Id, Fuente, final, errores.Count, resultados.Count);
        return new ResultadoCelda(EstadoCelda.Error, payload, null, null, null, transitorio ?? final)
        {
            IntentosPorPersona = intentos,
            TipoErrorSinReintento = transitorio is null ? null : final,
        };
    }

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

    private static async Task<Dictionary<Guid, ResultadoPorPersona>> PreviasAsync(DiagnosticoDb db, Guid celdaId, CancellationToken ct)
    {
        var payload = await db.Set<CeldaConsulta>().AsNoTracking().Where(c => c.Id == celdaId).Select(c => c.Payload).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return Leer(payload)?.Personas.GroupBy(p => p.PersonaId).ToDictionary(g => g.Key, g => g.First()) ?? [];
    }

    /// <summary>
    /// Descifra el documento y llama a la fuente. Una falla inesperada (no tipada) de la fuente queda como error definitivo de
    /// ESA persona, sin perder lo que respondieron las demás; la cancelación del proceso se propaga.
    /// </summary>
    private async Task<ConsultaPersona> ConsultarPersonaAsync(Guid empresa, PersonaVehiculo persona, CancellationToken ct)
    {
        var documento = new DocumentoPersona(persona.DocTipo, protector.Desproteger(persona.DocCifrado));
        try
        {
            return await ConsultarAsync(empresa, documento, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // La falla de una persona no tumba la celda: queda en ERROR esa persona.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new ConsultaPersona(ConsultaDe(documento), null, new ErrorConsulta(FallaInesperada, $"Falla inesperada de la fuente ({ex.GetType().Name})."), null, null);
        }
    }

    [LoggerMessage(EventId = 137920, Level = LogLevel.Information, Message = "La celda {CeldaId} de {Fuente} completó con {Personas} personas ({Consultadas} consultadas en esta ejecución)")]
    private static partial void CeldaCompletada(ILogger logger, Guid celdaId, Fuente fuente, int personas, int consultadas);

    [LoggerMessage(EventId = 137921, Level = LogLevel.Warning, Message = "La persona {PersonaId} de la celda {CeldaId} de {Fuente} terminó en error ({TipoError})")]
    private static partial void PersonaEnError(ILogger logger, Guid celdaId, Fuente fuente, Guid personaId, string tipoError);

    [LoggerMessage(EventId = 137922, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} terminó en error ({TipoError}): {Errores} de {Personas} personas en error")]
    private static partial void CeldaEnError(ILogger logger, Guid celdaId, Fuente fuente, string tipoError, int errores, int personas);
}
