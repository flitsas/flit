using System.Collections.Concurrent;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Tests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Comparendos.Tests.ConsultasPersonalizadas;

/// <summary>
/// La API de prueba con un almacén de archivos que se vence (como el timeout del HttpClient del file-manager, que lanza
/// <see cref="TaskCanceledException"/> sin que nadie cancele) para las empresas que se marquen.
/// </summary>
public sealed class ApiConAlmacenQueSeVence : ApiConFuente
{
    public ConcurrentDictionary<Guid, bool> EmpresasQueSeVencen { get; } = new();

    /// <summary>Identidad falsa para los dueños de los envíos: por usuario, sí / no / no responde (null). Sin entrada, sí.</summary>
    public DuenosDePrueba Duenos { get; } = new();

    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
        services.AddSingleton<IUsuariosEmpresa>(Duenos);
        services.AddScoped<IAlmacenArchivos>(sp => new AlmacenQueSeVence(new AlmacenEnBase(sp.GetRequiredService<ComparendosDb>()), this));
    }

    private sealed class AlmacenQueSeVence(IAlmacenArchivos interno, ApiConAlmacenQueSeVence api) : IAlmacenArchivos
    {
        public Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct) =>
            api.EmpresasQueSeVencen.ContainsKey(empresaId)
                ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.")
                : interno.GuardarAsync(empresaId, tipo, nombre, contentType, contenido, ct);

        public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) => interno.LeerAsync(referencia, ct);
    }
}

/// <summary>Doble de <see cref="IUsuariosEmpresa"/> que contesta por usuario y anota con qué se le preguntó.</summary>
public sealed class DuenosDePrueba : IUsuariosEmpresa
{
    public ConcurrentDictionary<Guid, bool?> Respuestas { get; } = new();

    public ConcurrentQueue<(Guid Tenant, Guid Usuario, string Permiso)> Preguntas { get; } = new();

    /// <summary>Si está, la consulta revienta (Identidad caída con una excepción y no con un «no sé»).</summary>
    public ConcurrentDictionary<Guid, bool> Revientan { get; } = new();

    public Task<IReadOnlyList<UsuarioConPermiso>> ConPermisoAsync(Guid tenant, string permiso, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsuarioConPermiso>>([]);

    public Task<bool?> TienePermisoAsync(Guid tenant, Guid usuario, string permiso, CancellationToken ct)
    {
        Preguntas.Enqueue((tenant, usuario, permiso));
        if (Revientan.ContainsKey(usuario))
            throw new InvalidOperationException("Identidad no responde (prueba).");
        return Task.FromResult(Respuestas.TryGetValue(usuario, out var r) ? r : true);
    }
}
