namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Señal de control del consumidor de resultados (ADR-0075, Q4, U2 = a; HU #13737): NO es un error. La lanza el consumidor
/// cuando un resultado transitorio tiene reintentos disponibles (la celda ya quedó PENDIENTE con su intento, en un scope propio)
/// o cuando vuelve un resultado demorado cuya espera aún no vence. El SDK revierte la transacción del mensaje (que no escribió
/// nada) y lleva ese MISMO resultado a <c>diagnostico.consultas-resultados.retry.n</c>, configurada en 5, 10 y 20 s
/// (<see cref="ProgramadorReintentos.Esperas"/>); al volver, el consumidor publica el trabajo nuevo. Su mensaje es técnico y fijo:
/// nunca lleva documentos ni datos de la fuente.
/// </summary>
/// <example>
/// <code>
/// throw new ReintentoProgramadoException("Reintento programado de la celda: el resultado espera en la cola de reintentos.");
/// </code>
/// </example>
public sealed class ReintentoProgramadoException : Exception
{
    public ReintentoProgramadoException()
        : base("Reintento programado: el resultado espera en la cola de reintentos.")
    {
    }

    public ReintentoProgramadoException(string message)
        : base(message)
    {
    }

    public ReintentoProgramadoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
