using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Documento de una persona del vehículo en claro, solo en memoria y solo durante la llamada a la fuente (HU #13792, R-3): lo
/// arma el ejecutor de la celda justo antes de consultar, a partir del cifrado de <c>personas_vehiculo</c>. Nunca se guarda ni
/// se registra: <see cref="ToString"/> enmascara el número para que un log o una excepción no lo filtren.
/// </summary>
/// <param name="Tipo">Tipo de documento (CC, CE, NIT, PAS, TI…).</param>
/// <param name="Numero">Número en claro.</param>
public sealed record DocumentoPersona(string Tipo, string Numero)
{
    /// <summary>Verdadero si la persona es jurídica (NIT): se consulta en el RUES, no en el RUNT persona (decisión Q2).</summary>
    public bool EsNit => string.Equals(Tipo?.Trim(), "NIT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Solo el tipo: el número nunca sale en texto.</summary>
    public override string ToString() => $"{Tipo} ***";
}

/// <summary>
/// Respuesta de un puerto de fuente por persona (HU #13792; la reutiliza SIMIT por persona, F05-H5): el resultado normalizado o
/// el error tipado (<see cref="ErrorConsulta"/>, mismo vocabulario de <see cref="ClasificadorErrores"/>), nunca los dos, con el
/// proveedor que respondió y su transacción para el intento auditable. El resultado nunca trae nombres, razón social ni documentos.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(empresa, documento, ct);
/// if (r.Error is { } error) return error.TipoError; // TIMEOUT se reintenta, RESPUESTA_INVALIDA no
/// </code>
/// </example>
public sealed record RespuestaFuente<T>(T? Resultado, ErrorConsulta? Error, ServicioConsulta? Servicio, string? TransaccionProveedorId)
    where T : class
{
    public bool Exitosa => Resultado is not null && Error is null;

    public static RespuestaFuente<T> Exito(T resultado, ServicioConsulta? servicio, string? transaccion) => new(resultado, null, servicio, transaccion);

    public static RespuestaFuente<T> Fallo(ErrorConsulta error, ServicioConsulta? servicio = null, string? transaccion = null) =>
        new(null, error, servicio, transaccion);
}

/// <summary>Errores fijos de las fuentes por persona: el mensaje es técnico y nunca lleva el documento ni datos del proveedor.</summary>
public static class ErroresFuentePersona
{
    public const string Timeout = "TIMEOUT";
    public const string RespuestaInvalida = "RESPUESTA_INVALIDA";
    public const string NoConectado = "NO_CONECTADO";

    public static ErrorConsulta NoConectada(string fuente) => new(NoConectado, $"La fuente {fuente} no está conectada en este ambiente.");
}
