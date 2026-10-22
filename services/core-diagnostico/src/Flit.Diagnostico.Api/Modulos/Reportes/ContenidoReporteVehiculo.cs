using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Tablero;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Contenido del reporte de UN vehículo (HU #13847, F07-H2, F07-C1/C2/C3/C9/C10): lo único que ven los renderizadores del PDF
/// (F07-H3), del Excel (F07-H4) y del ZIP (F07-H5). Es un tipo cerrado: no tiene origen, usuario manual, proveedor, transacción,
/// payload, documento cifrado ni nombres de personas (R-20), así que ningún archivo del cliente puede mostrarlos sin cambiar
/// este tipo (lo verifica <c>ArquitecturaReportesTests</c> por reflexión). Una celda llenada a mano y una de API con el mismo
/// dato dan el mismo contenido. Lo arma <see cref="LectorContenidoReporte"/>.
/// </summary>
/// <example>
/// <code>
/// var contenido = await lector.VehiculoAsync(usuario, diagnosticoId, vehiculoId, ct);
/// var entrada = await HuellaEvidencia.LeerEntradaAsync(db, ClaveEvidencia.Pdf(diagnosticoId, vehiculoId), 1, contenido.ParaHuella(), ct);
/// </code>
/// </example>
public sealed record ContenidoReporteVehiculo(EncabezadoReporte Encabezado, IReadOnlyList<SeccionReporte> Secciones)
{
    /// <summary>
    /// Lo que sale en el archivo y no viene de las celdas ni de la cabecera, para el <c>Encabezado</c> de
    /// <see cref="EntradaHuella"/> (HU #13846): empresa, creador, tipo de análisis, fecha de creación, identificación del vehículo
    /// y cada persona enmascarada. Cambiar cualquiera de estos textos cambia la huella e invalida el archivo guardado.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ParaHuella()
    {
        var claves = new Dictionary<string, string?>(StringComparer.Ordinal);
        Encabezado.Agregar(claves);
        SeccionReporte.AgregarPersonas(claves, "persona", Secciones);
        return claves;
    }
}

/// <summary>
/// Encabezado del reporte de un vehículo (F07-C1/C2): ID del diagnóstico, nombre del lote, empresa (claim <c>company_name</c>
/// de quien pide, o <c>evidencias.empresa_nombre</c> en el ZIP), nombre del usuario creador (Identidad; «No disponible» si no
/// responde), fecha de creación en hora Colombia (UTC−5), tipo de análisis (<see cref="EtiquetaTipoAnalisis"/>) e
/// identificación del vehículo (identificador normalizado, su tipo en código, placa y VIN).
/// </summary>
public sealed record EncabezadoReporte(
    Guid DiagnosticoId,
    string NombreLote,
    string Empresa,
    string UsuarioCreador,
    DateTimeOffset CreadoEn,
    string TipoAnalisis,
    string Identificador,
    string TipoIdentificador,
    string? Placa,
    string? Vin)
{
    internal void Agregar(Dictionary<string, string?> claves)
    {
        claves["diagnostico_id"] = DiagnosticoId.ToString();
        claves["nombre_lote"] = NombreLote;
        claves["empresa"] = Empresa;
        claves["usuario_creador"] = UsuarioCreador;
        claves["creado_en"] = CreadoEn.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        claves["tipo_analisis"] = TipoAnalisis;
        claves["identificador"] = Identificador;
        claves["tipo_identificador"] = TipoIdentificador;
        claves["placa"] = Placa;
        claves["vin"] = Vin;
    }
}

/// <summary>Cómo sale una sección en el reporte.</summary>
public enum EstadoSeccion
{
    /// <summary>Celda COMPLETADO: con su resultado (o sus personas).</summary>
    Completado,

    /// <summary>Celda N/A: con su motivo en código y en texto.</summary>
    NoAplica,

    /// <summary>Celda ERROR (solo emitible con el lote INCOMPLETO, Q7): «consulta no disponible», sin tipo de error ni causa.</summary>
    NoDisponible,

    /// <summary>Celda PENDIENTE o EN_CURSO (o aún sin celda): sin dato.</summary>
    Pendiente,
}

/// <summary>
/// Una sección del reporte: una por fuente del alcance del lote, en el orden de las columnas de la grilla
/// (<c>Alcance.Agrupar(cabecera.Alcance).Fuentes</c>). <see cref="Fuente"/> y <see cref="Grupo"/> en código de la base
/// (<c>SIMIT_VEHICULO</c>, <c>POR_VEHICULO</c>); <see cref="Leyenda"/> es el texto que se muestra en lugar del resultado
/// (motivo de N/A o «consulta no disponible»). <see cref="Resultado"/> solo en las fuentes del vehículo con la celda COMPLETADO
/// (proyección cerrada del detalle de celda, HU #13798); <see cref="Personas"/> solo en las fuentes de persona (lista vacía si
/// la celda aún no tiene dato o no aplica), null en las del vehículo.
/// </summary>
public sealed record SeccionReporte(
    string Fuente,
    string Grupo,
    EstadoSeccion Estado,
    string? MotivoNa,
    string? Leyenda,
    DateTimeOffset? DatoObtenidoEn,
    ResultadoDetalleDto? Resultado,
    IReadOnlyList<PersonaReporte>? Personas)
{
    /// <summary>Texto de una fuente en ERROR (decisión Q7): sin código de error, causa técnica ni origen.</summary>
    public const string ConsultaNoDisponible = "consulta no disponible";

    internal static void AgregarPersonas(Dictionary<string, string?> claves, string prefijo, IEnumerable<SeccionReporte> secciones)
    {
        foreach (var seccion in secciones)
        {
            foreach (var persona in seccion.Personas ?? [])
            {
                claves[$"{prefijo}/{seccion.Fuente}/{persona.Orden.ToString(CultureInfo.InvariantCulture)}"] =
                    $"{persona.Rol}|{persona.TipoDocumento}|{persona.DocumentoEnmascarado}|{(persona.DeclaradoPorCliente ? "DECLARADO" : "PROVEEDOR")}";
            }
        }
    }
}

/// <summary>Cómo sale una persona dentro de una sección de persona.</summary>
public enum EstadoPersonaReporte
{
    /// <summary>Consultada con resultado.</summary>
    Ok,

    /// <summary>Su consulta falló: «consulta no disponible», sin tipo de error.</summary>
    NoDisponible,

    /// <summary>La celda aún no tiene dato de esta persona.</summary>
    SinResultado,
}

/// <summary>
/// Una persona de una sección de persona: posición y rol (<c>PROPIETARIO</c>/<c>LOCATARIO</c>), tipo de documento, documento
/// enmascarado (<c>****</c> + 4 últimos, <see cref="EnmascaradorDocumento"/>), si lo declaró el cliente con su rótulo
/// (<see cref="RotuloDeclarado"/>), qué se consultó (<c>RUNT_PERSONA</c>, <c>RUES</c>, <c>SIMIT</c>), su estado, la fecha de su
/// consulta y su resultado normalizado. Nunca el nombre, el documento completo ni el id interno de la persona.
/// </summary>
public sealed record PersonaReporte(
    int Orden,
    string Rol,
    string TipoDocumento,
    string DocumentoEnmascarado,
    bool DeclaradoPorCliente,
    string? RotuloDeclarado,
    string? Consulta,
    EstadoPersonaReporte Estado,
    DateTimeOffset? ConsultadoEn,
    ResultadoDetalleDto? Resultado)
{
    /// <summary>Rótulo del documento declarado por el cliente (el mismo de la interfaz, HU #13754).</summary>
    public const string Declarado = "Declarado por el cliente";
}
