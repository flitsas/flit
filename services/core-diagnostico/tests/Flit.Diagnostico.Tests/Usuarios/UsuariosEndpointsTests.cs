using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Flit.Diagnostico.Tests.Usuarios;

/// <summary>
/// HU #14587 (contrato-api §3.1–§3.3, ADR-0089) — módulo Usuarios de la compañía: <c>/usuarios*</c> delega en el gRPC
/// <c>flit.platform.v1.UsuariosProductoService</c> de core-api. Aquí se prueba lo que pone core-diagnostico: el permiso
/// <c>diagnostico.usuarios.gestionar</c> en cada ruta (SP-17), la empresa solo del token (SP-13), la lista blanca de roles por código
/// (SP-10), SP-11 y la auto-acción antes de llamar, el 202 uniforme sin correo en los logs (SP-15/SP-16), el DTO sin datos de otros
/// productos (SP-18) y la plataforma no configurada. La plataforma es un doble del cliente gRPC; sin base. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~UsuariosEndpointsTests"
/// </code>
/// </example>
public sealed class UsuariosEndpointsTests
{
    private const string Base = Program.Prefijo + "/usuarios";

    private static readonly string[] CamposFila = ["id", "tipo", "nombre", "correo", "rol", "rol_etiqueta", "estado", "acciones", "motivo_sin_acciones"];

    // §3.3 GET /usuarios + SP-13 + SP-18 — lista paginada con la empresa del token, el actor y los filtros traducidos al rpc.
    [Fact]
    public async Task HU14587_listar_delega_con_la_empresa_del_token_y_devuelve_el_DTO_del_contrato()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        var activo = Guid.NewGuid();
        var invitado = Guid.NewGuid();
        falso.Lista.Total = 2;
        falso.Lista.RolesAsignables.Add("usuario_diagnostico");
        falso.Lista.Usuarios.Add(UsuariosDePrueba.Fila(activo, acciones: [AccionUsuario.CambiarRol, AccionUsuario.Suspender]));
        falso.Lista.Usuarios.Add(UsuariosDePrueba.Fila(invitado, rol: "admin_diagnostico", estado: EstadoUsuarioProducto.Invitado, tipo: TipoFila.Invitacion, motivo: MotivoSinAcciones.Invitacion));
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        var empresaA = Guid.NewGuid();
        var token = UsuariosDePrueba.Admin(api, empresaA);
        using var cliente = api.Cliente(token);
        cliente.DefaultRequestHeaders.Add("X-Flit-Tenant-Id", Guid.NewGuid().ToString());

        var respuesta = await cliente.GetAsync($"{Base}?busqueda=Pers&rol=usuario_diagnostico&rol=admin_diagnostico&estado=ACTIVO&estado=INVITADO&pagina=2&tamano=10", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var llamada = falso.Llamadas.Should().ContainSingle().Subject;
        llamada.Metodo.Should().Be("ListarUsuariosProducto");
        llamada.Empresa.Should().Be(empresaA.ToString(), "SP-13: la empresa sale solo del token, ni de la cabecera ni de un parámetro");
        llamada.Actor.Should().Be(Sujeto(token));
        var pedido = (ListarUsuariosProductoRequest)llamada.Peticion;
        pedido.Producto.Should().Be("diagnostico");
        pedido.Busqueda.Should().Be("Pers");
        pedido.Roles.Should().BeEquivalentTo("usuario_diagnostico", "admin_diagnostico");
        pedido.Estados.Should().BeEquivalentTo([EstadoUsuarioProducto.Activo, EstadoUsuarioProducto.Invitado]);
        pedido.Pagina.Should().Be(2);
        pedido.Tamano.Should().Be(10);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("pagina").GetInt32().Should().Be(2);
        cuerpo.GetProperty("tamano").GetInt32().Should().Be(10);
        cuerpo.GetProperty("total").GetInt32().Should().Be(2);
        cuerpo.GetProperty("roles_asignables").EnumerateArray().Select(r => r.GetString()).Should().Equal("usuario_diagnostico");
        var filas = cuerpo.GetProperty("usuarios").EnumerateArray().ToList();
        filas.Should().HaveCount(2);
        filas.Should().AllSatisfy(f => f.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(CamposFila, "SP-18: nada de roles de otros productos ni datos de sesión"));
        filas[0].GetProperty("id").GetGuid().Should().Be(activo);
        filas[0].GetProperty("tipo").GetString().Should().Be("USUARIO");
        filas[0].GetProperty("correo").GetString().Should().Be(UsuariosDePrueba.Correo);
        filas[0].GetProperty("rol").GetString().Should().Be("usuario_diagnostico");
        filas[0].GetProperty("rol_etiqueta").GetString().Should().Be("Usuario de Diagnóstico");
        filas[0].GetProperty("estado").GetString().Should().Be("ACTIVO");
        filas[0].GetProperty("acciones").EnumerateArray().Select(a => a.GetString()).Should().Equal("CAMBIAR_ROL", "SUSPENDER");
        filas[0].GetProperty("motivo_sin_acciones").ValueKind.Should().Be(JsonValueKind.Null);
        filas[1].GetProperty("tipo").GetString().Should().Be("INVITACION");
        filas[1].GetProperty("estado").GetString().Should().Be("INVITADO");
        filas[1].GetProperty("rol_etiqueta").GetString().Should().Be("Administrador de Diagnóstico");
        filas[1].GetProperty("motivo_sin_acciones").GetString().Should().Be("INVITACION");
        filas[1].GetProperty("acciones").GetArrayLength().Should().Be(0);
    }

