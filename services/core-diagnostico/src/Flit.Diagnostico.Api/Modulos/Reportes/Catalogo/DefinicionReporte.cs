namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Lista blanca cerrada de columnas por tipo de reporte (HU #14579, BE-2; contrato-api §4; SP-16). <c>ReportesDtoTests</c> falla si sale
/// una clave que no está aquí. En <see cref="AlcanceReporte.BackofficeGlobal"/> todas empiezan con <see cref="EmpresaNombre"/>; la columna
/// <c>origen</c> de consultas solo sale en el backoffice. Nunca <c>usuario_*</c>, <c>persona_id</c>, <c>consulta_id</c>, <c>tenant_id</c>
/// ni el nombre de una persona.
/// </summary>
internal static class DefinicionReporte
{
    public const string Texto = "texto";
    public const string Numero = "numero";
    public const string Fecha = "fecha";
    public const string FechaHora = "fecha_hora";
    public const string Moneda = "moneda";
    public const string Porcentaje = "porcentaje";

    /// <summary>Nombre de la compañía (primera columna en la vista global y en consumo).</summary>
    public const string EmpresaNombre = "empresa_nombre";

    /// <summary>Origen de la celda (API/MANUAL): solo en el backoffice.</summary>
    public const string Origen = "origen";

    /// <summary>Texto de un documento que no se pudo descifrar; sale precedido de su tipo («CC No disponible», CR-BE-12).</summary>
    public const string NoDisponible = "No disponible";

    private static readonly ColumnaReporte ColumnaEmpresa = new(EmpresaNombre, "Compañía", Texto, false);

    private static readonly ColumnaReporte[] Vehiculos =
    [
        new("fecha_diagnostico", "Fecha del diagnóstico", FechaHora, false),
        new("diagnostico_nombre", "Diagnóstico", Texto, false),
        new("modalidad", "Modalidad", Texto, false),
        new("tipo_analisis", "Tipo de análisis", Texto, false),
        new("tipo_tramite", "Tipo de trámite", Texto, false),
        new("placa", "Placa", Texto, true),
        new("vin", "VIN", Texto, true),
        new("servicio", "Servicio", Texto, false),
        new("organismo", "Organismo de tránsito", Texto, false),
        new("departamento", "Departamento", Texto, false),
        new("propietarios_documentos", "Propietarios (documento)", Texto, true),
        // HU #14582 (contrato §1.3, SP-6): el declarado es el propietario actual; el locatario sale solo del RUNT.
        new("propietario_declarado_documento", "Propietario actual declarado (documento)", Texto, true),
        new("cambio_propietario", "Cambio de propietario", Texto, false),
        new("locatario_runt_documento", "Locatario RUNT (documento)", Texto, true),
        new("veredicto", "Veredicto", Texto, false),
        new("estado_consultas", "Estado de las consultas", Texto, false),
        new("saneamiento_estado", "Saneamiento", Texto, false),
        new("tramite_numero", "Trámite", Texto, false),
    ];

    private static readonly ColumnaReporte[] Consultas =
    [
        new("fecha_diagnostico", "Fecha del diagnóstico", FechaHora, false),
        new("diagnostico_nombre", "Diagnóstico", Texto, false),
        new("placa", "Placa", Texto, true),
        new("fuente", "Fuente", Texto, false),
        new("estado", "Estado", Texto, false),
        new("motivo_na", "Motivo no aplica", Texto, false),
        new("tipo_error", "Tipo de error", Texto, false),
        new("intentos", "Intentos", Numero, false),
        new("duracion_ms", "Duración total (ms)", Numero, false),
        new("dato_obtenido_en", "Dato obtenido en", FechaHora, false),
        new("reutilizada", "Reutilizada", Texto, false),
    ];

    private static readonly ColumnaReporte[] Saneamientos =
    [
        new("solicitado_en", "Solicitado en", FechaHora, false),
        new("placa", "Placa", Texto, true),
        new("diagnostico_nombre", "Diagnóstico", Texto, false),
        new("estado", "Estado", Texto, false),
        new("tomado_en", "Tomado en", FechaHora, false),
        new("cerrado_en", "Cerrado en", FechaHora, false),
        new("dias_hasta_cierre", "Días hasta el cierre", Numero, false),
        new("motivo_cierre", "Motivo de cierre", Texto, false),
        new("renglones_total", "Renglones", Numero, false),
        new("renglones_pendientes", "Renglones pendientes", Numero, false),
    ];

    private static readonly ColumnaReporte[] Derivaciones =
    [
        new("fecha", "Fecha", FechaHora, false),
        new("placa", "Placa", Texto, true),
        new("diagnostico_nombre", "Diagnóstico", Texto, false),
        new("tipo_operacion", "Operación", Texto, false),
        new("tipo_tramite", "Tipo de trámite", Texto, false),
        new("estado", "Estado", Texto, false),
        new("motivo", "Motivo", Texto, false),
        new("tramite_numero", "Trámite", Texto, false),
    ];

    private static readonly ColumnaReporte[] Consumo =
    [
        ColumnaEmpresa,
        new("empresa_nit", "NIT", Texto, false),
        new("diagnosticos", "Diagnósticos", Numero, false),
        new("vehiculos", "Vehículos", Numero, false),
        new("consultas_exitosas", "Consultas exitosas", Numero, false),
        new("consultas_con_error", "Consultas con error", Numero, false),
        new("tasa_exito", "Tasa de éxito", Porcentaje, false),
        new("consultas_runt", "Consultas RUNT", Numero, false),
        new("consultas_simit", "Consultas SIMIT", Numero, false),
        new("consultas_avaluo", "Consultas avalúo", Numero, false),
        new("consultas_impuesto", "Consultas impuesto", Numero, false),
        new("saneamientos", "Saneamientos solicitados", Numero, false),
        new("tramites_creados", "Trámites creados", Numero, false),
    ];

    /// <summary>Las columnas del tipo en el ámbito, en el orden del contrato.</summary>
    public static IReadOnlyList<ColumnaReporte> Columnas(TipoReporte tipo, AlcanceReporte alcance)
    {
        IEnumerable<ColumnaReporte> columnas = tipo switch
        {
            TipoReporte.Vehiculos => Vehiculos,
            TipoReporte.Consultas => alcance == AlcanceReporte.Compania ? Consultas : [.. Consultas, new(Origen, "Origen", Texto, false)],
            TipoReporte.Saneamientos => Saneamientos,
            TipoReporte.Derivaciones => Derivaciones,
            TipoReporte.Consumo => Consumo,
            _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
        };
        if (alcance == AlcanceReporte.BackofficeGlobal && tipo != TipoReporte.Consumo)
            columnas = columnas.Prepend(ColumnaEmpresa);
        return [.. columnas];
    }
}
