namespace Flit.Impuestos.Domain.Liquidaciones;

/// <summary>
/// Estado de un ítem (una placa) de una liquidación (plan §3.2). En la base se guarda en mayúsculas con guion bajo
/// (<c>INCIERTO_REQUIERE_DECISION</c>).
/// </summary>
public enum EstadoItem
{
    /// <summary>Espera crédito de despacho.</summary>
    Pendiente,

    /// <summary>Consultando el RUNT para armar el payload.</summary>
    ConsultandoRunt,

    /// <summary>Reintento programado (RUNT transitorio o <c>rate_limited</c>).</summary>
    ReintentoProgramado,

    /// <summary>Enviando la consulta al API.</summary>
    Enviando,

    /// <summary>El API aceptó (202); espera el webhook o la consulta.</summary>
    Enviado,

    /// <summary>No se sabe si el API recibió la consulta (timeout o 5xx tras enviar).</summary>
    Incierto,

    /// <summary>El incierto venció sin hallar la consulta: decide una persona. Final para el cierre del lote.</summary>
    InciertoRequiereDecision,

    /// <summary>Falta un dato para el payload o no hay homologación (<c>campo_faltante</c>).</summary>
    DatosIncompletos,

    /// <summary>El departamento no está en el contrato de la compañía.</summary>
    DepartamentoNoHabilitado,

    /// <summary>Liquidación exitosa (con deuda o paz y salvo; puede ser parcial).</summary>
    Exitoso,

    /// <summary>Error con su clase (<see cref="ClaseError"/>).</summary>
    Error,

    /// <summary>El lote se cerró a mano sin resultado; todavía acepta un resultado tardío.</summary>
    SinResultadoLoteCerrado,

    /// <summary>El impuesto vehicular no aplica: el RUNT reporta el vehículo como de servicio público. Final; no se llama al proveedor.</summary>
    NoAplica,
}

/// <summary>Cómo cuenta un ítem en el dashboard (columna «Cuenta en el dashboard como» del plan §3.2).</summary>
public enum CategoriaDashboard
{
    Pendiente,
    Incierto,
    DatosIncompletos,
    DepartamentoNoHabilitado,
    Exitosa,
    Error,
    SinResultado,
    NoAplica,
}

/// <summary>Propiedades de cada <see cref="EstadoItem"/> (plan §3.2).</summary>
/// <example>
/// <code>
/// EstadosItem.EsFinal(EstadoItem.InciertoRequiereDecision); // true (DA-6b)
/// </code>
/// </example>
public static class EstadosItem
{
    /// <summary>Final para el cierre del lote (incluye <c>INCIERTO_REQUIERE_DECISION</c>, DA-6b, y <c>NO_APLICA</c>, R3).</summary>
    public static bool EsFinal(EstadoItem estado) => estado is EstadoItem.InciertoRequiereDecision or EstadoItem.DatosIncompletos
        or EstadoItem.DepartamentoNoHabilitado or EstadoItem.Exitoso or EstadoItem.Error or EstadoItem.SinResultadoLoteCerrado
        or EstadoItem.NoAplica;

    /// <summary>Categoría del dashboard; <c>NO_APLICA</c> cuenta aparte: ni error ni exitosa ni pendiente.</summary>
    public static CategoriaDashboard Categoria(EstadoItem estado) => estado switch
    {
        EstadoItem.Incierto or EstadoItem.InciertoRequiereDecision => CategoriaDashboard.Incierto,
        EstadoItem.DatosIncompletos => CategoriaDashboard.DatosIncompletos,
        EstadoItem.DepartamentoNoHabilitado => CategoriaDashboard.DepartamentoNoHabilitado,
        EstadoItem.Exitoso => CategoriaDashboard.Exitosa,
        EstadoItem.Error => CategoriaDashboard.Error,
        EstadoItem.SinResultadoLoteCerrado => CategoriaDashboard.SinResultado,
        EstadoItem.NoAplica => CategoriaDashboard.NoAplica,
        _ => CategoriaDashboard.Pendiente,
    };
}
