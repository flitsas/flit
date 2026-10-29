namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>Motivos de la compuerta de radicación (valores estables: los usa el 409 <c>identity_pending</c> y el front).</summary>
public static class MotivosCompuerta
{
    public const string Ok = "ok";

    /// <summary>Alguna parte obligada no tiene identidad aprobada para ESTA solicitud (pendiente, vencida, sin pedir o por enviar).</summary>
    public const string IdentidadPendiente = "identidad_pendiente";

    /// <summary>Alguna parte obligada fue rechazada: hay que reenviar y que vuelva a validar.</summary>
    public const string IdentidadRechazada = "identidad_rechazada";
}

/// <summary>Una parte obligada que todavía no tiene identidad aprobada, con su estado (<c>EstadosIdentidad</c>).</summary>
public sealed record ParteFaltante(string Parte, short Ordinal, string Estado);

/// <summary>
/// Veredicto de la compuerta. <see cref="Permite"/> solo con <see cref="MotivosCompuerta.Ok"/>; en los otros casos
/// <see cref="Partes"/> dice cuáles faltan (rol, posición y estado), nunca datos personales.
/// </summary>
public sealed record ResultadoCompuerta(bool Permite, string Motivo, IReadOnlyList<ParteFaltante> Partes)
{
    public static ResultadoCompuerta Aprobada { get; } = new(true, MotivosCompuerta.Ok, []);

    public static ResultadoCompuerta Bloqueada(string motivo, IReadOnlyList<ParteFaltante> partes) => new(false, motivo, partes);
}

/// <summary>
/// Compuerta de radicación (HU #14046, Feature #14023, Épica #14021): ninguna solicitud llega a FLIT 2 con partes sin
/// verificar. Exige identidad APROBADA de TODAS las partes que la definición congelada de la solicitud marca con
/// validación, sin vigencia en días (decisión del PO, 2026-10-27): la validación vale solo para esta solicitud, así que lo
/// que la misma cédula validó en otro trámite no cuenta y no se consulta. La radicación (HU D3) la invoca antes de encolar y,
/// si no permite, responde 409 <c>identity_pending</c> sin encolar nada.
/// </summary>
public interface ICompuertaDeRadicacion
{
    Task<ResultadoCompuerta> EvaluarAsync(Guid solicitudId, CancellationToken ct);
}
