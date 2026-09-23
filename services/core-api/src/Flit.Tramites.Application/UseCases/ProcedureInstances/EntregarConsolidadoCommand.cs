using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Cuál de los dos PDF del expediente se pide (decisión D2 de la épica #12760: ambos).</summary>
public enum ConsolidadoEntregaTipo
{
    /// <summary>Consolidado del wizard (adjunto tipo <c>consolidado</c>).</summary>
    Wizard,

    /// <summary>Consolidado maestro del OT (adjunto tipo <c>consolidado_maestro</c>).</summary>
    Maestro,
}

/// <summary>Valores de <see cref="GenerarConsolidadoResult.Modo"/> que emite la ruta de entrega (HU #12785).</summary>
public static class ConsolidadoEntregaModos
{
    /// <summary>La bandera estaba arriba: se sirvió el PDF cacheado sin escribir en storage (AC2).</summary>
    public const string Vigente = "vigente";

    /// <summary>La bandera estaba abajo o no había PDF: se reconstruyó (AC1/AC6).</summary>
    public const string Regenerado = "regenerado";

    /// <summary>Trámite en estado final: se sirve el PDF existente sin regenerar (AC3).</summary>
    public const string DefinitivoEstadoFinal = "definitivo_estado_final";

    /// <summary>Trámite migrado de V1 en estado final: foto de solo lectura (AC5).</summary>
    public const string MigradoSoloLectura = "migrado_solo_lectura";

    /// <summary>PDF cargado a mano por el SuperAdmin (<c>Source="user"</c>): prevalece siempre (AC4).</summary>
    public const string CargadoPorUsuario = "cargado_por_usuario";

    /// <summary>
    /// El llamador pidió el adjunto tal cual, sin comprobar vigencia (p. ej. la vista read-only del OT
    /// tras la radicación ante Quipux, decisión PO 2026-09-23 / HU #12787 AC2).
    /// </summary>
    public const string SoloLectura = "solo_lectura";

    /// <summary>
    /// Adjunto del tipo pedido: el más reciente por <c>UploadedAt</c> (el reemplazo borra el previo,
    /// pero una doble carga no puede decidir qué se entrega).
    /// </summary>
    internal static ProcedureInstanceAttachment? Existente(ProcedureInstance instance, string tipoAdjunto) =>
        instance.Attachments
            .Where(a => string.Equals(a.Tipo, tipoAdjunto, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefault();
}

/// <summary>Petición a la ruta de entrega del consolidado (HU #12785).</summary>
/// <param name="Force">
/// Reconstruye saltando la caché de vigencia. Nunca aplica en estado final, sobre un PDF
/// <c>Source="user"</c> ni en modo <paramref name="SoloLectura"/>.
/// </param>
/// <param name="SoloLectura">
/// Devuelve el adjunto existente tal cual, sin mirar la bandera y sin escribir nada. Es la vía de la
/// vista read-only del OT: el maestro radicado ante Quipux se sirve sin regenerarlo.
/// </param>
/// <param name="MatrizPrecedencia">Orden resuelto de la matriz del OT (solo maestro; ver <see cref="GenerarConsolidadoMaestroHandler"/>).</param>
public sealed record EntregarConsolidadoRequest(
    Guid Id,
    Guid TenantId,
    ConsolidadoEntregaTipo Tipo,
    Guid? UserId = null,
    bool Force = false,
    bool SoloLectura = false,
    IReadOnlyList<string>? MatrizPrecedencia = null);

/// <summary>
/// HU #12785 — ruta ÚNICA de obtención del consolidado (wizard o maestro): reconstruye SOLO si el PDF
/// está desactualizado, para que ningún consumidor (gestor, OT, SuperAdmin) descargue un PDF que no
/// refleja el expediente. No duplica la generación: delega en <see cref="GenerarConsolidadoHandler"/> y
/// <see cref="GenerarConsolidadoMaestroHandler"/>, que ya respetan la bandera de vigencia; aquí solo se
/// añaden las excepciones que la entrega debe garantizar ANTES de delegar:
/// <list type="number">
///   <item>Estado final (aprobado/anulado/revocado): se sirve el PDF existente, marcado como
///   definitivo; nunca se regenera (AC3). Migrado V1 ⇒ modo <c>migrado_solo_lectura</c> (AC5).</item>
///   <item><c>Source="user"</c>: el PDF del SuperAdmin prevalece incluso con <c>force</c> (AC4).</item>
///   <item><c>SoloLectura</c>: el adjunto tal cual, sin comprobar vigencia.</item>
/// </list>
/// Errores: <c>not_found</c>, <c>consolidado_no_generado</c> (final/solo lectura sin PDF),
/// <c>migrado_solo_lectura</c> (migrado final sin PDF) y los que devuelvan los generadores.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await handler.HandleAsync(new(id, tenantId, ConsolidadoEntregaTipo.Maestro), ct)</c>.</remarks>
public sealed class EntregarConsolidadoHandler(
    IProcedureInstanceRepository repo,
    GenerarConsolidadoHandler wizardHandler,
    GenerarConsolidadoMaestroHandler maestroHandler)
{
    public const string ConsolidadoNoGenerado = "consolidado_no_generado";

    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        EntregarConsolidadoRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instance = await repo.GetByIdWithAttachmentsAsync(request.Id, request.TenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var tipoAdjunto = request.Tipo == ConsolidadoEntregaTipo.Maestro ? "consolidado_maestro" : "consolidado";
        var existente = ConsolidadoEntregaModos.Existente(instance, tipoAdjunto);
        var esFinal = TramiteEstado.EsFinal(instance.Status);

        // AC3/AC5 — la documentación de un trámite final es la que el organismo tuvo a la vista: se
        // entrega tal cual, con la bandera como esté. Va ANTES que todo lo demás (incluido `force`).
        if (esFinal)
        {
            var modo = instance.IsMigrated
                ? ConsolidadoEntregaModos.MigradoSoloLectura
                : ConsolidadoEntregaModos.DefinitivoEstadoFinal;
            if (existente is null)
                return (null, instance.IsMigrated ? ConsolidadoEntregaModos.MigradoSoloLectura : ConsolidadoNoGenerado);

            return (Servir(existente, modo, definitivo: true), null);
        }

        if (request.SoloLectura)
        {
            return existente is null
                ? (null, ConsolidadoNoGenerado)
                : (Servir(existente, ConsolidadoEntregaModos.SoloLectura, definitivo: false), null);
        }

        // AC4 — el PDF cargado a mano por el SuperAdmin no se pisa, ni con `force`. El generador del
        // wizard ya lo protege (HU #12158); el del maestro no conoce ese origen, por eso el corte vive aquí.
        if (existente is not null && string.Equals(existente.Source, "user", StringComparison.OrdinalIgnoreCase))
            return (Servir(existente, ConsolidadoEntregaModos.CargadoPorUsuario, definitivo: false), null);

        // AC1/AC2/AC6 — el generador decide con la bandera: arriba ⇒ caché sin tocar storage; abajo o
        // sin PDF ⇒ reconstruye y la sube.
        var (result, error) = request.Tipo == ConsolidadoEntregaTipo.Maestro
            ? await maestroHandler
                .HandleAsync(request.Id, request.TenantId, request.MatrizPrecedencia, request.Force, ct)
                .ConfigureAwait(false)
            : await wizardHandler
                .HandleAsync(request.Id, request.TenantId, request.UserId, request.Force, ct)
                .ConfigureAwait(false);

        if (error is not null || result is null)
            return (null, error ?? "consolidado_sin_resultado");

        return (result with
        {
            DefinitivoPorEstadoFinal = false,
            Modo = result.Regenerado ? ConsolidadoEntregaModos.Regenerado : ConsolidadoEntregaModos.Vigente,
        }, null);
    }

    private static GenerarConsolidadoResult Servir(ProcedureInstanceAttachment adjunto, string modo, bool definitivo) =>
        new(
            new ConsolidadoDocumentDto(adjunto.Id, adjunto.Tipo, adjunto.Filename, adjunto.Sha256),
            Regenerado: false,
            DefinitivoPorEstadoFinal: definitivo,
            Modo: modo);
}
