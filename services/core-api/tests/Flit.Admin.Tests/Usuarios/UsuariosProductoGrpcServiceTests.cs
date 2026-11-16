using Flit.Admin.Application.Auditing;
using Flit.Api.Grpc;
using Flit.Api.Usuarios;
using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Usuarios;

/// <summary>
/// ADR-0089 / contrato §3.4 — el adaptador gRPC de usuarios de Diagnóstico: solo <c>svc-diagnostico</c>, producto atado al
/// cliente, actor reverificado (SP-14), errores con <c>ErrorInfo</c> y estado gRPC del contrato, invitación uniforme (SP-15) y
/// auditoría de cada escritura sin correo en claro (SP-16), ni en la auditoría ni en los logs.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await servicio.CambiarRolProducto(new CambiarRolProductoRequest { ... }, contexto)</c>.</remarks>
public sealed class UsuariosProductoGrpcServiceTests
{
    private const string P = "diagnostico";
    private static readonly Guid Empresa = Guid.Parse("0b0e0000-0000-4000-8000-0000000000e1");
    private static readonly Guid ActorId = Guid.Parse("0b0e0000-0000-4000-8000-000000000001");
    private static readonly ProductRoleRef UsuarioRol = new(Guid.Parse("0b0e0000-0000-4000-8000-0000000000a1"), "usuario_diagnostico");
    private static readonly ProductRoleRef AdminRol = new(Guid.Parse("0b0e0000-0000-4000-8000-0000000000a2"), "admin_diagnostico");

    private readonly IProductRoleRepository _repo = Substitute.For<IProductRoleRepository>();
    private readonly ActorFalso _actores = new();
    private readonly AuditoriaFalsa _auditoria = new();
    private readonly LoggerCapturado _logger = new();
    private readonly IProductInvitationSender _sender = Substitute.For<IProductInvitationSender>();

