using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Resolución compartida del organismo del trámite, para no duplicarla entre los dos casos.</summary>
internal static class MandateSignerSelectionResolver
{
    /// <summary>
    /// NIT de la empresa que OTORGA el mandato en <b>traspaso</b>: el vendedor (su vehículo).
    /// En matrícula inicial no hay vendedor: no se usa la cédula del comprador como NIT de empresa
    /// representada, porque eso filtraba a los mandatarios de la gestora y dejaba firmando a uno
    /// “aplica a todas” distinto del default de Mandatos.
    /// </summary>
    public static string? ResolveNitMandante(ProcedureInstance instance)
    {
        var vendedor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, "vendedor", StringComparison.OrdinalIgnoreCase));

        var nit = vendedor?.DocumentNumber?.Trim();
        return string.IsNullOrEmpty(nit) ? null : nit;
    }

    /// <summary>
    /// Organismo del trámite. Durante el wizard vive en <c>field_values.transit_office_id</c>; la columna
    /// solo se fija al radicar (<c>TramiteLifecycleService</c>), así que mirar solo la columna dejaría la
    /// elección sin candidatos justo cuando hace falta.
    /// </summary>
    public static Guid? ResolveTransitOfficeId(ProcedureInstance instance)
    {
        if (instance.TransitOfficeId is { } columna && columna != Guid.Empty)
            return columna;

        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
