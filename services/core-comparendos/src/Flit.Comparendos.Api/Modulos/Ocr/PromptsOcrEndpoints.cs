using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>
/// Prompt de OCR versionado (§7.24, MOD02 RN-13): el SuperAdmin crea versiones, las prueba contra un archivo de muestra
/// y publica una. Una versión publicada no se edita: se crea otra. Cada paquete guarda la versión con que se procesó.
/// </summary>
public static class PromptsOcrEndpoints
{
    public static readonly string[] Proveedores = ["ANTHROPIC", "OPENAI", "GEMINI"];

    public sealed record DatosPrompt(string? Texto, string? Proveedor, string? Modelo);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin/ocr/prompts").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        g.MapGet("/", Listar);
        g.MapPost("/", Crear);
        g.MapPut("/{id:guid}", Editar);
        g.MapPost("/{id:guid}/publicar", Publicar);
        g.MapPost("/{id:guid}/probar", Probar).DisableAntiforgery();
    }

    private static async Task<IResult> Listar(ComparendosDb db, CancellationToken ct) =>
        Results.Ok(await db.PromptsOcr.AsNoTracking().OrderByDescending(p => p.NumeroVersion)
            .Select(p => new { p.Id, p.NumeroVersion, p.Texto, p.Proveedor, p.Modelo, p.Publicada, p.CreadoEn, p.CreadoPor, p.ActualizadoEn }).ToListAsync(ct).ConfigureAwait(false));

    private static (string Texto, string Proveedor, string Modelo) Validar(DatosPrompt d)
    {
        if (string.IsNullOrWhiteSpace(d.Texto) || d.Texto.Length > 20000)
            throw new ReglaException("TEXTO_INVALIDO", "El prompt no puede estar vacío ni pasar de 20.000 caracteres.");
        var proveedor = (d.Proveedor ?? "ANTHROPIC").Trim().ToUpperInvariant();
        if (!Proveedores.Contains(proveedor))
            throw new ReglaException("PROVEEDOR_INVALIDO", "Proveedor inválido: ANTHROPIC, OPENAI o GEMINI.");
        if (proveedor != "ANTHROPIC")
            throw new ReglaException("PROVEEDOR_NO_DISPONIBLE", "Por ahora solo está conectado Anthropic; OpenAI y Gemini quedan para cuando se comparen con la muestra real.");
        if (string.IsNullOrWhiteSpace(d.Modelo) || d.Modelo.Trim().Length > 80)
            throw new ReglaException("MODELO_INVALIDO", "Indica el modelo (hasta 80 caracteres).");
        return (d.Texto.Trim(), proveedor, d.Modelo.Trim());
    }

    private static async Task<IResult> Crear(DatosPrompt d, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var (texto, proveedor, modelo) = Validar(d);
        var numero = (await db.PromptsOcr.MaxAsync(p => (int?)p.NumeroVersion, ct).ConfigureAwait(false) ?? 0) + 1;
        var p = new PromptOcrVersion { Id = Guid.CreateVersion7(), NumeroVersion = numero, Texto = texto, Proveedor = proveedor, Modelo = modelo, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
        db.PromptsOcr.Add(p);
        auditoria.Registrar(null, "prompt_ocr_version", p.Id, "CREAR", new CambioCampo("numero_version", null, numero), new CambioCampo("modelo", null, modelo));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/admin/ocr/prompts/{p.Id}", new { p.Id, p.NumeroVersion });
    }

    private static async Task<IResult> Editar(Guid id, DatosPrompt d, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var p = await db.PromptsOcr.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La versión");
        if (p.Publicada || await db.PaquetesOcr.AnyAsync(x => x.PromptVersionId == id, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("VERSION_INMUTABLE", "Esta versión ya se publicó o ya procesó paquetes: crea una versión nueva.");
        var (texto, proveedor, modelo) = Validar(d);
        auditoria.Registrar(null, "prompt_ocr_version", p.Id, "EDITAR", new CambioCampo("modelo", p.Modelo, modelo), new CambioCampo("texto", p.Texto.Length, texto.Length));
        (p.Texto, p.Proveedor, p.Modelo, p.ActualizadoEn, p.ActualizadoPor) = (texto, proveedor, modelo, reloj.Ahora, usuario.Id);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> Publicar(Guid id, ComparendosDb db, Auditoria auditoria, UsuarioActual usuario, Reloj reloj, CancellationToken ct)
    {
        var p = await db.PromptsOcr.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La versión");
        var anterior = await db.PromptsOcr.FirstOrDefaultAsync(x => x.Publicada && x.Id != id, ct).ConfigureAwait(false);
        if (anterior is not null)
        {
            anterior.Publicada = false;
            await db.SaveChangesAsync(ct).ConfigureAwait(false); // el índice único parcial exige soltar la anterior primero
        }

        (p.Publicada, p.ActualizadoEn, p.ActualizadoPor) = (true, reloj.Ahora, usuario.Id);
        auditoria.Registrar(null, "prompt_ocr_version", p.Id, "PUBLICAR", new CambioCampo("publicada", anterior?.NumeroVersion, p.NumeroVersion));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Prueba de una versión contra un archivo de muestra: devuelve el JSON tal como lo interpretaría el flujo, sin guardar nada.</summary>
    private static async Task<IResult> Probar(Guid id, IFormFile archivo, ComparendosDb db, IProveedorOcr proveedor, CancellationToken ct)
    {
        var p = await db.PromptsOcr.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La versión");
        var tipo = RegistroArchivos.TipoDe(archivo);
        if (!ServicioOcr.Formatos.Contains(tipo) || archivo.Length is 0 or > ServicioOcr.MaximoBytes)
            throw new ReglaException("ARCHIVO_TIPO_INVALIDO", "La muestra debe ser JPG, PNG, TIFF o PDF de hasta 20 MB.");
        await using var s = archivo.OpenReadStream();
        using var m = new MemoryStream();
        await s.CopyToAsync(m, ct).ConfigureAwait(false);
        var r = await proveedor.ExtraerAsync(m.ToArray(), tipo, p.Texto, p.Modelo, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            exitoso = r.Exitoso, modelo = r.Modelo, tipoFallo = r.TipoFallo?.ToString(), r.Mensaje,
            campos = r.Campos.ToDictionary(kv => kv.Key, kv => new { valor = kv.Value.Valor, confianza = kv.Value.Confianza }),
            crudo = r.Crudo,
        });
    }
}
