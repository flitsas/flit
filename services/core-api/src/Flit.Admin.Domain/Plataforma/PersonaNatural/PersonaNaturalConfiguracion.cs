namespace Flit.Admin.Domain.Plataforma.PersonaNatural;

/// <summary>
/// Parámetros generales del portal (HU #14072). Cada valor nulo = rige el predeterminado del BFF. NO hay datos bancarios ni
/// de pago: viven en otra épica. <see cref="RowVersion"/> es el control de concurrencia (AC4).
/// </summary>
public sealed record ParametrosPn(
    int? SesionVigenciaMinutos,
    int? BorradorVigenciaDias,
    int? TopeConsultasCiudadanoDia,
    int? TopeConsultasGlobalDia,
    bool? CaptchaActivo,
    string? SoporteCorreo,
    string? SoporteTelefono,
    string? SoporteHorario,
    long RowVersion,
    DateTimeOffset? ActualizadoEn,
    Guid? ActualizadoPor);

/// <summary>Resultado de guardar los parámetros: guardados, o la versión que el otro editor dejó (conflicto).</summary>
public sealed record GuardarParametrosPnResultado(ParametrosPn? Parametros, bool Conflicto);

/// <summary>
/// Un organismo con su estado para el portal. <c>TiposConTarifaVigente</c> cuenta los tipos habilitados y elegibles que ya
/// tienen derechos de tránsito y servicio FLIT vigentes ahí: un organismo habilitado con cero se avisa «Sin tarifa vigente».
/// </summary>
public sealed record OrganismoPnView(
    Guid Id, string Codigo, string Nombre, string Municipio, string Departamento, bool Habilitado, int TiposConTarifaVigente,
    bool ActivoEnFlit = true);

/// <summary>
/// Un tipo candidato para el portal. <c>Elegible</c> sale de la regla K2 (perfil del tipo, wizard encendido y publicado);
/// <c>Habilitado</c> es la decisión del superadministrador; <c>Ofrecible</c> = ambos. Un tipo habilitado que dejó de ser
/// elegible sigue en la lista (con su motivo) para poder apagarlo, pero el portal ya no lo ofrece.
/// </summary>
public sealed record TipoPnView(
    Guid Id, string Codigo, string Nombre, string Familia, bool Elegible, string Motivo, bool Habilitado, bool Ofrecible,
    int OrganismosConTarifaVigente,
    bool ActivoEnFlit = true);

/// <summary>Cambio de la lista blanca o de un grant: el estado anterior (para la auditoría) y si la entidad existe.</summary>
public sealed record CambioHabilitacionPn(bool Existe, bool EstadoAnterior);

/// <summary>
/// Persistencia de la configuración del portal (HU #14072): lista blanca de tipos
/// (<c>admin.persona_natural_enabled_types</c>), parámetros (<c>admin.persona_natural_settings</c>) y habilitación de organismos
/// con el grant que ya existe (<c>admin.tenant_transit_office_grants</c> de la empresa Persona Natural).
/// </summary>
public interface IPersonaNaturalConfigRepository
{
    Task<ParametrosPn> ObtenerParametrosAsync(CancellationToken ct = default);

    /// <summary>Guarda con control de concurrencia: si <paramref name="rowVersionEsperada"/> ya no es la vigente, no cambia nada.</summary>
    Task<GuardarParametrosPnResultado> GuardarParametrosAsync(ParametrosPn nuevos, long rowVersionEsperada, Guid actor, CancellationToken ct = default);

    Task<IReadOnlyList<OrganismoPnView>> ListarOrganismosAsync(bool soloHabilitados, CancellationToken ct = default);

    /// <summary>Habilita o deshabilita el organismo para la empresa Persona Natural. <c>Existe = false</c> si el organismo no existe o está inactivo.</summary>
    Task<CambioHabilitacionPn> EstablecerOrganismoAsync(Guid organismoId, bool habilitado, Guid actor, CancellationToken ct = default);

    /// <summary>Un organismo es operable si está «activo por FLIT» (mismo criterio de <c>OtOperabilityGate</c>); solo esos se pueden habilitar.</summary>
    Task<bool> OrganismoEsOperableAsync(Guid organismoId, CancellationToken ct = default);

    /// <summary>El organismo existe y está activo en el catálogo.</summary>
    Task<bool> ExisteOrganismoAsync(Guid organismoId, CancellationToken ct = default);

    Task<IReadOnlyList<TipoPnView>> ListarTiposAsync(CancellationToken ct = default);

    /// <summary>Un tipo solo se puede HABILITAR si es candidato (elegible); apagarlo siempre se puede. <c>Existe = false</c> si el tipo no existe.</summary>
    Task<CambioHabilitacionPn> EstablecerTipoAsync(Guid tipoId, bool habilitado, Guid actor, CancellationToken ct = default);

    /// <summary>Un tipo es candidato si está publicado, activo y es elegible para persona natural (K2).</summary>
    Task<bool> TipoEsCandidatoAsync(Guid tipoId, CancellationToken ct = default);
}
