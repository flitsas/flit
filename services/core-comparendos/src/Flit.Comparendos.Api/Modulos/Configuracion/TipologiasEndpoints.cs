using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Plazos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Configuracion;

/// <summary>
/// Tipologías de DP (HU #13474; ADO #12977, §7.15). El catálogo es un dato, no código: el SuperAdmin agrega, edita,
/// desactiva y reactiva sin desplegar (RN-130) y decide cuáles usa cada empresa (RN-131). Cada tipología declara en
/// qué estados legales procede [Excel C13 propuesta]; el código y el prefijo no cambian si ya hay DPs enviados.
/// </summary>
public static class TipologiasEndpoints
{
    private static readonly string[] EstadosValidos = [nameof(EstadoLegal.EnTiempo), nameof(EstadoLegal.Vencido), nameof(EstadoLegal.Caducado), nameof(EstadoLegal.Prescrito)];
    private static readonly string[] AdjuntosValidos = ["Cedula", "CertificadoRepresentacion", "EscrituraPoder"];

    public sealed record TipologiaDto(Guid Id, string Codigo, string Nombre, string DescripcionJuridica, string PrefijoRadicado, IReadOnlyList<string> EstadosLegalesPermitidos,
        bool PermiteMasivo, int PlazoRespuestaOtDiasHabiles, IReadOnlyList<string> AdjuntosRequeridos, IReadOnlyList<string> AliasV2, bool Activo, bool? Habilitada, int? EmpresasHabilitadas);

    public sealed record DatosTipologia(string Codigo, string Nombre, string DescripcionJuridica, string PrefijoRadicado, IReadOnlyList<string> EstadosLegalesPermitidos,
        bool PermiteMasivo, int PlazoRespuestaOtDiasHabiles, IReadOnlyList<string>? AdjuntosRequeridos, bool? Activo);

