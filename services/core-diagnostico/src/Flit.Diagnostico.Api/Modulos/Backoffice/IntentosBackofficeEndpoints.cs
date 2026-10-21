using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// <c>GET /backoffice/consultas/{consulta}/intentos?empresa=</c> (HU #13737, F03-C11, plan §6.1 fila 03): la auditoría de los
/// intentos de una celda, solo para el backoffice FLIT. Cuelga del grupo <c>/backoffice</c> (<see cref="PoliticaBackoffice"/>:
/// <c>diagnostico.backoffice.ver</c> + <c>tenant_type=FLIT</c>; empresa explícita, la lectura queda en
/// <c>lecturas_backoffice</c>) y exige además <c>diagnostico.auditoria.ver</c>. Un usuario de la compañía recibe 403. Lee la
/// celda y sus intentos de la empresa indicada con <see cref="LecturaEntreEmpresas"/>; si la celda no es de esa empresa, 404
/// <c>CONSULTA_NO_ENCONTRADA</c>. Los intentos no llevan documentos (solo placa o VIN consultado).
/// </summary>
internal static class IntentosBackofficeEndpoints
{
    public const string Ruta = "/consultas/{consulta:guid}/intentos";
    public const string CodigoNoEncontrada = "CONSULTA_NO_ENCONTRADA";

    public static void Map(RouteGroupBuilder backoffice) =>
        backoffice.MapGet(Ruta, Listar).RequirePermiso(Permisos.AuditoriaVer).WithName("ListarIntentosConsultaBackoffice");

    private static async Task<IResult> Listar(Guid consulta, HttpContext http, DiagnosticoDb db, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        if (!await db.DeLaEmpresa<CeldaConsulta>(empresa).AnyAsync(c => c.Id == consulta, ct).ConfigureAwait(false))
        {
            return Results.Json(
                new { code = CodigoNoEncontrada, error = "La consulta no existe en la empresa indicada." },
                statusCode: StatusCodes.Status404NotFound);
        }

        var intentos = await db.DeLaEmpresa<IntentoConsulta>(empresa)
            .Where(i => i.ConsultaId == consulta)
            .OrderBy(i => i.Inicio).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new IntentosConsultaDto(consulta, [.. intentos.Select(IntentoDto.De)]));
    }
}

/// <summary>Intentos de una celda para el backoffice (HU #13737).</summary>
internal sealed record IntentosConsultaDto(
    [property: JsonPropertyName("consulta")] Guid Consulta,
    [property: JsonPropertyName("intentos")] IReadOnlyList<IntentoDto> Intentos);

/// <summary>Un intento tal como lo audita el backoffice (HU #13737): códigos en mayúsculas, sin documentos.</summary>
internal sealed record IntentoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("servicio")] string? Servicio,
    [property: JsonPropertyName("identificador")] string? Identificador,
    [property: JsonPropertyName("disparador")] string Disparador,
    [property: JsonPropertyName("inicio")] DateTimeOffset Inicio,
    [property: JsonPropertyName("fin")] DateTimeOffset Fin,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("tipo_error")] string? TipoError,
    [property: JsonPropertyName("mensaje_error")] string? MensajeError,
    [property: JsonPropertyName("transaccion_proveedor_id")] string? TransaccionProveedorId,
    [property: JsonPropertyName("desde_cache")] bool DesdeCache,
    [property: JsonPropertyName("usuario_id")] Guid? UsuarioId)
{
    public static IntentoDto De(IntentoConsulta i) => new(
        i.Id,
        CodigosDeEnum.Codigo(i.Fuente),
        i.Servicio is { } s ? CodigosDeEnum.Codigo(s) : null,
        i.IdentificadorConsultado,
        CodigosDeEnum.Codigo(i.Disparador),
        i.Inicio,
        i.Fin,
        CodigosDeEnum.Codigo(i.Estado),
        i.TipoError,
        i.MensajeError,
        i.TransaccionProveedorId,
        i.DesdeCache,
        i.UsuarioId);
}
