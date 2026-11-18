using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plantillas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Plantillas;

/// <summary>
/// Visor y editor de plantillas (HU #13483; Excel J2, J3; §7.16). El visor lo usa quien tenga
/// <c>comparendos.plantillas.ver</c> (la empresa ve FLIT y su conjunto); el editor es del SuperAdmin: borradores,
/// vista previa y publicación con nota. Una versión publicada es inmutable; publicar retira la anterior y los DP y
/// correos ya enviados conservan la versión con que se armaron.
/// </summary>
public static class PlantillasEndpoints
{
    public sealed record VersionResumen(Guid Id, int NumeroVersion, string Estado, string? NotaCambio, DateTimeOffset CreadoEn, DateTimeOffset? PublicadaEn);

    public sealed record PlantillaDto(Guid Id, string Conjunto, string Tipo, Guid? TipologiaId, string? Tipologia, string Nombre, bool Activo, VersionResumen? Publicada);

    public sealed record VersionDto(Guid Id, Guid PlantillaId, int NumeroVersion, string Estado, string? Asunto, string CuerpoHtml, IReadOnlyList<string> VariablesUsadas, string? NotaCambio, DateTimeOffset? PublicadaEn, DateTimeOffset? RevisadaJuridicamenteEn = null,
        string? RevisadaPor = null, string? NotaRevisionJuridica = null);

    public sealed record VistaPrevia(string Tipo, string? Asunto, string CuerpoHtml, Guid? ComparendoId, string? Formato, string? Conjunto = null);

    public sealed record NuevaPlantilla(string Conjunto, string Tipo, Guid? TipologiaId);

    public sealed record DatosVersion(string? Asunto, string CuerpoHtml, string? NotaCambio);

    public sealed record Publicacion(string Nota);

    /// <summary>HU #14614 / #14612: la nota del abogado al aprobar el texto de una versión publicada.</summary>
    public sealed record RevisionJuridica(string Nota);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/plantillas", Listar).RequirePermiso(Permisos.PlantillasVer);
        api.MapGet("/plantillas/variables", (Func<IResult>)(() => Results.Ok(VariablesPlantilla.Todas))).RequirePermiso(Permisos.PlantillasVer);
        api.MapGet("/plantillas/{id:guid}", Detalle).RequirePermiso(Permisos.PlantillasVer);
        api.MapGet("/plantillas/versiones/{id:guid}", Version).RequirePermiso(Permisos.PlantillasVer);
        api.MapPost("/plantillas/vista-previa", Previsualizar).RequirePermiso(Permisos.PlantillasVer);

