namespace Flit.Diagnostico.Domain.Saneamiento;

/// <summary>
/// Estado del saneamiento de un vehículo (plan §3.2, S10, ADR-0087). En la base: <c>SOLICITADO</c>, <c>EN_GESTION</c>,
/// <c>SANEADO</c>, <c>NO_PROCEDE</c> o <c>ANULADO</c>. <see cref="Anulado"/> es final; los demás cuentan como «no anulado»
/// (un solo no anulado por vehículo, <c>ux_saneamiento_vigente</c>). El orden de los miembros es el de los CHECK.
/// </summary>
public enum EstadoSaneamiento
{
    /// <summary>La compañía marcó la bandera.</summary>
    Solicitado,

    /// <summary>FLIT lo tomó (o lo reabrió).</summary>
    EnGestion,

    /// <summary>FLIT lo cerró con éxito; no cambia el dictamen (S14).</summary>
    Saneado,

    /// <summary>FLIT lo cerró sin sanear, con motivo.</summary>
    NoProcede,

    /// <summary>Desmarcado por la compañía o anulado por FLIT. Final.</summary>
    Anulado,
}

/// <summary>Fuente de un renglón (plan §3.3, S11). <see cref="Otro"/> lo agrega FLIT con descripción; puede repetirse.</summary>
public enum FuenteSaneamiento
{
    SimitVehiculo,
    SimitPropietario,
    SimitLocatario,
    Impuesto,
    Soat,
    Rtm,
    Otro,
}

/// <summary>Hallazgo que generó un renglón automático (plan §3.3). Null en <see cref="FuenteSaneamiento.Otro"/>.</summary>
public enum HallazgoSaneamiento
{
    /// <summary>Comparendos, multas, sin paz y salvo o acuerdo con saldo (SIMIT); deuda o monto (impuesto).</summary>
    Deuda,

    /// <summary>SOAT o RTM no vigente y exigible.</summary>
    NoVigente,

    /// <summary>Leasing sin documento del locatario: su SIMIT no se pudo consultar.</summary>
    SinVerificar,
}

/// <summary>Resultado de un renglón (plan §3.3).</summary>
public enum ResultadoRenglonSaneamiento
{
    Pendiente,
    Saneado,
    NoProcede,
}

/// <summary>
/// Evento del historial del saneamiento (plan §4.3). Las transiciones llevan estado nuevo; los de renglón llevan renglón y
/// fuente; <see cref="CerradoNoProcede"/>, <see cref="Reabierto"/> y <see cref="AnuladoFlit"/> llevan motivo.
/// </summary>
public enum TipoEventoSaneamiento
{
    Solicitado,
    Desmarcado,
    Tomado,
    RenglonAgregado,
    RenglonQuitado,
    RenglonActualizado,
    EvidenciaCargada,
    EvidenciaQuitada,
    ObservacionEditada,
    CerradoSaneado,
    CerradoNoProcede,
    Reabierto,
    AnuladoFlit,
}
