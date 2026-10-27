using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using Flit.Identidad.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Configuracion;

/// <summary>
/// HU #13741, escenarios 15 y 16 — <c>GET /backoffice/empresas</c>: el editor de contrato del backoffice FLIT elige la
/// compañía de una lista real (id, nombre, NIT) que da Identidad por <c>EmpresasService.ListarEmpresasConProducto</c>
/// (producto <c>diagnostico</c>), ordenada por nombre. Misma autorización que el contrato (política backoffice +
/// <c>diagnostico.configuracion.contrato.editar</c>). Sin Identidad configurada o con Identidad caída: 503 con un
/// <c>code</c>, nunca una lista vacía. Sin Postgres: la ruta no lee la base. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EmpresasBackofficeTests
/// </code>
/// </example>
public sealed class EmpresasBackofficeTests
{
    private const string Url = Program.Prefijo + BackofficeEndpoints.Grupo + "/empresas";
    private const string Direccion = "http://127.0.0.1:1";

    private static readonly string[] CamposDto = ["id", "nombre", "nit"];

    // Escenario 15 — backoffice FLIT con el permiso del contrato: empresas de Identidad (producto diagnostico), ordenadas por nombre.
    [Fact]
    public async Task Backoffice_FLIT_recibe_empresas_de_Identidad_ordenadas_por_nombre()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new EmpresasFalso();
        var zeta = Guid.NewGuid();
        var alfa = Guid.NewGuid();
        var media = Guid.NewGuid();
        identidad.Empresas.AddRange(
        [
            new Empresa { Id = zeta.ToString(), Nombre = "Zeta Flotas Ficticias SAS", Nit = "900000003" },
            new Empresa { Id = "no-es-un-uuid", Nombre = "Registro roto", Nit = "900000009" },
            new Empresa { Id = alfa.ToString(), Nombre = "  Álamo Renting de Prueba  ", Nit = "900000001" },
            new Empresa { Id = media.ToString(), Nombre = "Medellín Leasing Inventado", Nit = "" },
        ]);
        await using var api = await ConIdentidadAsync(identidad);

        var respuesta = await api.Cliente(TokenContrato(api, "FLIT")).GetAsync(Url, ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        var lista = JsonDocument.Parse(texto).RootElement;
        lista.ValueKind.Should().Be(JsonValueKind.Array);
        lista.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Equal([alfa, media, zeta], "ordenadas por nombre; un id que no es uuid se descarta");
        lista[0].GetProperty("nombre").GetString().Should().Be("Álamo Renting de Prueba");
        lista[0].GetProperty("nit").GetString().Should().Be("900000001");
        lista[1].GetProperty("nit").ValueKind.Should().Be(JsonValueKind.Null, "un NIT vacío en Identidad sale null");
        lista[2].GetProperty("nombre").GetString().Should().Be("Zeta Flotas Ficticias SAS");
        lista.EnumerateArray().Should().OnlyContain(e => e.EnumerateObject().Select(p => p.Name).SequenceEqual(CamposDto));
        identidad.Productos.Should().Equal("diagnostico");
    }

    // Escenario 16 — compañía que no es FLIT (con los permisos): 403 y no se le pregunta a Identidad.
    [Fact]
    public async Task Compania_no_FLIT_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new EmpresasFalso();
        await using var api = await ConIdentidadAsync(identidad);

        foreach (var tipo in new[] { "RENTING", "CONCESIONARIO", null })
        {
            var respuesta = await api.Cliente(TokenContrato(api, tipo)).GetAsync(Url, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"tenant_type={tipo}");
        }

        identidad.Productos.Should().BeEmpty();
    }

    // Escenario 16 — compañía FLIT sin diagnostico.configuracion.contrato.editar (o un admin de compañía): 403.
    [Fact]
    public async Task Sin_permiso_contrato_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new EmpresasFalso();
        await using var api = await ConIdentidadAsync(identidad);
        var sinContrato = api.Token(permisos: [Permisos.BackofficeVer, Permisos.ConfiguracionVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        var adminDeCompania = api.Token(permisos: [Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar], roles: ["admin_diagnostico"]);

        foreach (var token in new[] { sinContrato, adminDeCompania })
            (await api.Cliente(token).GetAsync(Url, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        identidad.Productos.Should().BeEmpty();
    }

    // Escenario 16 — sin Diagnostico:Identidad:Address: 503 IDENTIDAD_NO_CONFIGURADA, nunca una lista vacía.
    [Fact]
    public async Task Sin_Identidad_configurada_responde_503_con_code()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var respuesta = await api.Cliente(TokenContrato(api, "FLIT")).GetAsync(Url, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await respuesta.Content.ReadAsStringAsync(ct));
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("IDENTIDAD_NO_CONFIGURADA");
    }

    // Escenario 16 — Identidad no responde: 503 IDENTIDAD_NO_DISPONIBLE con Retry-After, nunca una lista vacía.
    [Fact]
    public async Task Identidad_caida_responde_503_con_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new EmpresasFalso { FallaSiempre = StatusCode.Unavailable };
        await using var api = await ConIdentidadAsync(identidad);

        var respuesta = await api.Cliente(TokenContrato(api, "FLIT")).GetAsync(Url, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await respuesta.Content.ReadAsStringAsync(ct));
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("IDENTIDAD_NO_DISPONIBLE");
        respuesta.Headers.RetryAfter.Should().NotBeNull();
    }

    /// <summary>
    /// API sin base con <c>Diagnostico:Identidad:Address</c> configurada y el adaptador real sobre el doble del cliente gRPC
    /// de empresas. El doble se le pasa al adaptador (registro previo, que <c>TryAdd</c> respeta) porque
    /// <c>AddGrpcClient</c> registra el cliente tipado después y pisaría un registro previo del cliente.
    /// </summary>
    private static Task<ApiDePrueba> ConIdentidadAsync(EmpresasFalso identidad) =>
        ApiDePrueba.SinBaseAsync(configurar: (WebApplicationBuilder b) =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [GuardSuscripcionExtensions.IdentidadAddressKey] = Direccion });
            b.Services.AddScoped<IEmpresasPlataforma>(_ => new EmpresasPlataformaIdentidad(identidad));
        });

    private static string TokenContrato(ApiDePrueba api, string? tipoEmpresa) =>
        api.Token(permisos: [Permisos.BackofficeVer, Permisos.ConfiguracionContratoEditar], roles: ["backoffice_diagnostico"], tipoEmpresa: tipoEmpresa);

    /// <summary>Doble de <c>flit.identidad.v1.EmpresasService</c>: responde <see cref="Empresas"/> y anota el producto pedido.</summary>
    private sealed class EmpresasFalso : EmpresasService.EmpresasServiceClient
    {
        public List<Empresa> Empresas { get; } = [];

        public List<string> Productos { get; } = [];

        public StatusCode? FallaSiempre { get; init; }

        public override AsyncUnaryCall<ListarEmpresasConProductoResponse> ListarEmpresasConProductoAsync(ListarEmpresasConProductoRequest request, CallOptions options)
        {
            lock (Productos)
                Productos.Add(request.Producto);
            var r = new ListarEmpresasConProductoResponse();
            r.Empresas.AddRange(Empresas);
            var respuesta = FallaSiempre is { } codigo
                ? Task.FromException<ListarEmpresasConProductoResponse>(new RpcException(new Status(codigo, "falla de prueba")))
                : Task.FromResult(r);
            return new AsyncUnaryCall<ListarEmpresasConProductoResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }
    }
}
