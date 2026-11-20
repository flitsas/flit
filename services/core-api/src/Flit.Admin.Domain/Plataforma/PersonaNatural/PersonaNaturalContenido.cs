using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Admin.Domain.Plataforma.PersonaNatural;

/// <summary>Códigos de los enlaces del portal (los mismos del CHECK del DDL 165).</summary>
public static class CodigosEnlacePn
{
    public const string PoliticaDatos = "POLITICA_DATOS";
    public const string Terminos = "TERMINOS";
    public const string ContactoVentas = "CONTACTO_VENTAS";
    public const string Video = "VIDEO";

    public static readonly IReadOnlyList<string> Todos = [PoliticaDatos, Terminos, ContactoVentas, Video];

    /// <summary>Los dos legales: mientras no tengan documento oficial el portal conserva su aviso y no inventa texto.</summary>
    public static bool EsLegal(string codigo) => codigo is PoliticaDatos or Terminos;
}

/// <summary>Una pregunta frecuente del contenido de trabajo (borrador).</summary>
public sealed record FaqPn(
    Guid Id, string Pregunta, string Respuesta, int Orden, bool Activa, long RowVersion, DateTimeOffset CreadaEn, DateTimeOffset? ActualizadaEn);

/// <summary>
/// Un banner del contenido de trabajo. <see cref="ImagenPath"/> es la ruta opaca del almacenamiento: nunca sale por la API.
/// </summary>
public sealed record BannerPn(
    Guid Id,
    string Titulo,
    string TextoAlternativo,
    string? EnlaceUrl,
    int Orden,
    DateTimeOffset? VigenteDesde,
    DateTimeOffset? VigenteHasta,
    bool Activo,
    [property: JsonIgnore] string ImagenPath,
    string ImagenSha256,
    string ImagenContentType,
    long ImagenBytes,
    long RowVersion,
    DateTimeOffset CreadoEn,
    DateTimeOffset? ActualizadoEn);

/// <summary>Un enlace del portal. <c>Url</c> nula = sin documento oficial / sin valor.</summary>
public sealed record EnlacePn(string Codigo, string? Url, DateTimeOffset? ActualizadoEn);

/// <summary>Una pregunta tal como se publica (solo las activas).</summary>
public sealed record FaqPublicadaPn(Guid Id, string Pregunta, string Respuesta, int Orden);

/// <summary>Un banner tal como se publica (solo los activos). La ruta de la imagen es interna y no sale por la API.</summary>
public sealed record BannerPublicadoPn(
    Guid Id,
    string Titulo,
    string TextoAlternativo,
    string? EnlaceUrl,
    int Orden,
    DateTimeOffset? VigenteDesde,
    DateTimeOffset? VigenteHasta,
    [property: JsonIgnore] string ImagenPath,
    string ImagenSha256,
    string ImagenContentType);

/// <summary>El contenido publicable (o publicado): preguntas activas, banners activos y los cuatro enlaces.</summary>
public sealed record ContenidoPn(
    IReadOnlyList<FaqPublicadaPn> Preguntas, IReadOnlyList<BannerPublicadoPn> Banners, IReadOnlyList<EnlacePn> Enlaces);

/// <summary>Una versión publicada: foto inmutable del contenido con quién y cuándo la publicó.</summary>
public sealed record VersionContenidoPn(int Version, DateTimeOffset PublicadoEn, Guid? PublicadoPor, ContenidoPn Contenido);

public sealed record ResumenVersionContenidoPn(int Version, DateTimeOffset PublicadoEn, Guid? PublicadoPor, int Preguntas, int Banners);

/// <summary>Borrador tal como lo vería el portal si se publicara hoy, y si difiere de lo publicado.</summary>
public sealed record BorradorContenidoPn(ContenidoPn Contenido, int? VersionPublicada, bool HayCambiosSinPublicar);

