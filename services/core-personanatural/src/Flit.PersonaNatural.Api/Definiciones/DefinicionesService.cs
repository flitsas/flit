using System.Text.Json;
using Flit.PersonaNatural.Api.Cotizaciones;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Definiciones;

/// <summary>
/// Definición de un tipo de trámite para el portal (HU #14078). La estructura sale de core-api (único dueño de los tipos); el
/// BFF solo la cachea unos segundos y decide si el portal puede pintarla: el tipo tiene que ser disponible y TODA sección
/// tiene que tener su renderer (<see cref="SectionTypesConocidos"/>). Una definición con una sección desconocida se trata como
/// tipo no disponible entera, nunca a medias.
/// </summary>
public sealed class DefinicionesService(IFachadaDefiniciones fachada, IMemoryCache cache, IOptions<CotizacionesOptions> options)
{
    public const string CodigoTipoNoDisponible = "tipo_no_disponible";

    /// <summary>
    /// Catálogo cerrado de <c>section_type</c> de FLIT 2 que el portal sabe pintar (un solo lugar; se amplía junto con el
    /// renderer del front). Un tipo que declare otro no se ofrece.
    /// </summary>
    public static readonly IReadOnlySet<string> SectionTypesConocidos = new HashSet<string>(StringComparer.Ordinal)
    {
        "vehicle_query", "document_checklist", "actor_form", "commercial", "biometric", "signature_fur", "plate_request", "prenda_decision", "generic_form",
    };

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CotizacionesOptions _o = options.Value;

    /// <summary>
    /// La definición vigente del tipo en el organismo. La caché es corta y por (código, organismo): la versión solo se conoce
    /// al leer la respuesta, así que va dentro del valor y un cambio de versión o de contenido (huella) se ve al vencer el
    /// plazo. Lo que una solicitud ya congeló no se relee de aquí.
    /// </summary>
    /// <exception cref="FachadaException"><c>tipo_no_disponible</c> si el tipo no es elegible o trae una sección que el portal no pinta.</exception>
    public async Task<DefinicionTipoDto> ObtenerAsync(string codigo, string? organismoId, CancellationToken ct)
    {
        var clave = $"pn:def:{codigo.ToUpperInvariant()}:{organismoId?.ToLowerInvariant()}";
        if (cache.TryGetValue(clave, out DefinicionTipoDto? existente) && existente is not null)
            return existente;

        var definicion = await fachada.ObtenerDefinicionAsync(codigo, organismoId, ct).ConfigureAwait(false);
        if (!definicion.Disponible)
            throw new FachadaException(CodigoTipoNoDisponible, "El tipo de trámite no está disponible.", StatusCodes.Status404NotFound);
        if (definicion.Pasos.SelectMany(p => p.SectionTypes).Any(s => !SectionTypesConocidos.Contains(s)))
            throw new FachadaException(CodigoTipoNoDisponible, "El tipo de trámite no está disponible.", StatusCodes.Status404NotFound);

        if (_o.CacheCatalogoSegundos > 0)
            cache.Set(clave, definicion, TimeSpan.FromSeconds(_o.CacheCatalogoSegundos));
        return definicion;
    }

    /// <summary>La definición serializada como se congela en la solicitud (<c>definicion_snapshot</c>).</summary>
    public static string Serializar(DefinicionTipoDto definicion) => JsonSerializer.Serialize(definicion, Json);

    /// <summary>Lee un snapshot congelado; las reglas de una solicitud salen de aquí y nunca de código por tipo.</summary>
    public static DefinicionTipoDto? Leer(string snapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<DefinicionTipoDto>(snapshot, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
