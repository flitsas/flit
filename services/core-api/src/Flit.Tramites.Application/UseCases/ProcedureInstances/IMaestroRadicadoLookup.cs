namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12787 (AC2, Épica #12760) — ¿qué consolidado maestro se radicó ante Quipux? El maestro radicado
/// es el documento que la secretaría tiene en su expediente: queda FIJO en el servidor (no se regenera,
/// no se retira su fila, no se borra su binario), porque <c>quipux_submissions.attachment_id</c> no
/// tiene FK y un reemplazo lo dejaría apuntando a un adjunto inexistente (404 en el OT).
///
/// <para>Puerto de Application: la implementación vive en Infrastructure (lee
/// <c>tramites.quipux_submissions</c>), así el módulo Quipux no se acopla a Trámites ni al revés.
/// Criterio = el de HU #12791 (<c>OtClientProcedureRepository</c>): submission con
/// <c>RegisteredAt</c> con valor y estado distinto de <c>fallido</c>.</para>
///
/// <para>Tenant: ambos métodos filtran por <paramref name="tenantId"/> explícito (dueño del trámite);
/// el aislamiento no descansa en el RLS.</para>
/// </summary>
/// <remarks>Uso de ejemplo: <c>var fijo = await lookup.AttachmentRadicadoAsync(tenantId, id, ct);</c>.</remarks>
public interface IMaestroRadicadoLookup
{
    /// <summary>
    /// Adjunto <c>consolidado_maestro</c> de la ÚLTIMA radicación exitosa (por <c>RegisteredAt</c>), o
    /// <c>null</c> si el trámite no se ha radicado ante Quipux.
    /// </summary>
    Task<Guid?> AttachmentRadicadoAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// TODOS los adjuntos referenciados por alguna radicación exitosa del trámite (vacío si ninguna).
    /// Es el conjunto que el reemplazo seguro nunca retira ni borra (defensa en profundidad).
    /// </summary>
    Task<IReadOnlySet<Guid>> AttachmentsRadicadosAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default);
}

/// <summary>Implementación inerte: sin Quipux cableado (tests/composiciones antiguas) nada está radicado.</summary>
public sealed class NullMaestroRadicadoLookup : IMaestroRadicadoLookup
{
    public static readonly NullMaestroRadicadoLookup Instance = new();

    private static readonly IReadOnlySet<Guid> Vacio = new HashSet<Guid>();

    public Task<Guid?> AttachmentRadicadoAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default) =>
        Task.FromResult<Guid?>(null);

    public Task<IReadOnlySet<Guid>> AttachmentsRadicadosAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default) =>
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
