namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Claves de <c>field_values</c> del organismo de tránsito.
///
/// <para><b>Las canónicas (<c>transit_office_*</c>) son el organismo AL QUE VA el trámite</b>: se
/// promueven a <c>ProcedureInstance.TransitOfficeId</c> y con eso gobiernan el grant de la compañía,
/// el motor de reglas OT, la bandeja del organismo y la aprobación. En casi todos los tipos ese
/// organismo coincide con el que reporta el RUNT, porque el trámite se radica donde el vehículo está
/// matriculado, y por eso el auto-bind las llena desde la consulta.</para>
///
/// <para><b>El radicado de cuenta rompe esa coincidencia</b>: el trámite consiste en llevar la cuenta
/// a OTRO organismo, así que el canónico es el DESTINO —que es quien aprueba— y el del RUNT pasa a
/// ser un dato descriptivo. Para eso existen las claves <c>transit_office_actual_*</c>: conservan el
/// organismo donde el vehículo está hoy, que es el que imprime el encabezado del FUR.</para>
///
/// <para>Su ausencia es el caso normal: los veinte tipos restantes no las escriben y todo lo que las
/// lee cae a las canónicas, así que nada cambia para ellos.</para>
/// </summary>
public static class TransitOfficeFieldKeys
{
    /// <summary>Organismo al que va el trámite (destino donde aplique). Gobierna grants y bandeja.</summary>
    public const string Id = "transit_office_id";
    public const string Code = "transit_office_code";
    public const string Name = "transit_office_name";
    public const string City = "transit_office_city";

    /// <summary>Organismo donde el vehículo está matriculado HOY, según el RUNT. Solo descriptivo.</summary>
    public const string ActualId = "transit_office_actual_id";
    public const string ActualCode = "transit_office_actual_code";
    public const string ActualName = "transit_office_actual_name";
    public const string ActualCity = "transit_office_actual_city";

    /// <summary>
    /// Organismo al que se llevará la cuenta, DECLARADO en un trámite que se radica en otro sitio.
    ///
    /// <para>Es el caso del traslado de cuenta: lo expide el organismo de ORIGEN —él valida el paz y
    /// salvo y él aprueba— y el destino solo se declara, para que el FUR diga a dónde va. Por eso no
    /// puede vivir en las claves canónicas: esas son las del organismo que aprueba, y aquí ese es el
    /// de origen.</para>
    ///
    /// <para>No confundir con el radicado de cuenta, que es el trámite ESPEJO: allí la radicación se
    /// presenta en el organismo nuevo, así que el destino ES el canónico y estas claves no se usan.</para>
    /// </summary>
    public const string DestinoId = "transit_office_destino_id";
    public const string DestinoCode = "transit_office_destino_code";
    public const string DestinoName = "transit_office_destino_name";
    public const string DestinoCity = "transit_office_destino_city";
}
