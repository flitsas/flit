using System.Text.Json;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Licencias de conducción a partir del JSON del conductor (Kyverum <c>licencias</c> / Verifik <c>licenses</c>),
/// incluidas las que no están activas. No copia nombres de proveedor ni otros campos del cuerpo.
/// </summary>
internal static class LicenciasConductor
{
    public static IReadOnlyList<LicenciaDetalle> Leer(JsonElement raiz)
    {
        var lista = Lista(raiz) ?? ListaEn(raiz, "data") ?? ListaEn(raiz, "resultado");
        if (lista is not { } arreglo)
            return [];

        var filas = new List<LicenciaDetalle>();
        foreach (var item in arreglo.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var numero = Texto(item, "numeroLicencia", "licenceNumber", "numero", "numero_licencia");
            var estado = Texto(item, "estadoLicencia", "status", "estado");
            var organismo = Texto(item, "otExpide", "organismo", "authorityTransit");
            var expedida = Texto(item, "fechaExpedicion", "expeditionDate");
            var restricciones = Texto(item, "restricciones", "restrictions");
            var desde = Texto(item, "startDateSuspension", "suspension_desde");
            var hastaSuspension = Texto(item, "endDateSuspension", "suspension_hasta");
            var detalle = Arreglo(item, "detalleLicencia") ?? Arreglo(item, "detalle_licencia");
            if (detalle is { } categorias && categorias.GetArrayLength() > 0)
            {
                foreach (var categoria in categorias.EnumerateArray())
                {
                    if (categoria.ValueKind != JsonValueKind.Object)
                        continue;
                    var nombre = Texto(categoria, "categoria", "category");
                    var hasta = Texto(categoria, "fechaVencimiento", "dueDate", "vigente_hasta", "vigenteHasta");
                    if (nombre is null && numero is null)
                        continue;
                    filas.Add(new LicenciaDetalle(nombre, estado, hasta, numero, expedida, organismo, restricciones, desde, hastaSuspension));
                }
            }
            else
            {
                var nombre = Texto(item, "categoria", "category");
                var hasta = Texto(item, "fechaVencimiento", "dueDate", "vigente_hasta", "vigenteHasta");
                if (nombre is null && numero is null && estado is null)
                    continue;
                filas.Add(new LicenciaDetalle(nombre, estado, hasta, numero, expedida, organismo, restricciones, desde, hastaSuspension));
            }
        }

        return filas;
    }

    private static JsonElement? Lista(JsonElement objeto) =>
        Arreglo(objeto, "licencias") ?? Arreglo(objeto, "licenses");

    private static JsonElement? ListaEn(JsonElement objeto, string propiedad) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(propiedad, out var hijo) ? Lista(hijo) : null;

    private static JsonElement? Arreglo(JsonElement objeto, string propiedad) =>
        objeto.ValueKind == JsonValueKind.Object
        && objeto.TryGetProperty(propiedad, out var valor)
        && valor.ValueKind == JsonValueKind.Array
            ? valor
            : null;

    /// <summary>
    /// Completa inscripción, paz y salvo, certificados y trámites desde el cuerpo del conductor. No copia nombres ni el documento.
    /// </summary>
    public static ResultadoRuntPersonaDetalle Completar(ResultadoRuntPersonaDetalle actual, JsonElement crudo)
    {
        var data = Data(crudo);
        var licencias = Leer(crudo);
        var infracciones = Objeto(data, "infractions") ?? Objeto(data, "infracciones");
        var certificados = Certificados(data);
        var solicitudes = Solicitudes(data);
        return actual with
        {
            Licencias = licencias.Count > 0 ? licencias : actual.Licencias,
            FechaInscripcion = Texto(data, "inscriptionDate", "fechaInscripcion") ?? actual.FechaInscripcion,
            TieneMultas = infracciones is { } inf ? Texto(inf, "tieneMultas", "tiene_multas") : actual.TieneMultas,
            PazYSalvo = infracciones is { } paz ? Texto(paz, "nroPazYSalvo", "nro_paz_y_salvo") : actual.PazYSalvo,
            Certificados = certificados.Count > 0 ? certificados : actual.Certificados,
            Solicitudes = solicitudes.Count > 0 ? solicitudes : actual.Solicitudes,
        };
    }

    private static JsonElement Data(JsonElement raiz) =>
        raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data : raiz;

    private static List<CertificadoAptitudDetalle> Certificados(JsonElement data)
    {
        var lista = Arreglo(data, "aptitudeCertificates") ?? Arreglo(data, "certificadosAptitud");
        var filas = new List<CertificadoAptitudDetalle>();
        if (lista is not { } arreglo)
            return filas;
        foreach (var item in arreglo.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var fila = new CertificadoAptitudDetalle(
                Texto(item, "categoria", "category"), Texto(item, "estadoDocumento", "estado"), Texto(item, "fechaExpedicion"),
                Texto(item, "fechaVencimiento"), Texto(item, "tipoCertificado", "tipo"), Texto(item, "numeroCertificadoAptitud", "numero"),
                Texto(item, "nombreCea", "entidad"));
            if (fila.Categoria is not null || fila.Numero is not null || fila.Tipo is not null)
                filas.Add(fila);
        }

        return filas;
    }

    private static List<SolicitudConductorDetalle> Solicitudes(JsonElement data)
    {
        var lista = Arreglo(data, "requests") ?? Arreglo(data, "solicitudes");
        var filas = new List<SolicitudConductorDetalle>();
        if (lista is not { } arreglo)
            return filas;
        foreach (var item in arreglo.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var fila = new SolicitudConductorDetalle(
                Texto(item, "nombreTramite", "tramite"), Texto(item, "estadoSolicitud"), Texto(item, "estadoTramite"),
                Texto(item, "fechaSolicitud", "fecha"), Texto(item, "entidad"), Texto(item, "registro"));
            if (fila.Tramite is not null || fila.Entidad is not null)
                filas.Add(fila);
        }

        return filas;
    }

    private static JsonElement? Objeto(JsonElement objeto, string propiedad) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.Object ? valor : null;

    private static string? Texto(JsonElement objeto, params string[] propiedades)
    {
        foreach (var propiedad in propiedades)
        {
            if (objeto.ValueKind != JsonValueKind.Object || !objeto.TryGetProperty(propiedad, out var valor))
                continue;
            var texto = valor.ValueKind switch
            {
                JsonValueKind.String => valor.GetString()?.Trim(),
                JsonValueKind.Number => valor.GetRawText(),
                JsonValueKind.True => "SI",
                JsonValueKind.False => "NO",
                JsonValueKind.Array => string.Join(", ", valor.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : null).Where(v => !string.IsNullOrWhiteSpace(v))),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(texto))
                return texto;
        }

        return null;
    }
}
