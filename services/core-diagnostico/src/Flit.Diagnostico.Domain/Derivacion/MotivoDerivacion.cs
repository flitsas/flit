namespace Flit.Diagnostico.Domain.Derivacion;

/// <summary>
/// Códigos estables de por qué un vehículo no se deriva o por qué su ítem quedó OMITIDO / FALLIDO (Punto 4, diseño §3 y §5.2,
/// ADR-0084). Van en <c>derivaciones.motivo_codigo</c> (varchar 40), en <c>motivo_no_elegible</c> de la grilla y en
/// <c>motivo_codigo</c> del 422. Los cuatro de elegibilidad los decide Diagnóstico; los nueve de <see cref="RechazosTramites"/> los
/// responde core-api (<c>RechazoBorrador.codigo</c>) y ninguno es reintentable. <see cref="Mensaje"/> da el texto claro, sin datos
/// personales; la grilla pinta los rótulos del Punto 5.
/// </summary>
public static class MotivoDerivacion
{
    /// <summary>Alguna consulta del vehículo no terminó (R-7).</summary>
    public const string NoTerminado = "NO_TERMINADO";

    /// <summary>Lote Con análisis sin dictamen vigente VIABLE (BLOQUEADO o aún sin dictamen).</summary>
    public const string NoViable = "NO_VIABLE";

    /// <summary>El vehículo ya tiene un trámite creado desde Diagnóstico (R-16).</summary>
    public const string YaTieneTramite = "YA_TIENE_TRAMITE";

    /// <summary>Ya hay un ítem vivo (PENDIENTE o ENVIADO) del vehículo: doble clic o masivo en vuelo.</summary>
    public const string EnCurso = "EN_CURSO";

    /// <summary>Trámites no respondió o rechazó al cliente de servicio (status gRPC); se puede reintentar.</summary>
    public const string TramitesNoDisponible = "TRAMITES_NO_DISPONIBLE";

    /// <summary>Trámites respondió un rechazo con un código que este servicio no conoce.</summary>
    public const string RechazoTramites = "RECHAZO_TRAMITES";

    public const string TipoNoDisponible = "TIPO_NO_DISPONIBLE";
    public const string FamiliaBloqueada = "FAMILIA_BLOQUEADA";
    public const string UsuarioSinPermiso = "USUARIO_SIN_PERMISO";
    public const string OtNoHabilitado = "OT_NO_HABILITADO";
    public const string ReglaCompania = "REGLA_COMPANIA";
    public const string TramiteActivoDuplicado = "TRAMITE_ACTIVO_DUPLICADO";
    public const string EmpresaInactiva = "EMPRESA_INACTIVA";
    public const string ReferenciaDeOtroOrigen = "REFERENCIA_DE_OTRO_ORIGEN";
    public const string DatosInvalidos = "DATOS_INVALIDOS";

    private static readonly Dictionary<string, string> Mensajes = new(StringComparer.Ordinal)
    {
        [NoTerminado] = "El vehículo todavía tiene consultas sin terminar.",
        [NoViable] = "El vehículo no tiene un dictamen viable.",
        [YaTieneTramite] = "El vehículo ya tiene un trámite creado.",
        [EnCurso] = "Ya se está creando el trámite de este vehículo.",
        [TramitesNoDisponible] = "Trámites no está disponible en este momento. Intenta de nuevo en unos segundos.",
        [RechazoTramites] = "Trámites no pudo crear el borrador.",
        [TipoNoDisponible] = "El tipo de trámite no está disponible.",
        [FamiliaBloqueada] = "La familia del tipo de trámite está bloqueada para la compañía.",
        [UsuarioSinPermiso] = "Tu usuario no tiene permiso para crear este trámite.",
        [OtNoHabilitado] = "OT no habilitado para la compañía",
        [ReglaCompania] = "Una regla de la compañía impide crear el trámite.",
        [TramiteActivoDuplicado] = "Ya hay otro trámite activo del mismo tipo para el vehículo.",
        [EmpresaInactiva] = "La compañía no está activa en Trámites.",
        [ReferenciaDeOtroOrigen] = "El vehículo ya está ligado a un trámite que no se creó desde Diagnóstico.",
        [DatosInvalidos] = "Faltan datos del vehículo para crear el trámite.",
    };

    /// <summary>Los nueve códigos de rechazo de negocio del rpc <c>CrearBorradorDesdeDiagnostico</c> (diseño §3).</summary>
    public static IReadOnlyList<string> RechazosTramites { get; } =
    [
        TipoNoDisponible, FamiliaBloqueada, UsuarioSinPermiso, OtNoHabilitado, ReglaCompania, TramiteActivoDuplicado, EmpresaInactiva,
        ReferenciaDeOtroOrigen, DatosInvalidos,
    ];

    /// <summary>Texto claro del motivo; para un código desconocido, el de <see cref="RechazoTramites"/>.</summary>
    public static string Mensaje(string codigo) =>
        codigo is not null && Mensajes.TryGetValue(codigo, out var mensaje) ? mensaje : Mensajes[RechazoTramites];
}
