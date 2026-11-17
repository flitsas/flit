using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Contratos;

/// <summary>
/// HU #14563 AC43 (CF-30) — <c>contracts/openapi/diagnostico.v1.yaml</c> tras el rediseño: <c>info.version</c> 1.3.1 (la 1.3.0 con la ruptura
/// del PUT del backoffice anotada (B-PR6: <c>motivo</c> requerido, el cuerpo legado sin motivo se retiró), y los cambios del cierre
/// declarados (AC23 <c>ajuste_con_tramite</c> en la auditoría, <c>href</c> en plural); la 1.3.1, la decisión D23 del SuperAdmin).
/// Lectura de texto: el repo no tiene parser
/// YAML en las pruebas.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~OpenApiDiagnosticoContratoTests
/// </code>
/// </example>
public sealed class OpenApiDiagnosticoContratoTests
{
    private static readonly Lazy<string> Yaml = new(() =>
        File.ReadAllText(Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "..", "..", "contracts", "openapi", "diagnostico.v1.yaml"))
            .ReplaceLineEndings("\n"));

    [Fact]
    public void HU14563_AC43_version_1_3_0_con_la_ruptura_del_PUT_del_backoffice_anotada()
    {
        var info = Bloque("info:", sangria: 0);

        info.Should().Contain("version: \"1.6.0\"", "HU #14582 sube a 1.6.0 (propietario actual declarado)");
        info.Should().Contain("Versión 1.3.1").And.Contain("backoffice_diagnostico").And.Contain("SuperAdmin");
        info.Should().Contain("1.3.0").And.Contain("/backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen").And.Contain("motivo");
        Bloque("CuerpoOverrideDictamen:", sangria: 4).Should().Contain("required: [veredicto, motivo, version_esperada]", "B-PR6 retiró el cuerpo legado");
    }

    // HU #14563 code review menores 2, 4 y 10 — el contrato dice lo que hace el código: version_esperada obligatoria en todo PUT,
    // claves desconocidas 422 VALOR_INVALIDO y el ETag de la bandeja con el hash de los nombres de los lotes de la página.
    [Fact]
    public void HU14563_code_review_version_esperada_claves_desconocidas_y_ETag_con_lotes()
    {
        var put = Bloque("put:", sangria: 4, desde: "operationId: corregirDictamenVehiculoBackoffice");
        put.Should().Contain("VALOR_INVALIDO").And.Contain("clave desconocida");
        put.Should().Contain("VERSION_ESPERADA_INVALIDA (campo version_esperada) si falta");

        var bandeja = Bloque("get:", sangria: 4, desde: "operationId: bandejaNotificaciones");
        bandeja.Should().Contain("<hash de los nombres de los lotes>");
    }

    [Fact]
    public void HU14563_AC43_cambios_del_cierre_declarados_en_el_contrato()
    {
        var version = Bloque("VersionDictamen:", sangria: 4);
        version.Should().Contain("ajuste_con_tramite:", "AC23: la auditoría registra el ajuste confirmado con trámite");
        version[..version.IndexOf("properties:", StringComparison.Ordinal)].Should().Contain("ajuste_con_tramite");

        var accion = Bloque("AccionNotificacion:", sangria: 4);
        accion.Should().Contain("/diagnosticos/{diagnostico_id}").And.Contain("example: /diagnosticos/");
        accion.Should().NotContain("/diagnostico/{diagnostico_id}");
    }

    /// <summary>Rutas del saneamiento en esta rama (HU #14576): las del cliente (BE-A), las de deuda y placas (BE-C) y las del backoffice (BE-B).</summary>
    private static readonly string[] RutasSaneamiento =
    [
        "/diagnosticos/{id}/vehiculos/{vid}/saneamiento:",
        "/diagnosticos/{id}/vehiculos/{vid}/saneamiento/renglones/{rid}/evidencia:",
        "/saneamientos/solicitudes:",
        "/diagnosticos/{id}/vehiculos/{vid}/deuda:",
        "/diagnosticos/{id}/vehiculos/{vid}/deuda/pdf:",
        "/diagnosticos/{id}/vehiculos/{vid}/deuda/excel:",
        "/vehiculos/deuda/excel:",
        "/vehiculos:",
        "/backoffice/saneamientos:",
        "/backoffice/saneamientos/resumen:",
        "/backoffice/saneamientos/excel:",
        "/backoffice/saneamientos/tomar:",
        "/backoffice/saneamientos/{sid}:",
        "/backoffice/saneamientos/{sid}/deuda:",
        "/backoffice/saneamientos/{sid}/tomar:",
        "/backoffice/saneamientos/{sid}/observacion:",
        "/backoffice/saneamientos/{sid}/renglones:",
        "/backoffice/saneamientos/{sid}/renglones/{rid}:",
        "/backoffice/saneamientos/{sid}/renglones/{rid}/evidencia:",
        "/backoffice/saneamientos/{sid}/cerrar:",
        "/backoffice/saneamientos/{sid}/reabrir:",
        "/backoffice/saneamientos/{sid}/anular:",
    ];

    // HU #14576 AC51 — 1.4.0 aditivo: todas las rutas del saneamiento, los campos nuevos de la grilla y de la sesión, y los esquemas
    // con los nombres que ya usa el front (D-19, D-20, D-22, D-26).
    [Fact]
    public void HU14576_AC51_version_1_4_0_con_las_rutas_y_esquemas_del_saneamiento()
    {
        var info = Bloque("info:", sangria: 0);
        info.Should().Contain("Versión 1.4.0").And.Contain("aditivo");
        var paths = Bloque("paths:", sangria: 0);
        foreach (var ruta in RutasSaneamiento)
            paths.Split('\n').Should().Contain("  /api/v1/diagnostico" + ruta, $"el contrato declara {ruta}");

        Bloque("ListaBandejaSaneamientos:", sangria: 4).Should().Contain("saneamientos:", "D-19");
        Bloque("ListadoVehiculos:", sangria: 4).Should().Contain("vehiculos:", "D-19");
        Bloque("SaneamientoBackoffice:", sangria: 4).Should().Contain("usuario_id").And.NotContain("solicitado_por_mi", "D-22");
        Bloque("RenglonSaneamiento:", sangria: 4).Should().NotContain("referencia").And.NotContain("sha256", "D-22");
        Bloque("PersonaDeuda:", sangria: 4).Should().NotContain("nombre:", "SP-11, BE-C-1");
        Bloque("SaneamientoFilaBackoffice:", sangria: 4).Should().Contain("id:").And.NotContain("elegible");
        Bloque("SaneamientoFila:", sangria: 4).Should().Contain("elegible:").And.Contain("motivo_no_elegible:");
        Yaml.Value.Should().Contain("TEXTO_INVALIDO").And.Contain("VERSION_DESACTUALIZADA").And.Contain("VERSION_INVALIDA")
            .And.Contain("DEMASIADOS_REEMPLAZOS").And.Contain("ARCHIVO_NO_ES_PDF").And.Contain("DEMASIADAS_FILAS").And.Contain("NO_EDITABLE");
        Yaml.Value.Should().NotContain("CON_ANALISIS_IA", "C2: el modo se serializa SIMPLE | CON_ANALISIS");
        Bloque("Capacidades:", sangria: 4).Should().Contain("sanear:").And.Contain("gestionarSaneamiento:");
    }

    // HU #14576 H-9 (AC51, aditivo) — la campana declara los cuatro tipos del saneamiento y `datos` (solo contadores y códigos),
    // sin quitar los tipos v1.
    [Fact]
    public void HU14576_H9_NotificacionCampana_con_tipos_del_saneamiento_y_datos()
    {
        var campana = Bloque("NotificacionCampana:", sangria: 4);
        campana.Should().Contain("enum: [ZIP_EVIDENCIAS_LISTO, ZIP_EVIDENCIAS_FALLIDO, DERIVACION_TERMINADA, LOTE_TERMINADO, SANEAMIENTO_TOMADO, SANEAMIENTO_CERRADO, SANEAMIENTO_REABIERTO, SANEAMIENTO_ANULADO]");
        var datos = campana[campana.IndexOf("        datos:", StringComparison.Ordinal)..];
        datos.Should().Contain("type: object").And.Contain("nullable: true").And.Contain("additionalProperties:")
            .And.Contain("solo contadores y códigos, sin PII");
    }

    /// <summary>Las rutas nuevas que reciben JSON y llevan el tope de 64 KiB (CR-11); la subida de la evidencia tiene el suyo.</summary>
    private static readonly string[] OperacionesConCuerpoAcotado =
    [
        "operationId: solicitarSaneamientosMasivo",
        "operationId: descargarExcelDeudaVarios",
        "operationId: tomarSaneamientosBackoffice",
        "operationId: editarObservacionSaneamientoBackoffice",
        "operationId: agregarRenglonSaneamientoBackoffice",
        "operationId: editarRenglonSaneamientoBackoffice",
        "operationId: cerrarSaneamientoBackoffice",
        "operationId: reabrirSaneamientoBackoffice",
        "operationId: anularSaneamientoBackoffice",
    ];

    // HU #14576 AP-1 y CR-11 — la fila de la bandeja declara vin e identificador (nullables y siempre presentes, como FilaPlaca) y
    // cada ruta JSON nueva declara el 413 CUERPO_MUY_GRANDE de la respuesta común.
    [Fact]
    public void HU14576_AP1_CR11_bandeja_con_vin_e_identificador_y_413_del_cuerpo_JSON()
    {
        var fila = Bloque("FilaBandejaSaneamiento:", sangria: 4);
        fila.Should().Contain("vin: { type: string, nullable: true }").And.Contain("identificador: { type: string, nullable: true }");
        fila.Split('\n').Single(l => l.TrimStart().StartsWith("required:", StringComparison.Ordinal)).Should().Contain("vin").And.Contain("identificador");
        Bloque("CuerpoMuyGrande:", sangria: 4).Should().Contain("CUERPO_MUY_GRANDE").And.Contain("65536");
        foreach (var operacion in OperacionesConCuerpoAcotado)
            Respuestas(operacion).Should().Contain("\"413\": { $ref: \"#/components/responses/CuerpoMuyGrande\" }", operacion);
        Respuestas("operationId: subirEvidenciaSaneamientoBackoffice").Should().Contain("ARCHIVO_MUY_GRANDE").And.NotContain("CuerpoMuyGrande");
    }

    /// <summary>Rutas de la ola de estadísticas y reportes (contrato-api.md §0, mapa SP-1): E1–E3, R0–R3 y B0–B6.</summary>
    private static readonly string[] RutasEstadisticasYReportes =
    [
        "/estadisticas:",
        "/backoffice/estadisticas/global:",
        "/backoffice/estadisticas:",
        "/reportes:",
        "/reportes/{tipo}:",
        "/reportes/{tipo}/excel:",
        "/reportes/{tipo}/pdf:",
        "/backoffice/reportes:",
        "/backoffice/reportes/{tipo}/global:",
        "/backoffice/reportes/{tipo}/global/excel:",
        "/backoffice/reportes/{tipo}/global/pdf:",
        "/backoffice/reportes/{tipo}:",
        "/backoffice/reportes/{tipo}/excel:",
        "/backoffice/reportes/{tipo}/pdf:",
    ];

    /// <summary>Exportaciones (R2, R3, B2, B3, B5, B6): las que declaran 422 REPORTE_DEMASIADO_GRANDE y 429 EXPORTACION_EN_CURSO.</summary>
    private static readonly string[] OperacionesExportacion =
    [
        "operationId: exportarReporteExcel",
        "operationId: exportarReportePdf",
        "operationId: exportarReporteGlobalExcelBackoffice",
        "operationId: exportarReporteGlobalPdfBackoffice",
        "operationId: exportarReporteEmpresaExcelBackoffice",
        "operationId: exportarReporteEmpresaPdfBackoffice",
    ];

    // HU #14579, BE-0 (contrato-api §0 y §8) — 1.5.0 aditivo: las 14 rutas de la ola con sus esquemas y códigos, y las capacidades nuevas.
    [Fact]
    public void BE0_version_1_5_0_con_rutas_esquemas_y_codigos_de_estadisticas_y_reportes()
    {
        var info = Bloque("info:", sangria: 0);
        info.Should().Contain("version: \"1.6.0\"").And.Contain("Versión 1.5.0").And.Contain("aditivo");
        var paths = Bloque("paths:", sangria: 0).Split('\n');
        foreach (var ruta in RutasEstadisticasYReportes)
            paths.Should().Contain("  /api/v1/diagnostico" + ruta, $"el contrato declara {ruta}");

        Bloque("EstadisticasDto:", sangria: 4).Should().ContainAll("ambito:", "periodo:", "generado_en:", "resumen:", "serie:", "veredictos:",
            "tipos_analisis:", "fuentes:", "saneamientos:", "derivaciones:", "top_empresas:");
        Bloque("CatalogoReportesDto:", sangria: 4).Should().Contain("reportes:");
        Bloque("ReporteCatalogo:", sangria: 4).Should().ContainAll("tipo:", "nombre:", "descripcion:", "contiene_pii:", "fecha_referencia:", "filtros:", "maximo_excel:", "maximo_pdf:");
        Bloque("PaginaReporteDto:", sangria: 4).Should().ContainAll("tipo:", "ambito:", "pagina:", "tamano:", "total:", "acceso_id:", "columnas:", "filas:");
        Bloque("TipoReporte:", sangria: 4).Should().Contain("enum: [vehiculos, consultas, saneamientos, derivaciones, consumo]");
        Bloque("ErrorReporteDemasiadoGrande:", sangria: 4).Should().Contain("filas:").And.Contain("maximo:");

        var capacidades = Bloque("Capacidades:", sangria: 4);
        capacidades.Should().Contain("verReportes:").And.Contain("verEstadisticasGlobales:");

        Yaml.Value.Should().Contain("REPORTE_NO_ENCONTRADO").And.Contain("EMPRESA_NO_ENCONTRADA").And.Contain("REPORTE_DEMASIADO_GRANDE")
            .And.Contain("EXPORTACION_EN_CURSO").And.Contain("IDENTIDAD_NO_DISPONIBLE").And.Contain("diagnostico.reportes.ver");
        foreach (var operacion in OperacionesExportacion)
        {
            var respuestas = Respuestas(operacion);
            foreach (var codigo in new[] { "\"400\"", "\"403\"", "\"404\"", "\"422\"", "\"429\"" })
                respuestas.Should().Contain(codigo, $"{operacion} declara {codigo}");
        }

        foreach (var operacion in new[] { "operationId: estadisticasEmpresaBackoffice", "operationId: estadisticasGlobalesBackoffice", "operationId: reporteGlobalBackoffice", "operationId: reporteEmpresaBackoffice" })
            Respuestas(operacion).Should().Contain("\"503\"", operacion);
        Respuestas("operationId: estadisticasEmpresaBackoffice").Should().Contain("EMPRESA_NO_ENCONTRADA");
        Respuestas("operationId: reporteCompania").Should().Contain("\"404\"").And.Contain("REPORTE_NO_ENCONTRADO");
    }

    /// <summary>Claves de la lista blanca por tipo (SP-16), que el contrato publica en <c>PaginaReporteDto.x-columnas</c>.</summary>
    private static readonly string[] ColumnasDeLaListaBlanca =
    [
        "fecha_diagnostico", "diagnostico_nombre", "modalidad", "tipo_analisis", "tipo_tramite", "placa", "vin", "servicio", "organismo",
        "departamento", "propietarios_documentos", "propietario_declarado_documento", "cambio_propietario", "locatario_runt_documento",
        "veredicto", "estado_consultas", "saneamiento_estado", "tramite_numero", "fuente", "estado", "motivo_na", "tipo_error", "intentos", "duracion_ms", "dato_obtenido_en",
        "reutilizada", "origen", "solicitado_en", "tomado_en", "cerrado_en", "dias_hasta_cierre", "motivo_cierre", "renglones_total",
        "renglones_pendientes", "fecha", "tipo_operacion", "motivo", "empresa_nombre", "empresa_nit", "diagnosticos", "vehiculos",
        "consultas_exitosas", "consultas_con_error", "tasa_exito", "consultas_runt", "consultas_simit", "consultas_avaluo",
        "consultas_impuesto", "saneamientos", "tramites_creados",
    ];

    // HU #14579, corrección del code review y de seguridad (CR-BE-2, CR-BE-3, CR-BE-4, CR-BE-6, CR-BE-12; SEC-6, SEC-7): el contrato es
    // autocontenido (no remite a archivos no versionados), E2 y B1–B3 declaran los dos 503 de Identidad, consumo dice qué cuenta, el
    // documento ilegible sale «<tipo> No disponible» y B5/B6 no prometen «no audita» cuando lecturas_backoffice registra el intento.
    [Fact]
    public void BE4_correccion_contrato_autocontenido_503_global_consumo_y_registro_del_intento()
    {
        Yaml.Value.Should().NotContain(".claude/").And.NotContain("contrato-api", "el contrato público no remite a archivos no versionados");

        foreach (var operacion in new[] { "estadisticasGlobalesBackoffice", "reporteGlobalBackoffice", "exportarReporteGlobalExcelBackoffice", "exportarReporteGlobalPdfBackoffice" })
            Respuestas("operationId: " + operacion).Should().Contain("\"503\": { $ref: \"#/components/responses/IdentidadListaNoDisponible\" }", operacion);
        Bloque("IdentidadListaNoDisponible:", sangria: 4).Should().ContainAll(
            "IDENTIDAD_NO_CONFIGURADA", "IDENTIDAD_NO_DISPONIBLE", "Retry-After", "solo con IDENTIDAD_NO_DISPONIBLE");

        var pagina = Bloque("PaginaReporteDto:", sangria: 4);
        pagina.Should().Contain("x-columnas:").And.Contain("CC No disponible").And.Contain("COMPLETADO y ERROR no reutilizadas");
        pagina.Should().ContainAll(ColumnasDeLaListaBlanca);
        Bloque("TipoReporte:", sangria: 4).Should().Contain("404");

        foreach (var respuesta in new[] { "ReporteDemasiadoGrande:", "ExportacionEnCurso:" })
            Bloque(respuesta, sangria: 4).Should().Contain("lecturas_backoffice").And.NotContain("No audita").And.NotContain("no audita", respuesta);
        foreach (var operacion in new[] { "reporteEmpresaBackoffice", "exportarReporteEmpresaExcelBackoffice", "exportarReporteEmpresaPdfBackoffice" })
            Bloque("get:", sangria: 4, desde: "operationId: " + operacion).Should().Contain("intento", operacion);
    }

    // HU #14582 be1-b (contrato-api §1.3, §1.1, SP-5, SP-6) — reporte vehiculos con propietario_declarado_documento,
    // cambio_propietario y locatario_runt_documento (sin locatario_origen); Excel del lote con las dos columnas fijas; plantilla con
    // la hoja «Instrucciones»; 429 DEMASIADAS_SOLICITUDES con Retry-After en Express, estimado y confirmación.
    [Fact]
    public void HU14582_b_reportes_con_cambio_de_propietario_y_429_en_express_y_confirmacion()
    {
        var pagina = Bloque("PaginaReporteDto:", sangria: 4);
        pagina.Should().Contain("propietario_declarado_documento, cambio_propietario, locatario_runt_documento")
            .And.NotContain("locatario_documento,").And.NotContain("locatario_origen");
        Yaml.Value.Should().Contain("Propietario actual declarado («TIPO ****1234» o vacío), Cambio de propietario")
            .And.Contain("Una segunda hoja «Instrucciones»");
        Bloque("DemasiadasSolicitudes:", sangria: 4).Should().Contain("DEMASIADAS_SOLICITUDES").And.Contain("Retry-After");
        foreach (var operacion in new[] { "operationId: confirmarVehiculosDiagnostico", "operationId: crearDiagnosticoExpress", "operationId: estimarDiagnosticoExpress" })
            Respuestas(operacion).Should().Contain("\"429\": { $ref: \"#/components/responses/DemasiadasSolicitudes\" }", operacion);
    }

    // HU #14582 (contrato-api §1.1/§1.2, C1, C2, C5, SP-4) — 1.6.0: propietario actual declarado con alias obsoleto de entrada,
    // cambio_propietario requerido en tablas y dictamen, ningún documento en las lecturas ni en la confirmación.
    [Fact]
    public void HU14582_version_1_6_0_propietario_declarado_y_cambio_propietario()
    {
        var info = Bloque("info:", sangria: 0);
        info.Should().Contain("version: \"1.6.0\"").And.Contain("Versión 1.6.0").And.Contain("propietario actual");

        var fila = Bloque("FilaVehiculo:", sangria: 4);
        fila.Should().Contain("required: [id, orden, identificador, tipo_identificador, tipo_identificador_etiqueta, placa, vin, terminado, celdas, cambio_propietario]");
        fila.Should().Contain("cambio_propietario:").And.NotContain("documento_declarado").And.NotContain("propietarios:");
        Yaml.Value.Should().NotContain("    DocumentoDeclaradoGrilla:").And.NotContain("    PropietarioGrilla:");
        foreach (var esquema in new[] { "FilaPlaca:", "DictamenVehiculo:", "DictamenVehiculoBackoffice:" })
            Bloque(esquema, sangria: 4).Should().Contain("cambio_propietario:", esquema);

        var resumen = Bloque("FilaResumen:", sangria: 4);
        resumen.Should().Contain("tipo_documento_propietario:").And.Contain("documento_propietario:").And.NotContain("_locatario");
        resumen.Should().Contain("Tipo_Documento_Propietario").And.Contain("Documento_Propietario");
        foreach (var entrada in new[] { "FilaCapturada:", "CorreccionFila:", "ExpressEntrada:" })
        {
            var bloque = Bloque(entrada, sangria: 4);
            bloque.Should().Contain("tipo_documento_propietario:").And.Contain("documento_propietario:", entrada);
            bloque.Should().Contain("tipo_documento_locatario:").And.Contain("deprecated: true").And.Contain("CAMPOS_EXCLUYENTES", entrada);
        }

        Bloque("VehiculoConfirmado:", sangria: 4).Should().NotContain("documento", "C5: la confirmación no devuelve documentos");
        Yaml.Value.Should().Contain("ENCABEZADO_AMBIGUO").And.Contain("El RUNT no reporta locatario")
            .And.Contain("Identificador_Vehiculo, Tipo_Documento_Propietario y Documento_Propietario");
    }

    // HU #14587 (contrato-api §3.1–§3.3, D-5) — 1.6.0 sin subir versión: rutas de Usuarios de la compañía y del backoffice, esquemas del
    // contrato, la capacidad requerida gestionarUsuarios y los códigos de error de la traducción del gRPC.
    [Fact]
    public void HU14587_usuarios_de_Diagnostico_en_la_version_1_6_0()
    {
        var info = Bloque("info:", sangria: 0);
        info.Should().Contain("version: \"1.6.0\"").And.Contain("ADR-0089").And.Contain("diagnostico.usuarios.gestionar");

        string[] rutas =
        [
            "/api/v1/diagnostico/usuarios:", "/api/v1/diagnostico/usuarios/invitaciones:", "/api/v1/diagnostico/usuarios/{usuario}/rol:",
            "/api/v1/diagnostico/usuarios/{usuario}/suspension:", "/api/v1/diagnostico/backoffice/empresas/{empresa}/usuarios:",
            "/api/v1/diagnostico/backoffice/empresas/{empresa}/usuarios/invitaciones:", "/api/v1/diagnostico/backoffice/empresas/{empresa}/usuarios/{usuario}/rol:",
            "/api/v1/diagnostico/backoffice/empresas/{empresa}/usuarios/{usuario}/suspension:", "/api/v1/diagnostico/backoffice/usuarios/empresas:",
        ];
        foreach (var ruta in rutas)
            Yaml.Value.Should().Contain("\n  " + ruta + "\n", ruta);

        Bloque("Capacidades:", sangria: 4).Should().Contain("- gestionarUsuarios").And.Contain("gestionarUsuarios:");
        Bloque("ListaUsuariosDiagnostico:", sangria: 4).Should().Contain("required: [pagina, tamano, total, usuarios, roles_asignables]");
        var fila = Bloque("UsuarioDiagnostico:", sangria: 4);
        fila.Should().Contain("required: [id, tipo, nombre, correo, rol, rol_etiqueta, estado, acciones, motivo_sin_acciones]")
            .And.Contain("@pii:medium").And.Contain("CUENTA_INACTIVA").And.Contain("CAMBIAR_ROL");
        Bloque("RolDiagnostico:", sangria: 4).Should().Contain("enum: [usuario_diagnostico, admin_diagnostico]");
        Bloque("InvitacionUsuarioDiagnostico:", sangria: 4).Should().Contain("required: [correo, nombre, rol]").And.Contain("maxLength: 254");
        Bloque("InvitacionAceptada:", sangria: 4).Should().Contain("ACEPTADA");
        Bloque("EmpresaUsuarios:", sangria: 4).Should().Contain("usuarios_diagnostico:").And.Contain("administradores_diagnostico:");
        Bloque("ListaEmpresasUsuarios:", sangria: 4).Should().Contain("empresas:");

        foreach (var codigo in new[]
        {
            "EMPRESA_NO_PERMITIDA", "FILTRO_INVALIDO", "PAGINACION_INVALIDA", "CORREO_INVALIDO", "NOMBRE_REQUERIDO", "ROL_NO_PERMITIDO",
            "ROL_NO_ASIGNABLE", "OBJETIVO_PROTEGIDO", "ACCION_SOBRE_SI_MISMO", "USUARIO_NO_ENCONTRADO", "USUARIO_SUSPENDIDO",
            "INVITACION_PENDIENTE", "ULTIMO_ADMINISTRADOR", "ROL_YA_VIGENTE", "DEMASIADAS_SOLICITUDES", "PLATAFORMA_NO_DISPONIBLE",
            "PLATAFORMA_NO_CONFIGURADA",
        })
        {
            Yaml.Value.Should().Contain(codigo, codigo);
        }

        Respuestas("operationId: invitarUsuarioDiagnostico").Should().Contain("\"202\":").And.Contain("\"429\":");
    }

    /// <summary>El bloque <c>responses:</c> de la operación cuya línea contiene <paramref name="operacion"/> (el primero después de ella).</summary>
    private static string Respuestas(string operacion)
    {
        var lineas = Yaml.Value.Split('\n');
        var ancla = Array.FindIndex(lineas, l => l.Contains(operacion, StringComparison.Ordinal));
        ancla.Should().BeGreaterThanOrEqualTo(0, $"el contrato declara {operacion}");
        var inicio = Array.FindIndex(lineas, ancla, l => l == "      responses:");
        inicio.Should().BeGreaterThan(ancla, $"{operacion} declara sus respuestas");
        var fin = inicio + 1;
        while (fin < lineas.Length && lineas[fin].StartsWith("        ", StringComparison.Ordinal))
            fin++;
        return string.Join('\n', lineas[inicio..fin]);
    }

    /// <summary>Desde la línea <paramref name="cabecera"/> con esa sangría hasta la siguiente clave del mismo nivel.</summary>
    /// <param name="desde">Si viene, la cabecera es la última anterior a la primera línea que contiene este texto.</param>
    private static string Bloque(string cabecera, int sangria, string? desde = null)
    {
        var prefijo = new string(' ', sangria);
        var lineas = Yaml.Value.Split('\n');
        var ancla = desde is null ? -1 : Array.FindIndex(lineas, l => l.Contains(desde, StringComparison.Ordinal));
        var inicio = ancla < 0
            ? Array.FindIndex(lineas, l => l == prefijo + cabecera)
            : Array.FindLastIndex(lineas, ancla, l => l == prefijo + cabecera);
        inicio.Should().BeGreaterThanOrEqualTo(0, $"el contrato declara {cabecera}");
        var fin = inicio + 1;
        while (fin < lineas.Length && (lineas[fin].Length == 0 || lineas[fin].StartsWith(prefijo + " ", StringComparison.Ordinal)))
            fin++;
        return string.Join('\n', lineas[inicio..fin]);
    }
}
