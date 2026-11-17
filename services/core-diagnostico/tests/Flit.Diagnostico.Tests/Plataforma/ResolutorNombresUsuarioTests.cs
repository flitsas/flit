using System.Collections.Concurrent;
using System.Net;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Identidad.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13845 (F08-H1), AC1 y AC4 (F08-C6, decisión Q3) — nombre del creador por gRPC <c>ObtenerUsuario</c> a Identidad: solo
/// los creadores distintos, con caché por (compañía, usuario) y la compañía en <c>x-flit-tenant-id</c>; NOT_FOUND o
/// Identidad caída dan «No disponible» sin romper la respuesta del listado.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ResolutorNombresUsuarioTests
/// </code>
/// </example>
public sealed class ResolutorNombresUsuarioTests
{
    // AC1 (F08-C1) — un pedido con creadores repetidos pregunta una vez por cada uno; el segundo pedido sale de la caché.
    [Fact]
    public async Task Resuelve_solo_los_creadores_distintos_con_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new UsuariosIdentidadDePrueba();
        var empresa = Guid.NewGuid();
        var (ana, luis, ausente) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        identidad.Nombres[ana] = "Ana Prueba";
        identidad.Nombres[luis] = "Luis Ficticio";
        var resolutor = identidad.Resolutor();

        var primero = await resolutor.ResolverAsync(empresa, [ana, ana, luis, ausente, ana], ct);

        primero.Should().BeEquivalentTo(new Dictionary<Guid, string>
        {
            [ana] = "Ana Prueba",
            [luis] = "Luis Ficticio",
            [ausente] = ResolutorNombresUsuario.NoDisponible,
        });
        identidad.Pedidos.Select(p => p.Usuario).Should().BeEquivalentTo([ana.ToString(), luis.ToString(), ausente.ToString()],
            "una sola llamada por creador distinto");
        identidad.Pedidos.Should().OnlyContain(p => p.Empresa == empresa.ToString(), "la compañía del lote viaja en x-flit-tenant-id");

        var segundo = await resolutor.ResolverAsync(empresa, [luis, ana], ct);

        segundo.Should().BeEquivalentTo(new Dictionary<Guid, string> { [ana] = "Ana Prueba", [luis] = "Luis Ficticio" });
        identidad.Pedidos.Should().HaveCount(3, "los nombres ya resueltos salen de la caché");

        await resolutor.ResolverAsync(Guid.NewGuid(), [ana], ct);
        identidad.Pedidos.Should().HaveCount(4, "la caché es por compañía y usuario");
    }

    // AC4 (F08-C6) — Identidad no responde: el listado responde 200 con los lotes y el creador «No disponible».
    [Fact]
    public async Task Identidad_caida_devuelve_no_disponible_sin_fallar()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new UsuariosIdentidadDePrueba { Error = new RpcException(new Status(StatusCode.Unavailable, "caída de prueba")) };
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        identidad.Nombres[usuario] = "Ana Prueba";
        var resolutor = identidad.Resolutor();

        (await resolutor.ResolverAsync(empresa, [usuario], ct))[usuario].Should().Be(ResolutorNombresUsuario.NoDisponible);
        identidad.Error = new HttpRequestException("red de prueba");
        (await resolutor.ResolverAsync(empresa, [usuario], ct))[usuario].Should().Be(ResolutorNombresUsuario.NoDisponible);
        identidad.Error = null;
        (await resolutor.ResolverAsync(empresa, [usuario], ct))[usuario].Should().Be("Ana Prueba", "un fallo no queda guardado en la caché");

        var caida = new UsuariosIdentidadDePrueba { Error = new RpcException(new Status(StatusCode.Unavailable, "caída de prueba")) };
        await using var api = await ListadoDePrueba.ArrancarAsync(caida);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var compania = DosEmpresas.En(api).A;
        var lote = await ListadoDePrueba.LoteAsync(compania, "Lote con Identidad caída", ListadoDePrueba.Instante("2026-10-21T09:00:00-05:00"), ct);

        var respuesta = await compania.Cliente().GetAsync(ListadoDePrueba.Ruta, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var item = (await ListadoDePrueba.DiagnosticosAsync(respuesta, ct)).Single();
        item.GetProperty("id").GetGuid().Should().Be(lote.Id);
        item.GetProperty("usuario_creador").GetProperty("id").GetGuid().Should().Be(lote.UsuarioCreadorId);
        item.GetProperty("usuario_creador").GetProperty("nombre").GetString().Should().Be("No disponible");
        caida.Pedidos.Should().NotBeEmpty("sí se le preguntó a Identidad");
    }
}

/// <summary>
/// Identidad de prueba por gRPC para <c>ObtenerUsuario</c> (HU #13845): responde el nombre de <see cref="Nombres"/>, NOT_FOUND
/// si el usuario no está, o lanza <see cref="Error"/>; anota cada pedido con la compañía de <c>x-flit-tenant-id</c>. Nombres
/// ficticios.
/// </summary>
internal sealed class UsuariosIdentidadDePrueba : CallInvoker
{
    private readonly ConcurrentQueue<(string Usuario, string? Empresa)> _pedidos = new();

    public ConcurrentDictionary<Guid, string> Nombres { get; } = new();

    public Exception? Error { get; set; }

    public IReadOnlyList<(string Usuario, string? Empresa)> Pedidos => [.. _pedidos];

    public ResolutorNombresUsuario Resolutor() =>
        new(new IdentidadService.IdentidadServiceClient(this), new MemoryCache(new MemoryCacheOptions()), NullLogger<ResolutorNombresUsuario>.Instance);

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        var pedido = (ObtenerUsuarioRequest)(object)request;
        _pedidos.Enqueue((pedido.UsuarioId, options.Headers?.GetValue("x-flit-tenant-id")));
        Task<TResponse> respuesta;
        if (Error is { } error)
            respuesta = Task.FromException<TResponse>(error);
        else if (Guid.TryParse(pedido.UsuarioId, out var id) && Nombres.TryGetValue(id, out var nombre))
            respuesta = Task.FromResult((TResponse)(object)new ObtenerUsuarioResponse { Usuario = new Usuario { Id = pedido.UsuarioId, Nombre = nombre } });
        else
            respuesta = Task.FromException<TResponse>(new RpcException(new Status(StatusCode.NotFound, "usuario de prueba inexistente")));
        return new AsyncUnaryCall<TResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException();

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException();

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException();
}
