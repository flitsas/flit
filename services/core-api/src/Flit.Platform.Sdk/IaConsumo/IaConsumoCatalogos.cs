namespace Flit.Platform.Sdk.IaConsumo;

// Catálogos del contrato ia-consumo v1 (docs/contratos/ia-consumo.v1.md). Son texto en minúscula_con_guion_bajo y
// tienen que ser IDÉNTICOS en FLIT y FLITO: cambiar un valor aquí sin cambiar el contrato rompe la consolidación.

/// <summary>Quién originó la llamada (columna <c>origen</c>).</summary>
public static class IaOrigenes
{
    public const string Usuario = "usuario";
    public const string Job = "job";
    public const string Sistema = "sistema";
    /// <summary>Ruta pública por token, sin login.</summary>
    public const string Ciudadano = "ciudadano";
    public const string PruebaPrompt = "prueba_prompt";
}

/// <summary>Cómo terminó la llamada HTTP (columna <c>resultado</c>).</summary>
public static class IaResultados
{
    public const string Exito = "exito";
    public const string RateLimit = "rate_limit";
    public const string Timeout = "timeout";
    public const string Red = "red";
    /// <summary>5xx u otro HTTP no 2xx.</summary>
    public const string ErrorProveedor = "error_proveedor";
    /// <summary>4xx por el contenido o el archivo (400, 413, 415, 422).</summary>
    public const string Rechazado = "rechazado";
    /// <summary>2xx pero no interpretable o cortada.</summary>
    public const string RespuestaInvalida = "respuesta_invalida";
    public const string Cancelado = "cancelado";
    public const string NoConfigurado = "no_configurado";

    /// <summary>
    /// Clasifica una respuesta HTTP completa. Criterio común con FLITO: 429 → rate_limit; 400/413/415/422 → rechazado;
    /// cualquier otro no-2xx (401, 404, 408, 529…) → error_proveedor. Un 200 con <c>stop_reason</c> max_tokens o refusal lo
    /// clasifica el cliente como <see cref="RespuestaInvalida"/>.
    /// </summary>
    public static string DesdeHttp(int status) => status switch
    {
        >= 200 and < 300 => Exito,
        429 => RateLimit,
        400 or 413 or 415 or 422 => Rechazado,
        _ => ErrorProveedor,
    };
}

/// <summary>Tipo de tarea (columna <c>tipo_tarea</c>).</summary>
public static class IaTiposTarea
{
    public const string Ocr = "ocr";
    public const string Clasificacion = "clasificacion";
    public const string Orientacion = "orientacion";
    public const string Particion = "particion";
    public const string Chat = "chat";
    public const string Redaccion = "redaccion";
    public const string Captcha = "captcha";
    public const string ValidacionIdentidad = "validacion_identidad";
    public const string Recorte = "recorte";
    public const string Salud = "salud";
}

/// <summary>Proveedores (columna <c>proveedor</c>).</summary>
public static class IaProveedores
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public const string Kimi = "kimi";
    public const string DeepSeek = "deepseek";
    public const string Tesseract = "tesseract";
}

/// <summary>Módulos y procesos de FLIT (tabla «modulo / proceso — FLIT» del contrato).</summary>
public static class IaProcesosFlit
{
    public const string ModuloTramites = "tramites";
    public const string ModuloDrFlit = "dr_flit";
    public const string ModuloComparendos = "comparendos";
    public const string ModuloDiagnostico = "diagnostico";

    public const string OcrDocumento = "ocr_documento";
    public const string ClasificarLote = "clasificar_lote";
    public const string OcrLote = "ocr_lote";
    public const string OrientacionPagina = "orientacion_pagina";
    /// <summary>Verificación por OCR de la Licencia de Tránsito que adjunta el OT (agregado al catálogo en v1).</summary>
    public const string OcrLicenciaTransito = "ocr_licencia_transito";
    public const string Chat = "chat";
    public const string OcrComparendo = "ocr_comparendo";
    public const string EvaluarPrompt = "evaluar_prompt";
    public const string ProbarPrompt = "probar_prompt";
    public const string RedactarDictamen = "redactar_dictamen";
}

/// <summary>Valores que el contrato fija cuando falta el dato.</summary>
public static class IaValores
{
    /// <summary>Sin contexto ambiental: <c>modulo</c> y <c>proceso</c> valen esto (nunca falla).</summary>
    public const string Desconocido = "desconocido";
}
