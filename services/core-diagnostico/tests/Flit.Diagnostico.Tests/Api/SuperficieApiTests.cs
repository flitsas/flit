using System.Net;
using Flit.Diagnostico.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13724 (F00.1-H3), escenario 5 (R-4) — prueba de superficie: Diagnóstico no publica ninguna ruta para crear o
/// modificar compañías, usuarios, roles ni suscripciones (eso es de la plataforma). Recorre todos los endpoints del
/// servicio real y, además, llama directo a las rutas típicas con un token con todos los permisos: 404.
/// Una ruta de escritura cuyo último segmento literal nombre uno de esos recursos hace fallar la prueba
/// (<c>/backoffice/empresas/{empresa}/configuracion/contrato</c> no: su recurso es el contrato).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SuperficieApiTests
/// </code>
/// </example>
public sealed class SuperficieApiTests
{
    private static readonly HashSet<string> RecursosDePlataforma = new(StringComparer.OrdinalIgnoreCase)
    {
        "empresas", "empresa", "companias", "compania", "tenants", "usuarios", "usuario", "users", "roles", "rol",
        "permisos", "suscripciones", "suscripcion", "subscriptions", "productos", "products",
    };

    private static readonly string[] MetodosDeEscritura = [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete];

    // Escenario 5 — no existen rutas de escritura de compañías, usuarios, roles ni suscripciones.
    [Fact]
    public async Task No_existen_rutas_de_escritura_de_companias_usuarios_roles_ni_suscripciones()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();

        var rutas = api.App.Services.GetServices<EndpointDataSource>().SelectMany(f => f.Endpoints).OfType<RouteEndpoint>()
            .Select(e => (Metodos: e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [], Patron: e.RoutePattern.RawText ?? string.Empty))
            .ToList();
        var deEscritura = rutas
            .Where(r => r.Metodos.Intersect(MetodosDeEscritura, StringComparer.OrdinalIgnoreCase).Any())
            .Where(r => RecursosDePlataforma.Contains(UltimoLiteral(r.Patron)))
            .Select(r => $"{string.Join(',', r.Metodos)} {r.Patron}")
            .ToList();

        rutas.Should().Contain(r => r.Patron.TrimStart('/') == Program.Prefijo.TrimStart('/') + "/sesion", "la superficie que se recorre es la publicada");
        deEscritura.Should().BeEmpty("Diagnóstico no administra compañías, usuarios, roles ni suscripciones (R-4)");

        var todo = TodosLosPermisos();
        using var cliente = api.Cliente(api.Token(permisos: todo, roles: ["admin_diagnostico", "backoffice_diagnostico"], tipoEmpresa: "FLIT"));
        var id = Guid.NewGuid();
        (HttpMethod Metodo, string Ruta)[] intentos =
        [
            (HttpMethod.Post, "/empresas"), (HttpMethod.Put, $"/empresas/{id}"), (HttpMethod.Post, "/companias"),
            (HttpMethod.Post, "/usuarios"), (HttpMethod.Put, $"/usuarios/{id}"), (HttpMethod.Delete, $"/usuarios/{id}"),
            (HttpMethod.Post, "/roles"), (HttpMethod.Put, $"/roles/{id}"), (HttpMethod.Post, $"/usuarios/{id}/roles"),
            (HttpMethod.Post, "/suscripciones"), (HttpMethod.Put, $"/suscripciones/{id}"), (HttpMethod.Patch, $"/empresas/{id}/suscripcion"),
        ];
        foreach (var (metodo, ruta) in intentos)
        {
            using var peticion = new HttpRequestMessage(metodo, Program.Prefijo + ruta) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
            var respuesta = await cliente.SendAsync(peticion, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{metodo} {ruta} no existe en Diagnóstico");
        }
    }

    private static string UltimoLiteral(string patron) =>
        patron.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(s => !s.StartsWith('{')) ?? string.Empty;

    private static string[] TodosLosPermisos() =>
    [
        "diagnostico.diagnosticos.crear", "diagnostico.diagnosticos.ver", "diagnostico.backoffice.ver",
        "diagnostico.reportes.descargar", "diagnostico.derivacion.crear", "diagnostico.configuracion.ver",
        "diagnostico.configuracion.editar", "diagnostico.configuracion.contrato.editar", "diagnostico.consultas.reprocesar",
        "diagnostico.consultas.llenar", "diagnostico.dictamen.override", "diagnostico.auditoria.ver", "diagnostico.volumetria.ver",
    ];
}
