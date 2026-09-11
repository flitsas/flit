namespace Flit.Tramites.Application.BulkTramites;

/// <summary>
/// Definición de una columna de la hoja de datos de una plantilla de carga masiva: el
/// encabezado (fila 1, contrato con el parser de HU #12522), la guía que se muestra como
/// emergente de ayuda en Excel, y —cuando aplica— la lista cerrada de valores admitidos que
/// alimenta el desplegable.
/// </summary>
public sealed record BulkTramitesColumnSpec(
    string Header,
    string Guia,
    IReadOnlyList<string>? Opciones = null);
