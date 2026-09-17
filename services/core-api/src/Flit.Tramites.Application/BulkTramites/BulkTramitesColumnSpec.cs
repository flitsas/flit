namespace Flit.Tramites.Application.BulkTramites;

/// <summary>
/// Definición de una columna de la hoja de datos de una plantilla de carga masiva: el
/// encabezado (fila 1, contrato con el parser de HU #12522), la guía completa que va en la hoja
/// «Instrucciones», y —cuando aplica— la lista cerrada de valores admitidos que alimenta el
/// desplegable.
///
/// <para><paramref name="Ayuda"/> es el texto del emergente de la celda cuando la guía completa no
/// cabe: Excel limita ese mensaje a 255 caracteres y, si se pasa, NO lo trunca: da el archivo por
/// dañado y descarta todas las validaciones de la hoja al «repararlo» (Bug #12651). Si es
/// <c>null</c>, el emergente muestra la guía tal cual.</para>
/// </summary>
public sealed record BulkTramitesColumnSpec(
    string Header,
    string Guia,
    IReadOnlyList<string>? Opciones = null,
    string? Ayuda = null)
{
    /// <summary>Texto que se muestra al pararse en la celda.</summary>
    public string Prompt => Ayuda ?? Guia;
}

/// <summary>
/// Catálogos que solo se conocen en tiempo de generación, porque salen de la base y del tenant:
/// los tipos de trámite vigentes (plantilla «Otros») y los organismos de tránsito que la empresa
/// tiene habilitados (plantilla «Matrícula»). Alimentan únicamente los desplegables.
/// </summary>
public sealed record BulkTramitesDynamicCatalogs(
    IReadOnlyList<string>? TiposTramite = null,
    IReadOnlyList<string>? OrganismosTransito = null);
