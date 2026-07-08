namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Estado de una fila de prenda (IT-3). El versionado (R17) es intrínseco: cada modificación crea una fila
/// nueva <c>vigente</c> y marca la anterior <c>reemplazada</c>, dejando el historial completo. La invariante
/// "a lo sumo una vigente por instancia" la garantiza un índice único parcial en la BD.
/// </summary>
public static class PrendaEstado
{
    public const string Vigente = "vigente";
    public const string Reemplazada = "reemplazada";
}
