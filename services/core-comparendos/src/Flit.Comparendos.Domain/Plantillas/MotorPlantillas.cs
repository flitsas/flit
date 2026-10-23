using System.Net;
using System.Text.RegularExpressions;

namespace Flit.Comparendos.Domain.Plantillas;

/// <summary>Una variable que pueden usar las plantillas, con un ejemplo para la vista previa.</summary>
public sealed record Variable(string Nombre, string Grupo, string Descripcion, string Ejemplo, bool EsHtml = false);

/// <summary>
/// Catálogo cerrado de variables (§6.15). Una versión con una variable que no está aquí no se puede publicar. Las
/// fechas van «1 de octubre de 2026» y el dinero «$ 1.234.567».
/// </summary>
public static class VariablesPlantilla
{
    public static readonly IReadOnlyList<Variable> Todas =
    [
        new("comparendo.numero", "Comparendo", "Número del comparendo", "11001000000045678901"),
        new("comparendo.placa", "Comparendo", "Placa del vehículo", "ABC123"),
        new("comparendo.fecha_imposicion", "Comparendo", "Fecha de la infracción", "1 de octubre de 2026"),
        new("comparendo.fecha_notificacion", "Comparendo", "Fecha de notificación", "6 de octubre de 2026"),
        new("comparendo.tiene_notificacion", "Comparendo", "«sí» si ya tiene fecha de notificación; vacío si no (para {{#si}})", "sí"),
        new("comparendo.codigo_infraccion", "Comparendo", "Código de la infracción", "C29"),
        new("comparendo.descripcion_infraccion", "Comparendo", "Descripción de la infracción", "Conducir a velocidad superior a la máxima permitida."),
        new("comparendo.direccion_infraccion", "Comparendo", "Dirección donde ocurrió", "AV BOYACA # 80-20"),
        new("comparendo.valor_total", "Comparendo", "Valor total", "$ 604.100"),
        new("comparendo.secretaria", "Comparendo", "Secretaría (organismo) que lo impuso", "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA"),
        new("comparendo.es_fotomulta", "Comparendo", "«fotomulta» o «comparendo físico»", "fotomulta"),
        new("comparendo.fecha_caducidad", "Comparendo", "Fecha de caducidad", "6 de octubre de 2027"),
        new("comparendo.fecha_prescripcion", "Comparendo", "Fecha de prescripción", "8 de octubre de 2029"),
        new("comparendo.estado_legal", "Comparendo", "Estado legal a hoy", "En tiempo"),
        new("descuento.ventana", "Descuento", "Ventana de descuento vigente", "50% de descuento"),
        new("descuento.porcentaje", "Descuento", "Porcentaje vigente (50, 25 o 0)", "50"),
        new("descuento.fecha_fin_50", "Descuento", "Último día del 50%", "21 de octubre de 2026"),
        new("descuento.fecha_fin_25", "Descuento", "Último día del 25%", "12 de noviembre de 2026"),
        new("ot.nombre", "Organismo de tránsito", "Nombre del organismo", "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA"),
        new("ot.codigo", "Organismo de tránsito", "Código RUNT", "11001000"),
        new("ot.municipio", "Organismo de tránsito", "Municipio", "BOGOTA"),
        new("ot.departamento", "Organismo de tránsito", "Departamento", "BOGOTA D.C."),
        new("peticionario.nombre", "Peticionario", "Razón social o nombre del titular", "RENTING DE COLOMBIA S.A.S."),
        new("peticionario.tipo_documento", "Peticionario", "Tipo de documento", "NIT"),
        new("peticionario.numero_documento", "Peticionario", "Número de documento", "900123456-7"),
        new("peticionario.direccion", "Peticionario", "Dirección de notificación", "CALLE 100 # 9A-45, BOGOTA"),
        new("rl.nombres", "Representante legal", "Nombre del representante", "MARÍA FERNANDA GÓMEZ"),
        new("rl.tipo_documento", "Representante legal", "Tipo de documento", "CC"),
        new("rl.numero_documento", "Representante legal", "Número de documento", "52123456"),
        new("rl.cargo", "Representante legal", "Cargo", "Representante legal"),
        new("rl.correo", "Representante legal", "Correo", "juridico@renting.com.co"),
        new("rl.firma_imagen", "Representante legal", "Imagen de la firma", "[firma]", EsHtml: true),
        new("empresa.razon_social", "Empresa", "Razón social de la gestora", "RENTING DE COLOMBIA S.A.S."),
        new("empresa.nit", "Empresa", "NIT de la gestora", "900123456-7"),
        new("empresa.correo_respuesta", "Empresa", "Correo para respuestas", "juridico@renting.com.co"),
        new("dp.radicado", "Derecho de petición", "Radicado interno", "DA-2026-000123"),
        new("dp.fecha", "Derecho de petición", "Fecha de radicación", "9 de octubre de 2026"),
        new("dp.tipologia_nombre", "Derecho de petición", "Nombre de la tesis", "Solicitud de audiencia"),
        new("dp.fundamento", "Derecho de petición", "Fundamento jurídico de la tesis (del catálogo)", "Solicita la audiencia pública…"),
        new("conductor.nombres", "Conductor", "Nombre del conductor", "JUAN PÉREZ"),
        new("conductor.tipo_documento", "Conductor", "Tipo de documento", "CC"),
        new("conductor.numero_documento", "Conductor", "Número de documento", "1020304050"),
        new("conductor.correo", "Conductor", "Correo", "juan.perez@correo.com"),
        new("conductor.telefono", "Conductor", "Teléfono", "3001234567"),
        new("conductor.direccion", "Conductor", "Dirección", "CRA 7 # 45-10"),
        new("conductor.ciudad", "Conductor", "Ciudad", "BOGOTA"),
        new("portal.enlace", "Portal", "Enlace al portal del conductor", "https://comparendos.flitsas.online/portal/ejemplo"),
        new("portal.vence", "Portal", "Hasta cuándo sirve el enlace", "12 de diciembre de 2026"),
        new("hoy", "Sistema", "Fecha de hoy", "9 de octubre de 2026"),
        new("anio", "Sistema", "Año en curso", "2026"),
    ];

