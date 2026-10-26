using Flit.Platform.Sdk.Tenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Empresa del scope (HU #13727, ADR-0075): decora el accessor HTTP del SDK. En una petición es la empresa del token; un trabajo
/// en segundo plano (el procesador de la cola y, en la tanda B, el consumidor de resultados) la fija una vez con
/// <see cref="Fijar"/> antes de leer o escribir, y desde ahí el <c>DiagnosticoDb</c>, la outbox y el publicador de celdas del
/// MISMO scope filtran, validan y publican con ella (antes cada ejecución armaba su propio contexto con la empresa de la celda y
/// la outbox del scope no lo compartía). Una vez fijada no cambia a otra empresa: un scope trabaja para una sola. Queda como
/// servicio (<see cref="PlatformTenantContext.IsService"/>), nunca como SuperAdmin.
/// </summary>
/// <example>
/// <code>
/// await using var scope = scopes.CreateAsyncScope();
/// scope.ServiceProvider.GetRequiredService&lt;EmpresaDelTrabajo&gt;().Fijar(celda.TenantId);
/// var db = scope.ServiceProvider.GetRequiredService&lt;DiagnosticoDb&gt;(); // filtra y escribe con la empresa de la celda
/// </code>
/// </example>
public sealed class EmpresaDelTrabajo(IPlatformTenantAccessor peticion) : IPlatformTenantAccessor
{
    private PlatformTenantContext? _fijada;

    /// <summary>La fijada por el trabajo o, si no hay, la de la petición en curso (o ninguna).</summary>
    public PlatformTenantContext Current => _fijada ?? peticion.Current;

    /// <summary>Fija la empresa del trabajo; volver a fijar la misma no cambia nada.</summary>
    /// <exception cref="ArgumentException">Con la empresa cero.</exception>
    /// <exception cref="InvalidOperationException">Si el scope ya trabaja con otra empresa.</exception>
    public void Fijar(Guid empresa)
    {
        if (empresa == Guid.Empty)
            throw new ArgumentException("El trabajo exige una empresa.", nameof(empresa));
        if (_fijada is { TenantId: { } actual } && actual != empresa)
            throw new InvalidOperationException("El scope ya trabaja con otra empresa: cada scope es de una sola.");
        _fijada = new PlatformTenantContext(empresa, IsSuperAdmin: false, IsService: true);
    }
}

/// <summary>Registro de <see cref="EmpresaDelTrabajo"/>.</summary>
public static class RegistroEmpresaDelTrabajo
{
    /// <summary>
    /// Reemplaza <see cref="IPlatformTenantAccessor"/> por <see cref="EmpresaDelTrabajo"/> (scoped) sobre el que registró la
    /// autenticación de plataforma: se llama DESPUÉS de <c>AddFlitPlatformAuthentication</c>.
    /// </summary>
    public static IServiceCollection AddEmpresaDelTrabajo(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var http = services.LastOrDefault(d => d.ServiceType == typeof(IPlatformTenantAccessor))
            ?? throw new InvalidOperationException("EmpresaDelTrabajo decora el accessor de la autenticación de plataforma: regístrelo después de AddFlitPlatformAuthentication.");
        services.AddScoped(sp => new EmpresaDelTrabajo(Crear(sp, http)));
        services.Replace(ServiceDescriptor.Scoped<IPlatformTenantAccessor>(sp => sp.GetRequiredService<EmpresaDelTrabajo>()));
        return services;
    }

    private static IPlatformTenantAccessor Crear(IServiceProvider sp, ServiceDescriptor original) =>
        (IPlatformTenantAccessor)(original.ImplementationInstance
            ?? original.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, original.ImplementationType!));
}
