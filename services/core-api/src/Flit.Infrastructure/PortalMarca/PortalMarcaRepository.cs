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
        var oficina = Oficina(transitOfficeId);
        var fila = (await db.Database.SqlQuery<MarcaFila>($"""
            SELECT draft::text AS draft, published::text AS published, published_version, published_at, published_by, row_version
              FROM admin.transit_office_portal_brandings
             WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid)
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        return fila is null ? PortalMarcaRegistro.Vacio(transitOfficeId) : Mapear(transitOfficeId, fila);
    }

    public async Task<PortalMarcaRegistro?> GuardarBorradorAsync(
        Guid transitOfficeId, string? platformName, BrandColors? colors, long rowVersionEsperada, Guid actor, CancellationToken ct = default)
    {
        var parche = SerializarParche(platformName, colors);
        var oficina = Oficina(transitOfficeId);
        var ambito = Ambito(transitOfficeId);

        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET draft = (draft - 'platformName' - 'colors') || CAST({parche} AS jsonb),
                   updated_at = now(), updated_by = {actor}
             WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) AND row_version = {rowVersionEsperada}
            """, ct).ConfigureAwait(false);

        if (filas == 0)
        {
            if (rowVersionEsperada != 0)
                return null;

            // Primera vez: la fila nace con row_version 1 para que quien leyó «sin marca» (0) no pise a quien ya la creó.
            filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id, draft, created_by, updated_by, row_version)
                VALUES ({ambito}, CAST({oficina} AS uuid), CAST({parche} AS jsonb) || jsonb_build_object('schemaVersion', 1), {actor}, {actor}, 1)
                ON CONFLICT DO NOTHING
                """, ct).ConfigureAwait(false);
            if (filas == 0)
                return null;
        }

        return await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    public async Task<PortalMarcaRegistro?> PublicarAsync(Guid transitOfficeId, long rowVersionEsperada, Guid actor, CancellationToken ct = default)
    {
        var oficina = Oficina(transitOfficeId);
        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET published = draft, published_version = published_version + 1, published_at = now(), published_by = {actor},
                   updated_at = now(), updated_by = {actor}
             WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) AND row_version = {rowVersionEsperada}
            """, ct).ConfigureAwait(false);
        return filas == 0 ? null : await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    public async Task<PortalMarcaRegistro?> RetirarAsync(Guid transitOfficeId, long rowVersionEsperada, Guid actor, CancellationToken ct = default)
    {
        var oficina = Oficina(transitOfficeId);
        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET published = NULL, published_at = NULL, published_by = NULL, updated_at = now(), updated_by = {actor}
             WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) AND row_version = {rowVersionEsperada}
            """, ct).ConfigureAwait(false);
        return filas == 0 ? null : await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    public async Task<(PortalMarcaImagen Imagen, PortalMarcaRegistro Marca)> AgregarImagenAsync(
        Guid transitOfficeId, string kind, NuevaPortalMarcaImagen imagen, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        if (kind == PortalMarcaImagen.KindBanner && transitOfficeId != PortalMarcaAmbito.Web)
            return await ReemplazarPrimerBannerAsync(transitOfficeId, imagen, actor, ct).ConfigureAwait(false);

        var clave = ClaveJson(kind);
        var id = Guid.CreateVersion7();
        var idTexto = id.ToString();
        var tamano = (int)imagen.SizeBytes;
        var oficina = Oficina(transitOfficeId);
        var ambito = Ambito(transitOfficeId);

        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brand_images
                    (id, scope, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes, created_by)
                SELECT {id}, {ambito}, CAST({oficina} AS uuid), {kind}, COALESCE(MAX(version), 0) + 1, {imagen.ContentType}, {imagen.StoragePath},
                       {imagen.Sha256}, {tamano}, {actor}
                  FROM admin.transit_office_portal_brand_images
                 WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) AND kind = {kind}
                """, ct).ConfigureAwait(false);

            // La fila de la marca nace con la imagen del borrador, o se actualiza solo esa referencia (atómico, sin pisar
            // el nombre ni los colores que otra persona esté guardando). La unicidad del organismo y la de la web son índices
            // distintos y no hay ON CONFLICT con destino común: se actualiza, se crea si falta y, si otra sesión la creó en
            // medio, se actualiza de nuevo.
            var actualizar = () => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE admin.transit_office_portal_brandings
                   SET draft = draft || jsonb_build_object({clave}::text, {idTexto}::text), updated_at = now(), updated_by = {actor}
                 WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid)
                """, ct);
            if (await actualizar().ConfigureAwait(false) == 0
                && await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id, draft, created_by, updated_by, row_version)
                    VALUES ({ambito}, CAST({oficina} AS uuid), jsonb_build_object('schemaVersion', 1, {clave}::text, {idTexto}::text), {actor}, {actor}, 1)
                    ON CONFLICT DO NOTHING
                    """, ct).ConfigureAwait(false) == 0)
                await actualizar().ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var guardada = await ObtenerImagenAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("La imagen de marca no quedó registrada.");
        return (guardada, await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false));
    }

    public async Task<PortalMarcaRegistro> QuitarImagenAsync(Guid transitOfficeId, string kind, Guid actor, CancellationToken ct = default)
    {
        if (kind == PortalMarcaImagen.KindBanner && transitOfficeId != PortalMarcaAmbito.Web)
        {
            // Legacy sobre la lista: quitar el banner único = quitar el primero (sin banners no hay nada que quitar).
            var primero = (await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false)).Draft.Banners is { Count: > 0 } lista ? lista[0] : null;
            if (primero is not null)
                await QuitarBannerAsync(transitOfficeId, primero.ImageId, actor, ct).ConfigureAwait(false);
            return await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        }

        var clave = ClaveJson(kind);
        var oficina = Oficina(transitOfficeId);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.transit_office_portal_brandings
               SET draft = draft - {clave}::text, updated_at = now(), updated_by = {actor}
             WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) AND jsonb_exists(draft, {clave}::text)
            """, ct).ConfigureAwait(false);
        return await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
    }

    // ── Carrusel (borrador) ────────────────────────────────────────────────────────────────────────────────────────

    public async Task<(PortalMarcaImagen? Imagen, PortalMarcaRegistro? Marca)> AgregarCarruselAsync(
        Guid transitOfficeId, NuevaPortalMarcaImagen imagen, string alt, string? linkUrl, int maximo, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        var id = Guid.CreateVersion7();
        var tamano = (int)imagen.SizeBytes;
        var oficina = Oficina(transitOfficeId);
        var ambito = Ambito(transitOfficeId);
        const string kind = PortalMarcaImagen.KindCarousel;

        var marca = await ModificarListaAsync(transitOfficeId, actor, ClaveCarrusel, crearSiFalta: true, async items =>
        {
            if (items.Count >= maximo)
                return false;

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_portal_brand_images
                    (id, scope, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes, created_by)
                SELECT {id}, {ambito}, CAST({oficina} AS uuid), {kind}, COALESCE(MAX(version), 0) + 1, {imagen.ContentType}, {imagen.StoragePath},
                       {imagen.Sha256}, {tamano}, {actor}
                  FROM admin.transit_office_portal_brand_images
                 WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) AND kind = {kind}
                """, ct).ConfigureAwait(false);
            items.Add(new PortalMarcaCarruselItem(id, alt, linkUrl));
            return true;
        }, ct).ConfigureAwait(false);

        return marca is null
            ? (null, null)
            : (await ObtenerImagenAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("La imagen del carrusel no quedó registrada."), marca);
    }

    public Task<PortalMarcaRegistro?> ActualizarCarruselAsync(
        Guid transitOfficeId, Guid imageId, string alt, string? linkUrl, Guid actor, CancellationToken ct = default) =>
        ModificarListaAsync(transitOfficeId, actor, ClaveCarrusel, crearSiFalta: false, items =>
        {
            var i = items.FindIndex(x => x.ImageId == imageId);
            if (i < 0)
                return Task.FromResult(false);
            items[i] = items[i] with { Alt = alt, LinkUrl = linkUrl };
            return Task.FromResult(true);
        }, ct);

    public Task<PortalMarcaRegistro?> OrdenarCarruselAsync(
        Guid transitOfficeId, IReadOnlyList<Guid> imageIds, Guid actor, CancellationToken ct = default) =>
        ModificarListaAsync(transitOfficeId, actor, ClaveCarrusel, crearSiFalta: false, items =>
        {
            // Permutación exacta: mismos elementos, sin repetir ni sobrar.
            if (imageIds.Count != items.Count || imageIds.Distinct().Count() != items.Count
                || !items.Select(x => x.ImageId).ToHashSet().SetEquals(imageIds))
                return Task.FromResult(false);
            var porId = items.ToDictionary(x => x.ImageId);
            items.Clear();
            items.AddRange(imageIds.Select(g => porId[g]));
            return Task.FromResult(true);
        }, ct);

    public Task<PortalMarcaRegistro?> QuitarCarruselAsync(Guid transitOfficeId, Guid imageId, Guid actor, CancellationToken ct = default) =>
        ModificarListaAsync(transitOfficeId, actor, ClaveCarrusel, crearSiFalta: false, items =>
            Task.FromResult(items.RemoveAll(x => x.ImageId == imageId) > 0), ct);

    // ── Banners del portal del organismo (borrador) ────────────────────────────────────────────────────────────────

    private Task<int> InsertarImagenAsync(Guid id, Guid transitOfficeId, string kind, NuevaPortalMarcaImagen imagen, Guid actor, CancellationToken ct)
    {
        var tamano = (int)imagen.SizeBytes;
        var oficina = Oficina(transitOfficeId);
        var ambito = Ambito(transitOfficeId);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_portal_brand_images
                (id, scope, transit_office_id, kind, version, content_type, storage_path, storage_sha256, size_bytes, created_by)
            SELECT {id}, {ambito}, CAST({oficina} AS uuid), {kind}, COALESCE(MAX(version), 0) + 1, {imagen.ContentType}, {imagen.StoragePath},
                   {imagen.Sha256}, {tamano}, {actor}
              FROM admin.transit_office_portal_brand_images
             WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) AND kind = {kind}
            """, ct);
    }

    public async Task<(PortalMarcaImagen? Imagen, PortalMarcaRegistro? Marca)> AgregarBannerAsync(
        Guid transitOfficeId, NuevaPortalMarcaImagen imagen, string alt, string? linkUrl, int maximo, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        var id = Guid.CreateVersion7();
        var marca = await ModificarListaAsync(transitOfficeId, actor, ClaveBanners, crearSiFalta: true, async items =>
        {
            if (items.Count >= maximo)
                return false;
            await InsertarImagenAsync(id, transitOfficeId, PortalMarcaImagen.KindBanner, imagen, actor, ct).ConfigureAwait(false);
            items.Add(new PortalMarcaCarruselItem(id, alt, linkUrl));
            return true;
        }, ct).ConfigureAwait(false);

        return marca is null
            ? (null, null)
            : (await ObtenerImagenAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("El banner no quedó registrado."), marca);
    }

    public Task<PortalMarcaRegistro?> ActualizarBannerAsync(
        Guid transitOfficeId, Guid imageId, string alt, string? linkUrl, Guid actor, CancellationToken ct = default) =>
        ModificarListaAsync(transitOfficeId, actor, ClaveBanners, crearSiFalta: false, items =>
        {
            var i = items.FindIndex(x => x.ImageId == imageId);
            if (i < 0)
                return Task.FromResult(false);
            items[i] = items[i] with { Alt = alt, LinkUrl = linkUrl };
            return Task.FromResult(true);
        }, ct);

    public Task<PortalMarcaRegistro?> OrdenarBannersAsync(
        Guid transitOfficeId, IReadOnlyList<Guid> imageIds, Guid actor, CancellationToken ct = default) =>
        ModificarListaAsync(transitOfficeId, actor, ClaveBanners, crearSiFalta: false, items =>
        {
            if (imageIds.Count != items.Count || imageIds.Distinct().Count() != items.Count
                || !items.Select(x => x.ImageId).ToHashSet().SetEquals(imageIds))
                return Task.FromResult(false);
            var porId = items.ToDictionary(x => x.ImageId);
            items.Clear();
            items.AddRange(imageIds.Select(g => porId[g]));
            return Task.FromResult(true);
        }, ct);

    public Task<PortalMarcaRegistro?> QuitarBannerAsync(Guid transitOfficeId, Guid imageId, Guid actor, CancellationToken ct = default) =>
        ModificarListaAsync(transitOfficeId, actor, ClaveBanners, crearSiFalta: false, items =>
            Task.FromResult(items.RemoveAll(x => x.ImageId == imageId) > 0), ct);

    /// <summary>
    /// Endpoint legacy <c>POST /banner</c> de un organismo: opera sobre el PRIMER banner de la lista (lo reemplaza conservando su texto
    /// alternativo y enlace; si no hay ninguno, lo crea con el texto por defecto). El documento queda sin <c>bannerId</c> legacy.
    /// </summary>
    private async Task<(PortalMarcaImagen Imagen, PortalMarcaRegistro Marca)> ReemplazarPrimerBannerAsync(
        Guid transitOfficeId, NuevaPortalMarcaImagen imagen, Guid actor, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        var marca = await ModificarListaAsync(transitOfficeId, actor, ClaveBanners, crearSiFalta: true, async items =>
        {
            await InsertarImagenAsync(id, transitOfficeId, PortalMarcaImagen.KindBanner, imagen, actor, ct).ConfigureAwait(false);
            if (items.Count > 0)
                items[0] = items[0] with { ImageId = id };
            else
                items.Add(new PortalMarcaCarruselItem(id, PortalMarcaSnapshot.BannerAltPorDefecto, null));
            return true;
        }, ct).ConfigureAwait(false);

        return (await ObtenerImagenAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("El banner no quedó registrado."),
            marca ?? throw new InvalidOperationException("La marca del banner no quedó registrada."));
    }

    /// <summary>
    /// Lee el carrusel del borrador con la fila bloqueada (FOR UPDATE: dos cambios simultáneos se serializan), aplica el cambio en
    /// memoria y reescribe SOLO la clave <c>carousel</c> (no pisa nombre ni colores). <c>null</c> = el cambio no aplicó (no hay fila,
    /// no existe el elemento, orden inválido o carrusel lleno) y la transacción se revierte.
    /// </summary>
    private async Task<PortalMarcaRegistro?> ModificarListaAsync(
        Guid transitOfficeId, Guid actor, string clave, bool crearSiFalta, Func<List<PortalMarcaCarruselItem>, Task<bool>> cambiar, CancellationToken ct)
    {
        var oficina = Oficina(transitOfficeId);
        var ambito = Ambito(transitOfficeId);
        var aplicado = false;

        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            aplicado = false;
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (crearSiFalta)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO admin.transit_office_portal_brandings (scope, transit_office_id, draft, created_by, updated_by, row_version)
                    VALUES ({ambito}, CAST({oficina} AS uuid), jsonb_build_object('schemaVersion', 1), {actor}, {actor}, 1)
                    ON CONFLICT DO NOTHING
                    """, ct).ConfigureAwait(false);

            var json = (await db.Database.SqlQuery<string>($"""
                SELECT draft::text AS "Value" FROM admin.transit_office_portal_brandings
                 WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid) FOR UPDATE
                """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
            if (json is null)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return;
            }

            var items = LeerLista(json, clave);
            if (!await cambiar(items).ConfigureAwait(false))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return;
            }

            var nuevo = SerializarCarrusel(items);
            var quitarLegacy = clave == ClaveBanners;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE admin.transit_office_portal_brandings
                   SET draft = jsonb_set(CASE WHEN {quitarLegacy} THEN draft - 'bannerId' ELSE draft END, ARRAY[CAST({clave} AS text)], CAST({nuevo} AS jsonb), true), updated_at = now(), updated_by = {actor}
                 WHERE transit_office_id IS NOT DISTINCT FROM CAST({oficina} AS uuid)
                """, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            aplicado = true;
        }).ConfigureAwait(false);

        return aplicado ? await ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false) : null;
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
            : new PortalMarcaImagen(fila.Id, fila.TransitOfficeId ?? PortalMarcaAmbito.Web, fila.Kind, fila.Version, fila.ContentType, fila.StoragePath, fila.StorageSha256.Trim(), fila.SizeBytes);
    }

    public async Task<bool> ImagenEstaPublicadaAsync(Guid imagenId, CancellationToken ct = default)
    {
        var texto = imagenId.ToString();
        return (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM admin.transit_office_portal_brandings b
                 WHERE b.published IS NOT NULL
                   AND (b.published ->> 'logoId' = {texto} OR b.published ->> 'bannerId' = {texto}
                        OR jsonb_contains(b.published, jsonb_build_object('banners', jsonb_build_array(jsonb_build_object('imageId', {texto}::text))))
                        OR jsonb_contains(b.published, jsonb_build_object('carousel', jsonb_build_array(jsonb_build_object('imageId', {texto}::text)))))) AS "Value"
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
    }

    // ── Soporte ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>La marca web viaja como <see cref="PortalMarcaAmbito.Web"/> y en la base es una fila sin organismo.</summary>
    private static Guid? Oficina(Guid transitOfficeId) => transitOfficeId == PortalMarcaAmbito.Web ? null : transitOfficeId;

    private const string ClaveCarrusel = "carousel";
    private const string ClaveBanners = "banners";

    private static string Ambito(Guid transitOfficeId) => transitOfficeId == PortalMarcaAmbito.Web ? "web" : "organismo";

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

        var banners = LeerLista(raiz, ClaveBanners);
        return new PortalMarcaSnapshot(Texto(raiz, "platformName"), colores, GuidDe(raiz, "logoId"), banners.Count > 0 ? banners[0].ImageId : null)
        {
            Carousel = LeerLista(raiz, ClaveCarrusel),
            Banners = banners,
        };
    }

    private static List<PortalMarcaCarruselItem> LeerLista(string json, string clave)
    {
        using var doc = JsonDocument.Parse(json);
        return LeerLista(doc.RootElement, clave);
    }

    /// <summary>
    /// Ausente o mal formado = vacío; un elemento sin id válido o sin texto alternativo se descarta. Para <c>banners</c>, si la
    /// clave no existe pero hay un <c>bannerId</c> legacy (marca web o documento sin migrar), ese banner único es el primero.
    /// </summary>
    private static List<PortalMarcaCarruselItem> LeerLista(JsonElement raiz, string clave)
    {
        var items = new List<PortalMarcaCarruselItem>();
        if (raiz.ValueKind != JsonValueKind.Object)
            return items;
        if (!raiz.TryGetProperty(clave, out var arreglo) || arreglo.ValueKind != JsonValueKind.Array)
        {
            if (clave == ClaveBanners && GuidDe(raiz, "bannerId") is { } legacy)
                items.Add(new PortalMarcaCarruselItem(legacy, PortalMarcaSnapshot.BannerAltPorDefecto, null));
            return items;
        }

        foreach (var e in arreglo.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Object && GuidDe(e, "imageId") is { } id && Texto(e, "alt") is { } alt)
                items.Add(new PortalMarcaCarruselItem(id, alt, Texto(e, "linkUrl")));
        }

        return items;
    }

    private static string SerializarCarrusel(IReadOnlyList<PortalMarcaCarruselItem> items)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartArray();
            foreach (var i in items)
            {
                w.WriteStartObject();
                w.WriteString("imageId", i.ImageId.ToString());
                w.WriteString("alt", i.Alt);
                if (i.LinkUrl is not null)
                    w.WriteString("linkUrl", i.LinkUrl);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
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
        public Guid? TransitOfficeId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public int Version { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string StoragePath { get; set; } = string.Empty;
        public string StorageSha256 { get; set; } = string.Empty;
        public int SizeBytes { get; set; }
    }
}
