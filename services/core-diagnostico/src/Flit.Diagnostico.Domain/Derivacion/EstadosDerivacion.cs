namespace Flit.Diagnostico.Domain.Derivacion;

/// <summary>
/// Estado de un ítem de derivación: un vehículo de una operación «Crear trámite» (diseño §4, ADR-0084). PENDIENTE, ENVIADO y
/// CREADO son «vivos»: ocupan <c>ux_derivaciones_vehiculo_vivo</c> (un solo vivo por vehículo, R-16). OMITIDO y FALLIDO no.
/// </summary>
public enum EstadoItemDerivacion
{
    /// <summary>Encolado, sin crédito todavía (masivo, incremento 3).</summary>
    Pendiente,

    /// <summary>Enviado a Trámites (en vuelo).</summary>
    Enviado,

    /// <summary>Trámites creó (o ya tenía) el borrador; el vehículo guarda el trámite.</summary>
    Creado,

    /// <summary>No se creó por una regla (no elegible al ejecutar o rechazo de negocio de Trámites); lleva motivo.</summary>
    Omitido,

    /// <summary>Trámites no estuvo disponible; lleva motivo y se puede volver a intentar.</summary>
    Fallido,
}

/// <summary>Individual (síncrona, una placa) o masiva (encolada, incremento 3).</summary>
public enum TipoOperacionDerivacion
{
    Individual,
    Masiva,
}

/// <summary>Estado de una operación de derivación: TERMINADA cuando creadas + omitidas + fallidas = total.</summary>
public enum EstadoOperacionDerivacion
{
    EnCurso,
    Terminada,
}
