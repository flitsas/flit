namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Proveedor que respondió un intento (<c>diagnostico.intentos_consulta.servicio</c>, plan §5, 12659 #2, HU #13723). El código
/// en la base va en mayúsculas sin separador (<c>SMARTCLOUD</c>, <c>MERCADOLIBRE</c>, <c>BASEGRAVABLE</c>, <c>FLITFINES</c>).
/// </summary>
public enum ServicioConsulta
{
    Verifik,
    Kyverum,
    Intempo,
    Smartcloud,
    Fasecolda,
    Mercadolibre,
    Basegravable,
    Scraping,
    Flitfines,
    Otro,
}
