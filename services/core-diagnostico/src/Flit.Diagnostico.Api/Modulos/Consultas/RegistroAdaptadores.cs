using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Qué fuentes tienen adaptador (HU #13733, riesgo declarado de F03): una fuente sin adaptador no tiene quién ejecute sus
/// celdas, así que no se puede habilitar en el contrato ni pedir en un diagnóstico. Lo consulta <see cref="FuentesDisponibles"/>.
/// </summary>
public interface IRegistroAdaptadores
{
    bool TieneAdaptador(Fuente fuente);

    /// <summary>
    /// Verdadero si la fuente no está conectada (HU #13791): aunque tenga ejecutor, no se ofrece. Desde ADR-0075 Diagnóstico no
    /// tiene adaptadores de proveedor (la conexión es de core-consultas), así que el registro real nunca lo dice; queda para los
    /// dobles de prueba. Por defecto false.
    /// </summary>
    bool EsNoConectada(Fuente fuente) => false;
}

/// <summary>
/// Registro real: las fuentes con un <see cref="IEjecutorCelda"/> registrado, las únicas cuyas celdas se pueden cerrar con lo
/// que responde core-consultas (ADR-0075). Los ejecutores son scoped, así que el conjunto se calcula una vez, en un ámbito propio,
/// la primera vez que se consulta (el contenedor no cambia después de arrancar).
/// </summary>
/// <example>
/// <code>
/// services.TryAddSingleton&lt;IRegistroAdaptadores, RegistroAdaptadores&gt;();
/// registro.TieneAdaptador(Fuente.SimitVehiculo) // true: tiene ejecutor
/// </code>
/// </example>
internal sealed class RegistroAdaptadores(IServiceScopeFactory scopes) : IRegistroAdaptadores
{
    private readonly Lazy<IReadOnlySet<Fuente>> _fuentes = new(() =>
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetServices<IEjecutorCelda>().Select(e => e.Fuente).ToHashSet();
    });

    public bool TieneAdaptador(Fuente fuente) => _fuentes.Value.Contains(fuente);
}
