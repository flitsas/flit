namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Frases del servicio, en memoria. Una frase nueva cae en <c>NO_CONFIRMADO</c>.
/// El texto del proveedor no sale de aquí.
/// </summary>
internal static class ClasificadorRespuestaImpuesto
{
    public static string Clasificar(int typeError, string descripcion, string desarrollo, string validez)
    {
        var texto = TextoImpuesto.Normalizar($"{descripcion} {desarrollo} {validez}");
        if (Contiene(texto, "page.goto") || Contiene(texto, "timeout") || Contiene(texto, "tiempo de espera"))
            return "TIMEOUT";
        if (Contiene(texto, "error seleccionando modelo"))
            return "MODELO_NO_ACEPTADO";
        if (Contiene(texto, "documento no corresponde"))
            return "DOCUMENTO_NO_COINCIDE";
        if (Contiene(texto, "no encontrado") || Contiene(texto, "no registrado") || Contiene(texto, "no registrada") || Contiene(texto, "placa no encontrada"))
            return "VEHICULO_NO_ENCONTRADO";
        if (Contiene(texto, "cancelado") || Contiene(texto, "traslado de cuenta"))
            return "VEHICULO_NO_CONSULTABLE";
        if (Contiene(texto, "requiere_actualizacion") || Contiene(texto, "actualice sus datos"))
            return "REQUIERE_ACTUALIZACION";
        if (Contiene(texto, "credenciales faltantes") || Contiene(texto, "taxrequestdto.credentials"))
            return "AUTENTICACION";
        if (Contiene(texto, "error procesando la solicitud"))
            return "HTTP_5XX";
        if (typeError == 2)
            return "HTTP_5XX";
        return "NO_CONFIRMADO";
    }

    private static bool Contiene(string texto, string frase) => texto.Contains(frase, StringComparison.Ordinal);
}
