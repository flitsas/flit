using System.Text.RegularExpressions;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plantillas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Semillas;

/// <summary>
/// Plantillas base, versión 1 publicada: el conjunto FLIT (Semillas/Plantillas/*.html) y, desde la HU #13883, el de
/// Renting con los textos de V1 (Semillas/Plantillas/Renting/*.html). El documento del DP
/// se siembra una vez por tipología: con el texto jurídico de V1 de esa tesis si existe (Semillas/Plantillas/Tesis/
/// &lt;código&gt;.html, ver context/gdc/v1/tesis-textos-v1.md; pendiente de revisión del abogado) o, si no, con el texto
/// genérico cuyo fundamento sale del catálogo ({{dp.fundamento}}). Lo que Renting no tenga propio lo toma de FLIT (S-23). Nunca se pisa una plantilla que alguien editó: si una plantilla solo tiene versiones base (nadie publicó una
/// propia) y el texto sembrado cambió, se publica una versión base nueva y la anterior queda retirada (así llegan las
/// correcciones de texto a las bases ya sembradas).
/// </summary>
internal static partial class PlantillasSemilla
{
    [GeneratedRegex(@"<!--\s*(nombre|asunto):\s*(.*?)\s*-->\s*", RegexOptions.CultureInvariant)]
    private static partial Regex Encabezado();

    /// <summary>Nota de las versiones que siembra FLIT; una versión con otra nota es de un usuario.</summary>
    public const string NotaBase = "Versión base de FLIT.";

