using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13371 (Épica #13216, ADR-0070 nota v2) — entrega por trámite del lote de descarga masiva:
/// <b>el lote nunca regenera</b>. Regla del usuario (2026-10-07):
/// <list type="number">
///   <item>Si el trámite tiene adjunto del tipo pedido se toma TAL CUAL, sin mirar estado, vigencia,
///   <c>Source</c> ni FUR (<see cref="LoteDeliveryMode.Existente"/>). En el maestro, primero el radicado
///   ante Quipux (misma precedencia que la entrega individual).</item>
///   <item>Solo si no existe se genera con el generador oficial, con <c>force=false</c>, <c>userId=null</c>
///   (sin impronta en cascada, Q3) y <c>soloSiNoExiste=true</c> (<see cref="LoteDeliveryMode.Generado"/>).</item>
///   <item>Antes de generar se omiten: migrado V1 en estado final (<c>migrado_solo_lectura</c>, Q10) y
///   estado final sin FUR (<c>fur_requerido</c>, Q12, también para el maestro). Luego el gancho
///   <see cref="LoteItemEntregaRequest.AntesDeGenerar"/>, si lo hay.</item>
/// </list>
/// <para><b>Qué NO usa, a propósito:</b> la entrega individual (reconstruye lo no vigente y en final no
/// genera), el POST de generación del gestor (su guard de estado final sigue intacto) ni la cola de
/// regeneración anticipada. Lo vigila <c>ConsolidadoLoteArchitectureTests</c>. Generar en estado final por el
/// handler es lo mismo que ya hacen la aprobación del OT y los flujos de sistema.</para>
/// <para>No escribe nada en el trámite cuando toma un existente. No registra logs con datos del trámite.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await entregador.EntregarAsync(
///     new LoteItemEntregaRequest(id, tenantId, LoteTipoDocumento.ConsolidadoMaestro, matriz, ct => guard(ct)), ct);
/// // r.Estado == Incluido ⇒ r.Adjunto (snapshot) y r.DeliveryMode ("existente" | "generado")
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteEntregador(
    IProcedureInstanceRepository repo,
    GenerarConsolidadoHandler wizardHandler,
    GenerarConsolidadoMaestroHandler maestroHandler,
    ConsolidadoFalloBitacora? bitacora = null,
    IMaestroRadicadoLookup? maestroRadicado = null) : ILoteItemEntregador
{
    private readonly ConsolidadoFalloBitacora _bitacora = bitacora ?? new ConsolidadoFalloBitacora();

    private readonly IMaestroRadicadoLookup _maestroRadicado = maestroRadicado ?? NullMaestroRadicadoLookup.Instance;

    /// <summary>Errores del generador que son condiciones de negocio: se omiten con el mismo código.</summary>
    private static readonly HashSet<string> ErroresDePrecondicion = new(StringComparer.Ordinal)
    {
        ConsolidadoLoteOmisiones.FurRequerido,
        ConsolidadoLoteOmisiones.MigradoSoloLectura,
        ConsolidadoLoteOmisiones.SinAdjuntos,
        ConsolidadoLoteOmisiones.AdjuntoNoDisponible,
        ConsolidadoLoteOmisiones.MimetypeNoSoportado,
        ConsolidadoLoteOmisiones.OrganismoRequerido,
        ConsolidadoLoteOmisiones.ModalidadNoSoportada,
    };

    /// <summary>Causa cuando el generador o el gancho lanzaron una excepción.</summary>
    public const string CausaExcepcion = ConsolidadoFalloBitacora.CausaExcepcion;

    /// <summary>Causa cuando el generador no devolvió ni error ni documento localizable.</summary>
    public const string CausaSinResultado = "consolidado_sin_resultado";

    public async Task<LoteItemEntregaResult> EntregarAsync(LoteItemEntregaRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!LoteTipoDocumento.EsValido(request.TipoDocumento))
            throw new ArgumentException($"Tipo de documento no admitido por el lote: '{request.TipoDocumento}'.", nameof(request));

        var esMaestro = string.Equals(request.TipoDocumento, LoteTipoDocumento.ConsolidadoMaestro, StringComparison.Ordinal);
        var id = request.ProcedureInstanceId;
        var tenantId = request.TenantId;

        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).ConfigureAwait(false);
        if (instance is null || instance.DeletedAt is not null)
            return LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.AccesoRevocado);

        // 1. Existente tal cual (sin mirar estado, bandera, Source ni FUR). Maestro: el radicado primero.
        var existente = await ElegirExistenteAsync(instance, esMaestro, tenantId, ct).ConfigureAwait(false);
        if (existente is not null)
            return LoteItemEntregaResult.Incluido(Snapshot(existente), LoteDeliveryMode.Existente);

        // 2. Guardas previas a la primera generación.
        if (TramiteEstado.EsFinal(instance.Status))
        {
            if (instance.IsMigrated)
                return LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.MigradoSoloLectura);
            if (!GenerarConsolidadoHandler.TieneFur(instance))
                return LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.FurRequerido);
        }

        // 3. Gancho del origen (p. ej. el guard de Quipux de la bandeja OT): solo cuando hay que generar.
        if (request.AntesDeGenerar is { } antesDeGenerar)
        {
            string? motivo;
            try
            {
                motivo = await antesDeGenerar(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return LoteItemEntregaResult.Fallo(CausaExcepcion);
            }

            if (motivo is not null)
            {
                if (!ConsolidadoLoteOmisiones.EsValido(motivo))
                    throw new InvalidOperationException($"El gancho antesDeGenerar devolvió un motivo fuera del vocabulario: '{motivo}'.");
                return LoteItemEntregaResult.Omitido(motivo);
            }
        }

        // 4. Primera generación por el generador oficial.
        return await GenerarAsync(request, esMaestro, ct).ConfigureAwait(false);
    }

    private async Task<ProcedureInstanceAttachment?> ElegirExistenteAsync(
        ProcedureInstance instance, bool esMaestro, Guid tenantId, CancellationToken ct)
    {
        if (!esMaestro)
            return ConsolidadoEntregaModos.Existente(instance, LoteTipoDocumento.Consolidado);

        var radicadoId = await _maestroRadicado.AttachmentRadicadoAsync(tenantId, instance.Id, ct).ConfigureAwait(false);
        var radicado = radicadoId is { } rid ? instance.Attachments.FirstOrDefault(a => a.Id == rid) : null;
        return radicado ?? ConsolidadoEntregaModos.Existente(instance, LoteTipoDocumento.ConsolidadoMaestro);
    }

    private async Task<LoteItemEntregaResult> GenerarAsync(LoteItemEntregaRequest request, bool esMaestro, CancellationToken ct)
    {
        var id = request.ProcedureInstanceId;
        var tenantId = request.TenantId;
        var tipo = request.TipoDocumento;

        ConsolidadoConRespaldo salida;
        try
        {
            salida = await _bitacora
                .GenerarConRespaldoAsync(
                    tenantId, id, tipo, ConsolidadoFalloBitacora.Origenes.LoteDescargaMasiva,
                    anterior: null,
                    c => esMaestro
                        ? maestroHandler.HandleRespetandoRadicacionAsync(
                            id, tenantId, request.PrecedenciaMatriz, force: false, soloSiNoExiste: true, c)
                        : wizardHandler.HandleAsync(
                            id, tenantId, userId: null, force: false, bypassSourceUserProtection: false, soloSiNoExiste: true, c),
                    // Carrera con otra generación que guardó primero: se toma la que quedó (AC6).
                    (ex, c) => ConsolidadoVigenteTrasConflicto.ResolverAsync(repo, ex, id, tenantId, tipo, c),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return LoteItemEntregaResult.Fallo(CausaExcepcion);
        }

        if (salida.Error is { } error)
            return Clasificar(error);
        if (salida.Result is not { } result)
            return LoteItemEntregaResult.Fallo(CausaSinResultado);

        // El DTO del generador no trae ruta ni tamaño: se relee el adjunto para el snapshot del ítem.
        var fresca = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).ConfigureAwait(false);
        var adjunto = fresca?.Attachments.FirstOrDefault(a => a.Id == result.Document.AttachmentId);
        if (adjunto is null)
            return LoteItemEntregaResult.Fallo(CausaSinResultado);

        return LoteItemEntregaResult.Incluido(
            Snapshot(adjunto),
            result.Regenerado ? LoteDeliveryMode.Generado : LoteDeliveryMode.Existente);
    }

    /// <summary>Código de error del generador → omisión de negocio o fallo técnico.</summary>
    public static LoteItemEntregaResult Clasificar(string error)
    {
        if (ErroresDePrecondicion.Contains(error))
            return LoteItemEntregaResult.Omitido(error);

        return error switch
        {
            // El trámite desapareció de la compañía entre la lectura y la generación.
            "not_found" => LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.AccesoRevocado),
            // Maestro radicado cuyo adjunto ya no existe y sin otro maestro: el documento no está disponible.
            // (Literal y no la constante de la entrega individual: el lote no la referencia.)
            "consolidado_no_generado" => LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.AdjuntoNoDisponible),
            _ => LoteItemEntregaResult.Fallo(error),
        };
    }

    private static LoteItemAdjunto Snapshot(ProcedureInstanceAttachment a) =>
        new(a.Id, a.StoragePath, a.SizeBytes, a.Sha256, a.Filename);
}