/// <summary>Imagen de un banner en el almacenamiento (ruta opaca interna, hash y tipo).</summary>
public sealed record ImagenBannerPn(string Path, string Sha256, string ContentType);

public sealed record NuevoBannerPn(
    string Titulo, string TextoAlternativo, string? EnlaceUrl, int Orden, DateTimeOffset? VigenteDesde, DateTimeOffset? VigenteHasta,
    bool Activo, string ImagenPath, string ImagenSha256, string ImagenContentType, long ImagenBytes);

/// <summary>Cambios de un banner: la imagen nueva es opcional (nulos = se conserva la actual).</summary>
public sealed record CambiosBannerPn(
    string Titulo, string TextoAlternativo, string? EnlaceUrl, int Orden, DateTimeOffset? VigenteDesde, DateTimeOffset? VigenteHasta,
    bool Activo, string? ImagenPath, string? ImagenSha256, string? ImagenContentType, long? ImagenBytes);

/// <summary>
/// Persistencia del contenido del portal (HU #14073): contenido de trabajo (borrador) y versiones publicadas
/// (<c>admin.persona_natural_faqs/_banners/_links/_content_versions</c>, DDL 165). Tablas globales sin tenant.
/// </summary>
public interface IPersonaNaturalContenidoRepository
{
    Task<IReadOnlyList<FaqPn>> ListarFaqsAsync(CancellationToken ct = default);

    Task<FaqPn?> ObtenerFaqAsync(Guid id, CancellationToken ct = default);

    /// <summary>La pregunta nueva va al final (orden = último + 1).</summary>
    Task<FaqPn> CrearFaqAsync(string pregunta, string respuesta, bool activa, Guid actor, CancellationToken ct = default);

    Task<FaqPn?> ActualizarFaqAsync(Guid id, string pregunta, string respuesta, bool activa, Guid actor, CancellationToken ct = default);

    /// <summary>Reasigna el orden según la posición en la lista; <c>false</c> si la lista no es exactamente el conjunto de preguntas.</summary>
    Task<bool> ReordenarFaqsAsync(IReadOnlyList<Guid> ids, Guid actor, CancellationToken ct = default);

    Task<IReadOnlyList<BannerPn>> ListarBannersAsync(CancellationToken ct = default);

    Task<BannerPn?> ObtenerBannerAsync(Guid id, CancellationToken ct = default);

    Task<BannerPn> CrearBannerAsync(NuevoBannerPn banner, Guid actor, CancellationToken ct = default);

    Task<BannerPn?> ActualizarBannerAsync(Guid id, CambiosBannerPn cambios, Guid actor, CancellationToken ct = default);

    Task<bool> ReordenarBannersAsync(IReadOnlyList<Guid> ids, Guid actor, CancellationToken ct = default);

    /// <summary>Imagen del banner de trabajo (para la vista previa del administrador).</summary>
    Task<ImagenBannerPn?> ImagenDeTrabajoAsync(Guid id, CancellationToken ct = default);

    /// <summary>Siempre los cuatro enlaces (los que no tienen valor, con <c>Url</c> nula).</summary>
    Task<IReadOnlyList<EnlacePn>> ListarEnlacesAsync(CancellationToken ct = default);

    Task<EnlacePn> EstablecerEnlaceAsync(string codigo, string? url, Guid actor, CancellationToken ct = default);

    /// <summary>Lo que se publicaría hoy: preguntas y banners ACTIVOS ordenados, y los cuatro enlaces.</summary>
    Task<ContenidoPn> ContenidoPublicableAsync(CancellationToken ct = default);

    Task<VersionContenidoPn?> UltimaVersionAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ResumenVersionContenidoPn>> ListarVersionesAsync(CancellationToken ct = default);

    Task<VersionContenidoPn?> ObtenerVersionAsync(int version, CancellationToken ct = default);

    /// <summary>Foto del contenido publicable como versión nueva (en una transacción que serializa las publicaciones).</summary>
    Task<VersionContenidoPn> PublicarAsync(Guid actor, CancellationToken ct = default);
}

