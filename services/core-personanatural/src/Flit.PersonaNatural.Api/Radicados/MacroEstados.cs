namespace Flit.PersonaNatural.Api.Radicados;

public enum MacroEstado
{
    PendienteValidacion,
    EnRevisionOt,
    Subsanacion,
    Finalizado,
}

/// <summary>Macro-estado que ve el ciudadano: código estable para el front, etiqueta y mensaje en español.</summary>
public sealed record MacroEstadoInfo(MacroEstado Macro, string Codigo, string Etiqueta, string Mensaje);

/// <summary>
/// Tabla versionada que traduce el estado interno (<see cref="EstadoSolicitud"/> del BFF + estado del trámite de FLIT 2) a
/// los cuatro macro-estados del portal (HU #14042; docs/design/PERSONA-NATURAL-arquitectura-y-contratos.md §5.1, propuesta
/// pendiente de confirmar por el PO). Las etiquetas son las de <c>MACRO_ESTADOS</c> del front; si cambia el mapa, sube <see cref="Version"/>.
/// </summary>
public static class MacroEstados
{
    public const string Version = "1.0.0";

    private static readonly MacroEstadoInfo Pendiente = new(MacroEstado.PendienteValidacion, "pendiente_validacion", "Pendiente de Validación",
        "Estamos validando la información y la identidad de las partes antes de enviar el trámite al Organismo de Tránsito.");

    private static readonly MacroEstadoInfo EnRevision = new(MacroEstado.EnRevisionOt, "en_revision_ot", "En Revisión OT",
        "Tu trámite está en curso y aún no presenta respuesta por parte del Organismo de Tránsito. Te notificaremos a tu correo tan pronto sea procesado.");

    private static readonly MacroEstadoInfo Subsanacion = new(MacroEstado.Subsanacion, "subsanacion", "Subsanación",
        "El Organismo de Tránsito devolvió la solicitud. Revisa el motivo y corrige para que continúe.");

    private static readonly MacroEstadoInfo Aprobado = new(MacroEstado.Finalizado, "finalizado", "Finalizado",
        "Tu trámite ha sido aprobado satisfactoriamente por el Organismo de Tránsito.");

    private static readonly MacroEstadoInfo Cerrado = new(MacroEstado.Finalizado, "finalizado", "Finalizado",
        "Tu trámite fue cerrado por el Organismo de Tránsito y ya no admite cambios.");

    /// <summary>Etiquetas en el orden del tracking del front (<c>MACRO_ESTADOS</c>).</summary>
    public static IReadOnlyList<string> Etiquetas { get; } = [Pendiente.Etiqueta, EnRevision.Etiqueta, Subsanacion.Etiqueta, Aprobado.Etiqueta];

    /// <summary>
    /// Estado de FLIT 2 manda cuando se conoce; si falta (el espejo aún no lo trae) se usa el de la solicitud. Cualquier valor
    /// desconocido cae en «Pendiente de Validación»: es el más prudente (no promete una revisión ni un cierre que no existen) y nunca lanza.
    /// </summary>
    public static MacroEstadoInfo Resolver(string? estadoSolicitud, string? estadoTramite)
    {
        if (!string.IsNullOrWhiteSpace(estadoTramite))
        {
            return estadoTramite.Trim().ToLowerInvariant() switch
            {
                "borrador" or "preparado" => Pendiente,
                "preasignacion" or "asignado" or "entregado" => EnRevision,
                "rechazado" => Subsanacion,
                "aprobado" => Aprobado,
                "anulado" or "revocado" => Cerrado,
                _ => Pendiente,
            };
        }

        // Sin estado de FLIT 2 (borrador, validación, en cola, creada sin espejo aún, rechazada al crear, cancelada o un valor
        // desconocido) el trámite todavía no está en manos del OT: se muestra como pendiente.
        _ = estadoSolicitud;
        return Pendiente;
    }
}
