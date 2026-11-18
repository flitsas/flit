using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Usuarios.UsuariosEndpointsTests;

namespace Flit.Diagnostico.Tests.Usuarios;

/// <summary>
/// HU #14587 (contrato-api §3.2, §3.3, SP-13, SP-18) — el backoffice de usuarios del SuperAdmin:
/// <list type="bullet">
/// <item><c>/backoffice/empresas/{empresa}/usuarios*</c>: la empresa objetivo sale de la ruta, solo si tiene Diagnóstico activo
/// (<c>SoloEmpresaHabilitada</c>: 404 <c>EMPRESA_NO_ENCONTRADA</c> sin llamar a la plataforma) y va en <c>x-flit-tenant-id</c>; la
/// lectura queda en <c>lecturas_backoffice</c> como toda lectura entre compañías.</item>
/// <item><c>/backoffice/usuarios/empresas</c>: las compañías de Identidad con sus conteos (<c>UsuariosProductoConteoService</c>), con
/// búsqueda, filtro CON/SIN, orden y paginación en el servidor.</item>
/// </list>
/// Las lecturas con empresa explícita necesitan la base (auditoría): sin Postgres en local se omiten. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~UsuariosBackofficeTests"
/// </code>
/// </example>
public sealed class UsuariosBackofficeTests
{
    private const string Empresas = Program.Prefijo + "/backoffice/usuarios/empresas";

    private static string Base(Guid empresa) => $"{Program.Prefijo}/backoffice/empresas/{empresa}/usuarios";

