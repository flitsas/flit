using System.Collections.Concurrent;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// Doble de la suscripción (HU #13726), como el de Comparendos: sin Identidad, la respuesta la fija la prueba. Por
/// defecto toda compañía tiene Diagnóstico activo (las pruebas que no tratan de la suscripción no la notan);
/// <see cref="Apagar"/> suspende una compañía y <see cref="Encender"/> la reactiva. Cuenta las consultas por compañía para
/// probar que el guard pregunta en cada petición.
/// </summary>
/// <example>
/// <code>
/// await using var api = await ApiDePrueba.SinBaseAsync();
/// api.Productos.Apagar(empresa);   // la compañía queda con Diagnóstico suspendido
/// </code>
/// </example>
public sealed class ProductosHabilitadosFijo(bool todas = true) : IProductosHabilitados
{
    private readonly ConcurrentDictionary<Guid, bool> _porEmpresa = new();
    private readonly ConcurrentDictionary<Guid, int> _consultas = new();

    /// <summary>Respuesta para las compañías sin estado propio (true = activo; false = sin suscripción).</summary>
    public bool Todas { get; set; } = todas;

    public void Encender(Guid empresa) => _porEmpresa[empresa] = true;

    public void Apagar(Guid empresa) => _porEmpresa[empresa] = false;

    public int Consultas(Guid empresa) => _consultas.TryGetValue(empresa, out var n) ? n : 0;

    public int ConsultasTotales => _consultas.Values.Sum();

    public Task<bool> DiagnosticoHabilitadoAsync(Guid tenant, CancellationToken ct)
    {
        _consultas.AddOrUpdate(tenant, 1, (_, n) => n + 1);
        return Task.FromResult(_porEmpresa.TryGetValue(tenant, out var propia) ? propia : Todas);
    }
}
