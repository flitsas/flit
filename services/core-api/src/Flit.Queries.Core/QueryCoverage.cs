namespace Flit.Queries.Domain;

/// <summary>Qué pasó con un valor que el usuario pidió explícitamente.</summary>
public static class QueryCoverageResult
{
    public const string Encontrado = "encontrado";

    /// <summary>Existe en el universo consultable, pero otra condición lo dejó fuera. Se dice cuál.</summary>
    public const string Excluido = "excluido";

    /// <summary>No hay ningún trámite con ese valor al alcance de quien consulta.</summary>
    public const string NoExiste = "no_existe";
}

/// <summary>
/// Una línea del informe de cobertura: qué pasó con cada placa o VIN que el usuario pidió por
/// nombre.
///
/// <para>Es la diferencia entre un reporte en el que se confía y uno en el que no. Si alguien pega
/// dos placas, marca «tiene LT cargada» y le sale una sola fila, sin esto la lectura natural es «se
/// perdió un dato». Con esto la respuesta está en pantalla: la otra existe y la dejó fuera ese
/// filtro, o sencillamente no está.</para>
/// </summary>
public sealed record QueryCoverageItemDto(
    string Campo,
    string Valor,
    string Resultado,
    string? MotivoCampo,
    string? Motivo);