    public UsuariosProductoGrpcServiceTests()
    {
        _repo.FindAssignableRoleAsync(P, UsuarioRol.Code, Arg.Any<CancellationToken>()).Returns(UsuarioRol);
        _repo.FindAssignableRoleAsync(P, AdminRol.Code, Arg.Any<CancellationToken>()).Returns(AdminRol);
        _repo.CountOtherActiveAdminsAsync(default, default!, default, default).ReturnsForAnyArgs(1);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UsuariosProductoGrpcService Servicio() => new(
        _actores, new ListProductUsersHandler(_repo), new InviteProductUserHandler(_repo, _sender), new AssignProductRoleHandler(_repo),
        new SuspendProductRoleHandler(_repo), new ReactivateProductRoleHandler(_repo), _auditoria, _logger);

    private static ContextoFalso Contexto(string cliente = "svc-diagnostico", string? actor = null) =>
        new(cliente, Empresa, actor ?? ActorId.ToString(), Ct);

    private ProductMemberSnapshot Miembro(Guid id, ProductRoleRef? rol, string email = "persona@ejemplo.test")
    {
        var m = new ProductMemberSnapshot(id, "Persona", email, true, false, false, rol, null);
        _repo.GetMemberAsync(Empresa, id, P, Arg.Any<CancellationToken>()).Returns(m);
        return m;
    }

    private static async Task<RpcException> Falla(Func<Task> act, StatusCode estado, string? reason = null)
    {
        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(estado);
        if (reason is not null)
            PlatformRpcErrors.Reason(ex).Should().Be(reason);
        return ex;
    }

    [Fact]
    public async Task OtroClienteDeServicio_RecibePermissionDenied_SinTocarNada()
    {
        await Falla(() => Servicio().CambiarRolProducto(new CambiarRolProductoRequest { Producto = P, UsuarioId = Guid.NewGuid().ToString(), RolCodigo = "usuario_diagnostico" },
            Contexto("svc-ict")), StatusCode.PermissionDenied);

        _actores.Llamadas.Should().Be(0);
        await _repo.DidNotReceiveWithAnyArgs().ReplaceProductRoleAsync(default, default, default!, default, default, default, default);
    }

    [Fact]
    public async Task ProductoAjeno_OActorSinPermiso_RecibenPermissionDenied_YElRechazoSeAudita()
    {
        await Falla(() => Servicio().ListarUsuariosProducto(new ListarUsuariosProductoRequest { Producto = "tramites" }, Contexto()), StatusCode.PermissionDenied);

        _actores.Permitir = false;
        await Falla(() => Servicio().SuspenderEnProducto(new SuspenderEnProductoRequest { Producto = P, UsuarioId = Guid.NewGuid().ToString() }, Contexto()),
            StatusCode.PermissionDenied);

        _auditoria.Entradas.Should().ContainSingle().Which.Should().Match<AdminAuditEntry>(e =>
            e.Result == AuditVocabulary.Results.Failure && e.ErrorCode == "ACTOR_NO_AUTORIZADO" && e.ActorUserId == ActorId && e.TenantId == Empresa);
    }

    [Fact]
    public async Task CambiarRol_DevuelveLaFila_YAuditaAntesYDespues()
    {
        var id = Guid.NewGuid();
        Miembro(id, UsuarioRol);
        _actores.SuperAdmin = true;

        var respuesta = await Servicio().CambiarRolProducto(new CambiarRolProductoRequest { Producto = P, UsuarioId = id.ToString(), RolCodigo = "admin_diagnostico" }, Contexto());

        respuesta.Usuario.RolCodigo.Should().Be("admin_diagnostico");
        respuesta.Usuario.Estado.Should().Be(EstadoUsuarioProducto.Activo);
        respuesta.Usuario.Acciones.Should().Equal(AccionUsuario.CambiarRol, AccionUsuario.Suspender);
        await _repo.Received(1).ReplaceProductRoleAsync(Empresa, id, P, AdminRol.Id, ActorId, null, Arg.Any<CancellationToken>());
        var entrada = _auditoria.Entradas.Should().ContainSingle().Which;
        entrada.Operation.Should().Be(AuditVocabulary.Operations.AssignRole);
        entrada.Result.Should().Be(AuditVocabulary.Results.Success);
        entrada.TargetEntityId.Should().Be(id);
        entrada.OldValue.Should().Contain("usuario_diagnostico");
        entrada.NewValue.Should().Contain("admin_diagnostico");
    }

    [Fact]
    public async Task AdminDelProductoDandoAdmin_RecibePermissionDeniedConRolNoAsignable_YSeAudita()
    {
        var id = Guid.NewGuid();
        Miembro(id, UsuarioRol);

        await Falla(() => Servicio().CambiarRolProducto(new CambiarRolProductoRequest { Producto = P, UsuarioId = id.ToString(), RolCodigo = "admin_diagnostico" }, Contexto()),
            StatusCode.PermissionDenied, "ROL_NO_ASIGNABLE");

        _auditoria.Entradas.Should().ContainSingle().Which.ErrorCode.Should().Be("ROL_NO_ASIGNABLE");
    }

    [Fact]
    public async Task UsuarioDeOtraEmpresa_OIdNoUuid_RecibeNotFound()
    {
        await Falla(() => Servicio().SuspenderEnProducto(new SuspenderEnProductoRequest { Producto = P, UsuarioId = Guid.NewGuid().ToString() }, Contexto()),
            StatusCode.NotFound, "USUARIO_NO_ENCONTRADO");
        await Falla(() => Servicio().ReactivarEnProducto(new ReactivarEnProductoRequest { Producto = P, UsuarioId = "1 OR 1=1" }, Contexto()),
            StatusCode.NotFound, "USUARIO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task SuspenderAlUltimoAdmin_RecibeFailedPreconditionConUltimoAdministrador()
    {
        var id = Guid.NewGuid();
        Miembro(id, AdminRol);
        _repo.CountOtherActiveAdminsAsync(default, default!, default, default).ReturnsForAnyArgs(0);
        _actores.SuperAdmin = true;

        await Falla(() => Servicio().SuspenderEnProducto(new SuspenderEnProductoRequest { Producto = P, UsuarioId = id.ToString() }, Contexto()),
            StatusCode.FailedPrecondition, "ULTIMO_ADMINISTRADOR");
    }

    [Fact]
    public async Task Invitar_TresCasos_RespuestaIdentica_CausaSoloEnLaAuditoria_SinCorreoEnClaro()
    {
        _repo.ResolveInviteTargetAsync(Empresa, "nueva@ejemplo.test", P, Arg.Any<CancellationToken>()).Returns(new InviteTarget(InviteTargetKind.NewEmail));
        _repo.ResolveInviteTargetAsync(Empresa, "ajena@ejemplo.test", P, Arg.Any<CancellationToken>()).Returns(new InviteTarget(InviteTargetKind.OtherTenant));
        _repo.ResolveInviteTargetAsync(Empresa, "pendiente@ejemplo.test", P, Arg.Any<CancellationToken>()).Returns(new InviteTarget(InviteTargetKind.PendingInvitation));
        _sender.SendAsync(default, default!, default!, default, default, default).ReturnsForAnyArgs(new ProductInvitationSent(Guid.NewGuid(), true));

        var respuestas = new List<InvitarUsuarioProductoResponse>();
        foreach (var correo in new[] { "nueva@ejemplo.test", "ajena@ejemplo.test", "pendiente@ejemplo.test" })
        {
            respuestas.Add(await Servicio().InvitarUsuarioProducto(
                new InvitarUsuarioProductoRequest { Producto = P, Email = correo, Nombre = "Nueva", RolCodigo = "usuario_diagnostico" }, Contexto()));
        }

        respuestas.Should().AllSatisfy(r => r.Should().Be(new InvitarUsuarioProductoResponse()));
        _auditoria.Entradas.Select(e => e.ErrorCode).Should().Equal("INVITACION_CREADA", "OTRA_COMPANIA", "INVITACION_PENDIENTE");
        _auditoria.Entradas.Should().AllSatisfy(e =>
            string.Join("|", e.OldValue, e.NewValue, e.ErrorCode, e.TargetEntityType, e.UserAgent).Should().NotContain("@"));
        _logger.Mensajes.Should().NotBeEmpty().And.AllSatisfy(m => m.Should().NotContain("@"));
    }

    [Fact]
    public async Task Listar_MapeaEstadosAccionesYRolesAsignables()
    {
        var id = Guid.NewGuid();
        _repo.ListMembersAsync(Empresa, P, Arg.Any<CancellationToken>()).Returns([
            new ProductMemberSnapshot(id, "Ana", "ana@ejemplo.test", true, false, false, UsuarioRol, null)]);
        _repo.ListPendingInvitationsAsync(Empresa, P, Arg.Any<CancellationToken>()).Returns([
            new ProductInvitationSnapshot(Guid.NewGuid(), "Beto", "beto@ejemplo.test", "usuario_diagnostico")]);

        var respuesta = await Servicio().ListarUsuariosProducto(new ListarUsuariosProductoRequest { Producto = P }, Contexto());

        respuesta.Total.Should().Be(2);
        respuesta.RolesAsignables.Should().Equal("usuario_diagnostico");
        respuesta.Usuarios[0].Should().Match<UsuarioProducto>(u => u.Tipo == TipoFila.Usuario && u.Estado == EstadoUsuarioProducto.Activo
            && u.MotivoSinAcciones == MotivoSinAcciones.Unspecified && u.Id == id.ToString());
        respuesta.Usuarios[0].Acciones.Should().Equal(AccionUsuario.Suspender);
        respuesta.Usuarios[1].Should().Match<UsuarioProducto>(u => u.Tipo == TipoFila.Invitacion && u.Estado == EstadoUsuarioProducto.Invitado
            && u.MotivoSinAcciones == MotivoSinAcciones.Invitacion);
        _auditoria.Entradas.Should().BeEmpty("leer no se audita");
    }

    [Fact]
    public async Task Listar_ConEstadoSinEspecificar_RecibeInvalidArgumentConFiltroInvalido()
    {
        var request = new ListarUsuariosProductoRequest { Producto = P };
        request.Estados.Add(EstadoUsuarioProducto.Unspecified);

        await Falla(() => Servicio().ListarUsuariosProducto(request, Contexto()), StatusCode.InvalidArgument, "FILTRO_INVALIDO");
    }

    [Fact]
    public async Task Conteos_SoloSuperAdmin()
    {
        _repo.CountByTenantAsync(P, Arg.Any<IReadOnlyCollection<string>>(), "admin_diagnostico", Arg.Any<CancellationToken>()).Returns([new ProductTenantCount(Empresa, 4, 1)]);
        var servicio = new UsuariosProductoConteoGrpcService(_actores, new ListProductUsersHandler(_repo));

        await Falla(() => servicio.ContarPorEmpresa(new ContarPorEmpresaRequest { Producto = P }, new ContextoFalso("svc-diagnostico", Guid.Empty, ActorId.ToString(), Ct)),
            StatusCode.PermissionDenied);

        _actores.SuperAdmin = true;
        var respuesta = await servicio.ContarPorEmpresa(new ContarPorEmpresaRequest { Producto = P }, new ContextoFalso("svc-diagnostico", Guid.Empty, ActorId.ToString(), Ct));

        respuesta.Conteos.Should().ContainSingle().Which.Should().Match<ConteoEmpresa>(c => c.EmpresaId == Empresa.ToString() && c.Usuarios == 4 && c.Administradores == 1);
        _actores.UltimoTenant.Should().BeNull("los conteos son globales");
    }

    private sealed class ActorFalso : IActorUsuariosProducto
    {
        public bool Permitir { get; set; } = true;
        public bool EmpresaHabilitada { get; set; } = true;
        public bool SuperAdmin { get; set; }
        public int Llamadas { get; private set; }
        public Guid? UltimoTenant { get; private set; }

        public Task<ProductActor?> VerificarAsync(Guid? tenantId, Guid actorId, string producto, CancellationToken ct)
        {
            Llamadas++;
            UltimoTenant = tenantId;
            if (!Permitir)
                return Task.FromResult<ProductActor?>(null);
            if (tenantId is null && !SuperAdmin)
                return Task.FromResult<ProductActor?>(null);
            return Task.FromResult<ProductActor?>(new ProductActor(actorId, SuperAdmin));
        }

        public Task<bool> EmpresaHabilitadaAsync(Guid tenantId, string producto, CancellationToken ct) => Task.FromResult(EmpresaHabilitada);
    }

    [Fact]
    public async Task SuperAdmin_EmpresaSinDiagnosticoOInexistente_RecibeNotFoundEmpresaNoEncontrada_SinTocarNada()
    {
        _actores.SuperAdmin = true;
        _actores.EmpresaHabilitada = false;
        var id = Guid.NewGuid();
        Miembro(id, UsuarioRol);

        await Falla(() => Servicio().ListarUsuariosProducto(new ListarUsuariosProductoRequest { Producto = P }, Contexto()),
            StatusCode.NotFound, "EMPRESA_NO_ENCONTRADA");
        await Falla(() => Servicio().CambiarRolProducto(new CambiarRolProductoRequest { Producto = P, UsuarioId = id.ToString(), RolCodigo = "admin_diagnostico" }, Contexto()),
            StatusCode.NotFound, "EMPRESA_NO_ENCONTRADA");

        await _repo.DidNotReceiveWithAnyArgs().ReplaceProductRoleAsync(default, default, default!, default, default, default, default);
    }

    [Fact]
    public async Task Listar_ConEstadoDesconocido_RecibeInvalidArgumentConFiltroInvalido()
    {
        var request = new ListarUsuariosProductoRequest { Producto = P };
        request.Estados.Add((EstadoUsuarioProducto)99);

        await Falla(() => Servicio().ListarUsuariosProducto(request, Contexto()), StatusCode.InvalidArgument, "FILTRO_INVALIDO");
    }

    [Fact]
    public async Task Invitar_AdminCompany_RespuestaUniforme_CausaObjetivoProtegidoEnLaAuditoria()
    {
        _repo.ResolveInviteTargetAsync(Empresa, "admin.compania@ejemplo.test", P, Arg.Any<CancellationToken>())
            .Returns(new InviteTarget(InviteTargetKind.ProtectedMember, Guid.NewGuid()));
        _actores.SuperAdmin = true;

        var respuesta = await Servicio().InvitarUsuarioProducto(
            new InvitarUsuarioProductoRequest { Producto = P, Email = "admin.compania@ejemplo.test", Nombre = "Admin", RolCodigo = "usuario_diagnostico" }, Contexto());

        respuesta.Should().Be(new InvitarUsuarioProductoResponse());
        _auditoria.Entradas.Should().ContainSingle().Which.ErrorCode.Should().Be("OBJETIVO_PROTEGIDO");
        await _repo.DidNotReceiveWithAnyArgs().AddProductRoleAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task InvitacionPorPlataforma_RolQueNoExisteEnLaEmpresa_SeTraduceARolNoPermitido()
    {
        var invitaciones = Substitute.For<Flit.Modules.Security.Domain.Auth.IInvitationRepository>();
        invitaciones.RoleExistsInTenantAsync(default, default, default).ReturnsForAnyArgs(false);
        var handler = new Flit.Modules.Security.Application.Auth.CreateInvitation.CreateInvitationHandler(
            invitaciones, Substitute.For<Flit.Modules.Security.Domain.Auth.ISecureTokenGenerator>(), Substitute.For<Flit.Modules.Security.Domain.Auth.IEmailSender>(),
            new Flit.Modules.Security.Application.Auth.CreateInvitation.InvitationOptions(), Substitute.For<Flit.Modules.Security.Application.Auth.Network.INetworkUrlBaseResolver>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Flit.Modules.Security.Application.Auth.CreateInvitation.CreateInvitationHandler>.Instance);

        var act = () => new InvitacionProductoPorPlataforma(handler).SendAsync(Empresa, "nueva@ejemplo.test", "Nueva", Guid.NewGuid(), ActorId, Ct);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("ROL_NO_PERMITIDO");
    }

    private sealed class AuditoriaFalsa : IAdminAuditWriter
    {
        public List<AdminAuditEntry> Entradas { get; } = [];

        public Task WriteAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entradas.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class LoggerCapturado : ILogger<UsuariosProductoGrpcService>
    {
        public List<string> Mensajes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Mensajes.Add(formatter(state, exception));
    }

    private sealed class ContextoFalso : ServerCallContext
    {
        private readonly CancellationToken _ct;

        public ContextoFalso(string cliente, Guid empresa, string actor, CancellationToken ct)
        {
            _ct = ct;
            // Lo que deja el interceptor del SDK (PlatformServiceCallInterceptor) tras validar el token.
            UserState["flit.serviceCaller"] = new PlatformServiceCaller(cliente, empresa);
            RequestHeadersCore = new Metadata { { "x-flit-actor-id", actor } };
        }

        protected override string MethodCore => "/flit.platform.v1.UsuariosProductoService/Metodo";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; }
        protected override CancellationToken CancellationTokenCore => _ct;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
