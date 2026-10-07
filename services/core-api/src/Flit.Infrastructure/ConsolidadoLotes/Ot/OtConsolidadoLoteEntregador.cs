using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.OtClientProcedures;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Infrastructure.ConsolidadoLotes.Ot;

/// <summary>
/// HU #13392 (Épica #13216, Feature #13308, diseño 10 §2.3 D-FB3) — procesamiento de UN ítem del lote de maestros de la
/// bandeja del OT (origen <c>ot_bandeja</c>). No tiene carril propio: lo llama el <see cref="ProcesarItemLoteHandler"/>
/// del motor, en el scope de DI del ítem, con el paralelismo de <c>item_slots</c> (ADR-0070 D6).
/// <list type="number">
///   <item><b>Solicitante</b> (<see cref="TieneAccesoAsync"/>): el <see cref="IConsolidadoLoteAccessChecker"/> común
///   con su rama <c>ot_bandeja</c> (rol con el permiso y membresía en el tenant OT, o Super Admin activo; sin
///   suspensión vigente).</item>
///   <item><b>Trámite en la bandeja</b>: <see cref="IOtClientProcedureConsolidadoContext.ResolverAccesoAsync"/> con el
///   tenant OT del lote y su organismo congelado. <c>null</c> (otro organismo, borrado lógico, fuera de los estados de
///   la bandeja) o una compañía distinta de la del ítem ⇒ <c>acceso_revocado</c>. El grant no cuenta (D-FB1): la
///   bandeja de la HU #12350 no filtra por grant.</item>
///   <item><b>Entrega</b>: dentro de <see cref="IOtClientProcedureConsolidadoContext.EjecutarEnContextoClienteAsync{T}(Flit.Admin.Domain.OtClientProcedures.OtClientProcedure, Func{IReadOnlyList{string}, Task{T}}, CancellationToken)"/>
///   (una transacción con el tenant de la compañía cliente y la precedencia de la matriz del OT) se llama al entregador
///   común (#13371): el maestro guardado tal cual o, si no hay, la primera generación. El guard de Quipux con el tenant
///   OT es el gancho previo a generar: nunca corre si se toma un existente.</item>
///   <item><b>Fallo técnico</b>: si el entregador devuelve un error técnico (p. ej. almacenamiento no disponible) la
///   transacción se REVIERTE (no se confirma nada a medias) y la compensación diferida del generador borra el binario
///   nuevo; el handler deja el ítem en <c>pendiente</c> con reintento.</item>
/// </list>
/// <para>Nunca llama a la ruta de generación individual, a la entrega individual ni pide regenerar: el lote no regenera
/// (regla del usuario 2026-10-07). Sin logs propios: los del handler llevan solo ids y códigos.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo (lo resuelve <see cref="LoteItemOrigenPorOrigen"/>):
/// <code>
/// var origen = porOrigen.Para("ot_bandeja");               // este procesador
/// if (await origen.TieneAccesoAsync(ctx, ct)) { var r = await origen.EntregarAsync(ctx, ct); }
/// </code>
/// </remarks>
public sealed class OtConsolidadoLoteEntregador(
    IConsolidadoLoteAccessChecker acceso,
    IOtClientProcedureConsolidadoContext bandeja,
    ILoteItemEntregador entregador,
    IQuipuxReadOnlyGuard quipux) : ILoteItemOrigen
{
    /// <summary>Acción que se valida contra el modo Quipux de solo lectura, la misma de la generación individual.</summary>
    public const string AccionQuipux = "generar_consolidado_maestro";

    public string Origen => ConsolidadoExportOrigin.OtBandeja;

    public Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default) =>
        acceso.TieneAccesoAsync(contexto, ct);

    public async Task<LoteItemEntregaResult> EntregarAsync(LoteItemContexto contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (!string.Equals(contexto.Origen, Origen, StringComparison.Ordinal))
            throw new ArgumentException($"El ítem es del origen '{contexto.Origen}', no de '{Origen}'.", nameof(contexto));
        if (!string.Equals(contexto.TipoDocumento, LoteTipoDocumento.ConsolidadoMaestro, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"El lote de la bandeja OT solo entrega consolidados maestros; llegó '{contexto.TipoDocumento}'.");

        // El CHECK del lote OT exige tenant y organismo; sin ellos no hay bandeja contra la cual revalidar.
        if (contexto.CompaniaLoteId is not { } tenantOt || contexto.OrganismoId is not { } organismo)
            return LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.AccesoRevocado);

        // 1–2. ¿Sigue en la bandeja del organismo congelado, y de la compañía congelada en el ítem?
        var access = await bandeja
            .ResolverAccesoAsync(tenantOt, contexto.ProcedureInstanceId, organismo, ct)
            .ConfigureAwait(false);
        if (access is null || access.ClientTenantId != contexto.CompaniaTramiteId)
            return LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.AccesoRevocado);

        // 3. Existente o primera generación, en la transacción del cliente con la matriz del OT.
        try
        {
            return await bandeja.EjecutarEnContextoClienteAsync(
                access,
                async precedencia =>
                {
                    var resultado = await entregador.EntregarAsync(
                        new LoteItemEntregaRequest(
                            contexto.ProcedureInstanceId,
                            access.ClientTenantId,
                            LoteTipoDocumento.ConsolidadoMaestro,
                            precedencia,
                            c => GuardQuipuxAsync(tenantOt, c)),
                        ct).ConfigureAwait(false);

                    // 4. Un fallo técnico no se confirma a medias: se lanza para que la transacción se revierta y la
                    // compensación diferida borre el binario nuevo.
                    if (resultado.Estado == LoteItemEntregaEstado.ErrorTecnico)
                        throw new RevertirTransaccionException(resultado);
                    return resultado;
                },
                ct).ConfigureAwait(false);
        }
        catch (RevertirTransaccionException revertida)
        {
            return revertida.Resultado;
        }
    }

    /// <summary>
    /// Mismo guard y misma acción que la generación individual del maestro, con el tenant OT del lote (D-FB6). Hoy no
    /// restringe esa acción (Q11, intencional); se invoca igual para que el lote nunca sea más permisivo.
    /// </summary>
    private async Task<string?> GuardQuipuxAsync(Guid tenantOt, CancellationToken ct)
    {
        var resultado = await quipux.ValidateActionAsync(tenantOt, AccionQuipux, ct).ConfigureAwait(false);
        return resultado.IsAllowed ? null : ConsolidadoLoteOmisiones.QuipuxSoloLectura;
    }

    /// <summary>Sale de la acción del scope para revertir la transacción del cliente; lleva el resultado del ítem.</summary>
    private sealed class RevertirTransaccionException(LoteItemEntregaResult resultado)
        : Exception("Fallo técnico del ítem: se revierte la transacción del cliente.")
    {
        public LoteItemEntregaResult Resultado { get; } = resultado;
    }
}