    public static async Task SembrarAsync(ComparendosDb db, DateTimeOffset ahora, CancellationToken ct)
    {
        var asm = typeof(PlantillasSemilla).Assembly;
        var existentes = await db.Plantillas.Select(p => new { p.Conjunto, p.Tipo, p.TipologiaId }).ToListAsync(ct).ConfigureAwait(false);
        var tipologias = await db.Tipologias.Select(t => new { t.Id, t.Codigo }).ToListAsync(ct).ConfigureAwait(false);
        var tesis = new Dictionary<string, (string? Nombre, string Cuerpo)>(StringComparer.OrdinalIgnoreCase);
        foreach (var recurso in asm.GetManifestResourceNames().Where(n => n.Contains(".Semillas.Plantillas.Tesis.", StringComparison.Ordinal)))
        {
            var codigo = recurso[(recurso.LastIndexOf(".Tesis.", StringComparison.Ordinal) + ".Tesis.".Length)..^".html".Length];
            tesis[codigo] = await LeerAsync(asm, recurso, ct).ConfigureAwait(false);
        }

        // HU #13883: Semillas/Plantillas/*.html es el conjunto FLIT y Semillas/Plantillas/Renting/*.html el de Renting (los
        // textos de V1). Lo que Renting no tenga propio lo toma de FLIT al armar (S-23).
        foreach (var (conjunto, carpeta) in new[] { (ConjuntoPlantillas.Flit, ".Semillas.Plantillas."), (ConjuntoPlantillas.Renting, ".Semillas.Plantillas.Renting.") })
        {
            foreach (var recurso in asm.GetManifestResourceNames().Where(n => n.Contains(carpeta, StringComparison.Ordinal)))
            {
                var archivo = recurso[(recurso.IndexOf(carpeta, StringComparison.Ordinal) + carpeta.Length)..^".html".Length];
                if (!Enum.TryParse<TipoPlantilla>(archivo, out var tipo))
                    continue;

                var (nombre, cuerpo) = await LeerAsync(asm, recurso, ct).ConfigureAwait(false);
                var asunto = Asunto(asm, recurso);
                if (tipo != TipoPlantilla.DpDocumento)
                {
                    if (!existentes.Any(e => e.Conjunto == conjunto && e.Tipo == tipo && e.TipologiaId == null))
                        Crear(db, conjunto, tipo, null, nombre ?? tipo.ToString(), asunto, cuerpo, ahora);
                    else
                        await ActualizarBaseAsync(db, conjunto, tipo, null, asunto, cuerpo, ahora, ct).ConfigureAwait(false);
                    continue;
                }

                foreach (var t in tipologias)
                {
                    var propio = tesis.TryGetValue(t.Codigo, out var x) ? x : ((string? Nombre, string Cuerpo)?)null;
                    if (!existentes.Any(e => e.Conjunto == conjunto && e.Tipo == tipo && e.TipologiaId == t.Id))
                        Crear(db, conjunto, tipo, t.Id, propio?.Nombre ?? nombre ?? tipo.ToString(), null, propio?.Cuerpo ?? cuerpo, ahora);
                    else
                        await ActualizarBaseAsync(db, conjunto, tipo, t.Id, null, propio?.Cuerpo ?? cuerpo, ahora, ct).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Publica la versión base nueva si la plantilla solo tiene versiones base y su texto quedó viejo. Si un usuario
    /// publicó o guardó una versión propia, no se toca.
    /// </summary>
    private static async Task ActualizarBaseAsync(ComparendosDb db, ConjuntoPlantillas conjunto, TipoPlantilla tipo, Guid? tipologia, string? asunto, string cuerpo, DateTimeOffset ahora,
        CancellationToken ct)
    {
        var plantilla = await db.Plantillas.FirstOrDefaultAsync(p => p.Conjunto == conjunto && p.Tipo == tipo && p.TipologiaId == tipologia, ct).ConfigureAwait(false);
        if (plantilla is null)
            return;
        var versiones = await db.PlantillaVersiones.Where(v => v.PlantillaId == plantilla.Id).ToListAsync(ct).ConfigureAwait(false);
        if (versiones.Count == 0 || versiones.Any(v => v.NotaCambio != NotaBase))
            return;
        var publicada = versiones.FirstOrDefault(v => v.Id == plantilla.VersionPublicadaId);
        if (publicada is null || (publicada.CuerpoHtml == cuerpo && publicada.Asunto == asunto))
            return;

        publicada.Estado = EstadoPlantillaVersion.Retirada;
        var nueva = Version(plantilla.Id, versiones.Max(v => v.NumeroVersion) + 1, asunto, cuerpo, ahora);
        plantilla.VersionPublicadaId = nueva.Id;
        db.PlantillaVersiones.Add(nueva);
    }

    private static PlantillaVersion Version(Guid plantillaId, int numero, string? asunto, string cuerpo, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        PlantillaId = plantillaId,
        NumeroVersion = numero,
        Estado = EstadoPlantillaVersion.Publicada,
        Asunto = asunto,
        CuerpoHtml = cuerpo,
        VariablesUsadas = [.. MotorPlantillas.Validar(asunto, cuerpo).Usadas],
        NotaCambio = NotaBase,
        PublicadaEn = ahora,
        CreadoEn = ahora,
    };

    /// <summary>Una tipología nueva del catálogo nace con su plantilla de DP base (si no, no se podría enviar: SIN_PLANTILLA).</summary>
    public static async Task CrearParaTipologiaAsync(ComparendosDb db, Guid tipologiaId, DateTimeOffset ahora, CancellationToken ct)
    {
        var asm = typeof(PlantillasSemilla).Assembly;
        var recurso = asm.GetManifestResourceNames().Single(n => n.EndsWith(".Semillas.Plantillas.DpDocumento.html", StringComparison.Ordinal));
        await using var stream = asm.GetManifestResourceStream(recurso)!;
        using var lector = new StreamReader(stream);
        var cuerpo = Encabezado().Replace(await lector.ReadToEndAsync(ct).ConfigureAwait(false), string.Empty).Trim();
        Crear(db, ConjuntoPlantillas.Flit, TipoPlantilla.DpDocumento, tipologiaId, "Derecho de petición", null, cuerpo, ahora);
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

    private static void Crear(ComparendosDb db, ConjuntoPlantillas conjunto, TipoPlantilla tipo, Guid? tipologia, string nombre, string? asunto, string cuerpo, DateTimeOffset ahora)
    {
        var plantilla = new Plantilla { Id = Guid.CreateVersion7(), Conjunto = conjunto, Tipo = tipo, TipologiaId = tipologia, Nombre = nombre, CreadoEn = ahora };
        var version = Version(plantilla.Id, 1, asunto, cuerpo, ahora);
        plantilla.VersionPublicadaId = version.Id;
        db.Plantillas.Add(plantilla);
        db.PlantillaVersiones.Add(version);
    }
}
