namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>De dónde salió el dato de una celda (<c>diagnostico.consultas.origen</c>, plan §5, HU #13723).</summary>
public enum OrigenCelda
{
    /// <summary>Consulta a la fuente por core-consultas.</summary>
    Api,

    /// <summary>Llenado manual del backoffice.</summary>
    Manual,
}
