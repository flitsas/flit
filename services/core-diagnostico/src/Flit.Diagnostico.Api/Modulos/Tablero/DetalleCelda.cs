using System.Security.Cryptography;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Detalle de UNA celda para la compañía (HU #13798, F06-H1, F06-C1..C4/C12): <c>GET
/// /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}</c> con <c>diagnostico.diagnosticos.ver</c> y la visibilidad de
/// <see cref="AccesoLote"/> (otra compañía, PROPIOS de otro usuario, vehículo de otro lote o fuente sin celda: 404 sin datos;
/// código de fuente desconocido: 400 <see cref="CodigoFuenteInvalida"/>).
/// <list type="bullet">
/// <item>Fuentes del vehículo (RUNT, SIMIT_VEHICULO, AVALUO, IMPUESTO_DEPARTAMENTAL) con la celda COMPLETADO: <c>resultado</c> leído del payload a un tipo
/// propio y cerrado (<see cref="ResultadoDetalleDto"/>); lo que el payload traiga de más (proveedor, transacción, crudo) no
/// pasa porque el tipo no lo tiene. El AVALUO sin valor llega con <c>sin_valor_comercial=true</c> (Q4 = b). El impuesto
/// (HU #13864) trae <c>monto_total</c> y sus vigencias por año, sin el <c>departamento_codigo</c> del payload.</item>
/// <item>Fuentes de persona: <c>personas</c>, una por cada persona del rol en <c>personas_vehiculo</c>, con su documento
/// descifrado SOLO para enmascararlo (<see cref="EnmascaradorDocumento"/>), si lo declaró el cliente y su resultado del payload
/// (<c>OK</c>/<c>ERROR</c>, también en una celda ERROR <c>PERSONAS_CON_ERROR</c>); sin dato, <c>SIN_RESULTADO</c>.</item>
/// <item>SIMIT trae como mucho <see cref="TopeComparendos"/> comparendos por resultado; <c>cantidad_comparendos</c> es el total
/// y <c>comparendos_truncados</c> avisa del corte.</item>
/// </list>
/// Nunca origen ni usuario manual en campos propios: una celda manual y una de API tienen las mismas claves (R-20).
/// <para>
/// HU #14087 AC8 (decisión del usuario 2026-10-27; enmienda de ADR-0075 en Propuesto): <c>respuesta_fuente</c> trae además la
/// respuesta COMPLETA de la fuente tal como llegó de core-consultas (proto-JSON con los nombres del .proto), descifrada de
/// <c>consultas.respuesta_fuente_cifrada</c> (<see cref="RespuestaFuenteCelda"/>): en la celda en las fuentes del vehículo y en
/// cada persona en las de persona, solo con la celda COMPLETADO o ERROR. Incluye datos personales y lo que la fuente responda
/// (nombres, direcciones, documentos, proveedor). Null sin respuesta guardada (llenado manual, ERROR sin respuesta, celda anterior
/// al AC8) o si no se puede descifrar (warning sin datos; nunca 500).
/// </para>
/// </summary>
internal sealed partial class DetalleCelda(
    DiagnosticoDb db, UsuarioActual usuario, IProtectorDocumentoPersona protector, IProtectorRespuestaFuente protectorRespuesta, ILogger<DetalleCelda> log)
{
    /// <summary>Máximo de comparendos por resultado SIMIT en el detalle (la HU no pide paginar; SIMIT por persona puede traer decenas).</summary>
    public const int TopeComparendos = 100;

    public const string CodigoFuenteInvalida = "FUENTE_INVALIDA";

    /// <summary>Estado de una persona sin dato en la celda (pendiente, en curso, N/A o aún no consultada).</summary>
    public const string SinResultado = "SIN_RESULTADO";

    public async Task<DetalleCeldaDto> ObtenerAsync(Guid id, Guid vehiculoId, string fuente, CancellationToken ct)
    {
        if (!CodigosDeEnum.TryLeer<Fuente>(fuente, out var codigo))
            throw new ReglaException(CodigoFuenteInvalida, "fuente no es una fuente del diagnóstico.", StatusCodes.Status400BadRequest) { Campo = "fuente" };
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var celda = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == id && c.VehiculoId == vehiculoId && c.Fuente == codigo)
            .Select(c => new { c.Id, c.Estado, c.MotivoNa, c.TipoError, c.DatoObtenidoEn, Reutilizada = c.ReutilizadaDeId != null, c.Payload, c.RespuestaFuenteCifrada })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La celda");
        // HU #14087: la página del detalle en pestaña nueva se titula con la placa (o el VIN) y el nombre del lote.
        var identificador = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.Id == vehiculoId && v.DiagnosticoId == id)
            .Select(v => v.Placa ?? v.Vin ?? v.IdentificadorNorm)
            .SingleAsync(ct).ConfigureAwait(false);

        var conDato = celda.Estado is EstadoCelda.Completado or EstadoCelda.Error;
        IReadOnlyList<PersonaDetalleDto>? personas = null;
        ResultadoDetalleDto? resultado = null;
        JsonElement? respuestaFuente = null;
        var cifrada = conDato ? celda.RespuestaFuenteCifrada : null;
        if (EjecutorPorPersonas.EsPorPersona(codigo))
        {
            if (!RespuestaFuenteCelda.TryLeerPersonas(protectorRespuesta, cifrada, out var respuestas))
                RespuestaIlegible(log, celda.Id, codigo);
            personas = await PersonasAsync(cabecera.TenantId, vehiculoId, codigo, conDato ? celda.Payload : null, respuestas, ct).ConfigureAwait(false);
            foreach (var orden in ProyectorDetalleCelda.ConCrudoIlegible(respuestas))
                CrudoIlegible(log, celda.Id, codigo, orden);
        }
        else
        {
            if (celda.Estado == EstadoCelda.Completado)
                resultado = ProyectorDetalleCelda.ResultadoVehiculo(codigo, celda.Payload);
            if (!RespuestaFuenteCelda.TryLeerVehiculo(protectorRespuesta, cifrada, out respuestaFuente))
                RespuestaIlegible(log, celda.Id, codigo);
        }

        return new DetalleCeldaDto(
            celda.Id, vehiculoId, CodigosDeEnum.Codigo(codigo), CodigosDeEnum.Codigo(celda.Estado),
            celda.MotivoNa is { } motivo ? CodigosDeEnum.Codigo(motivo) : null, celda.TipoError, celda.DatoObtenidoEn, celda.Reutilizada,
            resultado, personas, respuestaFuente, identificador, cabecera.Nombre);
    }

    /// <summary>
    /// Las personas del rol de la fuente, en orden, con su resultado del payload si la celda ya tiene dato y su respuesta completa
    /// de la fuente por orden (HU #14087 AC8).
    /// </summary>
    private async Task<IReadOnlyList<PersonaDetalleDto>> PersonasAsync(
        Guid empresa, Guid vehiculoId, Fuente fuente, string? payload, IReadOnlyDictionary<int, JsonElement> respuestas, CancellationToken ct)
    {
        var filas = await DocumentosVehiculo.PersonasDelVehiculoAsync(db, empresa, vehiculoId, ct, EjecutorPorPersonas.RolDe(fuente)).ConfigureAwait(false);
        return ProyectorDetalleCelda.Personas(filas, payload, protector, respuestas);
    }

    [LoggerMessage(EventId = 140878, Level = LogLevel.Warning,
        Message = "La respuesta de la fuente de la celda {CeldaId} ({Fuente}) no se pudo descifrar o leer: el detalle va con respuesta_fuente = null")]
    private static partial void RespuestaIlegible(ILogger logger, Guid celdaId, Fuente fuente);

    // HU #14153 AC7: sin el contenido del crudo (datos personales): solo la celda, la fuente y el orden de la persona.
    [LoggerMessage(EventId = 141531, Level = LogLevel.Warning,
        Message = "La respuesta cruda del proveedor de la persona {Orden} de la celda {CeldaId} ({Fuente}) no es un objeto JSON válido: el detalle va con respuesta_proveedor = null")]
    private static partial void CrudoIlegible(ILogger logger, Guid celdaId, Fuente fuente, int orden);
}

