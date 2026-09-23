using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12797 (Épica #12760) — sustitución del PDF consolidado en tres tiempos, compartida por
/// <see cref="GenerarConsolidadoHandler"/>, <see cref="GenerarConsolidadoMaestroHandler"/> y
/// <see cref="CargarConsolidadoExternoHandler"/>. Antes cada handler borraba el binario anterior
/// <b>antes</b> de subir el nuevo y guardar: si la subida o el <c>SaveChanges</c> fallaban, el trámite
/// se quedaba apuntando a un objeto ya borrado (o sin consolidado).
///
/// <para>Orden garantizado:</para>
/// <list type="number">
///   <item>El handler sube el nuevo binario (<c>IAttachmentStorage.SaveAsync</c>). Si falla, no se ha
///   tocado nada: la fila sigue apuntando al anterior y su binario existe.</item>
///   <item><see cref="RetirarFilas"/> quita las filas previas del grafo y el handler registra la nueva;
///   ambas cosas viajan en el MISMO <c>SaveChanges</c> (atómico).</item>
///   <item><see cref="ConfirmarAsync"/> guarda; solo si el guardado confirma se borran los binarios
///   anteriores. Si el guardado falla se compensa: se borra el binario NUEVO (huérfano) y se restaura
///   el grafo en memoria; la BD conserva la fila anterior y la bandera de vigencia sin subir.</item>
/// </list>
///
/// <para>Claves por versión: el adaptador real (<c>FileManagerAttachmentStorage</c>) crea un registro
/// nuevo en cada <c>SaveAsync</c> (POST /files ⇒ id nuevo), así que dos versiones nunca comparten
/// clave. Aun así, si un backend devolviera la misma ruta para el nuevo y el anterior, ese binario NO se
/// borra: sería borrar el recién confirmado.</para>
/// </summary>
public static partial class ConsolidadoReemplazoSeguro
{
    /// <summary>Consolidados previos del <paramref name="tipo"/> a sustituir. No los modifica.</summary>
    public static IReadOnlyList<ProcedureInstanceAttachment> Previos(ProcedureInstance instance, string tipo)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.Attachments
            .Where(a => string.Equals(a.Tipo, tipo, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Retira las FILAS de <paramref name="previos"/> (colección en memoria + repositorio). El binario
    /// en storage NO se toca: lo borra <see cref="ConfirmarAsync"/> tras confirmar el guardado.
    /// </summary>
    public static void RetirarFilas(
        ProcedureInstance instance,
        IProcedureInstanceRepository repo,
        IReadOnlyList<ProcedureInstanceAttachment> previos)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(previos);

        foreach (var prev in previos)
        {
            instance.Attachments.Remove(prev);
            repo.RemoveAttachment(prev);
        }
    }

    /// <summary>
    /// Confirma la sustitución: <c>SaveChanges</c> y, solo si confirma, borra los binarios previos.
    /// <list type="bullet">
    ///   <item>Fallo del guardado (cualquier excepción, incluida la cancelación): borra el binario
    ///   <paramref name="nuevo"/> para no dejar huérfano, re-agrega <paramref name="previos"/> y quita
    ///   <paramref name="nuevoAdjunto"/> de la colección en memoria, ejecuta
    ///   <paramref name="revertirEnMemoria"/> (p. ej. devolver la bandera de vigencia a su valor
    ///   anterior) y relanza. La BD no cambió: la transacción de <c>SaveChanges</c> es atómica.</item>
    ///   <item>Fallo al borrar un binario previo TRAS confirmar: NO revienta la petición. El trámite ya
    ///   apunta al nuevo y es consistente; el anterior queda como objeto huérfano recuperable (el
    ///   file-manager ni siquiera expone borrado y lo recoge su job de ciclo de vida). Se registra una
    ///   advertencia con la ruta para su limpieza.</item>
    /// </list>
    /// </summary>
    public static async Task ConfirmarAsync(
        ProcedureInstance instance,
        IProcedureInstanceRepository repo,
        IAttachmentStorage storage,
        StoredFile nuevo,
        ProcedureInstanceAttachment nuevoAdjunto,
        IReadOnlyList<ProcedureInstanceAttachment> previos,
        Action? revertirEnMemoria,
        ILogger? logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(nuevo);
        ArgumentNullException.ThrowIfNull(nuevoAdjunto);
        ArgumentNullException.ThrowIfNull(previos);

        try
        {
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Misma clave que un previo ⇒ ese binario es el que la fila anterior sigue referenciando.
            if (!previos.Any(p => string.Equals(p.StoragePath, nuevo.StoragePath, StringComparison.Ordinal)))
                BorrarSinFallar(storage, nuevo.StoragePath, logger, "compensacion_guardado_fallido");
            instance.Attachments.Remove(nuevoAdjunto);
            foreach (var prev in previos)
            {
                if (!instance.Attachments.Contains(prev))
                    instance.Attachments.Add(prev);
            }
            revertirEnMemoria?.Invoke();
            throw;
        }

        foreach (var prev in previos)
        {
            if (string.Equals(prev.StoragePath, nuevo.StoragePath, StringComparison.Ordinal))
                continue;
            BorrarSinFallar(storage, prev.StoragePath, logger, "retiro_consolidado_anterior");
        }
    }

    internal static void BorrarSinFallar(IAttachmentStorage storage, string storagePath, ILogger? logger, string motivo)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return;
        try
        {
            storage.Delete(storagePath);
        }
        catch (Exception ex)
        {
            // Sin PII: la ruta es el id opaco del file-manager.
            if (logger is not null)
                LogBorradoFallido(logger, ex, storagePath, motivo);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Consolidado: no se pudo borrar el binario {StoragePath} ({Motivo}); queda como objeto huérfano recuperable.")]
    private static partial void LogBorradoFallido(ILogger logger, Exception ex, string storagePath, string motivo);
}