/// <summary>
/// Foto jsonb del contenido publicado. Se escribe y se lee con <c>Utf8JsonWriter</c>/<c>JsonDocument</c> (sin reflexión: el
/// proyecto es compatible con AOT) y el texto es CANÓNICO (listas ordenadas por orden e id): dos contenidos iguales dan el
/// mismo texto, y así se sabe si el borrador difiere de lo publicado.
/// </summary>
public static class ContenidoPnSnapshot
{
    public static string Escribir(ContenidoPn contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteStartArray("preguntas");
            foreach (var p in contenido.Preguntas.OrderBy(p => p.Orden).ThenBy(p => p.Id))
            {
                w.WriteStartObject();
                w.WriteString("id", p.Id);
                w.WriteString("pregunta", p.Pregunta);
                w.WriteString("respuesta", p.Respuesta);
                w.WriteNumber("orden", p.Orden);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("banners");
            foreach (var b in contenido.Banners.OrderBy(b => b.Orden).ThenBy(b => b.Id))
            {
                w.WriteStartObject();
                w.WriteString("id", b.Id);
                w.WriteString("titulo", b.Titulo);
                w.WriteString("textoAlternativo", b.TextoAlternativo);
                EscribirNullable(w, "enlaceUrl", b.EnlaceUrl);
                w.WriteNumber("orden", b.Orden);
                EscribirNullable(w, "vigenteDesde", b.VigenteDesde?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                EscribirNullable(w, "vigenteHasta", b.VigenteHasta?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                w.WriteString("imagenPath", b.ImagenPath);
                w.WriteString("imagenSha256", b.ImagenSha256);
                w.WriteString("imagenContentType", b.ImagenContentType);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("enlaces");
            foreach (var e in contenido.Enlaces.OrderBy(e => e.Codigo, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("codigo", e.Codigo);
                EscribirNullable(w, "url", e.Url);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static ContenidoPn Leer(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var preguntas = raiz.GetProperty("preguntas").EnumerateArray()
            .Select(e => new FaqPublicadaPn(e.GetProperty("id").GetGuid(), e.GetProperty("pregunta").GetString()!, e.GetProperty("respuesta").GetString()!, e.GetProperty("orden").GetInt32()))
            .ToList();
        var banners = raiz.GetProperty("banners").EnumerateArray()
            .Select(e => new BannerPublicadoPn(
                e.GetProperty("id").GetGuid(), e.GetProperty("titulo").GetString()!, e.GetProperty("textoAlternativo").GetString()!,
                Texto(e, "enlaceUrl"), e.GetProperty("orden").GetInt32(), Fecha(e, "vigenteDesde"), Fecha(e, "vigenteHasta"),
                e.GetProperty("imagenPath").GetString()!, e.GetProperty("imagenSha256").GetString()!, e.GetProperty("imagenContentType").GetString()!))
            .ToList();
        var enlaces = raiz.GetProperty("enlaces").EnumerateArray()
            .Select(e => new EnlacePn(e.GetProperty("codigo").GetString()!, Texto(e, "url"), null))
            .ToList();
        return new ContenidoPn(preguntas, banners, enlaces);
    }

    /// <summary>¿El contenido de trabajo produciría una versión distinta de la publicada? (sin versión publicada, hay cambios).</summary>
    public static bool Difiere(ContenidoPn borrador, ContenidoPn? publicado) =>
        publicado is null || !string.Equals(Escribir(borrador), Escribir(publicado), StringComparison.Ordinal);

    private static void EscribirNullable(Utf8JsonWriter w, string nombre, string? valor)
    {
        if (valor is null)
            w.WriteNull(nombre);
        else
            w.WriteString(nombre, valor);
    }

    private static string? Texto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Fecha(JsonElement e, string nombre) =>
        Texto(e, nombre) is { } t ? DateTimeOffset.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null;
}