        var admin = api.MapGroup("/admin/plantillas").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        admin.MapPost("", Crear);
        admin.MapPost("/{id:guid}/versiones", NuevaVersion);
        admin.MapPut("/versiones/{id:guid}", EditarBorrador);
        admin.MapPost("/versiones/{id:guid}/publicar", Publicar);
        admin.MapPost("/versiones/{id:guid}/revision-juridica", RevisarJuridicamente);
    }

    private static VersionDto Dto(PlantillaVersion v) =>
        new(v.Id, v.PlantillaId, v.NumeroVersion, v.Estado.ToString(), v.Asunto, v.CuerpoHtml, v.VariablesUsadas, v.NotaCambio, v.PublicadaEn, v.RevisadaJuridicamenteEn, v.RevisadaJuridicamentePorNombre, v.NotaRevisionJuridica);

    /// <summary>§7.16: la gestora ve FLIT y su propio conjunto; el SuperAdmin, todos.</summary>
    private static async Task<List<ConjuntoPlantillas>> PermitidosAsync(UsuarioActual usuario, Capacidades capacidades, CancellationToken ct)
    {
        var permitidos = new List<ConjuntoPlantillas> { ConjuntoPlantillas.Flit };
        if (usuario.EsSuperAdmin)
            permitidos.Add(ConjuntoPlantillas.Renting);
        else if (usuario.EmpresaId is { } e)
            permitidos.Add((await capacidades.DeAsync(e, ct).ConfigureAwait(false)).ConjuntoPlantillas);
        return permitidos;
    }

    private static async Task<IResult> Listar(string? conjunto, string? tipo, UsuarioActual usuario, ComparendosDb db, Capacidades capacidades, CancellationToken ct)
    {
        var permitidos = await PermitidosAsync(usuario, capacidades, ct).ConfigureAwait(false);

        var q = db.Plantillas.AsNoTracking().Where(p => permitidos.Contains(p.Conjunto));
        if (Enum.TryParse<ConjuntoPlantillas>(conjunto, true, out var c))
            q = q.Where(p => p.Conjunto == c);
        if (Enum.TryParse<TipoPlantilla>(tipo, true, out var t))
            q = q.Where(p => p.Tipo == t);

        var lista = await (from p in q
                           join tp in db.Tipologias.AsNoTracking() on p.TipologiaId equals tp.Id into tps
                           from tp in tps.DefaultIfEmpty()
                           join v in db.PlantillaVersiones.AsNoTracking() on p.VersionPublicadaId equals v.Id into vs
                           from v in vs.DefaultIfEmpty()
                           orderby p.Conjunto, p.Tipo, tp.Codigo
                           select new PlantillaDto(p.Id, p.Conjunto.ToString(), p.Tipo.ToString(), p.TipologiaId, tp == null ? null : tp.Codigo + " · " + tp.Nombre, p.Nombre, p.Activo,
                               v == null ? null : new VersionResumen(v.Id, v.NumeroVersion, v.Estado.ToString(), v.NotaCambio, v.CreadoEn, v.PublicadaEn)))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(lista);
    }

    private static async Task<IResult> Detalle(Guid id, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, CancellationToken ct)
    {
        var permitidos = await PermitidosAsync(usuario, capacidades, ct).ConfigureAwait(false);
        var p = await db.Plantillas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && permitidos.Contains(x.Conjunto), ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La plantilla");
        var versiones = await db.PlantillaVersiones.AsNoTracking().Where(v => v.PlantillaId == id).OrderByDescending(v => v.NumeroVersion)
            .Select(v => new VersionResumen(v.Id, v.NumeroVersion, v.Estado.ToString(), v.NotaCambio, v.CreadoEn, v.PublicadaEn)).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { p.Id, conjunto = p.Conjunto.ToString(), tipo = p.Tipo.ToString(), p.TipologiaId, p.Nombre, p.VersionPublicadaId, versiones });
    }

    private static async Task<IResult> Version(Guid id, UsuarioActual usuario, Capacidades capacidades, ComparendosDb db, CancellationToken ct)
    {
        var permitidos = await PermitidosAsync(usuario, capacidades, ct).ConfigureAwait(false);
        var v = await (from x in db.PlantillaVersiones.AsNoTracking()
                       join p in db.Plantillas.AsNoTracking() on x.PlantillaId equals p.Id
                       where x.Id == id && permitidos.Contains(p.Conjunto)
                       select x).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return Results.Ok(Dto(v ?? throw ReglaException.NoEncontrado("La versión")));
    }

    /// <summary>Vista previa con datos de ejemplo o de un comparendo real de la empresa del usuario; HTML o PDF.</summary>
    private static async Task<IResult> Previsualizar(VistaPrevia pedido, ServicioPlantillas plantillas, UsuarioActual usuario, AccesoComparendo acceso, ContextoPlantilla contexto, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoPlantilla>(pedido.Tipo, true, out var tipo))
            throw new ReglaException("TIPO_INVALIDO", "Tipo de plantilla desconocido.");
        var validacion = MotorPlantillas.Validar(pedido.Asunto, pedido.CuerpoHtml);
        var valores = pedido.ComparendoId is { } id
            ? await contexto.DeAsync(await acceso.ParaLeerAsync(id, ct).ConfigureAwait(false), null, ct).ConfigureAwait(false)
            : ContextoPlantilla.Ejemplo();
        var conjunto = Enum.TryParse<ConjuntoPlantillas>(pedido.Conjunto, true, out var c) ? c : ConjuntoPlantillas.Flit;
        var armado = ServicioPlantillas.Armar(new PlantillaVersion { Asunto = pedido.Asunto, CuerpoHtml = pedido.CuerpoHtml ?? string.Empty }, tipo, valores, conjunto, plantillas.Recursos);
        // HU #14616: sin comparendo el documento sale con datos de EJEMPLO y así se dice (marca de agua en el PDF, banda en el correo).
        var ejemplo = pedido.ComparendoId is null;
        if (string.Equals(pedido.Formato, "pdf", StringComparison.OrdinalIgnoreCase) && !ServicioPlantillas.EsCorreo(tipo))
            return Results.File(ServicioPlantillas.Pdf(armado, ejemplo ? "VISTA PREVIA · DATOS DE EJEMPLO" : "VISTA PREVIA"), "application/pdf");
        var html = ejemplo ? ConBandaDeEjemplo(armado.Html) : armado.Html;
        return Results.Ok(new { armado.Asunto, Html = html, desconocidas = validacion.Desconocidas, ejemplo });
    }

    private static string ConBandaDeEjemplo(string html)
    {
        const string banda = "<div style=\"background:#FFF4CE;color:#7A5A00;padding:8px 12px;font:bold 12px Arial,sans-serif;text-align:center\">VISTA PREVIA CON DATOS DE EJEMPLO</div>";
        var cuerpo = System.Text.RegularExpressions.Regex.Match(html, "<body[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return cuerpo.Success ? html.Insert(cuerpo.Index + cuerpo.Length, banda) : banda + html;
    }

    private static async Task<IResult> Crear(NuevaPlantilla pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, ServicioPlantillas servicio, CancellationToken ct)
    {
        if (!Enum.TryParse<ConjuntoPlantillas>(pedido.Conjunto, true, out var conjunto) || !Enum.TryParse<TipoPlantilla>(pedido.Tipo, true, out var tipo))
            throw new ReglaException("PLANTILLA_INVALIDA", "Elige el conjunto (Flit o Renting) y el tipo de plantilla.");
        if (tipo == TipoPlantilla.DpDocumento != pedido.TipologiaId is not null)
            throw new ReglaException("TIPOLOGIA_REQUERIDA", "El documento del DP va por tipología; los demás tipos no llevan tipología.");
        if (await db.Plantillas.AnyAsync(p => p.Conjunto == conjunto && p.Tipo == tipo && p.TipologiaId == pedido.TipologiaId, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("PLANTILLA_EXISTE", "Ya existe esa plantilla: crea una nueva versión.");

        // Nace con un borrador copiado de la versión vigente de FLIT, para que el editor parta de algo.
        var base_ = await servicio.PublicadaAsync(ConjuntoPlantillas.Flit, tipo, pedido.TipologiaId, ct).ConfigureAwait(false);
        var p = new Plantilla { Id = Guid.CreateVersion7(), Conjunto = conjunto, Tipo = tipo, TipologiaId = pedido.TipologiaId, Nombre = tipo.ToString(), CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
        db.Plantillas.Add(p);
        var v = new PlantillaVersion
        {
            Id = Guid.CreateVersion7(), PlantillaId = p.Id, NumeroVersion = 1, Asunto = base_?.Asunto, CuerpoHtml = base_?.CuerpoHtml ?? "<p></p>",
            VariablesUsadas = [.. base_?.VariablesUsadas ?? []], NotaCambio = "Copia de la versión vigente de FLIT.", CreadoEn = reloj.Ahora, CreadoPor = usuario.Id,
        };
        db.PlantillaVersiones.Add(v);
        auditoria.Registrar(null, "plantilla", p.Id, "CREAR", new CambioCampo("plantilla", null, $"{conjunto} {tipo}"));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/plantillas/{p.Id}", Dto(v));
    }

    private static async Task<IResult> NuevaVersion(Guid id, DatosVersion d, ComparendosDb db, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        if (!await db.Plantillas.AnyAsync(p => p.Id == id, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("La plantilla");
        Validar(d);
        var numero = (await db.PlantillaVersiones.Where(v => v.PlantillaId == id).MaxAsync(v => (int?)v.NumeroVersion, ct).ConfigureAwait(false) ?? 0) + 1;
        var v = new PlantillaVersion
        {
            Id = Guid.CreateVersion7(), PlantillaId = id, NumeroVersion = numero, Asunto = d.Asunto, CuerpoHtml = d.CuerpoHtml,
            VariablesUsadas = [.. MotorPlantillas.Validar(d.Asunto, d.CuerpoHtml).Usadas], NotaCambio = d.NotaCambio, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id,
        };
        db.PlantillaVersiones.Add(v);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/plantillas/versiones/{v.Id}", Dto(v));
    }

    private static async Task<IResult> EditarBorrador(Guid id, DatosVersion d, ComparendosDb db, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var v = await db.PlantillaVersiones.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La versión");
        if (v.Estado != EstadoPlantillaVersion.Borrador)
            throw ReglaException.Conflicto("VERSION_INMUTABLE", "Una versión publicada no se edita: crea una nueva versión.");
        Validar(d);
        (v.Asunto, v.CuerpoHtml, v.NotaCambio, v.ActualizadoEn, v.ActualizadoPor) = (d.Asunto, d.CuerpoHtml, d.NotaCambio, reloj.Ahora, usuario.Id);
        v.VariablesUsadas = [.. MotorPlantillas.Validar(d.Asunto, d.CuerpoHtml).Usadas];
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Dto(v));
    }

    private static async Task<IResult> Publicar(Guid id, Publicacion pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Nota))
            throw new ReglaException("NOTA_REQUERIDA", "Escribe qué cambió en esta versión.");
        var v = await db.PlantillaVersiones.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La versión");
        if (v.Estado != EstadoPlantillaVersion.Borrador)
            throw ReglaException.Conflicto("VERSION_INMUTABLE", "Esa versión ya fue publicada o retirada.");
        var validacion = MotorPlantillas.Validar(v.Asunto, v.CuerpoHtml);
        if (!validacion.Valida)
            throw new ReglaException("VARIABLES_DESCONOCIDAS", $"La plantilla usa variables que no existen: {string.Join(", ", validacion.Desconocidas.Select(x => "{{" + x + "}}"))}.");

        var p = await db.Plantillas.FirstAsync(x => x.Id == v.PlantillaId, ct).ConfigureAwait(false);
        var numeroAnterior = p.VersionPublicadaId is { } previa
            ? await db.PlantillaVersiones.Where(x => x.Id == previa).Select(x => (int?)x.NumeroVersion).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        // Retirar la anterior y publicar la nueva en el mismo SaveChanges: nunca queda la plantilla sin versión publicada.
        if (p.VersionPublicadaId is { } anterior && await db.PlantillaVersiones.FirstOrDefaultAsync(x => x.Id == anterior, ct).ConfigureAwait(false) is { } previaVersion)
            previaVersion.Estado = EstadoPlantillaVersion.Retirada;
        (v.Estado, v.PublicadaEn, v.PublicadaPor, v.NotaCambio, v.VariablesUsadas) = (EstadoPlantillaVersion.Publicada, reloj.Ahora, usuario.Id, pedido.Nota.Trim(), [.. validacion.Usadas]);
        p.VersionPublicadaId = v.Id;
        auditoria.Registrar(null, "plantilla", p.Id, "PUBLICAR", new CambioCampo("version_publicada", numeroAnterior, v.NumeroVersion));

        // HU #14614: un texto nuevo nace sin revisión jurídica, así que ninguna empresa sigue enviando en automático con él.
        // Se apaga para todas las que tienen la tipología (conservador: apagar de más es seguro; encender, lo decide el SuperAdmin).
        if (p.Tipo == TipoPlantilla.DpDocumento && p.TipologiaId is { } tipologiaId)
        {
            var automatizadas = await db.TipologiasEmpresa.Where(h => h.TipologiaId == tipologiaId && h.Automatizable).ToListAsync(ct).ConfigureAwait(false);
            foreach (var h in automatizadas)
            {
                (h.Automatizable, h.ActualizadoEn, h.ActualizadoPor) = (false, reloj.Ahora, usuario.Id);
                auditoria.Registrar(h.TenantId, "tipologia_empresa", tipologiaId, "DESAUTOMATIZAR", new CambioCampo("automatizable", true, false));
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Dto(v));
    }

    /// <summary>
    /// HU #14614 / #14612: un abogado aprueba el texto de una versión publicada. Solo las versiones publicadas se aprueban
    /// (el cuerpo no cambia: una versión nueva nace sin aprobación) y la nota es obligatoria. Sin esta aprobación la
    /// tipología no puede enviarse en automático.
    /// </summary>
    private static async Task<IResult> RevisarJuridicamente(Guid id, RevisionJuridica pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Nota) || pedido.Nota.Trim().Length > 1000)
            throw new ReglaException("NOTA_REQUERIDA", "Escribe la nota de la revisión jurídica (hasta 1.000 caracteres).");
        var v = await db.PlantillaVersiones.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La versión");
        if (v.Estado != EstadoPlantillaVersion.Publicada)
            throw ReglaException.Conflicto("VERSION_NO_PUBLICADA", "Solo se aprueba una versión publicada: publícala primero.");
        // Aprobar de nuevo una versión ya aprobada conserva la primera aprobación (fecha, quién y nota): no se reescribe la historia.
        if (v.RevisadaJuridicamenteEn is not null)
            return Results.Ok(Dto(v));
        (v.RevisadaJuridicamenteEn, v.RevisadaJuridicamentePor, v.RevisadaJuridicamentePorNombre, v.NotaRevisionJuridica, v.ActualizadoEn, v.ActualizadoPor) = (reloj.Ahora, usuario.Id, usuario.Nombre, pedido.Nota.Trim(), reloj.Ahora, usuario.Id);
        auditoria.Registrar(null, "plantilla", v.PlantillaId, "REVISION_JURIDICA", new CambioCampo("revisada_juridicamente_en", null, reloj.Ahora), new CambioCampo("version_revisada", null, v.NumeroVersion));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Dto(v));
    }

    private static void Validar(DatosVersion d)
    {
        if (string.IsNullOrWhiteSpace(d.CuerpoHtml) || d.CuerpoHtml.Length > 200_000)
            throw new ReglaException("CUERPO_INVALIDO", "El cuerpo de la plantilla es obligatorio (hasta 200.000 caracteres).");
        if (d.Asunto is { Length: > 300 })
            throw new ReglaException("ASUNTO_INVALIDO", "El asunto admite hasta 300 caracteres.");
    }
}
