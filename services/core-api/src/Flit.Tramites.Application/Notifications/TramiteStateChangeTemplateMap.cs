namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// HU #11463 — mapa cerrado estado de negocio → plantilla del catálogo.
/// Solo <c>aprobado</c> y <c>rechazado</c> disparan correo en v1 (ADR-0045).
/// </summary>
public static class TramiteStateChangeTemplateMap
{
    public static string? ResolveTemplateKey(string toStatus)
    {
        if (string.IsNullOrWhiteSpace(toStatus))
            return null;

        var normalized = toStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            "aprobado" => "tramites.aprobado",
            "rechazado" => "tramites.rechazado",
            _ => null,
        };
    }
}
