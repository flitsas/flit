using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Configuracion;
using Flit.Impuestos.Tests.Credenciales;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Usuarios;

/// <summary>
/// HU #14694 (M13, F5b-CF3..CF6; deltas 6 y 7 de impuestos.v1.yaml 1.1.0) — <c>/usuarios*</c> delegado en
/// <c>flit.platform.v1.UsuariosProductoService</c> de core-api: el Administrador Impuestos gestiona solo su compañía, el Gestor
/// recibe 403 y el Super Admin opera sobre cualquier compañía con Impuestos eligiéndola con <c>?empresa</c> (403
/// <c>EMPRESA_SOLO_SUPER_ADMIN</c> para otro usuario antes que 400 <c>EMPRESA_REQUERIDA</c>; 404 <c>EMPRESA_NO_ENCONTRADA</c>).
/// Filtros <c>rol</c> y <c>estado</c>, <c>protegido</c> para el AdminCompany y <c>empresa{id,nombre,nit}</c> en la página del
/// Super Admin. Los rechazos de la plataforma salen con el código y el literal exactos del contrato.
/// </summary>
/// <example>
/// <code>
/// var r = await api.Cliente(UsuariosEndpointsTests.TokenAdmin(api, empresa)).GetAsync(UsuariosEndpointsTests.Ruta + "?rol=gestor_impuestos", ct);
/// </code>
/// </example>
public sealed class UsuariosEndpointsTests(ApiUsuariosFixture fixture) : IClassFixture<ApiUsuariosFixture>
{
    public const string Ruta = Program.Prefijo + "/usuarios";

    private ApiDePrueba Api => fixture.Api;

    private UsuariosPlataformaDePrueba Plataforma => fixture.Plataforma;

    [Fact]
    public async Task El_Administrador_lista_su_compania_con_estado_rol_y_protegido_sin_empresa_en_la_pagina()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var (gestor, admin, invitado, suspendido) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Plataforma.Filas =
        [
            UsuariosPlataformaDePrueba.Fila(gestor, "Ana", "ana@empresa.test", "gestor_impuestos"),
            UsuariosPlataformaDePrueba.Fila(admin, "Beto", "beto@empresa.test", "admin_impuestos", motivo: MotivoSinAcciones.AdminCompania),
            UsuariosPlataformaDePrueba.Fila(invitado, "Caro", "caro@empresa.test", "gestor_impuestos", EstadoUsuarioProducto.Invitado, MotivoSinAcciones.Invitacion),
            UsuariosPlataformaDePrueba.Fila(suspendido, "Dani", "dani@empresa.test", "gestor_impuestos", EstadoUsuarioProducto.Suspendido),
        ];

        using var r = await Api.Cliente(TokenAdmin(Api, empresa, actor)).GetAsync(Ruta, ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using var json = JsonDocument.Parse(texto);
        var raiz = json.RootElement;
        raiz.GetProperty("total").GetInt32().Should().Be(4);
        raiz.GetProperty("page").GetInt32().Should().Be(1);
        raiz.GetProperty("pageSize").GetInt32().Should().Be(50);
        raiz.GetProperty("empresa").ValueKind.Should().Be(JsonValueKind.Null, "null para quien no es Super Admin");
        var items = raiz.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(gestor, admin, invitado, suspendido);
        items.Select(i => i.GetProperty("estado").GetString()).Should().Equal("ACTIVO", "ACTIVO", "INVITADO", "SUSPENDIDO");
        items.Select(i => i.GetProperty("rol").GetString()).Should().Equal("gestor_impuestos", "admin_impuestos", "gestor_impuestos", "gestor_impuestos");
        items.Select(i => i.GetProperty("protegido").GetBoolean()).Should().Equal(false, true, false, false);
        items[0].GetProperty("nombre").GetString().Should().Be("Ana");
        items[0].GetProperty("email").GetString().Should().Be("ana@empresa.test");

        var pedido = Plataforma.Pedidos.Should().ContainSingle().Subject;
        pedido.Contexto.Should().Be(new ContextoUsuarios(empresa, actor), "la compañía es la del token y el actor su sub");
        var listar = (ListarUsuariosProductoRequest)pedido.Pedido;
        listar.Pagina.Should().Be(1);
        listar.Tamano.Should().Be(50);
        listar.Roles.Should().BeEmpty();
        listar.Estados.Should().BeEmpty();
    }

