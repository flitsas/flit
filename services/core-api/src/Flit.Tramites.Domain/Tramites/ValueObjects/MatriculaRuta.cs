namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Ruta de una matrícula inicial (Epic #12550, ADR-0059 §Ruta Corta). La decide lo que el RUNT devuelve
/// para el VIN, nunca el gestor: con placa el vehículo ya la tiene preasignada y el trámite llega al
/// organismo en <c>entregado</c> (<see cref="Corta"/>); sin placa el organismo la asigna en
/// <c>preasignacion</c> (<see cref="Larga"/>). Es el mismo criterio que
/// <c>TramiteTransitionPolicy.DestinoDeRadicacion</c> aplica al radicar, adelantado al paso 1 para que
/// la pantalla y el contrato lo digan con nombre.
/// </summary>
public static class MatriculaRuta
{
    public const string Larga = "larga";
    public const string Corta = "corta";

    public static string Por(bool tienePlaca) => tienePlaca ? Corta : Larga;
}
