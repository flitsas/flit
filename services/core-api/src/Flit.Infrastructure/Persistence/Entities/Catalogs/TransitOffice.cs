namespace Flit.Infrastructure.Persistence.Entities.Catalogs;

/// <summary>Organismo de tránsito — <c>catalogs.transit_offices</c> (HU #10152).</summary>
public sealed class TransitOffice
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DepartmentCode { get; set; } = string.Empty;

    public string CityCode { get; set; } = string.Empty;

    /// <summary>Nombre legible del municipio (<c>city_name</c>). Fuente catálogo FLIT 1.0 / DDL 34.</summary>
    public string? CityName { get; set; }

    /// <summary>Nombre legible del departamento (<c>department_name</c>). Fuente catálogo FLIT 1.0 / DDL 34.</summary>
    public string? DepartmentName { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Código DIVIPO de la secretaría, tal como lo espera Quipux (HU #10710). Se carga a mano,
    /// secretaría por secretaría, porque no se tiene el de las 317 del catálogo.
    /// </summary>
    /// <remarks>
    /// Es un dato propio del organismo y NO se deriva de <see cref="CityCode"/> ni de
    /// <see cref="Code"/>: en FLIT 1.0 vivía en <c>traffic_secretaries.code_divipo</c>, como
    /// columna aparte de <c>code_dane_municipality</c>. Se descartó además derivarlo de
    /// <see cref="Code"/> (el <c>traffic_agency_code</c> del RUNT) porque este pierde el cero a la
    /// izquierda: Medellín es <c>code = '5001000'</c> con <c>city_code = '05001'</c>.
    /// <para>Null mientras no se conozca. Sin él la secretaría NO es elegible para radicar: es el
    /// fallo seguro frente a mandar un trámite a la secretaría equivocada.</para>
    /// </remarks>
    public string? DivipoCode { get; set; }

    /// <summary>¿Se radican las MATRÍCULAS de esta secretaría por Quipux? (HU #10710)</summary>
    /// <remarks>
    /// Las tres banderas replican el modelo de FLIT 1.0, donde <c>traffic_secretaries</c> tenía
    /// <c>id_parinttrasec_registration</c>, <c>id_parinttrasec_transfer</c> e
    /// <c>id_parinttrasec_otherservice</c> por separado: una secretaría puede estar integrada para
    /// una familia de trámites y no para otra, y el alta es gradual.
    /// <para><b>No confundir con <c>admin.transit_office_profiles.operation_mode = 'quipux'</c></b>:
    /// aquello describe al OT-CLIENTE cuya consola queda en solo lectura porque aprueba dentro de
    /// Quipux. Esto describe a la secretaría DESTINO a la que FLIT radica, que normalmente ni
    /// siquiera es cliente de FLIT (de las 317 del catálogo solo 12 tienen perfil). Son dos
    /// conceptos distintos que comparten el nombre "Quipux"; usar el perfil como filtro de
    /// radicación dejaba fuera a 305 secretarías.</para>
    /// </remarks>
    public bool QuipuxRegistration { get; set; }

    /// <summary>¿Se radican los TRASPASOS de esta secretaría por Quipux? (HU #10710)</summary>
    public bool QuipuxTransfer { get; set; }

    /// <summary>¿Se radican los OTROS trámites de esta secretaría por Quipux? (HU #10710)</summary>
    public bool QuipuxOther { get; set; }
}
