using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #14494 (ADR-0078) — comparación temporal del consolidado frente al último cambio relevante del
/// expediente en estado final.
/// </summary>
public static class ConsolidadoCoherenciaTemporal
{
    /// <summary>
    /// <c>T_pdf</c>: sello de generación del tipo pedido; si falta, <c>UploadedAt</c> del adjunto.
    /// </summary>
    public static DateTimeOffset? ResolverTpdf(
        ProcedureInstance instance,
        ConsolidadoEntregaTipo tipo,
        ProcedureInstanceAttachment? adjunto)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var generadoEn = tipo == ConsolidadoEntregaTipo.Maestro
            ? instance.ConsolidadoMaestroGeneradoEn
            : instance.ConsolidadoWizardGeneradoEn;
        return generadoEn ?? adjunto?.UploadedAt;
    }

    /// <summary>
    /// <c>T_cambio_relevante</c> = max(último <c>cambio_estado</c>, <see cref="ProcedureInstance.ExpedienteActualizadoEn"/>).
    /// </summary>
    public static DateTimeOffset? ResolverTCambioRelevante(
        DateTimeOffset? ultimoCambioEstado,
        DateTimeOffset? expedienteActualizadoEn)
    {
        if (ultimoCambioEstado is null)
            return expedienteActualizadoEn;
        if (expedienteActualizadoEn is null)
            return ultimoCambioEstado;
        return ultimoCambioEstado > expedienteActualizadoEn ? ultimoCambioEstado : expedienteActualizadoEn;
    }

    public static bool BanderaVigente(ProcedureInstance instance, ConsolidadoEntregaTipo tipo) =>
        tipo == ConsolidadoEntregaTipo.Maestro ? instance.ConsolidadoMaestroVigente : instance.ConsolidadoWizardVigente;

    /// <summary>
    /// Obsoleto si la bandera está abajo o <c>T_pdf</c> es anterior a <c>T_cambio_relevante</c>.
    /// Sin historial de cambios, solo cuenta la bandera (fallback ADR-0078).
    /// </summary>
    public static bool EsObsoleto(DateTimeOffset? tPdf, DateTimeOffset? tCambioRelevante, bool banderaVigente)
    {
        if (!banderaVigente)
            return true;
        if (tCambioRelevante is null)
            return false;
        if (tPdf is null)
            return true;
        return tPdf < tCambioRelevante;
    }
}