    // SP-10 / SP-18 — una fila o un rol asignable fuera de la lista blanca no llega al front (defensa en profundidad).
    [Fact]
    public async Task HU14587_SP10_filas_y_roles_asignables_fuera_de_la_lista_blanca_no_salen()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        falso.Lista.Total = 2;
        falso.Lista.RolesAsignables.Add("usuario_diagnostico");
        falso.Lista.RolesAsignables.Add("AdminCompany");
        falso.Lista.Usuarios.Add(UsuariosDePrueba.Fila(Guid.NewGuid()));
        falso.Lista.Usuarios.Add(UsuariosDePrueba.Fila(Guid.NewGuid(), rol: "AdminCompany"));
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var cuerpo = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid())).GetFromJsonAsync<JsonElement>(Base, ct);

        cuerpo.GetProperty("roles_asignables").EnumerateArray().Select(r => r.GetString()).Should().Equal("usuario_diagnostico");
        cuerpo.GetProperty("usuarios").EnumerateArray().Select(u => u.GetProperty("rol").GetString()).Should().Equal("usuario_diagnostico");
    }

    // §3.2 / SP-13 — en el grupo de la compañía, un parámetro empresa es un 400 y no se llama a la plataforma, en las cinco rutas.
    [Fact]
    public async Task HU14587_SP13_parametro_empresa_en_el_grupo_de_la_compania_es_400_EMPRESA_NO_PERMITIDA()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        using var cliente = api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()));
        var otra = Guid.NewGuid();

        foreach (var (metodo, ruta, json) in Rutas(Guid.NewGuid()))
        {
            using var pedido = Pedido(metodo, $"{ruta}?empresa={otra}", json);
            var respuesta = await cliente.SendAsync(pedido, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{metodo} {ruta}");
            (await Code(respuesta, ct)).Should().Be("EMPRESA_NO_PERMITIDA", $"{metodo} {ruta}");
        }

        falso.Llamadas.Should().BeEmpty();
    }

    // §3.1 / SP-17 — usuario_diagnostico (sin diagnostico.usuarios.gestionar) recibe 403 en cada ruta, sin llamar a la plataforma.
    [Fact]
    public async Task HU14587_SP17_usuario_diagnostico_recibe_403_en_cada_ruta()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        using var cliente = api.Cliente(UsuariosDePrueba.Usuario(api, Guid.NewGuid()));

        foreach (var (metodo, ruta, json) in Rutas(Guid.NewGuid()))
        {
            using var pedido = Pedido(metodo, ruta, json);
            (await cliente.SendAsync(pedido, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{metodo} {ruta}");
        }

        falso.Llamadas.Should().BeEmpty();
    }

    // §3.3 GET — filtros y paginación fuera del contrato: 400 sin llamar a la plataforma.
    [Theory]
    [InlineData("rol=AdminCompany", "FILTRO_INVALIDO")]
    [InlineData("rol=SuperAdmin", "FILTRO_INVALIDO")]
    [InlineData("estado=BORRADO", "FILTRO_INVALIDO")]
    [InlineData("busqueda=__101__", "FILTRO_INVALIDO")]
    [InlineData("pagina=0", "PAGINACION_INVALIDA")]
    [InlineData("pagina=x", "PAGINACION_INVALIDA")]
    [InlineData("tamano=101", "PAGINACION_INVALIDA")]
    [InlineData("tamano=0", "PAGINACION_INVALIDA")]
    [InlineData("pagina=401&tamano=25", "PAGINACION_INVALIDA")]
    public async Task HU14587_filtros_y_paginacion_invalidos_responden_400(string consulta, string codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var respuesta = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()))
            .GetAsync($"{Base}?{consulta.Replace("__101__", new string('a', 101), StringComparison.Ordinal)}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(respuesta, ct)).Should().Be(codigo);
        falso.Llamadas.Should().BeEmpty();
    }

    // §3.3 POST invitaciones + SP-15 + SP-16 — 202 uniforme para cualquier correo, solo el código de rol viaja y el correo no va a los logs.
    [Fact]
    public async Task HU14587_SP15_invitar_responde_202_uniforme_y_el_correo_no_va_a_los_logs()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        var logs = new CapturaDeLogs();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            falso.Registrar(b);
            logs.Registrar(b);
        });
        var empresa = Guid.NewGuid();
        var token = UsuariosDePrueba.Admin(api, empresa);
        using var cliente = api.Cliente(token);
        string[] correos = ["nueva.persona@empresa.test", "ya.miembro@empresa.test", "de.otra@otra-empresa.test"];
        var cuerpos = new List<string>();

        foreach (var correo in correos)
        {
            var respuesta = await cliente.PostAsync($"{Base}/invitaciones",
                Json($$"""{ "correo": "{{correo}}", "nombre": "  Persona Ficticia  ", "rol": "usuario_diagnostico", "rol_id": "{{Guid.NewGuid()}}" }"""), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, correo);
            cuerpos.Add(await respuesta.Content.ReadAsStringAsync(ct));
        }

        cuerpos.Distinct().Should().ContainSingle("SP-15: el mismo cuerpo en todos los casos");
        var json = JsonElement.Parse(cuerpos[0]);
        json.GetProperty("resultado").GetString().Should().Be("ACEPTADA");
        json.GetProperty("mensaje").GetString().Should().Be("Si el correo es válido, la persona recibirá la invitación o el acceso a Diagnóstico.");
        falso.Llamadas.Should().HaveCount(3).And.AllSatisfy(l =>
        {
            l.Metodo.Should().Be("InvitarUsuarioProducto");
            l.Empresa.Should().Be(empresa.ToString());
            l.Actor.Should().Be(Sujeto(token));
            var pedido = (InvitarUsuarioProductoRequest)l.Peticion;
            pedido.Producto.Should().Be("diagnostico");
            pedido.RolCodigo.Should().Be("usuario_diagnostico", "SP-10: solo el código; el rol_id del cliente se ignora");
            pedido.Nombre.Should().Be("Persona Ficticia");
        });
        ((InvitarUsuarioProductoRequest)falso.Llamadas.First().Peticion).Email.Should().Be(correos[0]);
        logs.Lineas.Should().NotBeEmpty();
        foreach (var correo in correos)
            logs.Lineas.Should().NotContain(l => l.Contains(correo, StringComparison.OrdinalIgnoreCase), "SP-16: ningún correo en claro en los logs");
    }

    // §3.3 POST invitaciones — validación de forma (400/422) antes de llamar a la plataforma; un rol_id libre no reemplaza al código.
    [Theory]
    [InlineData("""{ "correo": "no-es-correo", "nombre": "Persona", "rol": "usuario_diagnostico" }""", 400, "CORREO_INVALIDO", "correo")]
    [InlineData("""{ "nombre": "Persona", "rol": "usuario_diagnostico" }""", 400, "CORREO_INVALIDO", "correo")]
    [InlineData("""{ "correo": "a@b.test", "nombre": "   ", "rol": "usuario_diagnostico" }""", 400, "NOMBRE_REQUERIDO", "nombre")]
    [InlineData("""{ "correo": "a@b.test", "nombre": "__151__", "rol": "usuario_diagnostico" }""", 400, "NOMBRE_REQUERIDO", "nombre")]
    [InlineData("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "AdminCompany" }""", 422, "ROL_NO_PERMITIDO", "rol")]
    [InlineData("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "SuperAdmin" }""", 422, "ROL_NO_PERMITIDO", "rol")]
    [InlineData("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "usuario_tramites" }""", 422, "ROL_NO_PERMITIDO", "rol")]
    [InlineData("""{ "correo": "a@b.test", "nombre": "Persona", "rol_id": "0195f0a0-0000-7000-8000-000000000001" }""", 422, "ROL_NO_PERMITIDO", "rol")]
    [InlineData("""[]""", 400, "CORREO_INVALIDO", "correo")]
    public async Task HU14587_SP10_invitar_valida_correo_nombre_y_lista_blanca_de_roles(string cuerpo, int estado, string codigo, string campo)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var respuesta = await api.Cliente(UsuariosDePrueba.SuperAdmin(api))
            .PostAsync($"{Base}/invitaciones", Json(cuerpo.Replace("__151__", new string('n', 151), StringComparison.Ordinal)), ct);

        ((int)respuesta.StatusCode).Should().Be(estado);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("code").GetString().Should().Be(codigo);
        json.GetProperty("campo").GetString().Should().Be(campo);
        falso.Llamadas.Should().BeEmpty();
    }

    // SP-11 = a — un admin_diagnostico no da admin_diagnostico (403 ROL_NO_ASIGNABLE sin llamar); el SuperAdmin sí llega a la plataforma.
    [Fact]
    public async Task HU14587_SP11_admin_diagnostico_no_asigna_admin_diagnostico_y_el_SuperAdmin_si()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        var objetivo = Guid.NewGuid();
        using var admin = api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()));

        var invitar = await admin.PostAsync($"{Base}/invitaciones", Json("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "admin_diagnostico" }"""), ct);
        var cambiar = await admin.PutAsync($"{Base}/{objetivo}/rol", Json("""{ "rol": "admin_diagnostico" }"""), ct);

        invitar.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(invitar, ct)).Should().Be("ROL_NO_ASIGNABLE");
        cambiar.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(cambiar, ct)).Should().Be("ROL_NO_ASIGNABLE");
        falso.Llamadas.Should().BeEmpty();

        using var superAdmin = api.Cliente(UsuariosDePrueba.SuperAdmin(api));
        (await superAdmin.PutAsync($"{Base}/{objetivo}/rol", Json("""{ "rol": "admin_diagnostico" }"""), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        ((CambiarRolProductoRequest)falso.Llamadas.Should().ContainSingle().Subject.Peticion).RolCodigo.Should().Be("admin_diagnostico");
    }

    // §3.3 PUT rol — 200 con la fila actualizada; el rol es solo código de la lista blanca.
    [Fact]
    public async Task HU14587_SP9_cambiar_rol_delega_y_devuelve_la_fila_con_acciones_recalculadas()
    {
        var ct = TestContext.Current.CancellationToken;
        var objetivo = Guid.NewGuid();
        var falso = new UsuariosProductoFalso { Fila = UsuariosDePrueba.Fila(objetivo, acciones: [AccionUsuario.Suspender]) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        var empresa = Guid.NewGuid();
        using var cliente = api.Cliente(UsuariosDePrueba.Admin(api, empresa));

        var respuesta = await cliente.PutAsync($"{Base}/{objetivo}/rol", Json("""{ "rol": "usuario_diagnostico" }"""), ct);
        var prohibido = await cliente.PutAsync($"{Base}/{objetivo}/rol", Json("""{ "rol": "AdminCompany" }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var fila = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        fila.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(CamposFila);
        fila.GetProperty("id").GetGuid().Should().Be(objetivo);
        fila.GetProperty("acciones").EnumerateArray().Select(a => a.GetString()).Should().Equal("SUSPENDER");
        var llamada = falso.Llamadas.Should().ContainSingle().Subject;
        llamada.Empresa.Should().Be(empresa.ToString());
        var pedido = (CambiarRolProductoRequest)llamada.Peticion;
        pedido.Producto.Should().Be("diagnostico");
        pedido.UsuarioId.Should().Be(objetivo.ToString());
        pedido.RolCodigo.Should().Be("usuario_diagnostico");
        prohibido.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Code(prohibido, ct)).Should().Be("ROL_NO_PERMITIDO");
    }

    // SP-12 — la acción sobre sí mismo es 400 ACCION_SOBRE_SI_MISMO en cambiar rol, suspender y reactivar, sin llamar a la plataforma.
    [Fact]
    public async Task HU14587_SP12_accion_sobre_si_mismo_es_400_sin_llamar_a_la_plataforma()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        var yo = Guid.NewGuid();
        using var cliente = api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid(), sujeto: yo.ToString()));

        var respuestas = new[]
        {
            await cliente.PutAsync($"{Base}/{yo}/rol", Json("""{ "rol": "usuario_diagnostico" }"""), ct),
            await cliente.PostAsync($"{Base}/{yo}/suspension", null, ct),
            await cliente.DeleteAsync($"{Base}/{yo}/suspension", ct),
        };

        foreach (var respuesta in respuestas)
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Code(respuesta, ct)).Should().Be("ACCION_SOBRE_SI_MISMO");
        }

        falso.Llamadas.Should().BeEmpty();
    }

    // SP-12 = a — suspender y reactivar en el producto delegan en sus rpc y devuelven la fila con su estado de producto.
    [Fact]
    public async Task HU14587_SP12_suspender_y_reactivar_delegan_y_devuelven_la_fila()
    {
        var ct = TestContext.Current.CancellationToken;
        var objetivo = Guid.NewGuid();
        var falso = new UsuariosProductoFalso { Fila = UsuariosDePrueba.Fila(objetivo, estado: EstadoUsuarioProducto.Suspendido, acciones: [AccionUsuario.Reactivar]) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        var empresa = Guid.NewGuid();
        using var cliente = api.Cliente(UsuariosDePrueba.Admin(api, empresa));

        var suspension = await cliente.PostAsync($"{Base}/{objetivo}/suspension", null, ct);
        falso.Fila = UsuariosDePrueba.Fila(objetivo, motivo: MotivoSinAcciones.CuentaInactiva);
        var reactivacion = await cliente.DeleteAsync($"{Base}/{objetivo}/suspension", ct);

        suspension.StatusCode.Should().Be(HttpStatusCode.OK, await suspension.Content.ReadAsStringAsync(ct));
        var suspendida = await suspension.Content.ReadFromJsonAsync<JsonElement>(ct);
        suspendida.GetProperty("estado").GetString().Should().Be("SUSPENDIDO");
        suspendida.GetProperty("acciones").EnumerateArray().Select(a => a.GetString()).Should().Equal("REACTIVAR");
        reactivacion.StatusCode.Should().Be(HttpStatusCode.OK);
        var reactivada = await reactivacion.Content.ReadFromJsonAsync<JsonElement>(ct);
        reactivada.GetProperty("estado").GetString().Should().Be("ACTIVO");
        reactivada.GetProperty("motivo_sin_acciones").GetString().Should().Be("CUENTA_INACTIVA");
        falso.Llamadas.Select(l => l.Metodo).Should().Equal("SuspenderEnProducto", "ReactivarEnProducto");
        falso.Llamadas.Should().AllSatisfy(l => l.Empresa.Should().Be(empresa.ToString()));
        ((SuspenderEnProductoRequest)falso.Llamadas.First().Peticion).UsuarioId.Should().Be(objetivo.ToString());
        ((ReactivarEnProductoRequest)falso.Llamadas.Last().Peticion).Producto.Should().Be("diagnostico");
    }

    // CR-5 (review be2b) / D-5 — core-api escribió con éxito y devolvió una fila que no se puede mostrar (rol fuera de la lista blanca):
    // la respuesta sigue siendo 404 USUARIO_NO_ENCONTRADO, pero es un contrato roto y va al log como Error (en la lista, una fila rara
    // solo se omite con Warning).
    [Fact]
    public async Task HU14587_CR5_fila_no_mostrable_tras_una_escritura_se_registra_como_Error()
    {
        var ct = TestContext.Current.CancellationToken;
        var objetivo = Guid.NewGuid();
        var falso = new UsuariosProductoFalso { Fila = UsuariosDePrueba.Fila(objetivo, rol: "AdminCompany") };
        falso.Lista.Usuarios.Add(UsuariosDePrueba.Fila(Guid.NewGuid(), rol: "AdminCompany"));
        var logs = new CapturaDeLogs();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            falso.Registrar(b);
            logs.Registrar(b);
        });
        using var cliente = api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()));

        (await cliente.GetAsync(Base, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Lineas.Should().Contain(l => l.StartsWith("[Warning]", StringComparison.Ordinal) && l.Contains("se omiten", StringComparison.Ordinal));
        logs.Lineas.Should().NotContain(l => l.StartsWith("[Error]", StringComparison.Ordinal) && l.Contains("fila", StringComparison.OrdinalIgnoreCase));

        var respuesta = await cliente.PutAsync($"{Base}/{objetivo}/rol", Json("""{ "rol": "usuario_diagnostico" }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Code(respuesta, ct)).Should().Be("USUARIO_NO_ENCONTRADO");
        logs.Lineas.Should().Contain(l => l.StartsWith("[Error]", StringComparison.Ordinal) && l.Contains("CambiarRol", StringComparison.Ordinal),
            "una escritura con éxito que devuelve una fila fuera del contrato es un contrato roto");
    }

    // §3.3 — un {usuario} que no es uuid no existe (404), sin llamar a la plataforma.
    [Fact]
    public async Task HU14587_usuario_que_no_es_uuid_es_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var respuesta = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid())).PostAsync($"{Base}/no-es-uuid/suspension", null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        falso.Llamadas.Should().BeEmpty();
    }

    // §3.3 503 — sin la dirección gRPC de core-api no hay con quién hablar: 503 PLATAFORMA_NO_CONFIGURADA (sin Retry-After).
    [Fact]
    public async Task HU14587_sin_plataforma_configurada_responde_503_PLATAFORMA_NO_CONFIGURADA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        using var cliente = api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()));

        foreach (var (metodo, ruta, json) in Rutas(Guid.NewGuid()))
        {
            using var pedido = Pedido(metodo, ruta, json);
            var respuesta = await cliente.SendAsync(pedido, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{metodo} {ruta}");
            (await Code(respuesta, ct)).Should().Be("PLATAFORMA_NO_CONFIGURADA");
            respuesta.Headers.RetryAfter.Should().BeNull();
        }
    }

    // DM-2 / SP-15 — invitar lleva la política de límite diagnostico-invitaciones (el 429 lo da el limitador de be1-b).
    [Fact]
    public async Task HU14587_DM2_invitar_exige_la_politica_de_limite_diagnostico_invitaciones()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();

        var rutas = api.App.Services.GetServices<EndpointDataSource>().SelectMany(f => f.Endpoints).OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).EndsWith("/usuarios/invitaciones", StringComparison.Ordinal))
            .ToList();

        rutas.Should().HaveCount(2, "la de la compañía y la del backoffice");
        rutas.Should().AllSatisfy(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("diagnostico-invitaciones"));
    }

    internal static (HttpMethod Metodo, string Ruta, string? Json)[] Rutas(Guid usuario, string? baseRutas = null)
    {
        var b = baseRutas ?? Base;
        return
        [
            (HttpMethod.Get, b, null),
            (HttpMethod.Post, $"{b}/invitaciones", """{ "correo": "a@b.test", "nombre": "Persona", "rol": "usuario_diagnostico" }"""),
            (HttpMethod.Put, $"{b}/{usuario}/rol", """{ "rol": "usuario_diagnostico" }"""),
            (HttpMethod.Post, $"{b}/{usuario}/suspension", null),
            (HttpMethod.Delete, $"{b}/{usuario}/suspension", null),
        ];
    }

    internal static HttpRequestMessage Pedido(HttpMethod metodo, string ruta, string? json) =>
        new(metodo, ruta) { Content = json is null ? null : Json(json) };

    internal static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    internal static async Task<string?> Code(HttpResponseMessage respuesta, CancellationToken ct)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        return texto.Length == 0 ? null : JsonElement.Parse(texto).TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    internal static string Sujeto(string token) => new JsonWebTokenHandler().ReadJsonWebToken(token).Subject;
}