/// <summary>
/// Proyección cerrada del dato de una celda (HU #13798), compartida por el detalle de celda y el contenido de los reportes
/// (HU #13847, <c>LectorContenidoReporte</c>): el resultado de una fuente del vehículo leído del payload a un tipo propio y las
/// personas de una fuente de persona con el documento enmascarado. Lo que el payload traiga de más (proveedor, transacción,
/// crudo, nombres) no pasa porque los tipos no lo tienen. El documento se descifra solo para enmascararlo y no sale de aquí.
/// </summary>
internal static class ProyectorDetalleCelda
{
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>
    /// Las personas <paramref name="filas"/> (ya del rol de la fuente, en orden) con su resultado del payload de la celda
    /// (<paramref name="payload"/> null si la celda aún no tiene dato: todas <see cref="DetalleCelda.SinResultado"/>) y, si se dan,
    /// sus respuestas completas de la fuente por orden (<paramref name="respuestas"/>, HU #14087 AC8; null en los reportes).
    /// </summary>
    public static IReadOnlyList<PersonaDetalleDto> Personas(
        IEnumerable<PersonaVehiculo> filas, string? payload, IProtectorDocumentoPersona protector, IReadOnlyDictionary<int, JsonElement>? respuestas = null)
    {
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(protector);
        var resultados = EjecutorPorPersonas.Leer(payload)?.Personas.GroupBy(p => p.PersonaId).ToDictionary(g => g.Key, g => g.First()) ?? [];
        return [.. filas.Select(p =>
        {
            var leido = resultados.GetValueOrDefault(p.Id);
            return new PersonaDetalleDto(
                p.Orden, CodigosDeEnum.Codigo(p.Rol), p.DocTipo, Enmascarado(protector, p.DocCifrado), p.Origen == OrigenDocumento.DeclaradoCliente,
                leido?.Consulta, leido?.Estado ?? DetalleCelda.SinResultado, leido?.TipoError, leido?.ConsultadoEn,
                leido is { Estado: EjecutorPorPersonas.EstadoOk, Resultado: { } r } ? ResultadoPersona(leido.Consulta, r) : null,
                respuestas is not null && respuestas.TryGetValue(p.Orden, out var respuesta) ? respuesta : null,
                respuestas is not null && respuestas.TryGetValue(p.Orden, out var deLaFuente) ? RespuestaProveedor(deLaFuente, out _) : null);
        })];
    }

