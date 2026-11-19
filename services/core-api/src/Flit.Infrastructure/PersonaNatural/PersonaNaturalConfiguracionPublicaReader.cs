using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>
/// Lo que el portal puede ver (HU #14073, RPC <c>ObtenerConfiguracionPortal</c>): la última versión PUBLICADA del contenido,
/// con los banners ya filtrados por su vigencia, más el contacto de soporte y los parámetros públicos. Nunca trae el contenido
/// de trabajo (borrador), datos bancarios ni parámetros de protección (topes, captcha, sesión).
/// </summary>
public sealed record ConfiguracionPublicaPn(
    int Version,
    DateTimeOffset? PublicadoEn,
    IReadOnlyList<FaqPublicadaPn> Preguntas,
    IReadOnlyList<BannerPublicadoPn> Banners,
    IReadOnlyList<EnlacePn> Enlaces,
    string? SoporteCorreo,
    string? SoporteTelefono,
    string? SoporteHorario,
    int? BorradorVigenciaDias);

/// <summary>Lectura de solo lectura de la configuración pública del portal (aditiva: no toca nada existente).</summary>
public sealed class PersonaNaturalConfiguracionPublicaReader(FlitDbContext db, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    /// <summary>Contenido publicado y vigente ahora. Sin versión publicada: versión 0, listas vacías y los cuatro enlaces sin valor.</summary>
    public async Task<ConfiguracionPublicaPn> LeerAsync(CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow();
        var fila = (await db.Database.SqlQuery<VersionFila>($"""
            SELECT version, content::text AS content, published_at
              FROM admin.persona_natural_content_versions ORDER BY version DESC LIMIT 1
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        var ajustes = (await db.Database.SqlQuery<AjustesFila>($"""
            SELECT support_email, support_phone, support_hours, draft_validity_days FROM admin.persona_natural_settings LIMIT 1
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();

        if (fila is null)
            return new ConfiguracionPublicaPn(
                0, null, [], [], CodigosEnlacePn.Todos.Select(c => new EnlacePn(c, null, null)).ToList(),
                ajustes?.SupportEmail, ajustes?.SupportPhone, ajustes?.SupportHours, ajustes?.DraftValidityDays);

        var contenido = ContenidoPnSnapshot.Leer(fila.Content);
        var enlaces = CodigosEnlacePn.Todos
            .Select(c => contenido.Enlaces.FirstOrDefault(e => e.Codigo == c) ?? new EnlacePn(c, null, null))
            .ToList();
        return new ConfiguracionPublicaPn(
            fila.Version,
            fila.PublishedAt,
            contenido.Preguntas.OrderBy(p => p.Orden).ThenBy(p => p.Id).ToList(),
            contenido.Banners.Where(b => EstaVigente(b, ahora)).OrderBy(b => b.Orden).ThenBy(b => b.Id).ToList(),
            enlaces,
            ajustes?.SupportEmail, ajustes?.SupportPhone, ajustes?.SupportHours, ajustes?.DraftValidityDays);
    }

    /// <summary>Banner publicado y vigente ahora (para servir su imagen). <c>null</c> si no está en la última versión o venció.</summary>
    public async Task<BannerPublicadoPn?> BannerVigenteAsync(Guid id, CancellationToken ct)
    {
        var fila = (await db.Database.SqlQuery<VersionFila>($"""
            SELECT version, content::text AS content, published_at
              FROM admin.persona_natural_content_versions ORDER BY version DESC LIMIT 1
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        if (fila is null)
            return null;
        var ahora = _reloj.GetUtcNow();
        return ContenidoPnSnapshot.Leer(fila.Content).Banners.FirstOrDefault(b => b.Id == id && EstaVigente(b, ahora));
    }

    /// <summary>Sin fechas = siempre; con fechas, dentro de [desde, hasta].</summary>
    private static bool EstaVigente(BannerPublicadoPn b, DateTimeOffset ahora) =>
        (b.VigenteDesde is null || b.VigenteDesde <= ahora) && (b.VigenteHasta is null || b.VigenteHasta >= ahora);

    private sealed class VersionFila
    {
        public int Version { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; }
    }

    private sealed class AjustesFila
    {
        public string? SupportEmail { get; set; }
        public string? SupportPhone { get; set; }
        public string? SupportHours { get; set; }
        public int? DraftValidityDays { get; set; }
    }
}
