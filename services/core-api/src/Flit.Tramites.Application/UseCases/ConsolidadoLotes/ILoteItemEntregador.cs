namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13371 (Épica #13216) — entrega del PDF de UN trámite dentro de un lote de descarga masiva.
/// La implementación común es <see cref="ConsolidadoLoteEntregador"/> («existente o primera generación»);
/// las variantes por origen (p. ej. la bandeja del OT, #13308) la envuelven con su contexto y su gancho.
/// </summary>
/// <remarks>Uso de ejemplo: <c>var r = await entregador.EntregarAsync(new(id, tenantId, LoteTipoDocumento.Consolidado), ct);</c>.</remarks>
public interface ILoteItemEntregador
{
    Task<LoteItemEntregaResult> EntregarAsync(LoteItemEntregaRequest request, CancellationToken ct = default);
}

/// <summary>Tipos de documento que acepta el lote (columna <c>tipo_documento</c> del lote).</summary>
public static class LoteTipoDocumento
{
    /// <summary>Consolidado del wizard (adjunto tipo <c>consolidado</c>).</summary>
    public const string Consolidado = "consolidado";

    /// <summary>Consolidado maestro del OT (adjunto tipo <c>consolidado_maestro</c>).</summary>
    public const string ConsolidadoMaestro = "consolidado_maestro";

    /// <summary>¿Es uno de los dos tipos admitidos? (comparación exacta).</summary>
    public static bool EsValido(string? tipo) =>
        string.Equals(tipo, Consolidado, StringComparison.Ordinal)
        || string.Equals(tipo, ConsolidadoMaestro, StringComparison.Ordinal);
}

/// <summary>Valores de <c>items.delivery_mode</c> (DDL #13368).</summary>
public static class LoteDeliveryMode
{
    /// <summary>El adjunto ya estaba guardado y se tomó tal cual (cualquier estado o vigencia).</summary>
    public const string Existente = "existente";

    /// <summary>Primera generación hecha por el lote.</summary>
    public const string Generado = "generado";
}

/// <summary>Petición de entrega de un ítem.</summary>
/// <param name="ProcedureInstanceId">Trámite del ítem.</param>
/// <param name="TenantId">Compañía dueña del trámite (la congelada en el ítem). Todas las lecturas y la generación la reciben explícita.</param>
/// <param name="TipoDocumento"><see cref="LoteTipoDocumento.Consolidado"/> o <see cref="LoteTipoDocumento.ConsolidadoMaestro"/>.</param>
/// <param name="PrecedenciaMatriz">
/// Orden resuelto de la matriz del OT, solo para generar el maestro (A5.6 R-b). <c>null</c> en el lote del
/// Super Admin (A4.2, paridad con su entrega individual). Ignorado para el consolidado del wizard.
/// </param>
/// <param name="AntesDeGenerar">
/// Gancho que se invoca SOLO cuando hay que generar (nunca si se toma un existente), después de las guardas
/// de migrado y FUR. Devuelve <c>null</c> para seguir, o un código de
/// <see cref="Domain.Entities.ConsolidadoLotes.ConsolidadoLoteOmisiones"/> para omitir sin generar
/// (p. ej. <c>quipux_solo_lectura</c> en la bandeja del OT).
/// </param>
public sealed record LoteItemEntregaRequest(
    Guid ProcedureInstanceId,
    Guid TenantId,
    string TipoDocumento,
    IReadOnlyList<string>? PrecedenciaMatriz = null,
    Func<CancellationToken, Task<string?>>? AntesDeGenerar = null);

/// <summary>Desenlace de la entrega de un ítem.</summary>
public enum LoteItemEntregaEstado
{
    /// <summary>Hay PDF: <see cref="LoteItemEntregaResult.Adjunto"/> y <see cref="LoteItemEntregaResult.DeliveryMode"/> informados.</summary>
    Incluido,

    /// <summary>
    /// No se incluye por una condición de negocio; <see cref="LoteItemEntregaResult.Codigo"/> es un código de
    /// <see cref="Domain.Entities.ConsolidadoLotes.ConsolidadoLoteOmisiones"/>. No se reintenta.
    /// </summary>
    Omitido,

    /// <summary>
    /// Fallo técnico (almacenamiento, excepción, código no catalogado); <see cref="LoteItemEntregaResult.Codigo"/>
    /// lleva la causa cruda. El procesamiento del ítem (#13375) decide reintento u omisión <c>error_tecnico</c>.
    /// </summary>
    ErrorTecnico,
}

/// <summary>Snapshot del adjunto entregado (columnas del ítem incluido).</summary>
public sealed record LoteItemAdjunto(Guid AttachmentId, string StoragePath, long SizeBytes, string Sha256, string Filename);

/// <summary>Resultado de <see cref="ILoteItemEntregador.EntregarAsync"/>.</summary>
public sealed record LoteItemEntregaResult(
    LoteItemEntregaEstado Estado,
    LoteItemAdjunto? Adjunto = null,
    string? DeliveryMode = null,
    string? Codigo = null)
{
    public static LoteItemEntregaResult Incluido(LoteItemAdjunto adjunto, string deliveryMode) =>
        new(LoteItemEntregaEstado.Incluido, adjunto, deliveryMode);

    public static LoteItemEntregaResult Omitido(string codigo) =>
        new(LoteItemEntregaEstado.Omitido, Codigo: codigo);

    public static LoteItemEntregaResult Fallo(string causa) =>
        new(LoteItemEntregaEstado.ErrorTecnico, Codigo: causa);
}