    /// <summary>
    /// HU #14153 AC6/AC7 — la respuesta íntegra del proveedor dentro de la respuesta de la fuente de una persona: el texto de
    /// <c>resultado.respuesta_cruda.json</c> parseado como objeto JSON, tal cual (sin quitar claves). Null sin crudo; null y
    /// <paramref name="ilegible"/> = true si hay crudo pero no es JSON válido o no es un objeto.
    /// </summary>
    public static JsonElement? RespuestaProveedor(JsonElement respuestaFuente, out bool ilegible)
    {
        ilegible = false;
        if (respuestaFuente.ValueKind != JsonValueKind.Object
            || !respuestaFuente.TryGetProperty("resultado", out var resultado) || resultado.ValueKind != JsonValueKind.Object
            || !resultado.TryGetProperty("respuesta_cruda", out var cruda) || cruda.ValueKind != JsonValueKind.Object
            || !cruda.TryGetProperty("json", out var json) || json.ValueKind != JsonValueKind.String
            || json.GetString() is not { Length: > 0 } texto)
            return null;
        try
        {
            using var documento = JsonDocument.Parse(texto);
            if (documento.RootElement.ValueKind == JsonValueKind.Object)
                return documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Cae a ilegible: el contenido nunca se registra.
        }

        ilegible = true;
        return null;
    }

