namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12787 (AC2, Épica #12760) — ¿qué consolidado maestro se radicó ante Quipux? El maestro radicado
/// es el documento que la secretaría tiene en su expediente: queda FIJO en el servidor (no se regenera,
/// no se retira su fila, no se borra su binario), porque <c>quipux_submissions.attachment_id</c> no
/// tiene FK y un reemplazo lo dejaría apuntando a un adjunto inexistente (404 en el OT).
///
/// <para>Puerto de Application: la implementación vive en Infrastructure (lee
/// <c>tramites.quipux_submissions</c>), así el módulo Quipux no se acopla a Trámites ni al revés.
/// Dos criterios (re-review #12760, N1/N4): «radicado fijo» = radicación VIGENTE (<c>registrado</c> o
/// <c>aprobado</c> con <c>RegisteredAt</c>); «protegido» = todo lo que la secretaría tiene o puede estar
/// recibiendo (cualquier estado salvo <c>fallido</c>, incluidos <c>pendiente</c> y <c>rechazado</c>).</para>
///
/// <para>Tenant: ambos métodos filtran por <paramref name="tenantId"/> explícito (dueño del trámite);
/// el aislamiento no descansa en el RLS.</para>
/// </summary>
/// <remarks>Uso de ejemplo: <c>var fijo = await lookup.AttachmentRadicadoAsync(tenantId, id, ct);</c>.</remarks>
public interface IMaestroRadicadoLookup
{
    /// <summary>
    /// Adjunto <c>consolidado_maestro</c> de la ÚLTIMA radicación VIGENTE (<c>registrado</c> o
    /// <c>aprobado</c>, por <c>RegisteredAt</c>), o <c>null</c> si no hay ninguna. Un rechazo de Quipux
    /// deja de fijar el maestro: el worker y el POST OT vuelven a regenerarlo.
    /// </summary>
    Task<Guid?> AttachmentRadicadoAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// TODOS los adjuntos referenciados por una submission no <c>fallido</c> del trámite: pendientes (aún
    /// sin <c>RegisteredAt</c>), registradas, aprobadas y rechazadas. Vacío si ninguna. Es el conjunto que
    /// el reemplazo seguro nunca retira ni borra y que el gestor no puede eliminar.
    /// </summary>
    Task<IReadOnlySet<Guid>> AttachmentsProtegidosAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default);
}

/// <summary>Implementación inerte: sin Quipux cableado (tests/composiciones antiguas) nada está radicado.</summary>
public sealed class NullMaestroRadicadoLookup : IMaestroRadicadoLookup
{
    public static readonly NullMaestroRadicadoLookup Instance = new();

    private static readonly IReadOnlySet<Guid> Vacio = new HashSet<Guid>();

    public Task<Guid?> AttachmentRadicadoAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default) =>
        Task.FromResult<Guid?>(null);

    public Task<IReadOnlySet<Guid>> AttachmentsProtegidosAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default) =>
        Task.FromResult(Vacio);
}

/// <summary>
/// HU #12787 (AC2) — decisión común de «maestro radicado, fijo» para la entrega
/// (<see cref="EntregarConsolidadoHandler"/>) y el POST OT de generación
/// (<see cref="GenerarConsolidadoMaestroHandler.HandleRespetandoRadicacionAsync"/>).
/// </summary>
public static class MaestroRadicadoFijo
{
    /// <summary>Motivo de omisión/registro cuando el maestro está radicado.</summary>
    public const string Motivo = "maestro_radicado";

    /// <summary>
    /// Con <paramref name="radicadoId"/> null: no aplica (<c>Aplica=false</c>). Con valor: el adjunto
    /// radicado tal cual (<see cref="ConsolidadoEntregaModos.RadicadoFijo"/>). Si ese adjunto ya no existe
    /// (datos rotos), el comportamiento de solo lectura: el maestro existente o
    /// <see cref="EntregarConsolidadoHandler.ConsolidadoNoGenerado"/>. Nunca regenera.
    /// </summary>
    public static (bool Aplica, GenerarConsolidadoResult? Result, string? Error) Resolver(
        Domain.Entities.ProcedureInstance instance, Guid? radicadoId, bool definitivo = false)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (radicadoId is not { } id)
            return (false, null, null);

        var radicado = instance.Attachments.FirstOrDefault(a => a.Id == id);
        if (radicado is not null)
            return (true, Servir(radicado, ConsolidadoEntregaModos.RadicadoFijo, definitivo), null);

        var existente = ConsolidadoEntregaModos.Existente(instance, RegenerarConsolidadoAnticipadoHandler.TipoAdjuntoMaestro);
        return existente is null
            ? (true, null, EntregarConsolidadoHandler.ConsolidadoNoGenerado)
            : (true, Servir(existente, ConsolidadoEntregaModos.SoloLectura, definitivo), null);
    }

    internal static GenerarConsolidadoResult Servir(
        Domain.Entities.ProcedureInstanceAttachment adjunto, string modo, bool definitivo) =>
        new(
            new ConsolidadoDocumentDto(adjunto.Id, adjunto.Tipo, adjunto.Filename, adjunto.Sha256),
            Regenerado: false,
            DefinitivoPorEstadoFinal: definitivo,
            Modo: modo);
}

/// <summary>
/// Re-review #12760 (N5) — listado de documentos del OT: tras conservar el maestro radicado puede haber
/// dos filas <c>consolidado_maestro</c> (la radicada y la regenerada). El listado muestra UNA por tipo de
/// consolidado, la misma que sirve la entrega: el adjunto radicado fijo si aplica y, si no, el más
/// reciente. El resto de adjuntos pasan sin cambios y en el mismo orden.
/// </summary>
/// <remarks>Uso de ejemplo: <c>var docs = ConsolidadoListado.UnoPorTipo(response.Attachments, radicadoId);</c>.</remarks>
public static class ConsolidadoListado
{
    private static readonly HashSet<string> TiposConsolidado = new(StringComparer.OrdinalIgnoreCase)
    {
        RegenerarConsolidadoAnticipadoHandler.TipoAdjuntoWizard,
        RegenerarConsolidadoAnticipadoHandler.TipoAdjuntoMaestro,
    };

    public static IReadOnlyList<AttachmentDto> UnoPorTipo(IReadOnlyList<AttachmentDto> docs, Guid? radicadoId)
    {
        ArgumentNullException.ThrowIfNull(docs);

        var elegidos = docs
            .Where(d => TiposConsolidado.Contains(d.Tipo))
            .GroupBy(d => d.Tipo, StringComparer.OrdinalIgnoreCase)
            .Select(g => Elegir(g.Key, g.ToList(), radicadoId).Id)
            .ToHashSet();

        return docs
            .Where(d => !TiposConsolidado.Contains(d.Tipo) || elegidos.Contains(d.Id))
            .ToList();
    }

    private static AttachmentDto Elegir(string tipo, List<AttachmentDto> filas, Guid? radicadoId)
    {
        if (radicadoId is { } id
            && string.Equals(tipo, RegenerarConsolidadoAnticipadoHandler.TipoAdjuntoMaestro, StringComparison.OrdinalIgnoreCase)
            && filas.FirstOrDefault(f => f.Id == id) is { } radicado)
        {
            return radicado;
        }

        return filas.OrderByDescending(f => f.UploadedAt).First();
    }
}