    [Fact]
    public async Task Los_filtros_rol_estado_q_y_la_pagina_llegan_a_la_plataforma()
    {
        var ct = Preparar();

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).GetAsync(Ruta + "?rol=admin_impuestos&estado=SUSPENDIDO&q=ana&page=2&pageSize=20", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        var listar = (ListarUsuariosProductoRequest)Plataforma.Pedidos.Should().ContainSingle().Subject.Pedido;
        listar.Roles.Should().Equal("admin_impuestos");
        listar.Estados.Should().Equal(EstadoUsuarioProducto.Suspendido);
        listar.Busqueda.Should().Be("ana");
        listar.Pagina.Should().Be(2);
        listar.Tamano.Should().Be(20);
    }

    [Theory]
    [InlineData("?rol=usuario_impuestos", "FILTRO_INVALIDO", "Un filtro no es válido.")]
    [InlineData("?estado=BORRADO", "FILTRO_INVALIDO", "Un filtro no es válido.")]
    [InlineData("?rol=gestor_impuestos&rol=admin_impuestos", "FILTRO_INVALIDO", "Un filtro no es válido.")]
    [InlineData("?page=0", "PAGINACION_INVALIDA", "La paginación no es válida.")]
    [InlineData("?pageSize=201", "PAGINACION_INVALIDA", "La paginación no es válida.")]
    [InlineData("?pageSize=abc", "PAGINACION_INVALIDA", "La paginación no es válida.")]
    public async Task Un_filtro_o_una_pagina_invalidos_son_400_sin_llamar_a_la_plataforma(string consulta, string codigo, string literal)
    {
        var ct = Preparar();

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).GetAsync(Ruta + consulta, ct);

        await ConfiguracionTests.DebeSerRegla(r, HttpStatusCode.BadRequest, codigo, literal, null, ct);
        Plataforma.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_busqueda_de_mas_de_100_caracteres_es_400_FILTRO_INVALIDO()
    {
        var ct = Preparar();

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).GetAsync(Ruta + "?q=" + new string('a', 101), ct);

