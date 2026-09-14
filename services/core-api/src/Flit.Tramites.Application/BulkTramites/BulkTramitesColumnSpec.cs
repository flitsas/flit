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

/// <summary>
/// Catálogos que solo se conocen en tiempo de generación, porque salen de la base y del tenant:
/// los tipos de trámite vigentes (plantilla «Otros») y los organismos de tránsito que la empresa
/// tiene habilitados (plantilla «Matrícula»). Alimentan únicamente los desplegables.
/// </summary>
public sealed record BulkTramitesDynamicCatalogs(
    IReadOnlyList<string>? TiposTramite = null,
    IReadOnlyList<string>? OrganismosTransito = null);
