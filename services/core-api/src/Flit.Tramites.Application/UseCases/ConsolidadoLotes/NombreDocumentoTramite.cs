using System.Text;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #14734 (Feature #14733, Épica #14657) — nombre estándar de los documentos PDF de un trámite:
/// <c>{tipo}_{radicado}_{placa}.pdf</c>. Función pura: no consulta RUNT ni nada externo.
/// Reutiliza el saneo de <see cref="ConsolidadoLoteNombres"/> (placa <c>[A-Z0-9]</c>, radicado <c>[A-Z0-9-]</c>).
/// </summary>
/// <remarks>
/// <code>
/// NombreDocumentoTramite.Generar("consolidado_maestro", "FT1-0000101", "AAA111"); // consolidado_maestro_FT1-0000101_AAA111.pdf
/// NombreDocumentoTramite.Generar("fur", "FT1-0000102", null);                      // fur_FT1-0000102_SIN-PLACA.pdf
/// </code>
/// </remarks>
public static class NombreDocumentoTramite
{
    /// <summary>Tipo de reemplazo si no le queda ningún carácter válido.</summary>
    public const string SinTipo = "documento";

    /// <summary>
    /// Genera <c>{tipo}_{radicado}_{placa}.pdf</c>. El tipo se sanea a <c>[a-z0-9_]</c> en minúsculas;
    /// radicado vacío → <c>SIN-RADICADO</c>; placa vacía o sin caracteres válidos → <c>SIN-PLACA</c>.
    /// </summary>
    public static string Generar(string tipo, string? radicado, string? placa)
    {
        var r = ConsolidadoLoteNombres.Filtrar(radicado, permitirGuion: true);
        var p = ConsolidadoLoteNombres.SanearPlaca(placa);
        return $"{SanearTipo(tipo)}_{(r.Length == 0 ? ConsolidadoLoteNombres.SinRadicado : r)}_{(p.Length == 0 ? ConsolidadoLoteNombres.SinPlaca : p)}.pdf";
    }

    /// <summary>
    /// HU #14735 — nombre para un documento que FLIT genera de la instancia: radicado = <c>ReferenceNumber</c>;
    /// placa = <c>Plate</c> denormalizada o, si falta, el field value <c>plate</c>. No consulta RUNT.
    /// </summary>
    public static string Generar(string tipo, Flit.Tramites.Domain.Entities.ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var placa = instance.Plate;
        if (string.IsNullOrWhiteSpace(placa))
        {
            placa = instance.FieldValues
                .FirstOrDefault(f => string.Equals(f.FieldKey, "plate", StringComparison.OrdinalIgnoreCase))?.ValueText;
        }

        return Generar(tipo, instance.ReferenceNumber, placa);
    }

    private static string SanearTipo(string? tipo)
    {
        if (string.IsNullOrEmpty(tipo))
            return SinTipo;

        var sb = new StringBuilder(tipo.Length);
        foreach (var c in tipo.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
                sb.Append(c);
        }

        return sb.Length == 0 ? SinTipo : sb.ToString();
    }
}
