using System.Security.Cryptography;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
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
/// <item>Fuentes del vehículo (RUNT, SIMIT_VEHICULO, AVALUO) con la celda COMPLETADO: <c>resultado</c> leído del payload a un tipo
/// propio y cerrado (<see cref="ResultadoDetalleDto"/>); lo que el payload traiga de más (proveedor, transacción, crudo) no
/// pasa porque el tipo no lo tiene. El AVALUO sin valor llega con <c>sin_valor_comercial=true</c> (Q4 = b).</item>
/// <item>Fuentes de persona: <c>personas</c>, una por cada persona del rol en <c>personas_vehiculo</c>, con su documento
/// descifrado SOLO para enmascararlo (<see cref="EnmascaradorDocumento"/>), si lo declaró el cliente y su resultado del payload
/// (<c>OK</c>/<c>ERROR</c>, también en una celda ERROR <c>PERSONAS_CON_ERROR</c>); sin dato, <c>SIN_RESULTADO</c>.</item>
/// <item>SIMIT trae como mucho <see cref="TopeComparendos"/> comparendos por resultado; <c>cantidad_comparendos</c> es el total
/// y <c>comparendos_truncados</c> avisa del corte.</item>
/// </list>
/// Nunca origen, usuario manual, proveedor, transacción, nombres ni documentos completos: una celda manual y una de API tienen
/// las mismas claves (R-20).
/// </summary>
internal sealed class DetalleCelda(DiagnosticoDb db, UsuarioActual usuario, IProtectorDocumentoPersona protector)
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
            .Select(c => new { c.Id, c.Estado, c.MotivoNa, c.TipoError, c.DatoObtenidoEn, Reutilizada = c.ReutilizadaDeId != null, c.Payload })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La celda");

        var conDato = celda.Estado is EstadoCelda.Completado or EstadoCelda.Error;
        IReadOnlyList<PersonaDetalleDto>? personas = null;
        ResultadoDetalleDto? resultado = null;
        if (EjecutorPorPersonas.EsPorPersona(codigo))
            personas = await PersonasAsync(cabecera.TenantId, vehiculoId, codigo, conDato ? celda.Payload : null, ct).ConfigureAwait(false);
        else if (celda.Estado == EstadoCelda.Completado)
            resultado = ProyectorDetalleCelda.ResultadoVehiculo(codigo, celda.Payload);

        return new DetalleCeldaDto(
            celda.Id, vehiculoId, CodigosDeEnum.Codigo(codigo), CodigosDeEnum.Codigo(celda.Estado),
            celda.MotivoNa is { } motivo ? CodigosDeEnum.Codigo(motivo) : null, celda.TipoError, celda.DatoObtenidoEn, celda.Reutilizada,
            resultado, personas);
    }

    /// <summary>Las personas del rol de la fuente, en orden, con su resultado del payload si la celda ya tiene dato.</summary>
    private async Task<IReadOnlyList<PersonaDetalleDto>> PersonasAsync(Guid empresa, Guid vehiculoId, Fuente fuente, string? payload, CancellationToken ct)
    {
        var filas = await DocumentosVehiculo.PersonasDelVehiculoAsync(db, empresa, vehiculoId, ct, EjecutorPorPersonas.RolDe(fuente)).ConfigureAwait(false);
        return ProyectorDetalleCelda.Personas(filas, payload, protector);
    }
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
    /// (<paramref name="payload"/> null si la celda aún no tiene dato: todas <see cref="DetalleCelda.SinResultado"/>).
    /// </summary>
    public static IReadOnlyList<PersonaDetalleDto> Personas(IEnumerable<PersonaVehiculo> filas, string? payload, IProtectorDocumentoPersona protector)
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
                leido is { Estado: EjecutorPorPersonas.EstadoOk, Resultado: { } r } ? ResultadoPersona(leido.Consulta, r) : null);
        })];
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

    /// <summary>El resultado de una fuente del vehículo (RUNT, SIMIT_VEHICULO, AVALUO) leído del payload; null si no se puede leer.</summary>
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
