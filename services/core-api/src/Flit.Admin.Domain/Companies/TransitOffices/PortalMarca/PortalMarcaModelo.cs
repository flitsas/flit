using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;

/// <summary>
/// Forma compartida del borrador y del publicado de la marca del portal de un organismo (HU #14375, jsonb
/// <c>schemaVersion=1</c> de <c>admin.transit_office_portal_brandings</c>). El borrador puede estar incompleto.
/// Los colores reutilizan <see cref="BrandColors"/> de marca blanca (mismo contrato primary/secondary/onPrimary).
/// </summary>
public sealed record PortalMarcaSnapshot(string? PlatformName, BrandColors? Colors, Guid? LogoId, Guid? BannerId)
{
    public static readonly PortalMarcaSnapshot Empty = new(null, null, null, null);

    /// <summary>
    /// Campos obligatorios que faltan para publicar: nombre y los tres colores. Logo y banner son opcionales
    /// (sin ellos el portal usa la imagen de FLIT). Vocabulario igual al de marca blanca.
    /// </summary>
    public IReadOnlyList<string> MissingFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(PlatformName)) missing.Add("platformName");
        if (Colors is null || string.IsNullOrWhiteSpace(Colors.Primary)) missing.Add("colors.primary");
        if (Colors is null || string.IsNullOrWhiteSpace(Colors.Secondary)) missing.Add("colors.secondary");
        if (Colors is null || string.IsNullOrWhiteSpace(Colors.OnPrimary)) missing.Add("colors.onPrimary");
        return missing;
    }
}

/// <summary>
/// Ámbito de la marca de la web de persona natural (HU #14401). Reutiliza el modelo del organismo con un identificador reservado:
/// ningún organismo real tiene <see cref="Guid.Empty"/>; el repositorio lo guarda como fila de ámbito web, sin organismo.
/// </summary>
public static class PortalMarcaAmbito
{
    public static readonly Guid Web = Guid.Empty;
}

/// <summary>Marca del portal de un organismo: borrador, publicado y versión. Sin fila, es un registro vacío con versión 0.</summary>
public sealed record PortalMarcaRegistro(
    Guid TransitOfficeId,
    PortalMarcaSnapshot Draft,
    PortalMarcaSnapshot? Published,
    int PublishedVersion,
    DateTimeOffset? PublishedAt,
    Guid? PublishedBy,
    long RowVersion)
{
    /// <summary>El borrador difiere de lo publicado. Sin publicado, hay cambios si el borrador tiene algo.</summary>
    public bool HasUnpublishedChanges => Published is null ? Draft != PortalMarcaSnapshot.Empty : Draft != Published;

    public static PortalMarcaRegistro Vacio(Guid transitOfficeId) =>
        new(transitOfficeId, PortalMarcaSnapshot.Empty, null, 0, null, null, 0);
}

/// <summary>Versión de una imagen de marca (logo o banner). Inmutable: la anterior se conserva.</summary>
public sealed record PortalMarcaImagen(
    Guid Id,
    Guid TransitOfficeId,
    string Kind,
    int Version,
    string ContentType,
    string StoragePath,
    string Sha256,
    long SizeBytes)
{
    public const string KindLogo = "logo";
    public const string KindBanner = "banner";
}

/// <summary>Imagen ya guardada en el almacenamiento, lista para registrarse en el borrador.</summary>
public sealed record NuevaPortalMarcaImagen(string ContentType, string StoragePath, string Sha256, long SizeBytes);

/// <summary>Códigos de error estables del contrato de la marca del portal.</summary>
public static class PortalMarcaErrores
{
    public const string Contraste = "PORTAL_MARCA_CONTRASTE";
    public const string Incompleta = "PORTAL_MARCA_INCOMPLETA";
    public const string ArchivoInvalido = "PORTAL_MARCA_ARCHIVO_INVALIDO";
    public const string Conflicto = "PORTAL_MARCA_CONFLICTO";

    // Complementarios (mismo formato { code, message }).
    public const string Datos = "PORTAL_MARCA_DATOS_INVALIDOS";
    public const string OrganismoRequerido = "PORTAL_MARCA_ORGANISMO_REQUERIDO";
    public const string OrganismoNoExiste = "PORTAL_MARCA_ORGANISMO_NO_EXISTE";
    public const string Prohibido = "PORTAL_MARCA_PROHIBIDO";
    public const string NoEncontrada = "PORTAL_MARCA_NO_ENCONTRADA";
}

/// <summary>Puerto de persistencia de la marca del portal por organismo (SQL propio, sin tablas de marca blanca).</summary>
public interface IPortalMarcaRepository
{
    Task<bool> ExisteOrganismoAsync(Guid transitOfficeId, CancellationToken ct = default);

    /// <summary>Marca del organismo; sin fila devuelve <see cref="PortalMarcaRegistro.Vacio"/>.</summary>
    Task<PortalMarcaRegistro> ObtenerAsync(Guid transitOfficeId, CancellationToken ct = default);

    /// <summary>Guarda nombre y colores del borrador (no toca logo ni banner). <c>null</c> = la versión no coincide.</summary>
    Task<PortalMarcaRegistro?> GuardarBorradorAsync(
        Guid transitOfficeId, string? platformName, BrandColors? colors, long rowVersionEsperada, Guid actor, CancellationToken ct = default);

    /// <summary>Copia el borrador al publicado y sube la versión. <c>null</c> = la versión no coincide.</summary>
    Task<PortalMarcaRegistro?> PublicarAsync(Guid transitOfficeId, long rowVersionEsperada, Guid actor, CancellationToken ct = default);

    /// <summary>Deja el publicado en nulo y conserva el borrador. <c>null</c> = la versión no coincide.</summary>
    Task<PortalMarcaRegistro?> RetirarAsync(Guid transitOfficeId, long rowVersionEsperada, Guid actor, CancellationToken ct = default);

    /// <summary>Registra una versión nueva de imagen y la deja como la del borrador. La anterior se conserva.</summary>
    Task<(PortalMarcaImagen Imagen, PortalMarcaRegistro Marca)> AgregarImagenAsync(
        Guid transitOfficeId, string kind, NuevaPortalMarcaImagen imagen, Guid actor, CancellationToken ct = default);

    /// <summary>Quita la referencia de la imagen del borrador (las versiones se conservan).</summary>
    Task<PortalMarcaRegistro> QuitarImagenAsync(Guid transitOfficeId, string kind, Guid actor, CancellationToken ct = default);

    Task<PortalMarcaImagen?> ObtenerImagenAsync(Guid imagenId, CancellationToken ct = default);

    /// <summary>La imagen es la de logo o banner del publicado vigente de su organismo.</summary>
    Task<bool> ImagenEstaPublicadaAsync(Guid imagenId, CancellationToken ct = default);

    /// <summary>Organismo activo con ese código (sin distinguir mayúsculas) o <c>null</c>.</summary>
    Task<Guid?> ResolverOrganismoPorCodigoAsync(string codigo, CancellationToken ct = default);
}

/// <summary>Almacenamiento del binario de las imágenes de marca. No se usan URL prefirmadas.</summary>
public interface IPortalMarcaImagenStorage
{
    Task<NuevaPortalMarcaImagen> GuardarAsync(
        Guid transitOfficeId, string contentType, Stream contenido, CancellationToken ct = default);

    Task<Stream?> AbrirAsync(string storagePath, CancellationToken ct = default);
}
