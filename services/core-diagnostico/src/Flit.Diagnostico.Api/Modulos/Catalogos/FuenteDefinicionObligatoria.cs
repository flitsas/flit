using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Línea de <c>diagnostico.fuentes_definicion_obligatoria</c> (ADR-0078). Catálogo global de FLIT: no implementa
/// <c>IEntidadDeEmpresa</c>. La clave es <c>(DefinicionId, Fuente)</c>. No tiene token de concurrencia: el PUT
/// reemplaza el conjunto. <see cref="Fuente.Runt"/> no se guarda aquí.
/// </summary>
public sealed class FuenteDefinicionObligatoria
{
    /// <summary>Cabecera dueña de la línea.</summary>
    public Guid DefinicionId { get; init; }

    /// <summary>Fuente de la definición. Una fila por fuente.</summary>
    public Fuente Fuente { get; init; }
}
