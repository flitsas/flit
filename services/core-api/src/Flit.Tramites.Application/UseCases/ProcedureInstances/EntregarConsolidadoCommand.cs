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
    public const string DefinitivoEstadoFinal = Flit.Queries.Domain.Documentos.ConsolidadoVigencia.ModoDefinitivoEstadoFinal;

    /// <summary>Trámite migrado de V1 en estado final: foto de solo lectura (AC5).</summary>
    public const string MigradoSoloLectura = Flit.Queries.Domain.Documentos.ConsolidadoVigencia.ModoMigradoSoloLectura;

    /// <summary>PDF cargado a mano por el SuperAdmin (<c>Source="user"</c>): prevalece siempre (AC4).</summary>
    public const string CargadoPorUsuario = Flit.Queries.Domain.Documentos.ConsolidadoVigencia.ModoCargadoPorUsuario;

    /// <summary>
    /// El llamador pidió el adjunto tal cual, sin comprobar vigencia (p. ej. la vista read-only del OT
    /// tras la radicación ante Quipux, decisión PO 2026-09-23 / HU #12787 AC2).
    /// </summary>
    public const string SoloLectura = "solo_lectura";

    /// <summary>
    /// HU #12787 (AC2) — el maestro ya se radicó ante Quipux: se sirve EL adjunto radicado tal cual y no
    /// se regenera nunca, ni con <c>force</c> ni con <c>soloLectura=false</c>. Valor aditivo del enum
    /// <c>ConsolidadoEntregaResponse.modo</c>.
    /// </summary>
    public const string RadicadoFijo = "radicado_fijo";

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
/// <para>HU #12798 (AC4) — si la reconstrucción falla y hay un PDF anterior, se entrega ESE PDF con el
/// aviso en <see cref="GenerarConsolidadoResult.AvisosCascada"/> (<c>"consolidado: causa"</c>),
/// <c>Regenerado=false</c> y <c>Modo=null</c> (el enum de <c>modo</c> del contrato no tiene un valor para
/// este caso y no se amplía aquí); el fallo queda en la bitácora del trámite
/// (<see cref="ConsolidadoFalloBitacora"/>). Sin anterior, el error viaja como antes.</para>
/// </summary>
/// <remarks>Uso de ejemplo: <c>await handler.HandleAsync(new(id, tenantId, ConsolidadoEntregaTipo.Maestro), ct)</c>.</remarks>
/// <remarks>
/// <para><b>Solo rutas de gestor/SuperAdmin y OT.</b> Este handler REGENERA: no debe cablearse en rutas
/// <c>/network</c> (cabeza de red leyendo trámites de hijas), que son de lectura. Lo vigila
/// <c>ConsolidadoEntregaArchitectureTests</c> (Flit.Admin.Tests).</para>
/// <para>HU #12787 (AC2) — con <c>Tipo=Maestro</c> y el trámite radicado ante Quipux
/// (<see cref="IMaestroRadicadoLookup"/>) se sirve el adjunto radicado (<c>modo=radicado_fijo</c>) y no
/// se regenera nunca. HU #12797 — ante un conflicto de concurrencia con la regeneración anticipada se
/// relee el trámite y se sirve el adjunto VIGENTE, no el capturado (que el otro camino ya borró).</para>
/// </remarks>
public sealed class EntregarConsolidadoHandler(
    IProcedureInstanceRepository repo,
    GenerarConsolidadoHandler wizardHandler,
    GenerarConsolidadoMaestroHandler maestroHandler,
    ConsolidadoFalloBitacora? bitacora = null,
    IMaestroRadicadoLookup? maestroRadicado = null)
{
    public const string ConsolidadoNoGenerado = "consolidado_no_generado";

    private readonly ConsolidadoFalloBitacora _bitacora = bitacora ?? new ConsolidadoFalloBitacora();

    private readonly IMaestroRadicadoLookup _maestroRadicado = maestroRadicado ?? NullMaestroRadicadoLookup.Instance;

    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        EntregarConsolidadoRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instance = await repo.GetByIdWithAttachmentsAsync(request.Id, request.TenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var esMaestro = request.Tipo == ConsolidadoEntregaTipo.Maestro;
        var tipoAdjunto = esMaestro ? "consolidado_maestro" : "consolidado";
        var existente = ConsolidadoEntregaModos.Existente(instance, tipoAdjunto);
        var esFinal = TramiteEstado.EsFinal(instance.Status);

        // HU #12787 (AC2) — el maestro radicado ante Quipux es el documento de la secretaría.
        var radicadoId = esMaestro
            ? await _maestroRadicado.AttachmentRadicadoAsync(request.TenantId, request.Id, ct).ConfigureAwait(false)
            : null;

        // AC3/AC5 — la documentación de un trámite final es la que el organismo tuvo a la vista: se
        // entrega tal cual, con la bandera como esté. Va ANTES que todo lo demás (incluido `force`).
        // Si hubo radicación, lo que el organismo tuvo a la vista es el maestro radicado.
        if (esFinal)
        {
            var modo = instance.IsMigrated
                ? ConsolidadoEntregaModos.MigradoSoloLectura
                : ConsolidadoEntregaModos.DefinitivoEstadoFinal;
            var definitivo = (radicadoId is { } rid ? instance.Attachments.FirstOrDefault(a => a.Id == rid) : null)
                ?? existente;
            if (definitivo is null)
                return (null, instance.IsMigrated ? ConsolidadoEntregaModos.MigradoSoloLectura : ConsolidadoNoGenerado);

            return (Servir(definitivo, modo, definitivo: true), null);
        }

        // HU #12787 (AC2) — radicado ⇒ fijo: ni `force` ni `soloLectura=false` lo regeneran. Si el adjunto
        // radicado ya no existe (datos rotos), comportamiento de solo lectura; nunca se regenera.
        var fijo = MaestroRadicadoFijo.Resolver(instance, radicadoId);
        if (fijo.Aplica)
            return fijo.Error is null ? (fijo.Result, null) : (null, fijo.Error);

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
        // HU #12798 — el fallo se registra en la bitácora y, con PDF anterior, se entrega ese (AC4).
        var salida = await _bitacora
            .GenerarConRespaldoAsync(
                request.TenantId, request.Id, tipoAdjunto, ConsolidadoFalloBitacora.Origenes.EntregaConsolidado,
                existente,
                c => request.Tipo == ConsolidadoEntregaTipo.Maestro
                    ? maestroHandler.HandleAsync(request.Id, request.TenantId, request.MatrizPrecedencia, request.Force, c)
                    : wizardHandler.HandleAsync(request.Id, request.TenantId, request.UserId, request.Force, c),
                // HU #12797 (F4) — carrera con la regeneración anticipada: el otro camino ya sustituyó (y
                // borró) el PDF capturado arriba. Se relee y se sirve el vigente real.
                (ex, c) => ConsolidadoVigenteTrasConflicto.ResolverAsync(repo, ex, request.Id, request.TenantId, tipoAdjunto, c),
                ct)
            .ConfigureAwait(false);
        var (result, error) = (salida.Result, salida.Error);

        if (error is not null || result is null)
            return (null, error ?? "consolidado_sin_resultado");

        if (salida.SirvioAnterior)
            return (result with { DefinitivoPorEstadoFinal = false, Modo = null }, null);

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
