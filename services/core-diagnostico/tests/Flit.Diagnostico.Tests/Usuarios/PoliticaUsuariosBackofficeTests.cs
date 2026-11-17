using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;
using static Flit.Diagnostico.Tests.Usuarios.UsuariosEndpointsTests;

namespace Flit.Diagnostico.Tests.Usuarios;

/// <summary>
/// HU #14587 (contrato-api §3.2, SP-13, SP-18) — <c>PoliticaUsuariosBackoffice</c>: el backoffice de usuarios
/// (<c>/backoffice/empresas/{empresa}/usuarios*</c> y <c>/backoffice/usuarios/empresas</c>) es <b>solo del SuperAdmin</b>. Un usuario de
/// la compañía FLIT con <c>diagnostico.backoffice.ver</c> (que sí pasa el resto del backoffice) y un admin_diagnostico con
/// <c>diagnostico.usuarios.gestionar</c> reciben 403; un token de servicio también. «Pasa» = cualquier respuesta que no sea 401/403.
/// Sin base: las lecturas con empresa explícita fallan al auditar, y eso ya es «pasó la política». Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~PoliticaUsuariosBackofficeTests"
/// </code>
/// </example>
public sealed class PoliticaUsuariosBackofficeTests
{
    private static readonly string[] PermisosOperadorFlit = ["diagnostico.backoffice.ver", "diagnostico.auditoria.ver", UsuariosDePrueba.Gestionar];

    public static TheoryData<string, bool> Actores => new()
    {
        { "superadmin_flit", true },
        { "superadmin_cliente", true },
        { "superadmin_sin_tenant_type", true },
        { "flit_con_backoffice_y_gestionar", false },
        { "admin_diagnostico_con_gestionar", false },
        { "usuario_diagnostico", false },
        { "token_de_servicio", false },
    };

    // §3.2 — cada actor contra cada ruta del backoffice de usuarios.
    [Theory]
    [MemberData(nameof(Actores))]
    public async Task HU14587_backoffice_de_usuarios_solo_SuperAdmin(string actor, bool pasa)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = new UsuariosProductoFalso();
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: falso.Registrar);
        using var cliente = api.Cliente(Token(api, actor));
        var empresa = Guid.NewGuid();
        var rutas = Rutas(Guid.NewGuid(), $"{Program.Prefijo}/backoffice/empresas/{empresa}/usuarios")
            .Append((HttpMethod.Get, $"{Program.Prefijo}/backoffice/usuarios/empresas", (string?)null));
        var fallos = new List<string>();

        foreach (var (metodo, ruta, json) in rutas)
        {
            using var pedido = Pedido(metodo, ruta, json);
            var estado = (await cliente.SendAsync(pedido, ct)).StatusCode;
            var denegada = estado is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
            if (denegada == pasa)
                fallos.Add($"{metodo} {ruta}: {(int)estado}");
        }

        fallos.Should().BeEmpty($"{actor}: debe {(pasa ? "pasar" : "recibir 403")}");
        if (!pasa)
            falso.Llamadas.Should().BeEmpty("quien no pasa la política no llega a la plataforma");
    }

    private static string Token(ApiDePrueba api, string actor) => actor switch
    {
        "superadmin_flit" => UsuariosDePrueba.SuperAdmin(api, tipoEmpresa: "FLIT"),
        "superadmin_cliente" => UsuariosDePrueba.SuperAdmin(api, tipoEmpresa: "CLIENTE"),
        "superadmin_sin_tenant_type" => UsuariosDePrueba.SuperAdmin(api, tipoEmpresa: null),
        "flit_con_backoffice_y_gestionar" => api.Token(roles: ["operador"], permisos: PermisosOperadorFlit, tipoEmpresa: "FLIT"),
        "admin_diagnostico_con_gestionar" => UsuariosDePrueba.Admin(api, Guid.NewGuid()),
        "usuario_diagnostico" => UsuariosDePrueba.Usuario(api, Guid.NewGuid()),
        "token_de_servicio" => UsuariosDePrueba.SuperAdmin(api, sujeto: "svc-diagnostico"),
        _ => throw new ArgumentOutOfRangeException(nameof(actor), actor, null),
    };
}
