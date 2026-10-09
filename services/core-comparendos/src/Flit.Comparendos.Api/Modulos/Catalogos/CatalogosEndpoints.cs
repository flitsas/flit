using System.Net.Mail;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Textos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Catalogos;

/// <summary>
/// Catálogos globales (HU #13459): organismos de tránsito con sus correos de radicación [Excel J6], alias de
/// secretarías, fuentes municipales y códigos de infracción. Cualquier usuario del producto los consulta (filtros);
/// solo el SuperAdmin los cambia.
/// </summary>
public static class CatalogosEndpoints
{
    public sealed record OrganismoDto(Guid Id, string Codigo, string Nombre, string? Municipio, string? Departamento,
        IReadOnlyList<string> CorreosRadicacion, bool PermiteCambioContraventor, bool ActivoPlataforma);

    public sealed record CambioOrganismo(IReadOnlyList<string>? CorreosRadicacion, bool? PermiteCambioContraventor);

    public sealed record AliasDto(Guid Id, string Texto, Guid OrganismoTransitoId, string Organismo, string Origen);

    public sealed record NuevoAlias(string Texto, Guid OrganismoTransitoId);

    public sealed record MunicipioDto(Guid Id, string CodigoFuente, string Nombre, Guid? OrganismoTransitoId, string? Organismo, bool Activo);

    public sealed record NuevoMunicipio(string CodigoFuente, string Nombre, Guid? OrganismoTransitoId);

    public sealed record CambioMunicipio(string? Nombre, Guid? OrganismoTransitoId, bool? Activo);

    public sealed record InfraccionDto(string Codigo, string Descripcion, bool Vigente);

    public static void Map(RouteGroupBuilder api)
    {
        var usuario = api.MapGroup("").RequireAuthorization(AutorizacionExtensions.PoliticaUsuario);
        usuario.MapGet("/organismos", ListarOrganismos);
        usuario.MapGet("/infracciones", ListarInfracciones);

        var admin = api.MapGroup("/admin").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        admin.MapPatch("/organismos/{id:guid}", CambiarOrganismo);
        admin.MapGet("/organismos/alias", ListarAlias);
        admin.MapPost("/organismos/alias", CrearAlias);
        admin.MapDelete("/organismos/alias/{id:guid}", BorrarAlias);
        admin.MapGet("/municipios", ListarMunicipios);
        admin.MapPost("/municipios", CrearMunicipio);
        admin.MapPatch("/municipios/{id:guid}", CambiarMunicipio);
        admin.MapPut("/infracciones", CargarInfracciones);
    }

    private static OrganismoDto Dto(OrganismoTransito o) =>
        new(o.Id, o.Codigo, o.Nombre, o.Municipio, o.Departamento, o.CorreosRadicacion, o.PermiteCambioContraventor, o.ActivoPlataforma);

    private static async Task<IResult> ListarOrganismos(string? buscar, bool? sinCorreo, int? pagina, int? tamano, ComparendosDb db, CancellationToken ct)
    {
        var (p, t) = Paginacion.Normalizar(pagina, tamano, 50);
        var q = db.OrganismosTransito.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var texto = $"%{buscar.Trim()}%";
            q = q.Where(o => EF.Functions.ILike(o.Nombre, texto) || EF.Functions.ILike(o.Codigo, texto) || (o.Municipio != null && EF.Functions.ILike(o.Municipio, texto)));
        }

