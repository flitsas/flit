namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>
/// Contenido leído de un archivo de carga (HU #13749), igual para .xlsx y .csv: los encabezados de la fila 1 y las filas
/// de datos no vacías con su número de fila (el encabezado es la fila 1). Una celda ausente es null.
/// </summary>
internal sealed record TablaCarga(IReadOnlyList<string?> Encabezados, IReadOnlyList<FilaLeida> Filas);

/// <summary>Fila de datos: su número en la hoja o en el CSV y las celdas por posición de columna.</summary>
internal sealed record FilaLeida(int Numero, IReadOnlyList<string?> Celdas);
