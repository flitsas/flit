using System.Collections.Concurrent;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Impuestos.Tests.Usuarios;

/// <summary>
/// Doble de <see cref="IUsuariosPlataforma"/> (HU #14694, M13): guarda cada pedido con su contexto (empresa y actor) y responde
/// lo que la prueba fija. <see cref="Falla"/> hace que el siguiente pedido lance el rechazo dado (con su <c>ErrorInfo</c>).
/// </summary>
/// <example>
/// <code>
/// doble.Filas = [UsuariosPlataformaDePrueba.Fila(id, "Ana", "ana@x.test", "gestor_impuestos")];
/// doble.Falla = UsuariosPlataformaDePrueba.Rechazo("ULTIMO_ADMINISTRADOR", StatusCode.FailedPrecondition);
/// </code>
/// </example>
public sealed class UsuariosPlataformaDePrueba : IUsuariosPlataforma
{
    public ConcurrentQueue<(string Metodo, ContextoUsuarios Contexto, object Pedido)> Pedidos { get; } = new();

    public List<UsuarioProducto> Filas { get; set; } = [];

    public int Total { get; set; } = -1;

    public List<ConteoEmpresa> Conteos { get; set; } = [];

    /// <summary>Excepción del siguiente pedido (se consume); null = responde bien.</summary>
    public Exception? Falla { get; set; }

    /// <summary>Si es true, los conteos fallan (la lista del backoffice sigue sin el campo).</summary>
    public bool ConteosFallan { get; set; }

    public void Reiniciar()
    {
        Pedidos.Clear();
        Filas = [];
        Total = -1;
        Conteos = [];
        Falla = null;
        ConteosFallan = false;
    }

    public static UsuarioProducto Fila(Guid id, string nombre, string email, string rol,
        EstadoUsuarioProducto estado = EstadoUsuarioProducto.Activo, MotivoSinAcciones motivo = MotivoSinAcciones.Unspecified) =>
        new()
        {
            Id = id.ToString(), Tipo = estado == EstadoUsuarioProducto.Invitado ? TipoFila.Invitacion : TipoFila.Usuario,
            Nombre = nombre, Email = email, RolCodigo = rol, Estado = estado, MotivoSinAcciones = motivo,
        };

    public static RpcException Rechazo(string codigo, StatusCode estado) => PlatformRpcErrors.ToRpcException(codigo, "rechazo de prueba", estado);

    public Task<ListarUsuariosProductoResponse> ListarAsync(ContextoUsuarios contexto, ListarUsuariosProductoRequest pedido, CancellationToken ct)
    {
        Registrar("Listar", contexto, pedido.Clone());
        var r = new ListarUsuariosProductoResponse { Total = Total >= 0 ? Total : Filas.Count };
        var tamano = pedido.Tamano <= 0 ? 25 : pedido.Tamano;
        var pagina = pedido.Pagina <= 0 ? 1 : pedido.Pagina;
        r.Usuarios.AddRange(Filas.Skip((pagina - 1) * tamano).Take(tamano));
        r.RolesAsignables.Add("gestor_impuestos");
        return Task.FromResult(r);
    }

    public Task InvitarAsync(ContextoUsuarios contexto, InvitarUsuarioProductoRequest pedido, CancellationToken ct)
    {
        Registrar("Invitar", contexto, pedido.Clone());
        return Task.CompletedTask;
    }

    public Task<UsuarioProducto> CambiarRolAsync(ContextoUsuarios contexto, Guid usuario, string rol, CancellationToken ct)
    {
        Registrar("CambiarRol", contexto, (usuario, rol));
        return Task.FromResult(Fila(usuario, "Ana", "ana@empresa.test", rol));
    }

    public Task<UsuarioProducto> SuspenderAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct)
    {
        Registrar("Suspender", contexto, usuario);
        return Task.FromResult(Fila(usuario, "Ana", "ana@empresa.test", "gestor_impuestos", EstadoUsuarioProducto.Suspendido));
    }

    public Task<UsuarioProducto> ReactivarAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct)
    {
        Registrar("Reactivar", contexto, usuario);
        return Task.FromResult(Fila(usuario, "Ana", "ana@empresa.test", "gestor_impuestos"));
    }

    public Task<IReadOnlyList<ConteoEmpresa>> ContarPorEmpresaAsync(Guid actor, CancellationToken ct)
    {
        Pedidos.Enqueue(("Contar", new ContextoUsuarios(Guid.Empty, actor), actor));
        if (ConteosFallan)
            throw new RpcException(new Status(StatusCode.Unavailable, "caída de prueba"));
        return Task.FromResult<IReadOnlyList<ConteoEmpresa>>(Conteos);
    }

    private void Registrar(string metodo, ContextoUsuarios contexto, object pedido)
    {
        Pedidos.Enqueue((metodo, contexto, pedido));
        if (Falla is { } falla)
        {
            Falla = null;
            throw falla;
        }
    }
}

/// <summary>Doble de <see cref="IDirectorioEmpresas"/>: las compañías (id, nombre, NIT) que la prueba siembra.</summary>
public sealed class DirectorioEmpresasDePrueba : IDirectorioEmpresas
{
    public ConcurrentDictionary<Guid, EmpresaDirectorio> Empresas { get; } = new();

    public void Sembrar(Guid id, string nombre, string? nit) => Empresas[id] = new EmpresaDirectorio(id, nombre, nit);

    public Task<IReadOnlyDictionary<Guid, EmpresaDirectorio>> ListarAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, EmpresaDirectorio>>(new Dictionary<Guid, EmpresaDirectorio>(Empresas));
}
