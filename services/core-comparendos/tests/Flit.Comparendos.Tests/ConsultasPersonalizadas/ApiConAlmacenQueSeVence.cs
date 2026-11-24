using System.Collections.Concurrent;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Persistence;
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

    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
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
