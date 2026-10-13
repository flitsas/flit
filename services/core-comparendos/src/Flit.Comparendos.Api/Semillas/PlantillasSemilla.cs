using System.Text.RegularExpressions;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plantillas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Semillas;

/// <summary>
/// Plantillas base del conjunto FLIT, versión 1 publicada (los archivos Semillas/Plantillas/*.html). El documento del DP
/// se siembra una vez por tipología: con el texto jurídico de V1 de esa tesis si existe (Semillas/Plantillas/Tesis/
/// &lt;código&gt;.html, ver context/gdc/v1/tesis-textos-v1.md; pendiente de revisión del abogado) o, si no, con el texto
/// genérico cuyo fundamento sale del catálogo ({{dp.fundamento}}). Renting usa las de FLIT hasta que publique las suyas
/// (S-23). Nunca se pisa una plantilla existente.
/// </summary>
internal static partial class PlantillasSemilla
{
    [GeneratedRegex(@"<!--\s*(nombre|asunto):\s*(.*?)\s*-->\s*", RegexOptions.CultureInvariant)]
    private static partial Regex Encabezado();

    public static async Task SembrarAsync(ComparendosDb db, DateTimeOffset ahora, CancellationToken ct)
    {
        var asm = typeof(PlantillasSemilla).Assembly;
        var existentes = await db.Plantillas.Where(p => p.Conjunto == ConjuntoPlantillas.Flit).Select(p => new { p.Tipo, p.TipologiaId }).ToListAsync(ct).ConfigureAwait(false);
        var tipologias = await db.Tipologias.Select(t => new { t.Id, t.Codigo }).ToListAsync(ct).ConfigureAwait(false);
        var tesis = new Dictionary<string, (string? Nombre, string Cuerpo)>(StringComparer.OrdinalIgnoreCase);
        foreach (var recurso in asm.GetManifestResourceNames().Where(n => n.Contains(".Semillas.Plantillas.Tesis.", StringComparison.Ordinal)))
        {
            var codigo = recurso[(recurso.LastIndexOf(".Tesis.", StringComparison.Ordinal) + ".Tesis.".Length)..^".html".Length];
            tesis[codigo] = await LeerAsync(asm, recurso, ct).ConfigureAwait(false);
        }


        foreach (var recurso in asm.GetManifestResourceNames().Where(n => n.Contains(".Semillas.Plantillas.", StringComparison.Ordinal)))
        {
            var archivo = recurso[(recurso.LastIndexOf(".Plantillas.", StringComparison.Ordinal) + ".Plantillas.".Length)..^".html".Length];
            if (!Enum.TryParse<TipoPlantilla>(archivo, out var tipo))
                continue;

            var (nombre, cuerpo) = await LeerAsync(asm, recurso, ct).ConfigureAwait(false);
            var asunto = Asunto(asm, recurso);
            if (tipo != TipoPlantilla.DpDocumento)
            {
                if (!existentes.Any(e => e.Tipo == tipo && e.TipologiaId == null))
                    Crear(db, tipo, null, nombre ?? tipo.ToString(), asunto, cuerpo, ahora);
                continue;
            }

            foreach (var t in tipologias.Where(t => !existentes.Any(e => e.Tipo == tipo && e.TipologiaId == t.Id)))
            {
                var propio = tesis.TryGetValue(t.Codigo, out var x) ? x : ((string? Nombre, string Cuerpo)?)null;
                Crear(db, tipo, t.Id, propio?.Nombre ?? nombre ?? tipo.ToString(), null, propio?.Cuerpo ?? cuerpo, ahora);
            }
        }
    }

    /// <summary>Una tipología nueva del catálogo nace con su plantilla de DP base (si no, no se podría enviar: SIN_PLANTILLA).</summary>
    public static async Task CrearParaTipologiaAsync(ComparendosDb db, Guid tipologiaId, DateTimeOffset ahora, CancellationToken ct)
    {
        var asm = typeof(PlantillasSemilla).Assembly;
        var recurso = asm.GetManifestResourceNames().Single(n => n.EndsWith(".Semillas.Plantillas.DpDocumento.html", StringComparison.Ordinal));
        await using var stream = asm.GetManifestResourceStream(recurso)!;
        using var lector = new StreamReader(stream);
        var cuerpo = Encabezado().Replace(await lector.ReadToEndAsync(ct).ConfigureAwait(false), string.Empty).Trim();
        Crear(db, TipoPlantilla.DpDocumento, tipologiaId, "Derecho de petición", null, cuerpo, ahora);
    }

    private static async Task<(string? Nombre, string Cuerpo)> LeerAsync(System.Reflection.Assembly asm, string recurso, CancellationToken ct)
    {
        await using var stream = asm.GetManifestResourceStream(recurso)!;
        using var lector = new StreamReader(stream);
        var texto = await lector.ReadToEndAsync(ct).ConfigureAwait(false);
        var nombre = Encabezado().Matches(texto).FirstOrDefault(m => m.Groups[1].Value == "nombre")?.Groups[2].Value;
        return (nombre, Encabezado().Replace(texto, string.Empty).Trim());
    }

    private static string? Asunto(System.Reflection.Assembly asm, string recurso)
    {
        using var stream = asm.GetManifestResourceStream(recurso)!;
        using var lector = new StreamReader(stream);
        return Encabezado().Matches(lector.ReadToEnd()).FirstOrDefault(m => m.Groups[1].Value == "asunto")?.Groups[2].Value;
    }

    private static void Crear(ComparendosDb db, TipoPlantilla tipo, Guid? tipologia, string nombre, string? asunto, string cuerpo, DateTimeOffset ahora)
    {
        var plantilla = new Plantilla { Id = Guid.CreateVersion7(), Conjunto = ConjuntoPlantillas.Flit, Tipo = tipo, TipologiaId = tipologia, Nombre = nombre, CreadoEn = ahora };
        var version = new PlantillaVersion
        {
            Id = Guid.CreateVersion7(),
            PlantillaId = plantilla.Id,
            NumeroVersion = 1,
            Estado = EstadoPlantillaVersion.Publicada,
            Asunto = asunto,
            CuerpoHtml = cuerpo,
            VariablesUsadas = [.. MotorPlantillas.Validar(asunto, cuerpo).Usadas],
            NotaCambio = "Versión base de FLIT.",
            PublicadaEn = ahora,
            CreadoEn = ahora,
        };
        plantilla.VersionPublicadaId = version.Id;
        db.Plantillas.Add(plantilla);
        db.PlantillaVersiones.Add(version);
    }
}
