using System.Text.Json;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.PortalMarca;

/// <summary>
/// HU #14375/#14376/#14378 — marca del portal por organismo sobre <c>admin.transit_office_portal_brandings</c> y
/// <c>admin.transit_office_portal_brand_images</c> (DDL 180). SQL parametrizado: son tablas propias, no entidades EF y no se
/// comparten con marca blanca. Sin ninguna caché: cada lectura va a la base.
/// </summary>
internal sealed class PortalMarcaRepository(FlitDbContext db) : IPortalMarcaRepository
{
    public Task<bool> ExisteOrganismoAsync(Guid transitOfficeId, CancellationToken ct = default) =>
        db.TransitOffices.AsNoTracking().AnyAsync(o => o.Id == transitOfficeId, ct);

    public async Task<Guid?> ResolverOrganismoPorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var buscado = (codigo ?? string.Empty).Trim().ToUpperInvariant();
        if (buscado.Length == 0)
            return null;
        return await db.TransitOffices.AsNoTracking()
            .Where(o => o.IsActive && o.Code.ToUpper() == buscado)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<PortalMarcaRegistro> ObtenerAsync(Guid transitOfficeId, CancellationToken ct = default)
    {
        var fila = (await db.Database.SqlQuery<MarcaFila>($"""
            SELECT draft::text AS draft, published::text AS published, published_version, published_at, published_by, row_version
              FROM admin.transit_office_portal_brandings
             WHERE transit_office_id = {transitOfficeId}
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        return fila is null ? PortalMarcaRegistro.Vacio(transitOfficeId) : Mapear(transitOfficeId, fila);
    }

    public async Task<PortalMarcaRegistro?> GuardarBorradorAsync(
        Guid transitOfficeId, string? platformName, BrandColors? colors, long rowVersionEsperada, Guid actor, CancellationToken ct = default)
    {
        var parche = SerializarParche(platformName, colors);

        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET draft = (draft - 'platformName' - 'colors') || CAST({parche} AS jsonb),
                   updated_at = now(), updated_by = {actor}
             WHERE transit_office_id = {transitOfficeId} AND row_version = {rowVersionEsperada}
            """, ct).ConfigureAwait(false);

        if (filas == 0)
        {
            if (rowVersionEsperada != 0)
                return null;

            // Primera vez: la fila nace con row_version 1 para que quien leyó «sin marca» (0) no pise a quien ya la creó.
            filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brandings (transit_office_id, draft, created_by, updated_by, row_version)
                VALUES ({transitOfficeId}, CAST({parche} AS jsonb) || jsonb_build_object('schemaVersion', 1), {actor}, {actor}, 1)
                ON CONFLICT (transit_office_id) DO NOTHING
                """, ct).ConfigureAwait(false);
            if (filas == 0)
                return null;
        }

        return await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    public async Task<PortalMarcaRegistro?> PublicarAsync(Guid transitOfficeId, long rowVersionEsperada, Guid actor, CancellationToken ct = default)
    {
        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET published = draft, published_version = published_version + 1, published_at = now(), published_by = {actor},
                   updated_at = now(), updated_by = {actor}
             WHERE transit_office_id = {transitOfficeId} AND row_version = {rowVersionEsperada}
            """, ct).ConfigureAwait(false);
        return filas == 0 ? null : await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    public async Task<PortalMarcaRegistro?> RetirarAsync(Guid transitOfficeId, long rowVersionEsperada, Guid actor, CancellationToken ct = default)
    {
        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET published = NULL, published_at = NULL, published_by = NULL, updated_at = now(), updated_by = {actor}
             WHERE transit_office_id = {transitOfficeId} AND row_version = {rowVersionEsperada}
            """, ct).ConfigureAwait(false);
        return filas == 0 ? null : await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    public async Task<(PortalMarcaImagen Imagen, PortalMarcaRegistro Marca)> AgregarImagenAsync(
        Guid transitOfficeId, string kind, NuevaPortalMarcaImagen imagen, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        var clave = ClaveJson(kind);
        var id = Guid.CreateVersion7();
        var idTexto = id.ToString();
        var tamano = (int)imagen.SizeBytes;

        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brand_images
                    (id, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes, created_by)
                SELECT {id}, {transitOfficeId}, {kind}, COALESCE(MAX(version), 0) + 1, {imagen.ContentType}, {imagen.StoragePath},
                       {imagen.Sha256}, {tamano}, {actor}
                  FROM admin.transit_office_portal_brand_images
                 WHERE transit_office_id = {transitOfficeId} AND kind = {kind}
                """, ct).ConfigureAwait(false);

            // La fila de la marca nace con la imagen del borrador, o se actualiza solo esa referencia (atómico, sin pisar
            // el nombre ni los colores que otra persona esté guardando).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brandings (transit_office_id, draft, created_by, updated_by, row_version)
                VALUES ({transitOfficeId}, jsonb_build_object('schemaVersion', 1, {clave}::text, {idTexto}::text), {actor}, {actor}, 1)
                ON CONFLICT (transit_office_id) DO UPDATE
                    SET draft = admin.transit_office_portal_brandings.draft || jsonb_build_object({clave}::text, {idTexto}::text),
                        updated_at = now(), updated_by = {actor}
                """, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var guardada = await ObtenerImagenAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("La imagen de marca no quedó registrada.");
        return (guardada, await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false));
    }

    public async Task<PortalMarcaRegistro> QuitarImagenAsync(Guid transitOfficeId, string kind, Guid actor, CancellationToken ct = default)
    {
        var clave = ClaveJson(kind);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET draft = draft - {clave}::text, updated_at = now(), updated_by = {actor}
             WHERE transit_office_id = {transitOfficeId} AND jsonb_exists(draft, {clave}::text)
            """, ct).ConfigureAwait(false);
        return await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    public async Task<PortalMarcaImagen?> ObtenerImagenAsync(Guid imagenId, CancellationToken ct = default)
    {
        var fila = (await db.Database.SqlQuery<ImagenFila>($"""
            SELECT id, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes
              FROM admin.transit_office_portal_brand_images
             WHERE id = {imagenId}
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        return fila is null
            ? null
            : new PortalMarcaImagen(fila.Id, fila.TransitOfficeId, fila.Kind, fila.Version, fila.ContentType, fila.StoragePath, fila.StorageSha256.Trim(), fila.SizeBytes);
    }

    public async Task<bool> ImagenEstaPublicadaAsync(Guid imagenId, CancellationToken ct = default)
    {
        var texto = imagenId.ToString();
        return (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM admin.transit_office_portal_brandings b
                 WHERE b.published IS NOT NULL
                   AND (b.published ->> 'logoId' = {texto} OR b.published ->> 'bannerId' = {texto})) AS "Value"
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
    }

    // ── Soporte ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string ClaveJson(string kind) => kind switch
    {
        PortalMarcaImagen.KindLogo => "logoId",
        PortalMarcaImagen.KindBanner => "bannerId",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de imagen de marca desconocido."),
    };

    private static string SerializarParche(string? platformName, BrandColors? colors)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            if (platformName is not null)
                w.WriteString("platformName", platformName);
            if (colors is not null)
            {
                w.WriteStartObject("colors");
                w.WriteString("primary", colors.Primary);
                w.WriteString("secondary", colors.Secondary);
                w.WriteString("onPrimary", colors.OnPrimary);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static PortalMarcaRegistro Mapear(Guid transitOfficeId, MarcaFila f) => new(
        transitOfficeId,
        Leer(f.Draft) ?? PortalMarcaSnapshot.Empty,
        f.Published is null ? null : Leer(f.Published),
        f.PublishedVersion,
        f.PublishedAt,
        f.PublishedBy,
        f.RowVersion);

    private static PortalMarcaSnapshot? Leer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object)
            return null;

        BrandColors? colores = null;
        if (raiz.TryGetProperty("colors", out var c) && c.ValueKind == JsonValueKind.Object)
            colores = new BrandColors(Texto(c, "primary") ?? string.Empty, Texto(c, "secondary") ?? string.Empty, Texto(c, "onPrimary") ?? string.Empty);

        return new PortalMarcaSnapshot(Texto(raiz, "platformName"), colores, GuidDe(raiz, "logoId"), GuidDe(raiz, "bannerId"));
    }

    private static string? Texto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Guid? GuidDe(JsonElement e, string nombre) =>
        Texto(e, nombre) is { } t && Guid.TryParse(t, out var g) ? g : null;

    /// <summary>Forma de la fila leída por SQL (snake_case del DbContext = nombre de propiedad).</summary>
    private sealed class MarcaFila
    {
        public string Draft { get; set; } = "{}";
        public string? Published { get; set; }
        public int PublishedVersion { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public Guid? PublishedBy { get; set; }
        public long RowVersion { get; set; }
    }

    private sealed class ImagenFila
    {
        public Guid Id { get; set; }
        public Guid TransitOfficeId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public int Version { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string StoragePath { get; set; } = string.Empty;
        public string StorageSha256 { get; set; } = string.Empty;
        public int SizeBytes { get; set; }
    }
}
