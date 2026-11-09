namespace Flit.PersonaNatural.Api.Persistence;

/// <summary>Estados del cobro de una solicitud del punto. El CHECK de la tabla sale de <see cref="Todos"/>.</summary>
public static class EstadoCobro
{
    public const string Habilitado = "habilitado";
    public const string Pagado = "pagado";
    public const string Anulado = "anulado";

    public static readonly IReadOnlyList<string> Todos = [Habilitado, Pagado, Anulado];
}

/// <summary>Medio con el que se cierra el pago. Un segundo medio (pasarela) se suma aquí sin tocar el resto del modelo.</summary>
public static class MedioDePago
{
    /// <summary>Pago en la caja del organismo con el código público del trámite.</summary>
    public const string Caja = "caja";

    /// <summary>Total en $0: la persona confirma y no pasa por caja.</summary>
    public const string SinCobro = "sin_cobro";

    public static readonly IReadOnlyList<string> Todos = [Caja, SinCobro];
}

/// <summary>Cómo cobró la caja. Solo aplica cuando el medio es <see cref="MedioDePago.Caja"/>.</summary>
public static class MedioDeCaja
{
    public const string Efectivo = "efectivo";
    public const string Datafono = "datafono";
    public const string Transferencia = "transferencia";
    public const string Otro = "otro";

    public static readonly IReadOnlyList<string> Todos = [Efectivo, Datafono, Transferencia, Otro];
}

/// <summary>
/// Cobro de una solicitud del punto (Feature #14447). Una fila nace <c>habilitado</c> cuando la persona genera el pago en caja
/// (o directamente <c>pagado</c> con medio <c>sin_cobro</c> si el total es $0) y termina <c>pagado</c> o <c>anulado</c>. Solo
/// puede haber UN cobro pagado y UN cobro habilitado por solicitud (índices únicos parciales). El valor nunca lo envía el
/// cliente: lo calcula el servidor al confirmar. Sin datos personales.
/// </summary>
public sealed class CobroSolicitud
{
    public Guid Id { get; set; }
    public Guid SolicitudId { get; set; }
    public Guid OrganismoId { get; set; }

    /// <summary>Código de cobro: el código público de la solicitud (PN-AAAA-NNNNNN); no hay un PIN aparte.</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Estado { get; set; } = EstadoCobro.Habilitado;
    public DateTimeOffset? HabilitadoEn { get; set; }

    /// <summary>Último instante de vigencia del código de caja: 23:59:59 hora de Colombia del día en que se habilitó.</summary>
    public DateTimeOffset? VenceEn { get; set; }

    /// <summary>Total pagado en pesos colombianos; solo existe cuando el cobro está pagado (o anulado tras haberlo estado).</summary>
    public long? ValorCop { get; set; }

    public string? Medio { get; set; }
    public string? MedioCaja { get; set; }
    public string? MedioDetalle { get; set; }

    /// <summary>Prefactura que la caja adjuntó como soporte (opcional).</summary>
    public Guid? PrefacturaAdjuntoId { get; set; }

    public string? PagadoPor { get; set; }
    public DateTimeOffset? PagadoEn { get; set; }

    /// <summary>Líneas (JSON) y versión de tarifa con las que se calculó el total pagado: es lo que se congela al radicar.</summary>
    public string? Lineas { get; set; }

    public string? VersionTarifa { get; set; }
    /// <summary>
    /// HU #14466 — momento en que se tomó el envío del correo de cierre a quien radicó. Marca persistida que garantiza UN solo
    /// correo por pago: se reclama con un UPDATE condicionado y solo se libera si el envío falla (para reintentar).
    /// </summary>
    public DateTimeOffset? CorreoCierreEnviadoEn { get; set; }

    public string? AnuladoPor { get; set; }
    public DateTimeOffset? AnuladoEn { get; set; }
    public string? AnulacionMotivo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; } = 1;
}
