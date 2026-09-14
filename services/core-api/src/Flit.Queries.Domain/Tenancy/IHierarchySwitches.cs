namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// Interruptores globales de la jerarquía de clientes (HU #12323, Feature #12254, ADR-0057).
/// Se leen <b>por petición y sin caché</b>: apagar o encender un interruptor surte efecto en la
/// siguiente petición sin despliegue ni nuevo token. Contrato fail-closed: ante fila ausente o
/// error de infraestructura, cada lectura devuelve <c>false</c> (apagado = comportamiento previo
/// al Feature = seguro). Ninguno de los interruptores reutiliza <c>is_group_parent</c>: los datos
/// de la jerarquía quedan intactos al apagar.
/// </summary>
public interface IHierarchySwitches
{
    /// <summary>Clave del interruptor de alcance de lectura de grupo (cabeza lee a sus hijos).</summary>
    const string GroupReadScopeKey = "group_read_scope";

    /// <summary>Clave del interruptor de configuración heredada (hijos heredan del padre; Feature #12256).</summary>
    const string InheritedConfigurationKey = "inherited_configuration";

    /// <summary><c>true</c> si <see cref="GroupReadScopeKey"/> está encendido. Fail-closed: <c>false</c> si falta o falla.</summary>
    Task<bool> IsGroupReadScopeEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary><c>true</c> si <see cref="InheritedConfigurationKey"/> está encendido. Fail-closed: <c>false</c> si falta o falla.</summary>
    Task<bool> IsInheritedConfigurationEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Estado actual de todos los interruptores persistidos.</summary>
    Task<IReadOnlyList<HierarchySwitchState>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Conmuta el interruptor <paramref name="key"/>. Devuelve el estado resultante o <c>null</c>
    /// si la clave no es una de las conocidas (no crea filas nuevas).
    /// </summary>
    Task<HierarchySwitchState?> SetAsync(
        string key,
        bool isEnabled,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>Estado de un interruptor de jerarquía (HU #12323).</summary>
public sealed record HierarchySwitchState(string Key, bool IsEnabled, DateTimeOffset UpdatedAt, Guid? UpdatedBy);