    // §3.2 / SP-13 — SuperAdmin lee los usuarios de la compañía B: x-flit-tenant-id = B (no la suya) y la lectura queda auditada.
    [Fact]
    public async Task HU14587_SP13_SuperAdmin_lista_los_usuarios_de_la_empresa_de_la_ruta()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        falso.Lista.Total = 1;
        falso.Lista.RolesAsignables.AddRange(["usuario_diagnostico", "admin_diagnostico"]);
        falso.Lista.Usuarios.Add(UsuariosDePrueba.Fila(Guid.NewGuid(), rol: "admin_diagnostico", acciones: [AccionUsuario.CambiarRol]));
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: falso.Registrar);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresaB = Guid.NewGuid();
        var token = UsuariosDePrueba.SuperAdmin(api, empresa: Guid.NewGuid());

        var respuesta = await api.Cliente(token).GetAsync($"{Base(empresaB)}?tamano=5", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var llamada = falso.Llamadas.Should().ContainSingle().Subject;
        llamada.Empresa.Should().Be(empresaB.ToString());
        llamada.Actor.Should().Be(Sujeto(token));
        ((ListarUsuariosProductoRequest)llamada.Peticion).Tamano.Should().Be(5);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("roles_asignables").EnumerateArray().Select(r => r.GetString()).Should().Equal("usuario_diagnostico", "admin_diagnostico");
        cuerpo.GetProperty("usuarios")[0].GetProperty("rol").GetString().Should().Be("admin_diagnostico");

        await using var conexion = new Npgsql.NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT count(*) FROM diagnostico.lecturas_backoffice WHERE empresa_consultada = @e", conexion);
        cmd.Parameters.AddWithValue("e", empresaB);
        ((long)(await cmd.ExecuteScalarAsync(ct))!).Should().Be(1, "la lectura entre compañías queda auditada");
    }

    // D-6 — el 200 del backoffice trae la cabecera empresa { id, nombre, nit } de la misma resolución que SoloEmpresaHabilitada (Identidad);
    // una empresa que Identidad no lista (sin Diagnóstico o inexistente) es 404 EMPRESA_NO_ENCONTRADA sin llamar a la plataforma.
    [Fact]
    public async Task HU14587_D6_lista_del_backoffice_trae_la_cabecera_de_la_empresa()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        var conNit = new EmpresaPlataforma(Guid.NewGuid(), "Flotas Ficticias SAS", "900555666");
        var sinNit = new EmpresaPlataforma(Guid.NewGuid(), "Renting Ficticio", null);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            falso.Registrar(b);
            b.Services.AddScoped<IEmpresasPlataforma>(_ => new EmpresasFijas([conNit, sinNit]));
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        using var cliente = api.Cliente(UsuariosDePrueba.SuperAdmin(api));

        var a = await cliente.GetAsync(Base(conNit.Id), ct);
        var b = await cliente.GetFromJsonAsync<JsonElement>(Base(sinNit.Id), ct);
        var ajena = await cliente.GetAsync(Base(Guid.NewGuid()), ct);
        var cliente2 = await api.Cliente(UsuariosDePrueba.Admin(api, conNit.Id)).GetFromJsonAsync<JsonElement>(Program.Prefijo + "/usuarios", ct);

        a.StatusCode.Should().Be(HttpStatusCode.OK, await a.Content.ReadAsStringAsync(ct));
        var empresa = (await a.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("empresa");
        empresa.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "nombre", "nit");
        empresa.GetProperty("id").GetGuid().Should().Be(conNit.Id);
        empresa.GetProperty("nombre").GetString().Should().Be("Flotas Ficticias SAS");
        empresa.GetProperty("nit").GetString().Should().Be("900555666");
        b.GetProperty("empresa").GetProperty("nit").ValueKind.Should().Be(JsonValueKind.Null);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Code(ajena, ct)).Should().Be("EMPRESA_NO_ENCONTRADA");
        falso.Llamadas.Should().HaveCount(3, "las dos del backoffice y la del cliente; la empresa ajena no llega a la plataforma");
        cliente2.TryGetProperty("empresa", out _).Should().BeFalse("GET /usuarios de la compañía no cambia (D-6)");
    }

    // §3.2 / SP-13 — las escrituras del backoffice van a la empresa de la ruta; el SuperAdmin puede dar admin_diagnostico (SP-11 = a).
    [Fact]
    public async Task HU14587_SP13_escrituras_del_backoffice_van_a_la_empresa_de_la_ruta()
    {
        var ct = TestContext.Current.CancellationToken;
        var objetivo = Guid.NewGuid();
        var falso = new UsuariosProductoFalso { Fila = UsuariosDePrueba.Fila(objetivo) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        var empresaB = Guid.NewGuid();
        using var cliente = api.Cliente(UsuariosDePrueba.SuperAdmin(api, empresa: Guid.NewGuid()));

        (await cliente.PostAsync($"{Base(empresaB)}/invitaciones", Json("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "admin_diagnostico" }"""), ct))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await cliente.PutAsync($"{Base(empresaB)}/{objetivo}/rol", Json("""{ "rol": "admin_diagnostico" }"""), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.PostAsync($"{Base(empresaB)}/{objetivo}/suspension", null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.DeleteAsync($"{Base(empresaB)}/{objetivo}/suspension", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        falso.Llamadas.Select(l => l.Metodo).Should().Equal("InvitarUsuarioProducto", "CambiarRolProducto", "SuspenderEnProducto", "ReactivarEnProducto");
        falso.Llamadas.Should().AllSatisfy(l => l.Empresa.Should().Be(empresaB.ToString()));
        ((InvitarUsuarioProductoRequest)falso.Llamadas.First().Peticion).RolCodigo.Should().Be("admin_diagnostico");
    }

    // SP-13 — una compañía sin Diagnóstico activo (o inexistente) es 404 EMPRESA_NO_ENCONTRADA y no se llama a la plataforma.
    [Fact]
    public async Task HU14587_SP13_empresa_sin_Diagnostico_es_404_sin_llamar_a_la_plataforma()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        var apagada = Guid.NewGuid();
        api.Productos.Apagar(apagada);
        using var cliente = api.Cliente(UsuariosDePrueba.SuperAdmin(api, empresa: Guid.NewGuid()));

        foreach (var (metodo, ruta, json) in Rutas(Guid.NewGuid(), Base(apagada)))
        {
            using var pedido = Pedido(metodo, ruta, json);
            var respuesta = await cliente.SendAsync(pedido, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{metodo} {ruta}");
            (await Code(respuesta, ct)).Should().Be("EMPRESA_NO_ENCONTRADA", $"{metodo} {ruta}");
        }

        falso.Llamadas.Should().BeEmpty();
    }

    // §3.3 GET /backoffice/usuarios/empresas — compañías de Identidad con sus conteos, búsqueda sin tildes, filtro, orden y paginación.
    [Fact]
    public async Task HU14587_tabla_de_companias_con_conteos_busqueda_filtro_orden_y_paginacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var alamo = new EmpresaPlataforma(Guid.NewGuid(), "Álamo Renting", "900111222");
        var zeta = new EmpresaPlataforma(Guid.NewGuid(), "Zeta Flotas", null);
        var beta = new EmpresaPlataforma(Guid.NewGuid(), "Beta Leasing", "800333444");
        var conteo = new UsuariosConteoFalso();
        conteo.Conteos.Add(new ConteoEmpresa { EmpresaId = zeta.Id.ToString(), Usuarios = 7, Administradores = 2 });
        conteo.Conteos.Add(new ConteoEmpresa { EmpresaId = alamo.Id.ToString(), Usuarios = 3, Administradores = 1 });
        conteo.Conteos.Add(new ConteoEmpresa { EmpresaId = Guid.NewGuid().ToString(), Usuarios = 9, Administradores = 9 });
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            falso.Registrar(b, conteo);
            b.Services.AddScoped<IEmpresasPlataforma>(_ => new EmpresasFijas([zeta, alamo, beta]));
        });
        var token = UsuariosDePrueba.SuperAdmin(api);
        using var cliente = api.Cliente(token);

        var todas = await cliente.GetFromJsonAsync<JsonElement>(Empresas, ct);
        var porUsuarios = await cliente.GetFromJsonAsync<JsonElement>($"{Empresas}?orden=usuarios", ct);
        var buscada = await cliente.GetFromJsonAsync<JsonElement>($"{Empresas}?busqueda=alamo", ct);
        var porNit = await cliente.GetFromJsonAsync<JsonElement>($"{Empresas}?busqueda=8003", ct);
        var sin = await cliente.GetFromJsonAsync<JsonElement>($"{Empresas}?usuarios=SIN", ct);
        var con = await cliente.GetFromJsonAsync<JsonElement>($"{Empresas}?usuarios=CON&pagina=2&tamano=1", ct);

        todas.GetProperty("total").GetInt32().Should().Be(3);
        todas.GetProperty("pagina").GetInt32().Should().Be(1);
        todas.GetProperty("tamano").GetInt32().Should().Be(25);
        Nombres(todas).Should().Equal("Álamo Renting", "Beta Leasing", "Zeta Flotas");
        var primera = todas.GetProperty("empresas")[0];
        primera.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "nombre", "nit", "estado", "usuarios_diagnostico", "administradores_diagnostico");
        primera.GetProperty("id").GetGuid().Should().Be(alamo.Id);
        primera.GetProperty("nit").GetString().Should().Be("900111222");
        primera.GetProperty("estado").GetString().Should().Be("ACTIVA");
        primera.GetProperty("usuarios_diagnostico").GetInt32().Should().Be(3);
        primera.GetProperty("administradores_diagnostico").GetInt32().Should().Be(1);
        todas.GetProperty("empresas")[1].GetProperty("usuarios_diagnostico").GetInt32().Should().Be(0, "sin conteo = 0");
        todas.GetProperty("empresas")[2].GetProperty("nit").ValueKind.Should().Be(JsonValueKind.Null);
        Nombres(porUsuarios).Should().Equal("Zeta Flotas", "Álamo Renting", "Beta Leasing");
        Nombres(buscada).Should().Equal("Álamo Renting");
        Nombres(porNit).Should().Equal("Beta Leasing");
        Nombres(sin).Should().Equal("Beta Leasing");
        con.GetProperty("total").GetInt32().Should().Be(2);
        Nombres(con).Should().Equal("Zeta Flotas");
        conteo.Llamadas.Should().NotBeEmpty().And.AllSatisfy(l =>
        {
            l.Actor.Should().Be(Sujeto(token));
            ((ContarPorEmpresaRequest)l.Peticion).Producto.Should().Be("diagnostico");
        });
    }

    // §3.3 — filtros inválidos de la tabla de compañías: 400.
    [Theory]
    [InlineData("usuarios=ALGUNOS", "FILTRO_INVALIDO")]
    [InlineData("orden=nit", "FILTRO_INVALIDO")]
    [InlineData("busqueda=__101__", "FILTRO_INVALIDO")]
    [InlineData("tamano=500", "PAGINACION_INVALIDA")]
    public async Task HU14587_tabla_de_companias_valida_filtros(string consulta, string codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            falso.Registrar(b);
            b.Services.AddScoped<IEmpresasPlataforma>(_ => new EmpresasFijas([]));
        });

        var respuesta = await api.Cliente(UsuariosDePrueba.SuperAdmin(api))
            .GetAsync($"{Empresas}?{consulta.Replace("__101__", new string('a', 101), StringComparison.Ordinal)}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(respuesta, ct)).Should().Be(codigo);
    }

    // §3.3 503 — sin Identidad configurada, Identidad caída o la plataforma caída: 503 con su código, nunca una lista vacía.
    [Fact]
    public async Task HU14587_tabla_de_companias_responde_503_si_falta_Identidad_o_la_plataforma()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using (var sinIdentidad = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar))
        {
            var r = await sinIdentidad.Cliente(UsuariosDePrueba.SuperAdmin(sinIdentidad)).GetAsync(Empresas, ct);
            r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await Code(r, ct)).Should().Be("IDENTIDAD_NO_CONFIGURADA");
        }

        await using (var identidadCaida = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            falso.Registrar(b);
            b.Services.AddScoped<IEmpresasPlataforma>(_ => new EmpresasFijas(null, new RpcException(new Status(StatusCode.Unavailable, "caída"))));
        }))
        {
            var r = await identidadCaida.Cliente(UsuariosDePrueba.SuperAdmin(identidadCaida)).GetAsync(Empresas, ct);
            r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await Code(r, ct)).Should().Be("IDENTIDAD_NO_DISPONIBLE");
            r.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30));
        }

        var conteoCaido = new UsuariosConteoFalso { Falla = new RpcException(new Status(StatusCode.Unavailable, "caída")) };
        await using var plataformaCaida = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            falso.Registrar(b, conteoCaido);
            b.Services.AddScoped<IEmpresasPlataforma>(_ => new EmpresasFijas([new EmpresaPlataforma(Guid.NewGuid(), "Una", null)]));
        });
        var caida = await plataformaCaida.Cliente(UsuariosDePrueba.SuperAdmin(plataformaCaida)).GetAsync(Empresas, ct);
        caida.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Code(caida, ct)).Should().Be("PLATAFORMA_NO_DISPONIBLE");
    }

    private static List<string?> Nombres(JsonElement lista) =>
        lista.GetProperty("empresas").EnumerateArray().Select(e => e.GetProperty("nombre").GetString()).ToList();

    private sealed class EmpresasFijas(IReadOnlyList<EmpresaPlataforma>? empresas, Exception? falla = null) : IEmpresasPlataforma
    {
        public Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct) =>
            falla is not null ? Task.FromException<IReadOnlyList<EmpresaPlataforma>?>(falla) : Task.FromResult(empresas);
    }
}
