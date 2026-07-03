namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Códigos de error del módulo de estados (N 03). Contrato compartido entre el servicio de
/// ciclo de vida, los endpoints y el frontend. Los endpoints los exponen como
/// <c>ProblemDetails.title</c> con el mensaje en español en <c>detail</c> (ADR-0022).
/// </summary>
public static class TramiteEstadoErrores
{
    /// <summary>RF02 — la transición solicitada no existe en la máquina de estados (422).</summary>
    public const string TransicionNoPermitida = "transicion_no_permitida";

    /// <summary>RF04 — el trámite está en estado final (aprobado/anulado): ni transiciones ni edición (422).</summary>
    public const string EstadoFinal = "estado_final";

    /// <summary>RF03 — gate Borrador→Preparado: la validación de identidad no está aprobada/vigente (422).</summary>
    public const string IdentidadNoAprobada = "identidad_no_aprobada";

    /// <summary>RF03 — gate Borrador→Preparado: faltan documentos obligatorios del checklist (422).</summary>
    public const string DocumentosIncompletos = "documentos_incompletos";

    /// <summary>El estado destino no es un estado de negocio conocido (422).</summary>
    public const string EstadoDesconocido = "estado_desconocido";

    /// <summary>Anular o rechazar exige motivo (RF05) (422).</summary>
    public const string MotivoRequerido = "motivo_requerido";

    /// <summary>RNF01 — conflicto de concurrencia optimista (row_version): reintentar (409, sin efectos parciales).</summary>
    public const string ConflictoConcurrencia = "conflicto_concurrencia";

    /// <summary>La instancia no existe o no pertenece al tenant (404).</summary>
    public const string NoEncontrado = "not_found";
}
