using System.Globalization;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Aviso de datos personales de los archivos del módulo de reportes (HU #14579, BE-3; SP-15; Ley 1581 de 2012), literal: va en la hoja
/// «Aviso» y en la fila 1 de «Reporte» del Excel y en el pie de cada página del PDF, con la fecha de generación y el id de
/// <c>accesos_reporte</c>, que permite rastrear un archivo sin guardarlo. Sin nombre ni correo del usuario.
/// </summary>
internal static class AvisoDatosPersonales
{
    public const string Texto =
        "Contiene datos personales (Ley 1581 de 2012). Uso exclusivo para la finalidad del diagnóstico vehicular contratado. Prohibida su divulgación a terceros.";

    /// <summary>«Generado el {AAAA-MM-DD HH:mm} (hora Colombia) · Registro {id}».</summary>
    public static string Registro(DateTimeOffset generadoEn, Guid accesoId) =>
        $"Generado el {generadoEn.ToOffset(VigenciaDelDia.OffsetColombia).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} (hora Colombia) · Registro {accesoId}";
}

/// <summary>
/// Nombre fijo del archivo (HU #14579, BE-3; SP-10): <c>diagnostico-reporte-{tipo}-{AAAAMMDD}-{AAAAMMDD}[-global].{xlsx|pdf}</c>, con el
/// tipo del enum y las fechas del rango. Sin placa, compañía, NIT, usuario ni id de empresa.
/// </summary>
internal static class NombreArchivoReporte
{
    public static string De(FiltroReporte filtro, FormatoReporte formato, bool global = false)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var extension = formato switch
        {
            FormatoReporte.Xlsx => "xlsx",
            FormatoReporte.Pdf => "pdf",
            _ => throw new ArgumentOutOfRangeException(nameof(formato)),
        };
        return string.Create(CultureInfo.InvariantCulture,
            $"diagnostico-reporte-{TiposDeReporte.Codigo(filtro.Tipo)}-{filtro.Desde:yyyyMMdd}-{filtro.Hasta:yyyyMMdd}{(global ? "-global" : string.Empty)}.{extension}");
    }
}
