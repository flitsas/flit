using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Rutas de la compañía que el backoffice no puede usar (brief C7, HU #14576; antes privado en <c>DerivacionEndpoints</c>): si la
/// petición trae la empresa explícita del backoffice (<c>?empresa=</c>) responde 403 con el código de quien lo aplica, antes de
/// leer nada, ni siquiera a un SuperAdmin. Derivación conserva <c>DERIVACION_DESDE_BACKOFFICE</c>; el saneamiento usa
/// <c>SANEAMIENTO_DESDE_BACKOFFICE</c>.
/// </summary>
/// <example>
/// <code>
/// api.MapPut(ruta, Marcar).RequirePermiso(Permisos.SaneamientoSolicitar).SinBackoffice("SANEAMIENTO_DESDE_BACKOFFICE", "Desde el backoffice no se marca el saneamiento.");
/// </code>
/// </example>
internal static class FiltroSinBackoffice
{
    /// <summary>403 <paramref name="codigo"/> con <paramref name="mensaje"/> (fijo) si la petición trae <c>?empresa=</c>.</summary>
    public static TBuilder SinBackoffice<TBuilder>(this TBuilder builder, string codigo, string mensaje)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(mensaje);
        return builder.AddEndpointFilter(async (contexto, siguiente) =>
            contexto.HttpContext.Request.Query.ContainsKey(EmpresaExplicita.Parametro)
                ? throw ReglaException.Prohibido(codigo, mensaje)
                : await siguiente(contexto).ConfigureAwait(false));
    }
}
