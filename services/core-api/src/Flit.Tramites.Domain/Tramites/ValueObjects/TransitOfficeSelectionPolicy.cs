namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Bloque C (Feature #11189) — constantes del gate del organismo de tránsito en el paso 1. En matrícula
/// inicial el operador elige la secretaría (HU #11199); en traspaso la impone el RUNT (HU #11200). En
/// ambos casos el trámite no puede avanzar si ese organismo no está activo en FLIT y habilitado para la
/// compañía gestora — y el motivo concreto no se distingue en el mensaje a propósito: lo que el operador
/// debe hacer (pedirle al administrador que lo active y lo habilite) es lo mismo en los dos casos.
///
/// <para>Adelantar la comprobación al paso 1 <b>no la retira</b> de la radicación
/// (<c>TramiteLifecycleService</c>): un trámite puede quedar en borrador durante días y perder la
/// habilitación antes de radicarse. Son dos comprobaciones sobre el mismo hecho, en dos momentos
/// distintos, y ninguna sustituye a la otra.</para>
/// </summary>
public static class TransitOfficeSelectionPolicy
{
    /// <summary>Código 400: matrícula inicial sin secretaría elegida en el paso 1.</summary>
    public const string RequiredErrorCode = "TRANSIT_OFFICE_REQUIRED";

    /// <summary>Código 422: el organismo no está activo en FLIT o no está habilitado para la compañía.</summary>
    public const string UnavailableErrorCode = "TRANSIT_OFFICE_NOT_AVAILABLE";

    /// <summary>
    /// Marca del origen de la selección (<c>field_values.transit_office_origen</c>). La escriben los
    /// trámites creados con la secretaría ya elegida en el paso 1; el paso del FUR la usa para mostrar
    /// el organismo como dato en firme en vez de volver a pedirlo. Los borradores anteriores al cambio
    /// no la tienen y conservan el selector del FUR (convivencia, D8).
    /// </summary>
    public const string OrigenFieldKey = "transit_office_origen";

    /// <summary>Valor de <see cref="OrigenFieldKey"/> para la selección hecha en el primer paso.</summary>
    public const string OrigenPasoUno = "paso_1";
}
