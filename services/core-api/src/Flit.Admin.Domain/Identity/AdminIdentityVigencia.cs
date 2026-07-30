namespace Flit.Admin.Domain.Identity;

/// <summary>
/// HU #11059 / HU #11060 — estado de vigencia de la identidad de un sujeto administrativo
/// (representante legal o mandatario del OT), resumido para la consola.
/// <para>
/// El cálculo vivía embebido en <c>DbMandateSignerReader</c> y <b>descartaba la fecha</b>, así que
/// ninguna de las dos pantallas podía decir "hasta cuándo es válida". Aquí queda una vez, es pura y
/// devuelve también la vigencia, que es lo que faltaba.
/// </para>
/// </summary>
public static class AdminIdentityVigencia
{
    /// <summary>Aprobada y dentro de su vigencia. No hay nada que renovar.</summary>
    public const string Valid = "valid";

    /// <summary>Enviada o en curso: el sujeto todavía no completó la captura.</summary>
    public const string Pending = "pending";

    /// <summary>Hubo validación pero ya no sirve (aprobada vencida, rechazada o expirada) ⇒ RENOVAR.</summary>
    public const string Expired = "expired";

    /// <summary>Nunca se validó ⇒ ENVIAR por primera vez.</summary>
    public const string None = "none";

    /// <summary>Una validación del sujeto, reducida a lo que decide la vigencia.</summary>
    public readonly record struct Entrada(string? Status, DateTimeOffset? ValidUntil);

    /// <param name="Status">Uno de <see cref="Valid"/>/<see cref="Pending"/>/<see cref="Expired"/>/<see cref="None"/>.</param>
    /// <param name="ValidUntil">
    /// Hasta cuándo es válida; solo se informa cuando <paramref name="Status"/> es <see cref="Valid"/>.
    /// <c>null</c> en una aprobada sin caducidad registrada (vigente indefinidamente).
    /// </param>
    public readonly record struct Resultado(string Status, DateTimeOffset? ValidUntil);

    /// <summary>
    /// Resume las validaciones de UN sujeto. Precedencia: aprobada vigente → en curso → hubo alguna
    /// pero ya no sirve → ninguna. Cuando hay varias aprobadas vigentes se informa la que caduca más
    /// tarde: es la que de verdad manda hasta cuándo puede firmar.
    /// </summary>
    public static Resultado Resumir(IEnumerable<Entrada> entradas, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entradas);

        var items = entradas as ICollection<Entrada> ?? [.. entradas];
        if (items.Count == 0)
            return new Resultado(None, null);

        var vigentes = items
            .Where(e => e.Status == AdminIdentityEstados.Aprobado
                && (e.ValidUntil is null || e.ValidUntil > now))
            .ToList();

        if (vigentes.Count > 0)
        {
            // Una aprobada SIN caducidad no expira: gana sobre cualquier fecha.
            var sinCaducidad = vigentes.Any(e => e.ValidUntil is null);
            return new Resultado(Valid, sinCaducidad ? null : vigentes.Max(e => e.ValidUntil));
        }

        if (items.Any(e => e.Status is AdminIdentityEstados.Enviado or AdminIdentityEstados.EnProceso))
            return new Resultado(Pending, null);

        if (items.Any(e => e.Status is AdminIdentityEstados.Aprobado
            or AdminIdentityEstados.Expirado
            or AdminIdentityEstados.Rechazado))
            return new Resultado(Expired, null);

        return new Resultado(None, null);
    }
}
