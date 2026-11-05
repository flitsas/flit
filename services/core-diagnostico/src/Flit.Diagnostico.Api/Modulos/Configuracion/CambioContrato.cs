using Flit.Diagnostico.Api.Modulos.Comun;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// Un cambio del nivel contractual de la configuración de una compañía (<c>diagnostico.cambios_contrato</c>, HU #13736):
/// quién cambió qué campo, de qué valor a cuál y cuándo. Solo se inserta: no se edita ni se borra.
/// </summary>
public sealed class CambioContrato : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa cuya configuración cambió.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Usuario del backoffice que hizo el cambio (claim <c>sub</c>).</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Columna contractual cambiada (por ejemplo, <c>fuentes_habilitadas</c>).</summary>
    public required string Campo { get; init; }

    /// <summary>Valor anterior en JSON; null si no había.</summary>
    public string? ValorAnterior { get; init; }

    /// <summary>Valor nuevo en JSON.</summary>
    public required string ValorNuevo { get; init; }

    /// <summary>Momento del cambio; si no se indica, lo pone la base.</summary>
    public DateTimeOffset Fecha { get; init; }
}
