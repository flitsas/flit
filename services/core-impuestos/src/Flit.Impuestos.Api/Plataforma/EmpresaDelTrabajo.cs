using Flit.Platform.Sdk.Tenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Empresa del scope (forma de ADR-0075, sin código de Diagnóstico): decora el accessor HTTP del SDK. En una petición es la empresa
/// del token; un trabajo del bus (los consumidores de <c>impuestos.plataforma</c>) la fija con <see cref="Fijar"/> antes de leer o
/// escribir, y desde ahí el <c>ImpuestosDb</c> del MISMO scope (el que usa la bandeja de entrada del SDK) filtra y valida la
/// escritura con ella, así que el efecto y la fila de la bandeja quedan en una sola transacción. Una vez fijada no cambia a otra
/// empresa. Queda como servicio, nunca como SuperAdmin.
/// </summary>
/// <example>
/// <code>
/// empresa.Fijar(envelope.TenantId);
/// var fila = await db.Set&lt;AprovisionamientoEmpresa&gt;().SingleOrDefaultAsync(ct); // ya filtra por esa empresa
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
