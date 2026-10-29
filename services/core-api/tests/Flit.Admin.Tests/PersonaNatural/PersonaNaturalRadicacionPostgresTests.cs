using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Admin.Tests.Identity;
using Flit.Api.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14050 y #14051 contra Postgres real (con las migraciones aplicadas; la empresa «Persona Natural» la siembra la propia
/// prueba): creación del trámite del portal con los casos de uso de FLIT 2 y un cliente del portal falso. Cubre el rechazo de
/// negocio que no deja nada (savepoint), la idempotencia por <c>external_ref</c>, las validaciones de identidad ligadas al
/// trámite (sin buscar por documento), el espejo de estado y la subsanación.
/// </summary>
public sealed class PersonaNaturalRadicacionPostgresTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(_ => { }).ConEmpresaPersonaNatural();

    [Fact]
    public async Task UnTipoQueNoExiste_SeRechazaConTipoNoDisponible_SinLeerDatosPersonales()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var portal = new PortalFalso(e.Solicitud());

        var resultado = await e.ProcesarAsync(portal, e.Evento(tipo: "NO_EXISTE_ESTE_TIPO"), ct);

        resultado.Creada.Should().BeFalse();
        resultado.Codigo.Should().Be("tipo_no_disponible");
        resultado.Motivo.Should().Contain("tipo de trámite");
        portal.Lecturas.Should().Be(0, "los datos personales solo se piden cuando el tipo y el organismo son válidos");
        (await e.TramitesAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnOrganismoSinHabilitar_SeRechazaConOrganismoNoHabilitado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);

        var resultado = await e.ProcesarAsync(new PortalFalso(e.Solicitud()), e.Evento(organismo: e.OrganismoSinGrant), ct);

        resultado.Codigo.Should().Be("organismo_no_habilitado");
        (await e.TramitesAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnaParteSinIdentidadAprobada_SeRechazaAntesDeCrearNada_CuandoSeExige()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("PersonaNatural:Radicacion:ExigirIdentidadAlCrear", "true")).ConEmpresaPersonaNatural();
        await using var e = await Escenario.CrearAsync(host, ct);
        var solicitud = e.Solicitud(partes: [e.Parte("comprador", "1000000001", estado: "pendiente")]);

        var resultado = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        resultado.Codigo.Should().Be("identidad_no_validada");
        (await e.TramitesAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnaSolicitudQueYaNoSePuedeLeer_NoTieneEfecto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);

        var resultado = await e.ProcesarAsync(new PortalFalso(null, LecturaSolicitudPortal.NoRadicable), e.Evento(), ct);

        resultado.Ignorada.Should().BeTrue();
        (await e.TramitesAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnRechazoDespuesDeCrearElBorrador_LoAnulaConSuCodigo_YUnReintentoDevuelveElMismoRechazo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        // Documento con un tipo que FLIT 2 no acepta: el borrador ya se creó cuando PutActors lo rechaza.
        var solicitud = e.Solicitud(partes: [e.Parte("comprador", "1000000002", tipoDocumento: "XX"), e.Parte("vendedor", "1000000003")]);
        await e.SembrarValidacionesAsync(solicitud, ct);

        var portal = new PortalFalso(solicitud);
        var resultado = await e.ProcesarAsync(portal, e.Evento(), ct);

        resultado.Creada.Should().BeFalse();
        resultado.Codigo.Should().Be("invalid_document_type");
        resultado.Motivo.Should().NotContain("invalid_document_type");
        // El borrador no queda huérfano: anulado, con el código en su historial, y nada sin radicar en ninguna bandeja.
        var tramite = (await e.TramitesAsync(ct)).Should().ContainSingle().Which;
        tramite.Status.Should().Be("anulado");
        tramite.Origin.Should().Be("persona_natural");

        // Un reintento del mismo evento devuelve EL MISMO rechazo sin leer datos personales ni crear otro trámite.
        var reintento = await e.ProcesarAsync(portal, e.Evento(), ct);
        reintento.Creada.Should().BeFalse();
        reintento.Codigo.Should().Be("invalid_document_type");
        portal.Lecturas.Should().Be(1);
        (await e.TramitesAsync(ct)).Should().HaveCount(1);
    }

    [Fact]
    public async Task UnBorradorQueQuedoAMedias_SeRetomaSinCrearUnSegundoTramite()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var existente = await e.SembrarTramiteAsync(origen: "persona_natural", externalRef: e.SolicitudId.ToString(), empresa: PlatformTenants.PersonaNatural, ct, estado: "borrador");
        var solicitud = e.Solicitud(partes: [e.Parte("comprador", "1000000008"), e.Parte("vendedor", "1000000009")]);
        await e.SembrarValidacionesAsync(solicitud, ct);

        var resultado = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        resultado.ProcedureInstanceId.Should().Be(existente, resultado.Codigo);
        (await e.TramitesAsync(ct)).Should().HaveCount(1);
        (await e.ValidacionesLigadasAsync(ct)).Should().Be(2);
    }

    [Fact]
    public void ElCodigoDeUnRechazoPrevio_SeLeeDelMotivoDeLaAnulacion()
    {
        PersonaNaturalCreador.CodigoDeAnulacion($"{PersonaNaturalCreador.PrefijoAnulacion} [documentos_incompletos]").Should().Be("documentos_incompletos");
        PersonaNaturalCreador.CodigoDeAnulacion("Anulado por el gestor").Should().Be(MotivosDeRechazo.Generico);
        PersonaNaturalCreador.CodigoDeAnulacion(null).Should().Be(MotivosDeRechazo.Generico);
    }

    [Fact]
    public async Task UnaSolicitudValida_CreaElTramiteConOrigenYReferencia_LigaLasValidaciones_YEsIdempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var solicitud = e.Solicitud(partes: [e.Parte("comprador", "1000000004"), e.Parte("vendedor", "1000000005")]);
        await e.SembrarValidacionesAsync(solicitud, ct);
        var portal = new PortalFalso(solicitud);

        var primera = await e.ProcesarAsync(portal, e.Evento(), ct);

        primera.Creada.Should().BeTrue(primera.Codigo);
        primera.ProcedureInstanceId.Should().NotBeNull();
        var tramite = (await e.TramitesAsync(ct)).Should().ContainSingle().Which;
        tramite.Origin.Should().Be("persona_natural");
        tramite.ExternalRef.Should().Be(e.SolicitudId.ToString());
        tramite.TenantId.Should().Be(PlatformTenants.PersonaNatural);
        tramite.TransitOfficeId.Should().Be(e.Organismo);
        tramite.Status.Should().Be("borrador", "decisión del PO: el trámite del portal queda siempre en borrador (el pago es de otra épica)");
        primera.NumeroRadicado.Should().Be(tramite.ReferenceNumber);
        (await e.TramitesAsync(ct)).Should().OnlyContain(t => t.SubmittedAt == null, "no se llama a la radicación");
        (await e.ActoresAsync(tramite.Id, ct)).Should().Be(2);
        (await e.ValidacionesLigadasAsync(ct)).Should().Be(2, "cada validación de ESTA solicitud queda referenciada por el trámite");

        // Reintento (otro eventId): no crea un segundo trámite; el borrador ya creado se retoma.
        var segunda = await e.ProcesarAsync(portal, e.Evento(), ct);

        segunda.Creada.Should().BeTrue();
        segunda.ProcedureInstanceId.Should().Be(primera.ProcedureInstanceId);
        (await e.TramitesAsync(ct)).Should().HaveCount(1);
        segunda.Estado.Should().Be(primera.Estado);
        primera.Estado.Should().Be("borrador", "el resultado «creada» lleva el estado real del trámite: borrador");
    }

    [Fact]
    public async Task LaValidacionDeOtraSolicitud_NuncaSeLigaAlTramite_NiSeBuscaPorDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var solicitud = e.Solicitud(partes: [e.Parte("comprador", "1000000006"), e.Parte("vendedor", "1000000007")]);
        await e.SembrarValidacionesAsync(solicitud, ct);
        // Otra solicitud del portal con el MISMO documento del comprador y su propia validación aprobada.
        var otra = Guid.NewGuid();
        await e.SembrarValidacionAjenaAsync(otra, "comprador", "1000000006", ct);

        var resultado = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        resultado.Creada.Should().BeTrue(resultado.Codigo);
        (await e.ValidacionesLigadasAsync(ct)).Should().Be(2);
        (await e.ValidacionAjenaLigadaAsync(otra, ct)).Should().BeFalse("la identidad del portal no se reutiliza entre trámites");
    }

    private sealed class PortalFalso(SolicitudPortal? solicitud, string? razon = null) : IPersonaNaturalPortalClient
    {
        public int Lecturas { get; private set; }

        public Task<LecturaSolicitudPortal> ObtenerSolicitudParaRadicarAsync(Guid solicitudId, Guid tenantId, CancellationToken ct)
        {
            Lecturas++;
            return Task.FromResult(new LecturaSolicitudPortal(solicitud, razon));
        }
    }

    /// <summary>Organismo, tipo elegible y validaciones de una prueba, con códigos únicos; se retiran al terminar.</summary>
    internal sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        public FlitDbContext Db { get; }

        public Guid SolicitudId { get; } = Guid.NewGuid();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OrganismoSinGrant { get; } = Guid.NewGuid();
        public Guid Tipo { get; } = Guid.NewGuid();
        public string Codigo => "PN_D4_" + _sufijo;

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            Db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new Escenario(host.Services.CreateScope());
            foreach (var (id, n) in new[] { (e.Organismo, "A"), (e.OrganismoSinGrant, "B") })
            {
                var code = $"PD4{e._sufijo}{n}";
                await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {code}, {"Organismo de prueba " + code}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }

            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.tenant_transit_office_grants (tenant_id, transit_office_id, is_enabled, source)
                VALUES ({PlatformTenants.PersonaNatural}, {e.Organismo}, true, 'CLIENT')
                """, ct);
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                VALUES ({e.Tipo}, {e.Codigo}, {"Tipo de prueba " + e.Codigo}, 'TRASPASO', 1, {"{\"requiresBuyer\":true,\"requiresSeller\":true}"}::jsonb, 'published', now(), true, true, now())
                """, ct);
            return e;
        }

        public RadicacionSolicitadaDatos Evento(string? tipo = null, Guid? organismo = null) =>
            new(SolicitudId, "PN-2026-000001", tipo ?? Codigo, 1, "huella", organismo ?? Organismo, [], null, "llave-" + SolicitudId, Guid.NewGuid());

        public SolicitudPortal Solicitud(IReadOnlyList<ParteSolicitudPortal>? partes = null) =>
            new(SolicitudId, "PN-2026-000001", Codigo, 1, "huella", Organismo, "Organismo", null, null,
                new Dictionary<string, string>(), DatosSolicitudPortal.Vacios, partes ?? [], [], "llave-" + SolicitudId, Guid.NewGuid().ToString());

        public ParteSolicitudPortal Parte(string rol, string documento, string tipoDocumento = "CC", string estado = "aprobada") =>
            new(rol, 1, tipoDocumento, documento, "Persona de prueba", $"p{documento}@correo.test", "3001112233", "Calle 1", Guid.NewGuid(), estado);

        public async Task<ResultadoRadicacionPortal> ProcesarAsync(IPersonaNaturalPortalClient portal, RadicacionSolicitadaDatos evento, CancellationToken ct)
        {
            // El mismo manejador del consumidor: abre su propio alcance (conexión propia, sin la transacción de la bandeja).
            var radicador = new PersonaNaturalRadicacionHandler(_scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), portal);
            return await radicador.ProcesarAsync(evento, ct);
        }

        public async Task<List<ProcedureInstance>> TramitesAsync(CancellationToken ct)
        {
            var externa = SolicitudId.ToString();
            Db.ChangeTracker.Clear();
            return await Db.Set<ProcedureInstance>().AsNoTracking().Where(p => p.ExternalRef == externa).ToListAsync(ct);
        }

        public Task<int> ActoresAsync(Guid tramite, CancellationToken ct) =>
            Db.Set<ProcedureInstanceActor>().IgnoreQueryFilters().AsNoTracking().CountAsync(a => a.ProcedureInstanceId == tramite, ct);

        public async Task SembrarValidacionesAsync(SolicitudPortal solicitud, CancellationToken ct)
        {
            foreach (var p in solicitud.Partes)
                await SembrarValidacionAsync(p.ValidacionId!.Value, SolicitudId, p.Rol, p.NumeroDocumento, ct);
        }

        public Task SembrarValidacionAjenaAsync(Guid otraSolicitud, string rol, string documento, CancellationToken ct) =>
            SembrarValidacionAsync(Guid.NewGuid(), otraSolicitud, rol, documento, ct);

        private readonly List<string> _solicitudRefs = [];

        private async Task SembrarValidacionAsync(Guid id, Guid solicitud, string rol, string documento, CancellationToken ct)
        {
            _solicitudRefs.Add(solicitud.ToString());
            var ahora = DateTimeOffset.UtcNow;
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString())));
            await Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await Db.Database.BeginTransactionAsync(ct);
                await Db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_tenant_id', {PlatformTenants.PersonaNatural.ToString()}, true)", ct);
                await Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO tramites.procedure_instance_biometric_validations
                        (id, tenant_id, portal_solicitud_ref, portal_parte, name, document_type, document_number, email, registered_email,
                         status, token_hash, expires_at, provider, validated_at, valid_until, created_at, attempts, max_attempts)
                    VALUES ({id}, {PlatformTenants.PersonaNatural}, {solicitud.ToString()}, {rol}, 'Persona de prueba', 'CC', {documento}, 'p@correo.test', 'p@correo.test',
                            'aprobado', {hash}, {ahora.AddHours(24)}, 'mock', {ahora}, {ahora.AddDays(29)}, {ahora}, 0, 5)
                    """, ct);
                await tx.CommitAsync(ct);
            });
        }

        public Task<int> ValidacionesLigadasAsync(CancellationToken ct)
        {
            var externa = SolicitudId.ToString();
            return Db.ProcedureInstanceBiometricValidations.AsNoTracking()
                .CountAsync(v => v.PortalSolicitudRef == externa && v.ProcedureInstanceId != null, ct);
        }

        public async Task<bool> ValidacionAjenaLigadaAsync(Guid otraSolicitud, CancellationToken ct)
        {
            var externa = otraSolicitud.ToString();
            return await Db.ProcedureInstanceBiometricValidations.AsNoTracking()
                .AnyAsync(v => v.PortalSolicitudRef == externa && v.ProcedureInstanceId != null, ct);
        }

        public async Task<Guid> SembrarTramiteAsync(string? origen, string? externalRef, Guid empresa, CancellationToken ct, string estado = "entregado", string? motivoRechazo = null)
        {
            var id = Guid.NewGuid();
            var creador = await PersonaNaturalCreador.ResolverCreadorAsync(Db, empresa, ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_instances (id, tenant_id, procedure_type_id, reference_number, status, created_by_user_id, created_at, origin, external_ref, subsanacion_activa)
                VALUES ({id}, {empresa}, {Tipo}, {"PD4-" + id.ToString("N")[..12]}, {estado}, {creador}, now(), {origen}, {externalRef}, false)
                """, ct);
            if (motivoRechazo is not null)
            {
                await Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO tramites.procedure_instance_status_history (id, tenant_id, procedure_instance_id, from_status, to_status, changed_at, reason)
                    VALUES (uuidv7(), {empresa}, {id}, 'entregado', 'rechazado', now(), {motivoRechazo})
                    """, ct);
            }

            return id;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                var externa = SolicitudId.ToString();
                var orgs = new[] { Organismo, OrganismoSinGrant };
                await using var tx = await Db.Database.BeginTransactionAsync();
                await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var refs = _solicitudRefs.ToArray();
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_instance_biometric_validations WHERE tenant_id = {PlatformTenants.PersonaNatural} AND portal_solicitud_ref = ANY({refs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_state_change_outbox WHERE tenant_id = {PlatformTenants.PersonaNatural} AND procedure_instance_id IN (SELECT id FROM tramites.procedure_instances WHERE procedure_type_id = {Tipo})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_instances WHERE tenant_id = {PlatformTenants.PersonaNatural} AND procedure_type_id = {Tipo}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_transit_office_grants WHERE transit_office_id = ANY({orgs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = {Tipo}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = ANY({orgs})");
                _ = externa;
                await tx.CommitAsync();
            }
            catch (Exception)
            {
                // Retiro best-effort: la base de la prueba es efímera.
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}
