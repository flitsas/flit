using System.Globalization;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Contenido del Excel del lote (HU #13847, F07-H2; columnas de F07-H4): la hoja de identificación (<see cref="Encabezado"/>) y
/// un <see cref="VehiculoReporte"/> por vehículo, en el orden de la carga, con las mismas secciones que el PDF de cada uno
/// (<see cref="ContenidoReporteVehiculo"/>). Tipo cerrado, sin origen, usuario manual, proveedor, transacción, payload ni
/// documentos completos (R-20).
/// </summary>
/// <example>
/// <code>
/// var lote = await lector.LoteAsync(usuario, diagnosticoId, ct);
/// var entrada = await HuellaEvidencia.LeerEntradaAsync(db, ClaveEvidencia.Xlsx(diagnosticoId), 1, lote.ParaHuella(), ct);
/// </code>
/// </example>
public sealed record ContenidoReporteLote(EncabezadoLoteReporte Encabezado, IReadOnlyList<VehiculoReporte> Vehiculos)
{
    /// <summary>
    /// Lo que sale en el Excel y no viene de las celdas ni de la cabecera (para <see cref="EntradaHuella.Encabezado"/>): los textos
    /// de la hoja de identificación, la identificación de cada vehículo y cada persona enmascarada.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ParaHuella()
    {
        var claves = new Dictionary<string, string?>(StringComparer.Ordinal);
        Encabezado.Agregar(claves);
        foreach (var vehiculo in Vehiculos)
        {
            claves[$"vehiculo/{vehiculo.VehiculoId}"] = string.Join(
                '|', vehiculo.Orden.ToString(CultureInfo.InvariantCulture), vehiculo.Identificador, vehiculo.TipoIdentificador, vehiculo.Placa, vehiculo.Vin);
            SeccionReporte.AgregarPersonas(claves, $"persona/{vehiculo.VehiculoId}", vehiculo.Secciones);
        }

        return claves;
    }
}

/// <summary>Hoja de identificación del lote: los datos del encabezado del PDF que no son del vehículo.</summary>
public sealed record EncabezadoLoteReporte(
    Guid DiagnosticoId,
    string NombreLote,
    string Empresa,
    string UsuarioCreador,
    DateTimeOffset CreadoEn,
    string TipoAnalisis)
{
    internal void Agregar(Dictionary<string, string?> claves)
    {
        claves["diagnostico_id"] = DiagnosticoId.ToString();
        claves["nombre_lote"] = NombreLote;
        claves["empresa"] = Empresa;
        claves["usuario_creador"] = UsuarioCreador;
        claves["creado_en"] = CreadoEn.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        claves["tipo_analisis"] = TipoAnalisis;
    }
}

/// <summary>Un vehículo del lote en el Excel: su id (para casar con su PDF en el ZIP), orden, identificación y secciones.</summary>
public sealed record VehiculoReporte(
    Guid VehiculoId,
    int Orden,
    string Identificador,
    string TipoIdentificador,
    string? Placa,
    string? Vin,
    IReadOnlyList<SeccionReporte> Secciones);