        await ConfiguracionTests.DebeSerRegla(r, HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "Un filtro no es válido.", null, ct);
    }

    [Fact]
    public async Task Un_pageSize_mayor_que_el_de_la_plataforma_se_arma_con_varias_paginas_en_orden()
    {
        var ct = Preparar();
        var ids = Enumerable.Range(0, 250).Select(_ => Guid.NewGuid()).ToList();
        Plataforma.Filas = [.. ids.Select((id, i) => UsuariosPlataformaDePrueba.Fila(id, $"U{i:000}", $"u{i}@empresa.test", "gestor_impuestos"))];

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).GetAsync(Ruta + "?page=2&pageSize=150", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        json.RootElement.GetProperty("total").GetInt32().Should().Be(250);
        json.RootElement.GetProperty("pageSize").GetInt32().Should().Be(150);
        json.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(ids.Skip(150));
        Plataforma.Pedidos.Select(p => ((ListarUsuariosProductoRequest)p.Pedido).Tamano).Should().OnlyContain(t => t <= 100);
    }

    [Fact]
    public async Task El_Gestor_recibe_403_en_todas_las_rutas_sin_llamar_a_la_plataforma()
    {
        var ct = Preparar();
        var gestor = Api.Cliente(TokenGestor(Api, Guid.NewGuid()));
        var usuario = Guid.NewGuid();

        using var listar = await gestor.GetAsync(Ruta, ct);
        using var invitar = await gestor.PostAsJsonAsync(Ruta, new { email = "x@empresa.test", nombre = "X", rol = "gestor_impuestos" }, ct);
        using var rol = await gestor.PutAsJsonAsync($"{Ruta}/{usuario}/rol", new { rol = "gestor_impuestos" }, ct);
        using var suspender = await gestor.PostAsync($"{Ruta}/{usuario}/suspender", null, ct);
        using var reactivar = await gestor.PostAsync($"{Ruta}/{usuario}/reactivar", null, ct);

        new[] { listar, invitar, rol, suspender, reactivar }.Select(x => x.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Forbidden);
        Plataforma.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Quien_no_es_Super_Admin_y_manda_empresa_recibe_403_EMPRESA_SOLO_SUPER_ADMIN()
    {
        var ct = Preparar();
        var admin = Api.Cliente(TokenAdmin(Api, Guid.NewGuid()));

        using var listar = await admin.GetAsync($"{Ruta}?empresa=no-es-uuid", ct);
        using var suspender = await admin.PostAsync($"{Ruta}/{Guid.NewGuid()}/suspender?empresa={Guid.NewGuid()}", null, ct);

        await ConfiguracionTests.DebeSerRegla(listar, HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.", null, ct);
        await ConfiguracionTests.DebeSerRegla(suspender, HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.", null, ct);
        Plataforma.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task El_Super_Admin_sin_empresa_valida_recibe_400_y_con_una_sin_Impuestos_404()
    {
        var ct = Preparar();
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));

        using var sinEmpresa = await sa.GetAsync(Ruta, ct);
        using var noUuid = await sa.PostAsync($"{Ruta}/{Guid.NewGuid()}/suspender?empresa=abc", null, ct);
        using var sinImpuestos = await sa.GetAsync($"{Ruta}?empresa={Guid.NewGuid()}", ct);
        using var invitarSinImpuestos = await sa.PostAsJsonAsync($"{Ruta}?empresa={Guid.NewGuid()}", new { email = "x@empresa.test", nombre = "X", rol = "admin_impuestos" }, ct);

        await ConfiguracionTests.DebeSerRegla(sinEmpresa, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
        await ConfiguracionTests.DebeSerRegla(noUuid, HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.", null, ct);
        await ConfiguracionTests.DebeSerRegla(sinImpuestos, HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.", null, ct);
        await ConfiguracionTests.DebeSerRegla(invitarSinImpuestos, HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.", null, ct);
        Plataforma.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task El_Super_Admin_gestiona_cualquier_compania_con_Impuestos_y_la_pagina_trae_la_empresa_con_su_NIT()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, EstadoAprovisionamiento.Listo);
        fixture.Directorio.Sembrar(empresa, "Transportes Andinos S.A.S.", "900123456");
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));
        var usuario = Guid.NewGuid();

        using var listar = await sa.GetAsync($"{Ruta}?empresa={empresa}", ct);
        using var suspender = await sa.PostAsync($"{Ruta}/{usuario}/suspender?empresa={empresa}", null, ct);

        var texto = await listar.Content.ReadAsStringAsync(ct);
        listar.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using var json = JsonDocument.Parse(texto);
        var cabecera = json.RootElement.GetProperty("empresa");
        cabecera.GetProperty("id").GetGuid().Should().Be(empresa);
        cabecera.GetProperty("nombre").GetString().Should().Be("Transportes Andinos S.A.S.");
        cabecera.GetProperty("nit").GetString().Should().Be("900123456");
        suspender.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Plataforma.Pedidos.Select(p => p.Contexto).Should().OnlyContain(c => c == new ContextoUsuarios(empresa, CargaManualCredencialesTests.ActorSuperAdmin));
    }

    [Fact]
    public async Task Invitar_responde_201_con_el_usuario_invitado_y_manda_correo_nombre_y_rol()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        var invitacion = Guid.NewGuid();
        Plataforma.Filas = [UsuariosPlataformaDePrueba.Fila(invitacion, "Eva Ruiz", "eva@empresa.test", "gestor_impuestos", EstadoUsuarioProducto.Invitado, MotivoSinAcciones.Invitacion)];

        using var r = await Api.Cliente(TokenAdmin(Api, empresa)).PostAsJsonAsync(Ruta, new { email = "Eva@Empresa.test", nombre = " Eva Ruiz ", rol = "gestor_impuestos" }, ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        using var json = JsonDocument.Parse(texto);
        json.RootElement.GetProperty("id").GetGuid().Should().Be(invitacion);
        json.RootElement.GetProperty("estado").GetString().Should().Be("INVITADO");
        json.RootElement.GetProperty("rol").GetString().Should().Be("gestor_impuestos");
        var pedido = (InvitarUsuarioProductoRequest)Plataforma.Pedidos.First(p => p.Metodo == "Invitar").Pedido;
        pedido.Email.Should().Be("Eva@Empresa.test");
        pedido.Nombre.Should().Be("Eva Ruiz");
        pedido.RolCodigo.Should().Be("gestor_impuestos");
    }

    [Theory]
    [InlineData("eva@empresa.test", "Eva", "admin_diagnostico", 422, "ROL_NO_PERMITIDO", "El rol no es válido para Impuestos.", "rol")]
    [InlineData("eva@empresa.test", "Eva", null, 422, "ROL_NO_PERMITIDO", "El rol no es válido para Impuestos.", "rol")]
    [InlineData("no-es-correo", "Eva", "gestor_impuestos", 400, "CORREO_INVALIDO", "Un correo no es válido.", "email")]
    [InlineData("eva@empresa.test", "  ", "gestor_impuestos", 400, "NOMBRE_REQUERIDO", "Escribe el nombre.", "nombre")]
    public async Task Invitar_valida_el_cuerpo_antes_de_llamar_a_la_plataforma(
        string email, string nombre, string? rol, int status, string codigo, string literal, string campo)
    {
        var ct = Preparar();

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).PostAsJsonAsync(Ruta, new { email, nombre, rol }, ct);

        await ConfiguracionTests.DebeSerRegla(r, (HttpStatusCode)status, codigo, literal, campo, ct);
        Plataforma.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Cambiar_rol_suspender_y_reactivar_responden_204_y_delegan_en_la_plataforma()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var admin = Api.Cliente(TokenAdmin(Api, empresa));

        using var rol = await admin.PutAsJsonAsync($"{Ruta}/{usuario}/rol", new { rol = "gestor_impuestos" }, ct);
        using var suspender = await admin.PostAsync($"{Ruta}/{usuario}/suspender", null, ct);
        using var reactivar = await admin.PostAsync($"{Ruta}/{usuario}/reactivar", null, ct);

        new[] { rol, suspender, reactivar }.Select(x => x.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.NoContent);
        Plataforma.Pedidos.Select(p => p.Metodo).Should().Equal("CambiarRol", "Suspender", "Reactivar");
        Plataforma.Pedidos.First().Pedido.Should().Be((usuario, "gestor_impuestos"));
        Plataforma.Pedidos.Skip(1).Select(p => p.Pedido).Should().Equal(usuario, usuario);
    }

    [Fact]
    public async Task Cambiar_a_un_rol_que_no_es_de_Impuestos_es_422_y_un_usuarioId_que_no_es_uuid_404()
    {
        var ct = Preparar();
        var admin = Api.Cliente(TokenAdmin(Api, Guid.NewGuid()));

        using var rolAjeno = await admin.PutAsJsonAsync($"{Ruta}/{Guid.NewGuid()}/rol", new { rol = "usuario_diagnostico" }, ct);
        using var noUuid = await admin.PostAsync($"{Ruta}/no-es-uuid/suspender", null, ct);

        await ConfiguracionTests.DebeSerRegla(rolAjeno, HttpStatusCode.UnprocessableEntity, "ROL_NO_PERMITIDO", "El rol no es válido para Impuestos.", "rol", ct);
        await ConfiguracionTests.DebeSerRegla(noUuid, HttpStatusCode.NotFound, "USUARIO_NO_ENCONTRADO", "El usuario no existe en Impuestos de esta compañía.", null, ct);
        Plataforma.Pedidos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ROL_NO_ASIGNABLE", StatusCode.PermissionDenied, 403, "No puedes asignar ese rol.")]
    [InlineData("OBJETIVO_PROTEGIDO", StatusCode.PermissionDenied, 403, "Este usuario se gestiona desde el administrador de la plataforma.")]
    [InlineData("USUARIO_NO_ENCONTRADO", StatusCode.NotFound, 404, "El usuario no existe en Impuestos de esta compañía.")]
    [InlineData("ULTIMO_ADMINISTRADOR", StatusCode.FailedPrecondition, 409, "La compañía debe conservar al menos un Administrador Impuestos.")]
    [InlineData("USUARIO_SUSPENDIDO", StatusCode.FailedPrecondition, 409, "El usuario está suspendido en Impuestos.")]
    [InlineData("INVITACION_PENDIENTE", StatusCode.FailedPrecondition, 409, "El usuario todavía no acepta su invitación.")]
    [InlineData("ROL_YA_VIGENTE", StatusCode.FailedPrecondition, 409, "El usuario ya tiene un rol vigente en Impuestos.")]
    [InlineData("ACCION_SOBRE_SI_MISMO", StatusCode.InvalidArgument, 400, "No puedes hacer esta acción sobre tu propio usuario.")]
    [InlineData("ROL_NO_PERMITIDO", StatusCode.InvalidArgument, 422, "El rol no es válido para Impuestos.")]
    public async Task Los_rechazos_de_la_plataforma_salen_con_el_codigo_y_el_literal_del_contrato(string codigo, StatusCode estado, int status, string literal)
    {
        var ct = Preparar();
        Plataforma.Falla = UsuariosPlataformaDePrueba.Rechazo(codigo, estado);

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).PutAsJsonAsync($"{Ruta}/{Guid.NewGuid()}/rol", new { rol = "gestor_impuestos" }, ct);

        await ConfiguracionTests.DebeSerRegla(r, (HttpStatusCode)status, codigo, literal, null, ct);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Internal)]
    public async Task Si_la_plataforma_no_responde_es_503_PLATAFORMA_NO_DISPONIBLE_con_Retry_After(StatusCode estado)
    {
        var ct = Preparar();
        Plataforma.Falla = new RpcException(new Status(estado, "caída de prueba"));

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).GetAsync(Ruta, ct);

        await ConfiguracionTests.DebeSerRegla(r, HttpStatusCode.ServiceUnavailable, "PLATAFORMA_NO_DISPONIBLE", "La plataforma no responde. Intenta más tarde.", null, ct);
        r.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Un_permiso_negado_por_la_plataforma_sin_codigo_es_403_sin_cuerpo()
    {
        var ct = Preparar();
        Plataforma.Falla = new RpcException(new Status(StatusCode.PermissionDenied, "actor sin permiso en la base"));

        using var r = await Api.Cliente(TokenAdmin(Api, Guid.NewGuid())).PostAsync($"{Ruta}/{Guid.NewGuid()}/suspender", null, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await r.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_la_plataforma_configurada_es_503_PLATAFORMA_NO_DISPONIBLE()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        using var r = await api.Cliente(TokenAdmin(api, Guid.NewGuid())).GetAsync(Ruta, ct);

        await ConfiguracionTests.DebeSerRegla(r, HttpStatusCode.ServiceUnavailable, "PLATAFORMA_NO_DISPONIBLE", "La plataforma no responde. Intenta más tarde.", null, ct);
    }

    public static string TokenAdmin(ApiDePrueba api, Guid empresa, Guid? usuario = null) =>
        api.Token(empresa: empresa, roles: ["admin_impuestos"], permisos: [Permisos.UsuariosGestionar, Permisos.LiquidacionesVer], usuario: usuario ?? Guid.NewGuid());

    public static string TokenGestor(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, roles: ["gestor_impuestos"], permisos: [.. Permisos.RolesPorDefecto.Single(r => r.Codigo == "gestor_impuestos").Permisos]);

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        Plataforma.Reiniciar();
        return TestContext.Current.CancellationToken;
    }
}

/// <summary>Una API con base para la clase, con los dobles de la plataforma de usuarios y del directorio de compañías.</summary>
public sealed class ApiUsuariosFixture : IAsyncLifetime
{
    public UsuariosPlataformaDePrueba Plataforma { get; } = new();

    public DirectorioEmpresasDePrueba Directorio { get; } = new();

    public ApiDePrueba Api { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Api = await ApiDePrueba.ConBaseAsync(b =>
    {
        b.Services.AddSingleton<IUsuariosPlataforma>(Plataforma);
        b.Services.AddSingleton<IDirectorioEmpresas>(Directorio);
    });

    public async ValueTask DisposeAsync() => await Api.DisposeAsync();
}
