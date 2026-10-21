using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>Respuesta de <c>POST /diagnosticos/{id}/cerrar-panel</c>: el lote ya cerrado.</summary>
public sealed record CierrePanelDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("cerrado_en")] DateTimeOffset CerradoEn);

/// <summary>
/// Cierre del panel (HU #13743, F04-C5/C7, plan §3.5): un lote EN_PROCESO con todas sus celdas terminadas y al menos una en
/// ERROR pasa a INCOMPLETO, con <c>cerrado_en</c> y una fila del historial <c>CIERRE_PANEL</c> con el usuario, en un solo
/// guardado. Si no procede responde 409 sin cambiar nada: <c>LOTE_NO_EN_PROCESO</c> (CREADO, COMPLETADO o INCOMPLETO),
/// <c>CELDAS_SIN_TERMINAR</c> (hay celdas PENDIENTE o EN_CURSO) o <c>LOTE_SIN_ERRORES</c> (se completa solo). Si el procesador
/// toca el lote entre la lectura y el guardado (<c>xmin</c>), 409 <c>LOTE_MODIFICADO</c>: el cliente vuelve a leer la grilla.
/// Misma visibilidad que la grilla (<see cref="AccesoLote"/>): otra compañía o un lote ajeno con PROPIOS, 404.
/// </summary>
internal sealed class CierrePanel(DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj)
{
    public const string CodigoLoteNoEnProceso = "LOTE_NO_EN_PROCESO";
    public const string CodigoCeldasSinTerminar = "CELDAS_SIN_TERMINAR";
    public const string CodigoLoteSinErrores = "LOTE_SIN_ERRORES";
    public const string CodigoLoteModificado = "LOTE_MODIFICADO";

    public async Task<CierrePanelDto> CerrarAsync(Guid id, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: true, ct).ConfigureAwait(false);
        var autor = usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el cierre del panel no se puede registrar.");
        var estados = await db.Set<CeldaConsulta>().AsNoTracking().Where(c => c.DiagnosticoId == id).Select(c => c.Estado).ToListAsync(ct).ConfigureAwait(false);

        switch (EstadoGlobalLote.EvaluarCierrePanel(cabecera.Estado, estados))
        {
            case ResultadoCierrePanel.LoteNoEnProceso:
                throw ReglaException.Conflicto(CodigoLoteNoEnProceso, $"El panel solo se cierra con el lote en proceso; está {CodigosDeEnum.Codigo(cabecera.Estado)}.");
            case ResultadoCierrePanel.CeldasSinTerminar:
                throw ReglaException.Conflicto(CodigoCeldasSinTerminar, "Todavía hay consultas en proceso: espera a que terminen para cerrar el panel.");
            case ResultadoCierrePanel.SinErrores:
                throw ReglaException.Conflicto(CodigoLoteSinErrores, "El lote no tiene consultas en error: se completa solo.");
        }

        var ahora = reloj.GetUtcNow();
        var transicion = TransicionesLote.Transicionar(
            cabecera.Estado, EstadoGlobalLote.Calcular(cabecera.Estado, estados, cierrePanel: true), DisparadorEstado.CierrePanel);
        cabecera.Estado = transicion.Nuevo;
        cabecera.CerradoEn = ahora;
        db.Add(new HistorialEstadoDiagnostico
        {
            TenantId = cabecera.TenantId,
            DiagnosticoId = cabecera.Id,
            EstadoAnterior = transicion.Anterior,
            EstadoNuevo = transicion.Nuevo,
            Disparador = transicion.Disparador,
            UsuarioId = autor,
            En = ahora,
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ReglaException.Conflicto(CodigoLoteModificado, "El lote cambió mientras se cerraba el panel. Vuelve a cargar el tablero.");
        }

        return new CierrePanelDto(cabecera.Id, CodigosDeEnum.Codigo(cabecera.Estado), ahora);
    }
}