    /// <summary>Los órdenes de las personas de <paramref name="respuestas"/> con un crudo del proveedor que no es un objeto JSON válido.</summary>
    public static IEnumerable<int> ConCrudoIlegible(IReadOnlyDictionary<int, JsonElement> respuestas)
    {
        ArgumentNullException.ThrowIfNull(respuestas);
        foreach (var (orden, respuesta) in respuestas.OrderBy(r => r.Key))
        {
            RespuestaProveedor(respuesta, out var ilegible);
            if (ilegible)
                yield return orden;
        }
    }

    /// <summary>El documento descifrado solo para enmascararlo; si no se puede descifrar, solo la máscara.</summary>
    private static string Enmascarado(IProtectorDocumentoPersona protector, byte[] cifrado)
    {
        try
        {
            return EnmascaradorDocumento.Enmascarar(protector.Desproteger(cifrado));
        }
        catch (CryptographicException)
        {
            return EnmascaradorDocumento.Mascara;
        }
    }

    /// <summary>El resultado de una fuente del vehículo (RUNT, SIMIT_VEHICULO, AVALUO, IMPUESTO_DEPARTAMENTAL) leído del payload; null si no se puede leer.</summary>
    public static ResultadoDetalleDto? ResultadoVehiculo(Fuente fuente, string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            using var documento = JsonDocument.Parse(payload);
            return fuente switch
            {
                Fuente.Runt => Runt(documento.RootElement),
                Fuente.SimitVehiculo => Simit(documento.RootElement),
                Fuente.Avaluo => Avaluo(documento.RootElement),
                Fuente.ImpuestoDepartamental => Impuesto(documento.RootElement),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ResultadoDetalleDto? ResultadoPersona(string consulta, JsonElement resultado)
    {
        try
        {
            return consulta switch
            {
                EjecutorCeldaRuntPersona.ConsultaRuntPersona => resultado.Deserialize<ResultadoRuntPersonaDetalle>(Json) is { } r
                    ? r with { Licencias = r.Licencias ?? [] }
                    : null,
                EjecutorCeldaRuntPersona.ConsultaRues => resultado.Deserialize<ResultadoRuesDetalle>(Json),
                EjecutorCeldaSimitPersona.ConsultaSimit => Simit(resultado),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ResultadoRuntDetalle? Runt(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.Deserialize<ResultadoRuntDetalle>(Json) is { } r ? r with { Propietarios = r.Propietarios ?? [] } : null;

    private static ResultadoAvaluoDetalle? Avaluo(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.Deserialize<ResultadoAvaluoDetalle>(Json) is { } r ? r with { Fuentes = r.Fuentes ?? [] } : null;

    /// <summary>
    /// Impuesto departamental (HU #13864): total adeudado y vigencias por año ascendente. El payload de <c>EjecutorCeldaImpuesto</c>
    /// trae además <c>departamento_codigo</c>, que no se proyecta (R-20: solo lo que el tipo tiene).
    /// </summary>
    private static ResultadoImpuestoDetalle? Impuesto(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.Deserialize<ResultadoImpuestoDetalle>(Json) is { } r
            ? r with { Vigencias = [.. (r.Vigencias ?? []).OrderBy(v => v.Anio)] }
            : null;

    /// <summary>SIMIT con como mucho <see cref="DetalleCelda.TopeComparendos"/> comparendos; el corte lo calcula el detalle, no el payload.</summary>
    private static ResultadoSimitDetalle? Simit(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || e.Deserialize<ResultadoSimitDetalle>(Json) is not { } r)
            return null;
        var comparendos = r.Comparendos ?? [];
        return comparendos.Count > DetalleCelda.TopeComparendos
            ? r with { Comparendos = [.. comparendos.Take(DetalleCelda.TopeComparendos)], ComparendosTruncados = true }
            : r with { Comparendos = comparendos, ComparendosTruncados = false };
    }
}
