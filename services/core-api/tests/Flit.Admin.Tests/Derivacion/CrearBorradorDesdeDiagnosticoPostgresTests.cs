using System.Text.Json;
using Flit.Admin.Tests.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Cliente = Flit.Tramites.Grpc.V1.DerivacionTramitesService.DerivacionTramitesServiceClient;

namespace Flit.Admin.Tests.Derivacion;

/// <summary>
/// ADR-0084 / diseño §9 (core-api) — el rpc <c>CrearBorradorDesdeDiagnostico</c> de punta a punta: servidor OIDC real de las
/// pruebas, token de <c>svc-diagnostico</c> con <c>platform.tramites.derivacion</c>, casos de uso reales y PostgreSQL con
/// las migraciones aplicadas (mismo arnés que <c>PersonaNaturalRadicacionPostgresTests</c>). Cada prueba siembra su propia
/// empresa, organismo, tipo y usuario con datos inventados.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: ct)</c>.</remarks>
public sealed class CrearBorradorDesdeDiagnosticoPostgresTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scope = "platform.tramites.derivacion";
    private const string Diagnostico = "svc-diagnostico";

    private WebApplicationFactory<Program> Host(bool habilitada = true, bool exigirPermisos = true) =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("Tramites:Derivacion:Habilitada", habilitada ? "true" : "false");
            b.UseSetting("Security:ModulePermissionEnforcement:Enabled", exigirPermisos ? "true" : "false");
            foreach (var cliente in new[] { Diagnostico, "svc-otro-prueba" })
            {
                b.UseSetting($"Suite:Oidc:ServiceClients:{cliente}:Secret", OidcServerTests.ServiceSecret);
                b.UseSetting($"Suite:Oidc:ServiceClients:{cliente}:Scopes:0", Scope);
            }
        });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreaElBorrador_ConOrigenReferenciaCanalAutorYOrganismo()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);

        respuesta.ResultadoCase.Should().Be(CrearBorradorDesdeDiagnosticoResponse.ResultadoOneofCase.Creado, respuesta.Rechazo?.Codigo);
        respuesta.Creado.YaExistia.Should().BeFalse();
        var tramite = (await e.TramitesAsync(Ct)).Should().ContainSingle().Which;
        tramite.Id.ToString().Should().Be(respuesta.Creado.ProcedureInstanceId);
        tramite.Origin.Should().Be("diagnostico");
        tramite.Channel.Should().Be("tramites");
        tramite.CreatedByUserId.Should().Be(e.Usuario);
        tramite.TransitOfficeId.Should().Be(e.Organismo);
        tramite.Status.Should().Be("borrador");
        var campos = await e.CamposAsync(tramite.Id, Ct);
        campos.Should().Contain("plate", "ABC123").And.Contain("owner_document_number", "1000000001")
            .And.Contain("transit_office_id", e.Organismo.ToString()).And.Contain("vehicle_brand", "MARCA DE PRUEBA");
    }

    [Fact]
    public async Task DosLlamadasConLaMismaReferencia_UnaSolaCreacion()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var primera = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);
        var segunda = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);

        segunda.Creado.YaExistia.Should().BeTrue();
        segunda.Creado.ProcedureInstanceId.Should().Be(primera.Creado.ProcedureInstanceId);
        (await e.TramitesAsync(Ct)).Should().HaveCount(1);
    }

    [Fact]
    public async Task DosLlamadasConcurrentes_NuncaDuplican()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct).ResponseAsync));

        respuestas.Select(r => r.Creado.ProcedureInstanceId).Distinct().Should().ContainSingle();
        (await e.TramitesAsync(Ct)).Should().HaveCount(1);
    }

    [Fact]
    public async Task LaReferenciaYaUsadaPorOtroOrigen_SeRechaza()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        await e.SembrarTramiteDeOtroOrigenAsync(Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);

        respuesta.Rechazo.Codigo.Should().Be("REFERENCIA_DE_OTRO_ORIGEN");
        (await e.TramitesAsync(Ct)).Should().ContainSingle().Which.Origin.Should().Be("ict");
    }

    [Fact]
    public async Task UnUsuarioInactivo_SeRechazaConUsuarioSinPermiso()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct, estadoUsuario: "inactive");
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);

        respuesta.Rechazo.Codigo.Should().Be("USUARIO_SIN_PERMISO");
        (await e.TramitesAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnUsuarioDeOtraCompania_SeRechazaConUsuarioSinPermiso()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct, rolEnOtraEmpresa: true);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);

        respuesta.Rechazo.Codigo.Should().Be("USUARIO_SIN_PERMISO");
        (await e.TramitesAsync(Ct)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("tramites.create")]
    [InlineData("traspasos.create")]
    public async Task UnUsuarioSinUnoDeLosDosPermisos_SeRechazaConUsuarioSinPermiso(string permisoQueFalta)
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct, sinPermiso: permisoQueFalta);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);

        respuesta.Rechazo.Codigo.Should().Be("USUARIO_SIN_PERMISO");
    }

    [Fact]
    public async Task UnOrganismoFueraDeLaListaEfectiva_SeRechazaConOtNoHabilitado()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(organismoNombre: e.NombreOrganismoSinGrant, organismoCodigo: e.CodigoOrganismoSinGrant), cancellationToken: Ct);

        respuesta.Rechazo.Codigo.Should().Be("OT_NO_HABILITADO");
        (await e.TramitesAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task SinNombreQueCase_ElOrganismoSeResuelvePorCodigo()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(organismoNombre: "NOMBRE QUE NO CASA", organismoCodigo: e.CodigoOrganismo), cancellationToken: Ct);

        respuesta.Creado.Should().NotBeNull(respuesta.Rechazo?.Codigo);
        (await e.TramitesAsync(Ct)).Single().TransitOfficeId.Should().Be(e.Organismo);
    }

    [Fact]
    public async Task UnTipoInexistente_SeRechazaConTipoNoDisponible()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(tipo: "NO_EXISTE_" + Guid.NewGuid().ToString("N")[..6]), cancellationToken: Ct);

        respuesta.Rechazo.Codigo.Should().Be("TIPO_NO_DISPONIBLE");
    }

    [Fact]
    public async Task ObtenerTramitesPorReferencia_DevuelveElCreado()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);
        var creado = await cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct);

        var request = new ObtenerTramitesPorReferenciaRequest();
        request.ExternalRefs.Add(e.Vehiculo.ToString());
        request.ExternalRefs.Add(Guid.NewGuid().ToString());
        var respuesta = await cliente.ObtenerTramitesPorReferenciaAsync(request, cancellationToken: Ct);

        respuesta.Tramites.Should().ContainSingle().Which.ProcedureInstanceId.Should().Be(creado.Creado.ProcedureInstanceId);
    }

    [Fact]
    public async Task OtroClienteDeServicio_ConElMismoScope_NoEntra()
    {
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, "svc-otro-prueba", e.Empresa);

        var llamada = () => cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct).ResponseAsync;

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf(StatusCode.PermissionDenied, StatusCode.Unauthenticated);
        (await e.TramitesAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApagadaPorConfiguracion_ElServicioNoExiste()
    {
        using var host = Host(habilitada: false);
        await using var e = await Escenario.CrearAsync(host, Ct);
        var cliente = await ClienteAsync(host, Diagnostico, e.Empresa);

        var llamada = () => cliente.CrearBorradorDesdeDiagnosticoAsync(e.Solicitud(), cancellationToken: Ct).ResponseAsync;

        // Sin mapear, la ruta cae en la autorización por defecto del host: según el orden de los middlewares llega como
        // UNIMPLEMENTED o UNAUTHENTICATED (mismo criterio que la fachada de persona natural). Lo que importa es que nadie la
        // atiende ni con el token correcto, y que no se crea nada.
        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf(
            [StatusCode.Unimplemented, StatusCode.Unauthenticated, StatusCode.PermissionDenied],
            "el servicio ni siquiera se mapea: no lo atiende nadie, ni con el token correcto");
        (await e.TramitesAsync(Ct)).Should().BeEmpty();
    }

    // ── Infraestructura ───────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<Cliente> ClienteAsync(WebApplicationFactory<Program> host, string clientId, Guid empresa)
    {
        var token = await TokenAsync(host, clientId, Ct);
        var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        return new Cliente(canal.CreateCallInvoker().Intercept(metadata =>
        {
            metadata.Add("authorization", $"Bearer {token}");
            metadata.Add("x-flit-tenant-id", empresa.ToString());
            return metadata;
        }));
    }

    private static async Task<string> TokenAsync(WebApplicationFactory<Program> host, string clientId, CancellationToken ct)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = OidcServerTests.ServiceSecret,
            ["scope"] = Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        private readonly List<Guid> _rolesCreados = [];

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            Db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public FlitDbContext Db { get; }
        public Guid Empresa { get; } = Guid.NewGuid();
        public Guid OtraEmpresa { get; } = Guid.NewGuid();
        public Guid Usuario { get; } = Guid.NewGuid();
        public Guid Vehiculo { get; } = Guid.CreateVersion7();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OrganismoSinGrant { get; } = Guid.NewGuid();
        public Guid Tipo { get; } = Guid.NewGuid();
        public string CodigoTipo => "DG_P4_" + _sufijo;
        public string CodigoOrganismo => $"DG{_sufijo}A";
        public string CodigoOrganismoSinGrant => $"DG{_sufijo}B";
        public string NombreOrganismo => "Organismo de prueba " + CodigoOrganismo;
        public string NombreOrganismoSinGrant => "Organismo de prueba " + CodigoOrganismoSinGrant;

        public static async Task<Escenario> CrearAsync(
            WebApplicationFactory<Program> host, CancellationToken ct, string estadoUsuario = "active", bool rolEnOtraEmpresa = false, string? sinPermiso = null)
        {
            var e = new Escenario(host.Services.CreateScope());
            var ahora = DateTimeOffset.UtcNow;
            e.Db.Tenants.Add(new Tenant { Id = e.Empresa, Code = $"IT-DGP4-{e._sufijo}", LegalName = "Empresa de prueba derivación", TaxId = TestNit.Unique(), TenantType = "RENTING", IsActive = true, CreatedAt = ahora });
            e.Db.Tenants.Add(new Tenant { Id = e.OtraEmpresa, Code = $"IT-DGP4O-{e._sufijo}", LegalName = "Otra empresa de prueba", TaxId = TestNit.Unique(), TenantType = "RENTING", IsActive = true, CreatedAt = ahora });
            e.Db.Users.Add(new User { Id = e.Usuario, Email = $"dg-p4-{e.Usuario:N}@flit.local", DisplayName = "Usuario de prueba derivación", Status = estadoUsuario, HomeTenantId = e.Empresa, CreatedAt = ahora });
            await e.Db.SaveChangesAsync(ct);

            foreach (var (id, codigo) in new[] { (e.Organismo, e.CodigoOrganismo), (e.OrganismoSinGrant, e.CodigoOrganismoSinGrant) })
            {
                await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {codigo}, {"Organismo de prueba " + codigo}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }

            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.tenant_transit_office_grants (tenant_id, transit_office_id, is_enabled, source)
                VALUES ({e.Empresa}, {e.Organismo}, true, 'CLIENT')
                """, ct);
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({e.Tipo}, {e.CodigoTipo}, {"Tipo de prueba " + e.CodigoTipo}, 'TRASPASO', 1, {"{}"}::jsonb, 'published', now(), true, true, now())
                """, ct);

            // Rol propio con los permisos de creación (menos el que la prueba quiera quitar), asignado en la empresa del lote o en otra.
            var rol = Guid.NewGuid();
            e._rolesCreados.Add(rol);
            e.Db.Roles.Add(new Role { Id = rol, Code = $"DgP4-{e._sufijo}", Name = "Rol de prueba derivación", TargetEntityType = "COMPANY", ProductCode = "tramites", IsActive = true, CreatedAt = ahora });
            await e.Db.SaveChangesAsync(ct);
            foreach (var slug in new[] { "tramites.create", "traspasos.create" }.Where(s => s != sinPermiso))
            {
                var permiso = await e.Db.RbacActions.AsNoTracking().Where(a => a.Slug == slug && a.IsActive).Select(a => a.Id).FirstAsync(ct);
                e.Db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), RoleId = rol, PermissionId = permiso, CreatedAt = ahora });
            }

            e.Db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.CreateVersion7(), TenantId = rolEnOtraEmpresa ? e.OtraEmpresa : e.Empresa, UserId = e.Usuario, RoleId = rol, AssignedAt = ahora, CreatedAt = ahora,
            });
            await e.Db.SaveChangesAsync(ct);
            e.Db.ChangeTracker.Clear();
            return e;
        }

        public CrearBorradorDesdeDiagnosticoRequest Solicitud(string? tipo = null, string? organismoNombre = null, string? organismoCodigo = null) => new()
        {
            ExternalRef = Vehiculo.ToString(),
            UsuarioId = Usuario.ToString(),
            TipoTramiteCodigo = tipo ?? CodigoTipo,
            Placa = "ABC123",
            OrganismoNombreRunt = organismoNombre ?? NombreOrganismo,
            OrganismoCodigo = organismoCodigo ?? string.Empty,
            Propietario = new DocumentoPersona { TipoDocumento = "CC", NumeroDocumento = "1000000001" },
            Vehiculo = new VehiculoBorrador { Marca = "MARCA DE PRUEBA", Linea = "LINEA X", Modelo = "2020", Color = "BLANCO" },
            DiagnosticoId = Guid.NewGuid().ToString(),
            IntentoId = Guid.NewGuid().ToString(),
        };

        public async Task SembrarTramiteDeOtroOrigenAsync(CancellationToken ct)
        {
            Db.Add(new ProcedureInstance
            {
                Id = Guid.NewGuid(), TenantId = Empresa, ProcedureTypeId = Tipo, CreatedByUserId = Usuario, Status = "borrador",
                ExternalRef = Vehiculo.ToString(), Origin = "ict",
            });
            await Db.SaveChangesAsync(ct);
            Db.ChangeTracker.Clear();
        }

        public async Task<List<ProcedureInstance>> TramitesAsync(CancellationToken ct)
        {
            var externa = Vehiculo.ToString();
            Db.ChangeTracker.Clear();
            return await Db.Set<ProcedureInstance>().AsNoTracking().Where(p => p.TenantId == Empresa && p.ExternalRef == externa).ToListAsync(ct);
        }

        public async Task<Dictionary<string, string?>> CamposAsync(Guid tramite, CancellationToken ct)
        {
            Db.ChangeTracker.Clear();
            return await Db.Set<ProcedureInstanceFieldValue>().IgnoreQueryFilters().AsNoTracking()
                .Where(f => f.ProcedureInstanceId == tramite)
                .ToDictionaryAsync(f => f.FieldKey, f => f.ValueText, ct);
        }

        public ValueTask DisposeAsync()
        {
            // Los datos inventados quedan en la base de pruebas con sufijo único (como las demás pruebas Postgres del proyecto):
            // procedure_instance_field_values es inmutable por trigger y no se puede borrar en cascada.
            _scope.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
