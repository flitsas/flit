using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>Respuesta de <c>GET /diagnosticos/{id}/historial</c>: el estado actual y sus transiciones, de la más antigua a la más reciente.</summary>
public sealed record HistorialLoteDto(
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("transiciones")] IReadOnlyList<TransicionLoteDto> Transiciones);

/// <summary>
/// Una transición del lote: estado anterior (null en la creación), nuevo, disparador (<c>AUTO</c>, <c>CIERRE_PANEL</c> o
/// <c>REPROCESO</c>; el <c>MANUAL</c> se presenta como <c>AUTO</c> a la compañía, HU #13798), usuario (null si fue el
/// procesador o el llenado manual) y fecha. La ruta del backoffice (HU #13799) usa el mismo registro con el disparador real
/// (<c>MANUAL</c> incluido) y su usuario.
/// </summary>
public sealed record TransicionLoteDto(
    [property: JsonPropertyName("estado_anterior")] string? EstadoAnterior,
    [property: JsonPropertyName("estado_nuevo")] string EstadoNuevo,
    [property: JsonPropertyName("disparador")] string Disparador,
    [property: JsonPropertyName("usuario_id")] Guid? UsuarioId,
    [property: JsonPropertyName("en")] DateTimeOffset En);

/// <summary>
/// Historial de estados del lote (HU #13743, F04-C7, R-8): lectura de <c>historial_estado_diagnostico</c> (solo inserción) con
/// la misma visibilidad que la grilla (<see cref="AccesoLote"/>). Orden: fecha ascendente y, a igual fecha, id (uuid v7). Es la
/// vista de la compañía: una transición <c>MANUAL</c> (llenado del backoffice) se devuelve como <c>AUTO</c> y sin usuario
/// (HU #13798, F06-C5, decisión Q5 = a); el backoffice ve el disparador y el usuario reales por su propia ruta (F06-H2).
/// </summary>
internal sealed class HistorialLote(DiagnosticoDb db, UsuarioActual usuario)
{
    public async Task<HistorialLoteDto> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var filas = await db.Set<HistorialEstadoDiagnostico>().AsNoTracking()
            .Where(h => h.DiagnosticoId == id)
            .OrderBy(h => h.En).ThenBy(h => h.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return new HistorialLoteDto(cabecera.Id, CodigosDeEnum.Codigo(cabecera.Estado), [.. filas.Select(h =>
        {
            // HU #13798 (Q5 = a): el llenado manual del backoffice se presenta a la compañía como AUTO y sin usuario.
            var manual = h.Disparador == DisparadorEstado.Manual;
            return new TransicionLoteDto(
                h.EstadoAnterior is { } anterior ? CodigosDeEnum.Codigo(anterior) : null, CodigosDeEnum.Codigo(h.EstadoNuevo),
                CodigosDeEnum.Codigo(manual ? DisparadorEstado.Auto : h.Disparador), manual ? null : h.UsuarioId, h.En);
        })]);
    }
}