        if (sinCorreo == true)
            q = q.Where(o => o.CorreosRadicacion.Count == 0);

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await q.OrderBy(o => o.Nombre).Skip((p - 1) * t).Take(t).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new Pagina<OrganismoDto>(items.Select(Dto).ToList(), total, p, t));
    }

    private static async Task<IResult> CambiarOrganismo(Guid id, CambioOrganismo pedido, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var ot = await db.OrganismosTransito.FirstOrDefaultAsync(o => o.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El organismo de tránsito");
        var correos = pedido.CorreosRadicacion is null ? ot.CorreosRadicacion : NormalizarCorreos(pedido.CorreosRadicacion);
        var permite = pedido.PermiteCambioContraventor ?? ot.PermiteCambioContraventor;
        var cambios = Auditoria.Diferencias(
            ("correos_radicacion", string.Join(", ", ot.CorreosRadicacion), string.Join(", ", correos)),
            ("permite_cambio_contraventor", ot.PermiteCambioContraventor, permite));
        if (cambios.Length > 0)
        {
            ot.CorreosRadicacion = [.. correos];
            ot.PermiteCambioContraventor = permite;
            ot.ActualizadoEn = reloj.Ahora;
            ot.ActualizadoPor = usuario.Id;
            auditoria.Registrar(null, "organismo_transito", ot.Id, "ACTUALIZAR", cambios);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(ot));
    }

    /// <summary>Correos válidos, en minúsculas, sin repetidos; uno inválido rechaza todo el cambio.</summary>
    public static List<string> NormalizarCorreos(IEnumerable<string> correos)
    {
        ArgumentNullException.ThrowIfNull(correos);
        var lista = new List<string>();
        foreach (var c in correos.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToLowerInvariant()))
        {
            if (!MailAddress.TryCreate(c, out var direccion) || direccion.Address != c || !c.Contains('.', StringComparison.Ordinal))
                throw new ReglaException("CORREO_INVALIDO", $"«{c}» no es un correo válido.");
            if (!lista.Contains(c))
                lista.Add(c);
        }

        return lista;
    }

    private static async Task<IResult> ListarAlias(Guid? organismo, ComparendosDb db, CancellationToken ct)
    {
        var q = from a in db.OrganismosTransitoAlias.AsNoTracking()
                join o in db.OrganismosTransito.AsNoTracking() on a.OrganismoTransitoId equals o.Id
                select new AliasDto(a.Id, a.TextoNormalizado, o.Id, o.Nombre, a.Origen);
        if (organismo is { } id)
            q = q.Where(a => a.OrganismoTransitoId == id);
        return Results.Ok(await q.OrderBy(a => a.Texto).ToListAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> CrearAlias(NuevoAlias pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var texto = TextoNormalizado.De(pedido.Texto);
        if (texto.Length is 0 or > 250)
            throw new ReglaException("ALIAS_INVALIDO", "Escribe el texto de la secretaría tal como lo trae la fuente.");
        if (!await db.OrganismosTransito.AnyAsync(o => o.Id == pedido.OrganismoTransitoId, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El organismo de tránsito");
        if (await db.OrganismosTransitoAlias.AnyAsync(a => a.TextoNormalizado == texto, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("ALIAS_EXISTE", "Ese texto ya está asociado a un organismo.");

        var alias = new OrganismoTransitoAlias { Id = Guid.CreateVersion7(), TextoNormalizado = texto, OrganismoTransitoId = pedido.OrganismoTransitoId, Origen = "MANUAL", CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
        db.OrganismosTransitoAlias.Add(alias);
        auditoria.Registrar(null, "organismo_transito_alias", alias.Id, "CREAR", new CambioCampo("texto", null, texto));

        // Los comparendos que llegaron con ese texto y sin OT quedan asociados ahora.
        var huerfanos = await db.Comparendos.Where(c => c.OrganismoTransitoId == null && c.SecretariaTexto != null).ToListAsync(ct).ConfigureAwait(false);
        foreach (var c in huerfanos.Where(c => TextoNormalizado.De(c.SecretariaTexto) == texto))
            c.OrganismoTransitoId = pedido.OrganismoTransitoId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/admin/organismos/alias/{alias.Id}", alias.Id);
    }

    private static async Task<IResult> BorrarAlias(Guid id, ComparendosDb db, Auditoria auditoria, CancellationToken ct)
    {
        var alias = await db.OrganismosTransitoAlias.FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El alias");
        db.OrganismosTransitoAlias.Remove(alias);
        auditoria.Registrar(null, "organismo_transito_alias", alias.Id, "BORRAR", new CambioCampo("texto", alias.TextoNormalizado, null));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static MunicipioDto Dto(MunicipioFuente m, string? organismo = null) => new(m.Id, m.CodigoFuente, m.Nombre, m.OrganismoTransitoId, organismo, m.Activo);

    private static async Task<IResult> ListarMunicipios(ComparendosDb db, CancellationToken ct)
    {
        var lista = await (from m in db.MunicipiosFuente.AsNoTracking()
                           join o in db.OrganismosTransito.AsNoTracking() on m.OrganismoTransitoId equals o.Id into os
                           from o in os.DefaultIfEmpty()
                           orderby m.Nombre
                           select new { m, organismo = o == null ? null : o.Nombre }).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(lista.Select(x => Dto(x.m, x.organismo)));
    }

    private static async Task<IResult> CrearMunicipio(NuevoMunicipio pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var codigo = (pedido.CodigoFuente ?? string.Empty).Trim();
        if (codigo.Length is 0 or > 60 || string.IsNullOrWhiteSpace(pedido.Nombre))
            throw new ReglaException("MUNICIPIO_INVALIDO", "El código de la fuente y el nombre son obligatorios.");
        if (await db.MunicipiosFuente.AnyAsync(m => m.CodigoFuente == codigo, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("MUNICIPIO_EXISTE", "Ese código de fuente ya está en el catálogo.");

        var m = new MunicipioFuente { Id = Guid.CreateVersion7(), CodigoFuente = codigo, Nombre = pedido.Nombre.Trim(), OrganismoTransitoId = pedido.OrganismoTransitoId, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
        db.MunicipiosFuente.Add(m);
        auditoria.Registrar(null, "municipio_fuente", m.Id, "CREAR", new CambioCampo("codigo_fuente", null, codigo));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/admin/municipios/{m.Id}", Dto(m));
    }

    private static async Task<IResult> CambiarMunicipio(Guid id, CambioMunicipio pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var m = await db.MunicipiosFuente.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El municipio");
        var nombre = string.IsNullOrWhiteSpace(pedido.Nombre) ? m.Nombre : pedido.Nombre.Trim();
        var ot = pedido.OrganismoTransitoId ?? m.OrganismoTransitoId;
        var activo = pedido.Activo ?? m.Activo;
        var cambios = Auditoria.Diferencias(("nombre", m.Nombre, nombre), ("organismo_transito_id", m.OrganismoTransitoId, ot), ("activo", m.Activo, activo));
        if (cambios.Length > 0)
        {
            (m.Nombre, m.OrganismoTransitoId, m.Activo, m.ActualizadoEn, m.ActualizadoPor) = (nombre, ot, activo, reloj.Ahora, usuario.Id);
            auditoria.Registrar(null, "municipio_fuente", m.Id, pedido.Activo is null ? "ACTUALIZAR" : activo ? "ACTIVAR" : "DESACTIVAR", cambios);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(m));
    }

    private static async Task<IResult> ListarInfracciones(string? buscar, ComparendosDb db, CancellationToken ct)
    {
        var q = db.InfraccionesCatalogo.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var texto = $"%{buscar.Trim()}%";
            q = q.Where(i => EF.Functions.ILike(i.Codigo, texto) || EF.Functions.ILike(i.Descripcion, texto));
        }

        return Results.Ok(await q.OrderBy(i => i.Codigo).Select(i => new InfraccionDto(i.Codigo, i.Descripcion, i.Vigente)).ToListAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Carga o actualiza códigos de infracción (el listado oficial no viene en ninguna fuente, §9.3-Q10).</summary>
    private static async Task<IResult> CargarInfracciones(IReadOnlyList<InfraccionDto> infracciones, ComparendosDb db, Auditoria auditoria, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(infracciones);
        var existentes = await db.InfraccionesCatalogo.ToDictionaryAsync(i => i.Codigo, ct).ConfigureAwait(false);
        var nuevos = 0;
        foreach (var i in infracciones.Where(i => !string.IsNullOrWhiteSpace(i.Codigo) && !string.IsNullOrWhiteSpace(i.Descripcion)))
        {
            var codigo = i.Codigo.Trim().ToUpperInvariant();
            if (codigo.Length > 10)
                throw new ReglaException("CODIGO_INVALIDO", $"El código «{codigo}» supera 10 caracteres.");
            if (existentes.TryGetValue(codigo, out var actual))
            {
                actual.Descripcion = i.Descripcion.Trim();
                actual.Vigente = i.Vigente;
            }
            else
            {
                db.InfraccionesCatalogo.Add(new InfraccionCatalogo { Codigo = codigo, Descripcion = i.Descripcion.Trim(), Vigente = i.Vigente });
                existentes[codigo] = null!;
                nuevos++;
            }
        }

        auditoria.Registrar(null, "infraccion_catalogo", "lote", "CARGAR", new CambioCampo("registros", null, infracciones.Count));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { nuevos, total = infracciones.Count });
    }
}