    private static readonly Dictionary<string, Variable> PorNombre = Todas.ToDictionary(v => v.Nombre, StringComparer.Ordinal);

    public static bool Existe(string nombre) => PorNombre.ContainsKey(nombre);

    public static bool EsHtml(string nombre) => PorNombre.TryGetValue(nombre, out var v) && v.EsHtml;

    public static IReadOnlyDictionary<string, string> Ejemplos() => Todas.ToDictionary(v => v.Nombre, v => v.Ejemplo, StringComparer.Ordinal);
}

/// <summary>Resultado de validar una plantilla.</summary>
public sealed record ValidacionPlantilla(IReadOnlyList<string> Usadas, IReadOnlyList<string> Desconocidas)
{
    public bool Valida => Desconocidas.Count == 0;
}

/// <summary>
/// Plantillas con variables <c>{{grupo.nombre}}</c>: extracción, validación contra el catálogo y reemplazo. Los valores
/// se escapan como HTML (salvo las variables marcadas como HTML, p. ej. la imagen de la firma); una variable sin valor
/// queda vacía.
/// <para>
/// Bloques condicionales: <c>{{#si variable}}…{{#sino}}…{{/si}}</c> (con valor) y
/// <c>{{#si variable == "texto"}}…{{/si}}</c> o <c>!=</c> (comparación exacta, sin distinguir mayúsculas). Se pueden
/// anidar; <c>{{#sino}}</c> es opcional. Así un mismo texto cubre fotomulta o comparendo físico, con o sin conductor, etc.
/// </para>
/// </summary>
public static partial class MotorPlantillas
{
    // Bloque más interno: su cuerpo no abre otro {{#si}}. Se reemplaza de adentro hacia afuera.
    [GeneratedRegex(@"\{\{#si\s+([a-z_]+(?:\.[a-z_0-9]+)?)\s*(?:(==|!=)\s*""([^""]*)""\s*)?\}\}((?:(?!\{\{#si\s).)*?)\{\{/si\}\}", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex BloqueRegex();

    [GeneratedRegex(@"\{\{\s*(#si\b[^}]*|#sino|/si)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex EtiquetaBloqueRegex();

    [GeneratedRegex(@"\{\{\s*([a-z_]+(?:\.[a-z_0-9]+)?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableRegex();

    [GeneratedRegex(@"\{\{([^}]*)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex CualquierLlaveRegex();

    public static ValidacionPlantilla Validar(params string?[] textos)
    {
        var usadas = new List<string>();
        var desconocidas = new List<string>();
        void Usar(string nombre)
        {
            if (VariablesPlantilla.Existe(nombre))
            {
                if (!usadas.Contains(nombre))
                    usadas.Add(nombre);
            }
            else if (!desconocidas.Contains(nombre))
            {
                desconocidas.Add(nombre);
            }
        }

        foreach (var texto in textos.Where(t => !string.IsNullOrEmpty(t)))
        {
            // Los bloques bien formados se validan por su variable; lo que sobre ({{#si}} sin cerrar, {{/si}} suelto)
            // queda como «desconocida» y la versión no se puede publicar.
            var resto = texto!;
            while (BloqueRegex().Match(resto) is { Success: true } b)
            {
                Usar(b.Groups[1].Value);
                resto = resto[..b.Index] + b.Groups[4].Value.Replace("{{#sino}}", string.Empty, StringComparison.Ordinal) + resto[(b.Index + b.Length)..];
            }

            foreach (Match e in EtiquetaBloqueRegex().Matches(resto))
            {
                var etiqueta = e.Groups[1].Value.Trim() + " (bloque sin cerrar o mal formado)";
                if (!desconocidas.Contains(etiqueta))
                    desconocidas.Add(etiqueta);
            }

            resto = EtiquetaBloqueRegex().Replace(resto, string.Empty);
            foreach (Match m in CualquierLlaveRegex().Matches(resto))
                Usar(m.Groups[1].Value.Trim());
        }

        return new ValidacionPlantilla(usadas, desconocidas);
    }

    public static string Renderizar(string plantilla, IReadOnlyDictionary<string, string?> valores, bool html = true)
    {
        ArgumentNullException.ThrowIfNull(plantilla);
        ArgumentNullException.ThrowIfNull(valores);
        var texto = plantilla;
        while (BloqueRegex().Match(texto) is { Success: true } b)
        {
            var valor = valores.TryGetValue(b.Groups[1].Value, out var v) ? v : null;
            var cumple = b.Groups[2].Value switch
            {
                "==" => string.Equals(valor ?? string.Empty, b.Groups[3].Value, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(valor ?? string.Empty, b.Groups[3].Value, StringComparison.OrdinalIgnoreCase),
                _ => !string.IsNullOrWhiteSpace(valor),
            };
            var partes = b.Groups[4].Value.Split("{{#sino}}", 2, StringSplitOptions.None);
            var elegido = cumple ? partes[0] : partes.Length > 1 ? partes[1] : string.Empty;
            texto = texto[..b.Index] + elegido + texto[(b.Index + b.Length)..];
        }

        return VariableRegex().Replace(texto, m =>
        {
            var nombre = m.Groups[1].Value;
            var valor = valores.TryGetValue(nombre, out var v) ? v ?? string.Empty : string.Empty;
            return !html || VariablesPlantilla.EsHtml(nombre) ? valor : WebUtility.HtmlEncode(valor);
        });
    }
}
