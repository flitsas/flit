using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>
/// HU #14073 (Feature J, Épica #14021) — contenido administrable del portal sobre las tablas del DDL 165. SQL parametrizado
/// (las tablas no son entidades EF). Lo que se edita es el contenido de TRABAJO; <see cref="PublicarAsync"/> lo congela como
/// una versión inmutable y es lo único que lee el portal (<see cref="PersonaNaturalPortalReader"/>).
/// </summary>
internal sealed class PersonaNaturalContenidoRepository(FlitDbContext db) : IPersonaNaturalContenidoRepository
{
    // ── Preguntas frecuentes ──────────────────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FaqPn>> ListarFaqsAsync(CancellationToken ct = default) =>
        (await FaqsAsync(null, ct).ConfigureAwait(false)).OrderBy(f => f.Orden).ThenBy(f => f.Id).ToList();

    public async Task<FaqPn?> ObtenerFaqAsync(Guid id, CancellationToken ct = default) =>
        (await FaqsAsync(id, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<FaqPn> CrearFaqAsync(string pregunta, string respuesta, bool activa, Guid actor, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.persona_natural_faqs (id, question, answer, sort_order, is_active, created_by)
            VALUES ({id}, {pregunta}, {respuesta}, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM admin.persona_natural_faqs), {activa}, {actor})
            """, ct).ConfigureAwait(false);
        return (await ObtenerFaqAsync(id, ct).ConfigureAwait(false))!;
    }

    public async Task<FaqPn?> ActualizarFaqAsync(Guid id, string pregunta, string respuesta, bool activa, Guid actor, CancellationToken ct = default)
    {
        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.persona_natural_faqs
               SET question = {pregunta}, answer = {respuesta}, is_active = {activa}, updated_at = now(), updated_by = {actor}
             WHERE id = {id}
            """, ct).ConfigureAwait(false);
        return filas == 0 ? null : await ObtenerFaqAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<bool> ReordenarFaqsAsync(IReadOnlyList<Guid> ids, Guid actor, CancellationToken ct = default)
    {
        var existentes = (await FaqsAsync(null, ct).ConfigureAwait(false)).Select(f => f.Id).ToHashSet();
        if (!existentes.SetEquals(ids))
            return false;
        await ReordenarAsync("admin.persona_natural_faqs", ids, actor, ct).ConfigureAwait(false);
        return true;
    }

    // ── Banners ───────────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BannerPn>> ListarBannersAsync(CancellationToken ct = default) =>
        (await BannersAsync(null, ct).ConfigureAwait(false)).OrderBy(b => b.Orden).ThenBy(b => b.Id).ToList();

    public async Task<BannerPn?> ObtenerBannerAsync(Guid id, CancellationToken ct = default) =>
        (await BannersAsync(id, ct).ConfigureAwait(false)).FirstOrDefault();

    public async Task<BannerPn> CrearBannerAsync(NuevoBannerPn b, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(b);
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.persona_natural_banners
                (id, title, alt_text, link_url, image_path, image_sha256, image_content_type, image_bytes, sort_order, valid_from, valid_until, is_active, created_by)
            VALUES ({id}, {b.Titulo}, {b.TextoAlternativo}, {b.EnlaceUrl}::text, {b.ImagenPath}, {b.ImagenSha256}, {b.ImagenContentType}, {b.ImagenBytes},
                    {b.Orden}, {b.VigenteDesde}::timestamptz, {b.VigenteHasta}::timestamptz, {b.Activo}, {actor})
            """, ct).ConfigureAwait(false);
        return (await ObtenerBannerAsync(id, ct).ConfigureAwait(false))!;
    }

    public async Task<BannerPn?> ActualizarBannerAsync(Guid id, CambiosBannerPn c, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        // La imagen nueva es opcional: COALESCE conserva la actual.
        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.persona_natural_banners
               SET title = {c.Titulo}, alt_text = {c.TextoAlternativo}, link_url = {c.EnlaceUrl}::text, sort_order = {c.Orden},
                   valid_from = {c.VigenteDesde}::timestamptz, valid_until = {c.VigenteHasta}::timestamptz, is_active = {c.Activo},
                   image_path = COALESCE({c.ImagenPath}::text, image_path),
                   image_sha256 = COALESCE({c.ImagenSha256}::text, image_sha256),
                   image_content_type = COALESCE({c.ImagenContentType}::text, image_content_type),
                   image_bytes = COALESCE({c.ImagenBytes}::bigint, image_bytes),
                   updated_at = now(), updated_by = {actor}
             WHERE id = {id}
            """, ct).ConfigureAwait(false);
        return filas == 0 ? null : await ObtenerBannerAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<bool> ReordenarBannersAsync(IReadOnlyList<Guid> ids, Guid actor, CancellationToken ct = default)
    {
        var existentes = (await BannersAsync(null, ct).ConfigureAwait(false)).Select(b => b.Id).ToHashSet();
        if (!existentes.SetEquals(ids))
            return false;
        await ReordenarAsync("admin.persona_natural_banners", ids, actor, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<ImagenBannerPn?> ImagenDeTrabajoAsync(Guid id, CancellationToken ct = default) =>
        (await ObtenerBannerAsync(id, ct).ConfigureAwait(false)) is { } b ? new ImagenBannerPn(b.ImagenPath, b.ImagenSha256, b.ImagenContentType) : null;

    // ── Enlaces ───────────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<EnlacePn>> ListarEnlacesAsync(CancellationToken ct = default)
    {
        var filas = await db.Database.SqlQuery<EnlaceFila>($"SELECT code, url, updated_at FROM admin.persona_natural_links").ToListAsync(ct).ConfigureAwait(false);
        return CodigosEnlacePn.Todos
            .Select(codigo => filas.FirstOrDefault(f => f.Code == codigo) is { } f ? new EnlacePn(codigo, f.Url, f.UpdatedAt) : new EnlacePn(codigo, null, null))
            .ToList();
    }

    public async Task<EnlacePn> EstablecerEnlaceAsync(string codigo, string? url, Guid actor, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.persona_natural_links (code, url, created_by) VALUES ({codigo}, {url}::text, {actor})
            ON CONFLICT (code) DO UPDATE SET url = EXCLUDED.url, updated_at = now(), updated_by = {actor}
            """, ct).ConfigureAwait(false);
        return (await ListarEnlacesAsync(ct).ConfigureAwait(false)).First(e => e.Codigo == codigo);
    }

    // ── Publicación ───────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<ContenidoPn> ContenidoPublicableAsync(CancellationToken ct = default)
    {
        var preguntas = (await FaqsAsync(null, ct).ConfigureAwait(false)).Where(f => f.Activa)
            .Select(f => new FaqPublicadaPn(f.Id, f.Pregunta, f.Respuesta, f.Orden)).ToList();
        var banners = (await BannersAsync(null, ct).ConfigureAwait(false)).Where(b => b.Activo)
            .Select(b => new BannerPublicadoPn(
                b.Id, b.Titulo, b.TextoAlternativo, b.EnlaceUrl, b.Orden, b.VigenteDesde, b.VigenteHasta, b.ImagenPath, b.ImagenSha256, b.ImagenContentType))
            .ToList();
        var enlaces = await ListarEnlacesAsync(ct).ConfigureAwait(false);
        return new ContenidoPn(preguntas, banners, enlaces.Select(e => new EnlacePn(e.Codigo, e.Url, null)).ToList());
    }

    public async Task<VersionContenidoPn?> UltimaVersionAsync(CancellationToken ct = default) =>
        (await VersionesAsync(null, ct).ConfigureAwait(false)).OrderByDescending(v => v.Version).FirstOrDefault() is { } fila ? ToVersion(fila) : null;

    public async Task<IReadOnlyList<ResumenVersionContenidoPn>> ListarVersionesAsync(CancellationToken ct = default) =>
        (await VersionesAsync(null, ct).ConfigureAwait(false))
            .OrderByDescending(v => v.Version)
            .Select(ToVersion)
            .Select(v => new ResumenVersionContenidoPn(v.Version, v.PublicadoEn, v.PublicadoPor, v.Contenido.Preguntas.Count, v.Contenido.Banners.Count))
            .ToList();

    public async Task<VersionContenidoPn?> ObtenerVersionAsync(int version, CancellationToken ct = default) =>
        (await VersionesAsync(version, ct).ConfigureAwait(false)).FirstOrDefault() is { } fila ? ToVersion(fila) : null;

    public async Task<VersionContenidoPn> PublicarAsync(Guid actor, CancellationToken ct = default)
    {
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            // Serializa las publicaciones: dos superadministradores no obtienen el mismo número de versión.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended('persona_natural_content_versions', 0))", ct).ConfigureAwait(false);
            var contenido = await ContenidoPublicableAsync(ct).ConfigureAwait(false);
            var json = ContenidoPnSnapshot.Escribir(contenido);
            var version = (await db.Database.SqlQuery<int>($"SELECT COALESCE(MAX(version), 0) + 1 AS \"Value\" FROM admin.persona_natural_content_versions")
                .ToListAsync(ct).ConfigureAwait(false)).Single();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.persona_natural_content_versions (version, content, published_by)
                VALUES ({version}, {json}::jsonb, {actor})
                """, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return (await ObtenerVersionAsync(version, ct).ConfigureAwait(false))!;
        }).ConfigureAwait(false);
    }

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task ReordenarAsync(string tabla, IReadOnlyList<Guid> ids, Guid actor, CancellationToken ct)
    {
        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var posicion = i + 1;
                // La tabla sale de una lista cerrada del propio repositorio (nunca de la entrada): FormattableString la compone como texto.
                if (tabla == "admin.persona_natural_faqs")
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE admin.persona_natural_faqs SET sort_order = {posicion}, updated_at = now(), updated_by = {actor} WHERE id = {id}", ct).ConfigureAwait(false);
                else
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE admin.persona_natural_banners SET sort_order = {posicion}, updated_at = now(), updated_by = {actor} WHERE id = {id}", ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task<List<FaqPn>> FaqsAsync(Guid? id, CancellationToken ct) =>
        (await db.Database.SqlQuery<FaqFila>($"""
            SELECT id, question, answer, sort_order, is_active, row_version, created_at, updated_at
              FROM admin.persona_natural_faqs WHERE {id}::uuid IS NULL OR id = {id}::uuid
            """).ToListAsync(ct).ConfigureAwait(false))
        .Select(f => new FaqPn(f.Id, f.Question, f.Answer, f.SortOrder, f.IsActive, f.RowVersion, f.CreatedAt, f.UpdatedAt)).ToList();

    private async Task<List<BannerPn>> BannersAsync(Guid? id, CancellationToken ct) =>
        (await db.Database.SqlQuery<BannerFila>($"""
            SELECT id, title, alt_text, link_url, sort_order, valid_from, valid_until, is_active, image_path, image_sha256,
                   image_content_type, image_bytes, row_version, created_at, updated_at
              FROM admin.persona_natural_banners WHERE {id}::uuid IS NULL OR id = {id}::uuid
            """).ToListAsync(ct).ConfigureAwait(false))
        .Select(b => new BannerPn(
            b.Id, b.Title, b.AltText, b.LinkUrl, b.SortOrder, b.ValidFrom, b.ValidUntil, b.IsActive, b.ImagePath, b.ImageSha256,
            b.ImageContentType, b.ImageBytes, b.RowVersion, b.CreatedAt, b.UpdatedAt)).ToList();

    private async Task<List<VersionFila>> VersionesAsync(int? version, CancellationToken ct) =>
        await db.Database.SqlQuery<VersionFila>($"""
            SELECT version, content::text AS content, published_at, published_by
              FROM admin.persona_natural_content_versions WHERE {version}::int IS NULL OR version = {version}::int
            """).ToListAsync(ct).ConfigureAwait(false);

    private static VersionContenidoPn ToVersion(VersionFila f) =>
        new(f.Version, f.PublishedAt, f.PublishedBy, ContenidoPnSnapshot.Leer(f.Content));

    private sealed class FaqFila
    {
        public Guid Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public long RowVersion { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class BannerFila
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string AltText { get; set; } = string.Empty;
        public string? LinkUrl { get; set; }
        public int SortOrder { get; set; }
        public DateTimeOffset? ValidFrom { get; set; }
        public DateTimeOffset? ValidUntil { get; set; }
        public bool IsActive { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public string ImageSha256 { get; set; } = string.Empty;
        public string ImageContentType { get; set; } = string.Empty;
        public long ImageBytes { get; set; }
        public long RowVersion { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class EnlaceFila
    {
        public string Code { get; set; } = string.Empty;
        public string? Url { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class VersionFila
    {
        public int Version { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; }
        public Guid? PublishedBy { get; set; }
    }
}
