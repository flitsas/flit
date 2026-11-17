using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Grpc.Core;
using Xunit;
using static Flit.Diagnostico.Tests.Usuarios.UsuariosEndpointsTests;

namespace Flit.Diagnostico.Tests.Usuarios;

/// <summary>
/// HU #14587 (contrato-api §3.4 «Mapeo de errores gRPC → REST», SP-13, SP-15) — cada rechazo del gRPC de usuarios de core-api llega al
/// front con el estado y el <c>code</c> del contrato. Se prueba por HTTP con el doble del cliente gRPC lanzando el
/// <see cref="RpcException"/> con <c>google.rpc.ErrorInfo</c> (dominio flitsas.com) como lo hace core-api. Un <c>NOT_FOUND</c> da el mismo
/// cuerpo con o sin detalle (SP-13: no revela si el usuario existe). Sin base; datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~TraduccionErroresUsuariosTests"
/// </code>
/// </example>
public sealed class TraduccionErroresUsuariosTests
{
    private const string Base = Program.Prefijo + "/usuarios";

    // §3.4 — los códigos de negocio de core-api, en las rutas por {usuario} (PUT rol).
    [Theory]
    [InlineData("ROL_NO_ASIGNABLE", StatusCode.PermissionDenied, 403, null)]
    [InlineData("OBJETIVO_PROTEGIDO", StatusCode.PermissionDenied, 403, null)]
    [InlineData("USUARIO_NO_ENCONTRADO", StatusCode.NotFound, 404, null)]
    [InlineData("ULTIMO_ADMINISTRADOR", StatusCode.FailedPrecondition, 409, null)]
    [InlineData("USUARIO_SUSPENDIDO", StatusCode.FailedPrecondition, 409, null)]
    [InlineData("INVITACION_PENDIENTE", StatusCode.FailedPrecondition, 409, null)]
    [InlineData("ROL_YA_VIGENTE", StatusCode.FailedPrecondition, 409, null)]
    [InlineData("ROL_NO_PERMITIDO", StatusCode.InvalidArgument, 422, "rol")]
    [InlineData("ACCION_SOBRE_SI_MISMO", StatusCode.InvalidArgument, 400, null)]
    public async Task HU14587_codigo_de_negocio_de_core_api_se_traduce_al_estado_del_contrato(string codigo, StatusCode grpc, int http, string? campo)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso { Falla = UsuariosDePrueba.Error(codigo, grpc) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var respuesta = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()))
            .PutAsync($"{Base}/{Guid.NewGuid()}/rol", Json("""{ "rol": "usuario_diagnostico" }"""), ct);

        ((int)respuesta.StatusCode).Should().Be(http, codigo);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("code").GetString().Should().Be(codigo);
        json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        if (campo is null)
            json.TryGetProperty("campo", out _).Should().BeFalse();
        else
            json.GetProperty("campo").GetString().Should().Be(campo);
    }

    // §3.4 — los INVALID_ARGUMENT de forma de invitar y listar llegan como 400 con su campo cuando lo tienen.
    [Theory]
    [InlineData("CORREO_INVALIDO", "correo")]
    [InlineData("NOMBRE_REQUERIDO", "nombre")]
    public async Task HU14587_invitar_traduce_los_rechazos_de_forma_de_core_api(string codigo, string campo)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso { Falla = UsuariosDePrueba.Error(codigo, StatusCode.InvalidArgument) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var respuesta = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()))
            .PostAsync($"{Base}/invitaciones", Json("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "usuario_diagnostico" }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("code").GetString().Should().Be(codigo);
        json.GetProperty("campo").GetString().Should().Be(campo);
    }

    [Theory]
    [InlineData("FILTRO_INVALIDO")]
    [InlineData("PAGINACION_INVALIDA")]
    public async Task HU14587_listar_traduce_FILTRO_y_PAGINACION_de_core_api(string codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso { Falla = UsuariosDePrueba.Error(codigo, StatusCode.InvalidArgument) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var respuesta = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid())).GetAsync(Base, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(respuesta, ct)).Should().Be(codigo);
    }

    // SP-13 — NOT_FOUND con o sin ErrorInfo: el mismo 404 USUARIO_NO_ENCONTRADO, byte a byte (no revela si existe en otra compañía).
    [Fact]
    public async Task HU14587_SP13_NOT_FOUND_con_y_sin_detalle_da_el_mismo_cuerpo()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso { Falla = UsuariosDePrueba.Error("USUARIO_NO_ENCONTRADO", StatusCode.NotFound) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        using var cliente = api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()));

        var conDetalle = await cliente.PostAsync($"{Base}/{Guid.NewGuid()}/suspension", null, ct);
        falso.Falla = new RpcException(new Status(StatusCode.NotFound, "otro mensaje interno de core-api"));
        var sinDetalle = await cliente.PostAsync($"{Base}/{Guid.NewGuid()}/suspension", null, ct);

        conDetalle.StatusCode.Should().Be(HttpStatusCode.NotFound);
        sinDetalle.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await sinDetalle.Content.ReadAsStringAsync(ct)).Should().Be(await conDetalle.Content.ReadAsStringAsync(ct));
        (await Code(sinDetalle, ct)).Should().Be("USUARIO_NO_ENCONTRADO");
    }

    // D-5 (CR-2) — core-api responde NOT_FOUND por la empresa (SuperAdmin sobre una empresa sin Diagnóstico o inexistente): 404
    // EMPRESA_NO_ENCONTRADA, el mismo código que da SoloEmpresaHabilitada. En listar e invitar (sin {usuario}) un NOT_FOUND sin detalle
    // también es de la empresa; en las rutas por {usuario}, EMPRESA_NO_ENCONTRADA con detalle sigue siendo de la empresa.
    [Fact]
    public async Task HU14587_D5_NOT_FOUND_de_empresa_es_404_EMPRESA_NO_ENCONTRADA()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso { Falla = UsuariosDePrueba.Error("EMPRESA_NO_ENCONTRADA", StatusCode.NotFound) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        using var cliente = api.Cliente(UsuariosDePrueba.SuperAdmin(api));
        var baseBackoffice = $"{Program.Prefijo}/backoffice/empresas/{Guid.NewGuid()}/usuarios";

        var porUsuario = await cliente.PostAsync($"{baseBackoffice}/{Guid.NewGuid()}/suspension", null, ct);
        var invitar = await cliente.PostAsync($"{baseBackoffice}/invitaciones", Json("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "usuario_diagnostico" }"""), ct);
        falso.Falla = new RpcException(new Status(StatusCode.NotFound, "sin detalle"));
        var invitarSinDetalle = await cliente.PostAsync($"{baseBackoffice}/invitaciones", Json("""{ "correo": "a@b.test", "nombre": "Persona", "rol": "usuario_diagnostico" }"""), ct);

        foreach (var respuesta in new[] { porUsuario, invitar, invitarSinDetalle })
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Code(respuesta, ct)).Should().Be("EMPRESA_NO_ENCONTRADA");
        }
    }

    // §3.3 «403 sin permiso (sin cuerpo)» — core-api rechaza al actor (PERMISSION_DENIED sin ErrorInfo): 403 sin cuerpo y sin el mensaje interno.
    [Fact]
    public async Task HU14587_PERMISSION_DENIED_sin_detalle_es_403_sin_cuerpo()
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso { Falla = new RpcException(new Status(StatusCode.PermissionDenied, "actor sin admin_diagnostico vigente")) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);

        var respuesta = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid())).GetAsync(Base, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
    }

    // §3.4 — UNAVAILABLE y DEADLINE_EXCEEDED (y la red) son 503 PLATAFORMA_NO_DISPONIBLE con Retry-After: 30.
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Internal)]
    public async Task HU14587_plataforma_caida_es_503_PLATAFORMA_NO_DISPONIBLE_con_Retry_After(StatusCode grpc)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso { Falla = new RpcException(new Status(grpc, "caída de prueba")) };
        var logs = new CapturaDeLogs();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            falso.Registrar(b);
            logs.Registrar(b);
        });

        var respuesta = await api.Cliente(UsuariosDePrueba.Admin(api, Guid.NewGuid()))
            .PostAsync($"{Base}/invitaciones", Json($$"""{ "correo": "{{UsuariosDePrueba.Correo}}", "nombre": "Persona", "rol": "usuario_diagnostico" }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Code(respuesta, ct)).Should().Be("PLATAFORMA_NO_DISPONIBLE");
        respuesta.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30));
        logs.Lineas.Should().NotContain(l => l.Contains(UsuariosDePrueba.Correo, StringComparison.OrdinalIgnoreCase), "SP-16: tampoco en el log del fallo");
    }
}
