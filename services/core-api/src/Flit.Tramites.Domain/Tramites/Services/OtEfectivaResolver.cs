using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// OT efectiva del trámite: la columna <see cref="ProcedureInstance.TransitOfficeId"/> y, si está
/// vacía, el OT elegido en el FUR (field_value <c>transit_office_id</c>). Los borradores creados antes
/// de HU #11199 solo tienen la OT en field_values hasta que la radicación la promueve a la columna.
///
/// <para>Fuente ÚNICA para todos los consumidores de la política compañía+OT del certificado de prenda
/// (estado del wizard, PUT de la decisión de prenda y gate de preparación): si cada uno la resolviera a
/// su manera, el wizard ofrecería «Omitir prenda» que el guardado luego rechaza.</para>
/// </summary>
public static class OtEfectivaResolver
{
    /// <summary>Clave del field_value con el id del OT elegido en el FUR.</summary>
    public const string FieldKey = "transit_office_id";

    /// <summary>Columna <c>TransitOfficeId</c> ?? OT de los field_values cargados en la instancia.</summary>
    public static Guid? Resolve(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.TransitOfficeId ?? FromFieldValues(instance.FieldValues);
    }

    /// <summary>
    /// Id del OT del field_value <c>transit_office_id</c>, o <c>null</c> si no hay selección, no es un
    /// GUID válido o es <see cref="Guid.Empty"/>.
    /// </summary>
    public static Guid? FromFieldValues(IEnumerable<ProcedureInstanceFieldValue>? fieldValues)
    {
        var raw = fieldValues?.FirstOrDefault(f =>
            string.Equals(f.FieldKey, FieldKey, StringComparison.OrdinalIgnoreCase))?.ValueText;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
