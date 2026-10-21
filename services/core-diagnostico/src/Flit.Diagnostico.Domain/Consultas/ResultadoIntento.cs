namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Resultado de un intento (<c>diagnostico.intentos_consulta.estado</c>, plan §5, HU #13723). En la base, como los demás
/// códigos del schema: <c>SUCCESS</c> o <c>ERROR</c>.
/// </summary>
public enum ResultadoIntento
{
    /// <summary>La fuente respondió con dato.</summary>
    Success,

    /// <summary>La fuente falló; el tipo va en <c>tipo_error</c>.</summary>
    Error,
}