    public sealed record Habilitacion(bool Habilitada);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/tipologias", DeLaEmpresa).RequireAuthorization(AutorizacionExtensions.PoliticaUsuario);
        var admin = api.MapGroup("/admin").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        admin.MapGet("/tipologias", Catalogo);
        admin.MapPost("/tipologias", Crear);
        admin.MapPut("/tipologias/{id:guid}", Editar);
        admin.MapPut("/empresas/{empresa:guid}/tipologias/{id:guid}", Habilitar);
    }

    private static TipologiaDto Dto(TipologiaDp t, bool? habilitada = null, int? empresas = null) => new(
        t.Id, t.Codigo, t.Nombre, t.DescripcionJuridica, t.PrefijoRadicado, t.EstadosLegalesPermitidos, t.PermiteMasivo, t.PlazoRespuestaOtDiasHabiles,
        t.AdjuntosRequeridos, t.AliasV2, t.Activo, habilitada, empresas);

    /// <summary>El catálogo con la marca de cuáles tiene habilitadas la empresa (la del usuario, o la que elija el SuperAdmin).</summary>
    private static async Task<IResult> DeLaEmpresa(Guid? empresa, bool? soloHabilitadas, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa);
        var habilitadas = await db.TipologiasEmpresa.AsNoTracking().Where(h => h.TenantId == tenant && h.Habilitada).Select(h => h.TipologiaId).ToListAsync(ct).ConfigureAwait(false);
        var catalogo = await db.Tipologias.AsNoTracking().Where(t => t.Activo).OrderBy(t => t.Codigo).ToListAsync(ct).ConfigureAwait(false);
        var lista = catalogo.Select(t => Dto(t, habilitadas.Contains(t.Id))).Where(t => soloHabilitadas != true || t.Habilitada == true);
        return Results.Ok(lista);
    }

    private static async Task<IResult> Catalogo(Guid? empresa, ComparendosDb db, CancellationToken ct)
    {
        var conteo = await db.TipologiasEmpresa.AsNoTracking().Where(h => h.Habilitada).GroupBy(h => h.TipologiaId).Select(g => new { g.Key, n = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.n, ct).ConfigureAwait(false);
        var habilitadas = empresa is { } e
            ? await db.TipologiasEmpresa.AsNoTracking().Where(h => h.TenantId == e && h.Habilitada).Select(h => h.TipologiaId).ToListAsync(ct).ConfigureAwait(false)
            : null;
        var lista = await db.Tipologias.AsNoTracking().OrderBy(t => t.Codigo).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(lista.Select(t => Dto(t, habilitadas?.Contains(t.Id), conteo.GetValueOrDefault(t.Id))));
    }

    private static (string Codigo, string Prefijo, List<string> Estados, List<string> Adjuntos) Validar(DatosTipologia d)
    {
        var codigo = (d.Codigo ?? string.Empty).Trim().ToUpperInvariant();
        var prefijo = (d.PrefijoRadicado ?? string.Empty).Trim().ToUpperInvariant();
        if (codigo.Length is 0 or > 20 || !codigo.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new ReglaException("CODIGO_INVALIDO", "El código admite letras, números y guiones (hasta 20).");
        if (prefijo.Length is 0 or > 6 || !prefijo.All(char.IsAsciiLetterUpper))
            throw new ReglaException("PREFIJO_INVALIDO", "El prefijo del radicado son de 1 a 6 letras.");
        if (string.IsNullOrWhiteSpace(d.Nombre) || string.IsNullOrWhiteSpace(d.DescripcionJuridica))
            throw new ReglaException("TIPOLOGIA_INCOMPLETA", "El nombre y la descripción jurídica son obligatorios.");
        var estados = (d.EstadosLegalesPermitidos ?? []).Distinct().ToList();
        if (estados.Count == 0 || estados.Any(e => !EstadosValidos.Contains(e)))
            throw new ReglaException("ESTADOS_INVALIDOS", "Elige al menos un estado legal en que procede: EnTiempo, Vencido, Caducado o Prescrito.");
        var adjuntos = (d.AdjuntosRequeridos ?? []).Distinct().ToList();
        if (adjuntos.Any(a => !AdjuntosValidos.Contains(a)))
            throw new ReglaException("ADJUNTOS_INVALIDOS", "Los adjuntos posibles son Cedula, CertificadoRepresentacion y EscrituraPoder.");
        if (d.PlazoRespuestaOtDiasHabiles is < 1 or > 90)
            throw new ReglaException("PLAZO_INVALIDO", "El plazo de respuesta del OT va de 1 a 90 días hábiles.");
        return (codigo, prefijo, estados, adjuntos);
    }

    private static async Task<IResult> Crear(DatosTipologia d, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var (codigo, prefijo, estados, adjuntos) = Validar(d);
        if (await db.Tipologias.AnyAsync(t => t.Codigo == codigo, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("CODIGO_EXISTE", $"Ya existe la tipología {codigo}.");
        var t = new TipologiaDp
        {
            Id = Guid.CreateVersion7(),
            Codigo = codigo,
            Nombre = d.Nombre.Trim(),
            DescripcionJuridica = d.DescripcionJuridica.Trim(),
            PrefijoRadicado = prefijo,
            EstadosLegalesPermitidos = estados,
            PermiteMasivo = d.PermiteMasivo,
            PlazoRespuestaOtDiasHabiles = (short)d.PlazoRespuestaOtDiasHabiles,
            AdjuntosRequeridos = adjuntos,
            Activo = d.Activo ?? true,
            CreadoEn = reloj.Ahora,
            CreadoPor = usuario.Id,
        };
        db.Tipologias.Add(t);
        auditoria.Registrar(null, "tipologia_dp", t.Id, "CREAR", new CambioCampo("codigo", null, codigo));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/admin/tipologias/{t.Id}", Dto(t));
    }

    private static async Task<IResult> Editar(Guid id, DatosTipologia d, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var t = await db.Tipologias.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La tipología");
        var (codigo, prefijo, estados, adjuntos) = Validar(d);
        if ((codigo != t.Codigo || prefijo != t.PrefijoRadicado) && await db.DerechosPeticion.AnyAsync(x => x.TipologiaId == id, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("TIPOLOGIA_CON_DP", "El código y el prefijo no se pueden cambiar: ya hay derechos de petición enviados con esta tipología.");
        if (codigo != t.Codigo && await db.Tipologias.AnyAsync(x => x.Codigo == codigo, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("CODIGO_EXISTE", $"Ya existe la tipología {codigo}.");

        var cambios = Auditoria.Diferencias(("codigo", t.Codigo, codigo), ("nombre", t.Nombre, d.Nombre.Trim()), ("descripcion_juridica", t.DescripcionJuridica, d.DescripcionJuridica.Trim()),
            ("prefijo_radicado", t.PrefijoRadicado, prefijo), ("estados_legales_permitidos", string.Join(",", t.EstadosLegalesPermitidos), string.Join(",", estados)),
            ("permite_masivo", t.PermiteMasivo, d.PermiteMasivo), ("plazo_respuesta_ot", (int)t.PlazoRespuestaOtDiasHabiles, d.PlazoRespuestaOtDiasHabiles),
            ("adjuntos_requeridos", string.Join(",", t.AdjuntosRequeridos), string.Join(",", adjuntos)), ("activo", t.Activo, d.Activo ?? t.Activo));
        if (cambios.Length > 0)
        {
            (t.Codigo, t.Nombre, t.DescripcionJuridica, t.PrefijoRadicado, t.EstadosLegalesPermitidos) = (codigo, d.Nombre.Trim(), d.DescripcionJuridica.Trim(), prefijo, estados);
            (t.PermiteMasivo, t.PlazoRespuestaOtDiasHabiles, t.AdjuntosRequeridos, t.Activo) = (d.PermiteMasivo, (short)d.PlazoRespuestaOtDiasHabiles, adjuntos, d.Activo ?? t.Activo);
            (t.ActualizadoEn, t.ActualizadoPor) = (reloj.Ahora, usuario.Id);
            auditoria.Registrar(null, "tipologia_dp", t.Id, d.Activo is { } a && a != t.Activo ? (a ? "ACTIVAR" : "DESACTIVAR") : "ACTUALIZAR", cambios);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(t));
    }

    private static async Task<IResult> Habilitar(Guid empresa, Guid id, Habilitacion pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var t = await db.Tipologias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La tipología");
        if (pedido.Habilitada && !t.Activo)
            throw ReglaException.Conflicto("TIPOLOGIA_INACTIVA", "La tipología está desactivada en el catálogo.");
        var h = await db.TipologiasEmpresa.FirstOrDefaultAsync(x => x.TenantId == empresa && x.TipologiaId == id, ct).ConfigureAwait(false);
        var antes = h?.Habilitada ?? false;
        if (antes == pedido.Habilitada)
            return Results.Ok(new { habilitada = antes });

        if (h is null)
        {
            db.TipologiasEmpresa.Add(new TipologiaEmpresa { TenantId = empresa, TipologiaId = id, Habilitada = pedido.Habilitada, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id });
        }
        else
        {
            (h.Habilitada, h.ActualizadoEn, h.ActualizadoPor) = (pedido.Habilitada, reloj.Ahora, usuario.Id);
        }

        auditoria.Registrar(empresa, "tipologia_empresa", $"{t.Codigo}", pedido.Habilitada ? "ACTIVAR" : "DESACTIVAR", new CambioCampo("habilitada", antes, pedido.Habilitada));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { habilitada = pedido.Habilitada });
    }
}
