using System.Text;
using Flit.Impuestos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Catalogos;

/// <summary>Secretaría homologada para la solicitud al API: <c>secretaryDepartmentCode</c> (DANE, 2 cifras) y <c>secretaryCity</c>.</summary>
public sealed record SecretariaHomologada(string DepartamentoCodigo, string Ciudad);

/// <summary>
/// Homologación de la secretaría de tránsito que reporta el RUNT (F2a-CF3, LP-03, D-ENTRADA) con la réplica propia de organismos
/// (<c>impuestos.organismos</c>, <see cref="SincronizacionOrganismos"/>): es dato de referencia, no código de Diagnóstico (EP-CF1).
/// Por código si viene y está en la réplica; si no, por nombre con <see cref="Normalizar"/>. El nombre que corresponde a organismos
/// de departamentos o ciudades distintos es ambiguo. Ausente, ambiguo, o sin departamento de 2 cifras o sin ciudad: null, y el ítem
/// queda «Datos incompletos» sin llamar al API (AC-BE-31, M09). Catálogo global: se lee sin filtro de empresa.
/// </summary>
/// <example>
/// <code>
/// var secretaria = await homologacion.HomologarAsync(vehiculo.OrganismoCodigo, vehiculo.OrganismoNombre, ct);
/// // null: «Datos incompletos» (secretaría sin homologar); si no, secretaryDepartmentCode y secretaryCity.
/// </code>
/// </example>
public sealed class HomologacionSecretaria(ImpuestosDb db)
{
    private const string ConTilde = "ÁÀÄÂÉÈËÊÍÌÏÎÓÒÖÔÚÙÜÛÑáàäâéèëêíìïîóòöôúùüûñ";
    private const string SinTilde = "AAAAEEEEIIIIOOOOUUUUNAAAAEEEEIIIIOOOOUUUUN";

    /// <summary>
    /// Forma comparable de un nombre: mayúsculas invariantes, sin tildes, diéresis ni Ñ (tabla propia) y un solo espacio entre
    /// palabras; cadena vacía si es nulo o solo espacios. Es la que guarda <c>organismos.nombre_normalizado</c>.
    /// </summary>
    public static string Normalizar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return string.Empty;
        var sb = new StringBuilder(nombre.Length);
        foreach (var c in nombre)
        {
            var i = ConTilde.IndexOf(c, StringComparison.Ordinal);
            sb.Append(i >= 0 ? SinTilde[i] : char.ToUpperInvariant(c));
        }

        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>La secretaría homologada del organismo, o null si no se puede homologar sin ambigüedad.</summary>
    public async Task<SecretariaHomologada?> HomologarAsync(string? codigo, string? nombre, CancellationToken ct)
    {
        var organismos = db.Set<Organismo>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(codigo))
        {
            var limpio = codigo.Trim();
            var porCodigo = await organismos.Where(o => o.Codigo == limpio)
                .Select(o => new { o.DepartamentoCodigo, o.Municipio }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (porCodigo is not null)
                return Armar(porCodigo.DepartamentoCodigo, porCodigo.Municipio);
        }

        var normalizado = Normalizar(nombre);
        if (normalizado.Length == 0)
            return null;
        var candidatos = await organismos.Where(o => o.NombreNormalizado == normalizado)
            .Select(o => new { o.DepartamentoCodigo, o.Municipio }).Distinct().Take(2).ToListAsync(ct).ConfigureAwait(false);
        return candidatos.Count == 1 ? Armar(candidatos[0].DepartamentoCodigo, candidatos[0].Municipio) : null;
    }

    private static SecretariaHomologada? Armar(string? departamento, string? municipio) =>
        departamento is { Length: 2 } d && d.All(char.IsAsciiDigit) && !string.IsNullOrWhiteSpace(municipio)
            ? new SecretariaHomologada(d, municipio.Trim())
            : null;
}
