using System.Collections.Concurrent;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.Diagnostico.Tests.Catalogos;

/// <summary>
/// Doble del cliente gRPC de <c>flit.tramites.v1.CatalogosService</c> (HU #13742): responde
/// <c>ListarTiposTramite</c> con <see cref="Publicados"/> y, si la prueba lo pide, falla con el código gRPC dado las
/// primeras veces o siempre. Cuenta las llamadas para verificar reintentos y caché. No lo usa el servicio real.
/// </summary>
/// <example>
/// <code>
/// api.Catalogos.FallarLasPrimeras(2, StatusCode.Unavailable);
/// // ... GET /tipos-tramite → 200 y api.Catalogos.Llamadas == 3
/// </code>
/// </example>
public sealed class CatalogosFalso : CatalogosService.CatalogosServiceClient
{
    private readonly ConcurrentQueue<StatusCode> _fallas = new();
    private int _llamadas;

    /// <summary>Catálogo publicado de prueba: los tres de la etapa, uno fuera de ella y uno con el nombre de otro pero otro código.</summary>
    public List<TipoTramite> Publicados { get; } =
    [
        new() { Codigo = "MATRICULA_INICIAL", Nombre = "MATRÍCULA INICIAL", Familia = "MATRICULAS", Activo = true, WizardEnabled = true },
        new() { Codigo = "TRASPASO_INDETERMINADA", Nombre = "TRASPASO", Familia = "TRASPASO", Activo = true, WizardEnabled = true },
        new() { Codigo = "TRASPASO_STANDARD", Nombre = "TRASPASO", Familia = "TRASPASO", Activo = true, WizardEnabled = true },
        new() { Codigo = "TRASPASO_TRANSFERENCIA_DE_DOMINIO", Nombre = "TRANSFERENCIA DE DOMINIO", Familia = "TRASPASO", Activo = true, WizardEnabled = true },
        new() { Codigo = "TRASPASO_UNILATERAL", Nombre = "TRASPASO UNILATERAL", Familia = "TRASPASO", Activo = true, WizardEnabled = true },
    ];

    /// <summary>Si tiene valor, toda llamada falla con ese código.</summary>
    public StatusCode? FallaSiempre { get; set; }

    public int Llamadas => Volatile.Read(ref _llamadas);

    public void FallarLasPrimeras(int veces, StatusCode codigo)
    {
        for (var i = 0; i < veces; i++)
            _fallas.Enqueue(codigo);
    }

    public override AsyncUnaryCall<ListarTiposTramiteResponse> ListarTiposTramiteAsync(ListarTiposTramiteRequest request, CallOptions options)
    {
        Interlocked.Increment(ref _llamadas);
        StatusCode? falla = FallaSiempre ?? (_fallas.TryDequeue(out var c) ? c : null);
        var respuesta = falla is { } codigo
            ? Task.FromException<ListarTiposTramiteResponse>(new RpcException(new Status(codigo, "falla de prueba")))
            : Task.FromResult(Respuesta());
        return new AsyncUnaryCall<ListarTiposTramiteResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    private ListarTiposTramiteResponse Respuesta()
    {
        var r = new ListarTiposTramiteResponse();
        r.Tipos.AddRange(Publicados);
        return r;
    }
}
