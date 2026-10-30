using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Admin.Tests.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.PersonaNatural;
using Flit.Platform.Sdk;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14072 (Feature J, Épica #14021) — configuración del portal: parámetros con rangos y concurrencia, organismos (el
/// grant de la empresa Persona Natural) y lista blanca de tipos. Las pruebas con SQL corren contra la base con las
/// migraciones aplicadas (DDL 163 y 164); cada una arma sus propios organismos, tipos y usuario y los retira al terminar.
/// </summary>
public sealed class ConfiguracionPnTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // ── AC3 — rangos (sin base) ───────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(14, null, null, null, "sesionVigenciaMinutos", "15", "1440")]
    [InlineData(1441, null, null, null, "sesionVigenciaMinutos", "15", "1440")]
    [InlineData(null, 0, null, null, "borradorVigenciaDias", "1", "90")]
    [InlineData(null, 91, null, null, "borradorVigenciaDias", "1", "90")]
    [InlineData(null, null, 0, null, "topeConsultasCiudadanoDia", "1", "100")]
    [InlineData(null, null, 101, null, "topeConsultasCiudadanoDia", "1", "100")]
    [InlineData(null, null, null, 0, "topeConsultasGlobalDia", "1", "100000")]
    [InlineData(null, null, null, 100_001, "topeConsultasGlobalDia", "1", "100000")]
    public void AC3_UnValorFueraDeRangoSeRechazaConUnMensajeQueIndicaElRango(
        int? sesion, int? borrador, int? ciudadano, int? global, string campo, string min, string max)
    {
        var invalido = ParametrosPnReglas.Validar(Comando(sesion, borrador, ciudadano, global));

        invalido.Should().NotBeNull();
        invalido!.Value.Error.Should().Be(ConfiguracionPnErrores.ValorFueraDeRango);
        invalido.Value.Campo.Should().Be(campo);
        invalido.Value.Mensaje.Should().Contain(min).And.Contain(max);
    }

    [Fact]
    public void AC3_LosLimitesDelRangoYLosValoresNulosSonValidos()
    {
        ParametrosPnReglas.Validar(Comando(15, 1, 1, 1)).Should().BeNull();
        ParametrosPnReglas.Validar(Comando(1440, 90, 100, 100_000)).Should().BeNull();
        ParametrosPnReglas.Validar(Comando(null, null, null, null)).Should().BeNull("nulo = predeterminado del BFF");
    }

    [Theory]
    [InlineData("sin-arroba", null, ConfiguracionPnErrores.CorreoInvalido)]
    [InlineData("a@b.co", "abc", ConfiguracionPnErrores.TelefonoInvalido)]
    public void AC3_ElContactoDeSoporteSeValidaPorFormato(string correo, string? telefono, string esperado)
    {
        var c = Comando(null, null, null, null) with { SoporteCorreo = correo, SoporteTelefono = telefono };

        ParametrosPnReglas.Validar(c)!.Value.Error.Should().Be(esperado);
    }

    // ── SQL real ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_ElSegundoQueGuardaConUnaVersionDesactualizadaRecibeConflictoYNoPierdeElCambioDelPrimero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var original = await e.Parametros.ObtenerAsync(ct);
        try
        {
            var primero = await e.Parametros.GuardarAsync(Comando(30, 15, 5, 500) with { RowVersion = original.RowVersion, Actor = e.Actor, SoporteCorreo = "soporte@flit.test" }, ct);
            var segundo = await e.Parametros.GuardarAsync(Comando(60, 30, 9, 900) with { RowVersion = original.RowVersion, Actor = e.Actor }, ct);

            primero.Error.Should().BeNull();
            primero.Parametros!.RowVersion.Should().BeGreaterThan(original.RowVersion);
            segundo.Error.Should().Be(ConfiguracionPnErrores.Conflicto);
            var vigente = await e.Parametros.ObtenerAsync(ct);
            vigente.SesionVigenciaMinutos.Should().Be(30, "el cambio del primero no se pierde");
            vigente.SoporteCorreo.Should().Be("soporte@flit.test");
            segundo.Parametros!.RowVersion.Should().Be(vigente.RowVersion, "el conflicto devuelve la versión vigente para recargar");
        }
        finally
        {
            await e.Parametros.GuardarAsync(
                Comando(original.SesionVigenciaMinutos, original.BorradorVigenciaDias, original.TopeConsultasCiudadanoDia, original.TopeConsultasGlobalDia)
                    with { RowVersion = (await e.Parametros.ObtenerAsync(ct)).RowVersion, Actor = e.Actor, CaptchaActivo = original.CaptchaActivo, SoporteCorreo = original.SoporteCorreo },
                ct);
        }
    }

    [Fact]
    public async Task AC3_UnValorFueraDeRangoNoCambiaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var antes = await e.Parametros.ObtenerAsync(ct);

        var r = await e.Parametros.GuardarAsync(Comando(5, null, null, null) with { RowVersion = antes.RowVersion, Actor = e.Actor }, ct);

        r.Error.Should().Be(ConfiguracionPnErrores.ValorFueraDeRango);
        (await e.Parametros.ObtenerAsync(ct)).RowVersion.Should().Be(antes.RowVersion);
    }

    [Fact]
    public async Task AC5_ElCambioDeParametrosQuedaAuditadoConActorYValoresAnteriorYNuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        var original = await e.Parametros.ObtenerAsync(ct);
        try
        {
            await e.Parametros.GuardarAsync(
                Comando(45, null, null, null) with { RowVersion = original.RowVersion, Actor = e.Actor, CaptchaActivo = true }, ct);

            var filas = await e.Db.Database.SqlQuery<AuditoriaFila>($"""
                SELECT changed_by, old_value, new_value, operation FROM admin.tenant_config_audit_logs
                 WHERE entity_name = 'persona_natural_settings' AND changed_by = {e.Actor}
                """).ToListAsync(ct);
            var fila = filas.Should().ContainSingle().Subject;
            fila.Operation.Should().Be("update");
            fila.NewValue.Should().MatchRegex(@"""sesionVigenciaMinutos"":\s*45\b");
        }
        finally
        {
            await e.Parametros.GuardarAsync(
                Comando(original.SesionVigenciaMinutos, null, null, null)
                    with { RowVersion = (await e.Parametros.ObtenerAsync(ct)).RowVersion, Actor = e.Actor, CaptchaActivo = original.CaptchaActivo },
                ct);
        }
    }

    [Fact]
    public async Task AC1_HabilitarYDeshabilitarUnOrganismoUsaElGrantDeLaEmpresaPersonaNatural_YQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);

        (await e.Organismos.EstablecerAsync(e.Organismo, true, e.Actor, ct)).Should().BeNull();
        var grant = await e.Db.Database.SqlQuery<GrantFila>($"""
            SELECT is_enabled, source FROM admin.tenant_transit_office_grants
             WHERE tenant_id = {PlatformTenants.PersonaNatural} AND transit_office_id = {e.Organismo}
            """).ToListAsync(ct);
        grant.Should().ContainSingle().Which.Should().Match<GrantFila>(g => g.IsEnabled && g.Source == "SYSTEM");
        (await e.Organismos.ListarAsync(true, ct)).Select(o => o.Id).Should().Contain(e.Organismo).And.NotContain(e.OtroOrganismo);

        // Repetir el mismo estado no cambia nada ni audita de nuevo.
        await e.Organismos.EstablecerAsync(e.Organismo, true, e.Actor, ct);
        await e.Organismos.EstablecerAsync(e.Organismo, false, e.Actor, ct);

        (await e.Organismos.ListarAsync(true, ct)).Select(o => o.Id).Should().NotContain(e.Organismo);
        (await e.Organismos.ListarAsync(false, ct)).Single(o => o.Id == e.Organismo).Habilitado.Should().BeFalse();
        var auditoria = await e.Db.Database.SqlQuery<AuditoriaFila>($"""
            SELECT changed_by, old_value, new_value, operation FROM admin.tenant_config_audit_logs
             WHERE entity_name = 'persona_natural_organismos' AND target_entity_id = {e.Organismo}
            """).ToListAsync(ct);
        auditoria.Select(a => a.Operation).Should().BeEquivalentTo("activate", "deactivate");
        (await e.Organismos.EstablecerAsync(Guid.NewGuid(), true, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.OrganismoNoExiste, "un organismo inexistente no se habilita");
    }

    [Fact]
    public async Task HU14216_ElCanalDelPuntoNoTocaElGrant_YElCodigoResuelveSoloSiEstaEncendidoYOperable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);
        if (!await e.ExisteTablaCanalAsync(ct))
        {
            await e.RetirarRestosDePruebaAsync(ct);
            Assert.Skip("La base local aún no tiene admin.persona_natural_point_channel. Las pruebas dejan Database:AutoMigrate en false; aplica la migración 20261029120000_HU14216 con core-api detenido (pnpm migrate:core-api).");
        }

        (await e.Canal.EstablecerAsync(e.Organismo, false, e.Actor, ct)).Should().BeNull("apagar un canal apagado no falla");
        (await e.Canal.EstablecerAsync(e.OtroOrganismo, true, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.OrganismoNoOperable);
        (await e.Canal.EstablecerAsync(Guid.NewGuid(), false, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.OrganismoNoExiste);

        (await e.Config.ResolverCanalPuntoAsync(e.CodigoOrganismo, ct)).Estado.Should().Be(CanalPuntoResolucion.CanalApagado);

        (await e.Canal.EstablecerAsync(e.Organismo, true, e.Actor, ct)).Should().BeNull();
        (await e.Organismos.ListarAsync(false, ct)).Single(o => o.Id == e.Organismo)
            .Should().Match<OrganismoPnView>(o => o.CanalPuntoEncendido && !o.Habilitado);

        var grants = await GrantsAsync(e, ct);
        grants.Should().BeEmpty("encender el canal no crea el grant del portal");

        var ok = await e.Config.ResolverCanalPuntoAsync(e.CodigoOrganismo.ToLowerInvariant(), ct);
        ok.Estado.Should().Be(CanalPuntoResolucion.Ok);
        ok.OrganismoId.Should().Be(e.Organismo);
        ok.Codigo.Should().Be(e.CodigoOrganismo);

        (await e.Config.ResolverCanalPuntoAsync("NO-EXISTE", ct)).Estado.Should().Be(CanalPuntoResolucion.CodigoDesconocido);
        (await e.Config.ResolverCanalPuntoAsync(e.CodigoOtro, ct)).Estado.Should().Be(CanalPuntoResolucion.OrganismoNoOperable);

        (await e.Canal.EstablecerAsync(e.Organismo, false, e.Actor, ct)).Should().BeNull();
        (await e.Config.ResolverCanalPuntoAsync(e.CodigoOrganismo, ct)).Estado.Should().Be(CanalPuntoResolucion.CanalApagado);
        (await GrantsAsync(e, ct)).Should().BeEmpty("apagar el canal tampoco toca el grant");
    }

    private static async Task<List<GrantFila>> GrantsAsync(Escenario e, CancellationToken ct) =>
        await e.Db.Database.SqlQuery<GrantFila>($"""
            SELECT is_enabled, source FROM admin.tenant_transit_office_grants
             WHERE tenant_id = {PlatformTenants.PersonaNatural} AND transit_office_id = {e.Organismo}
            """).ToListAsync(ct);

    [Fact]
    public async Task HU14072_SoloSeListanLosOrganismosActivosPorFlit_NoSeHabilitanLosNoOperables_YElHabilitadoQueDejoDeSerloSigueParaApagarlo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);

        // El organismo B (sin perfil OT) existe en el catÃ¡logo pero no estÃ¡ activo por FLIT.
        var lista = await e.Organismos.ListarAsync(false, ct);
        lista.Single(o => o.Id == e.Organismo).ActivoEnFlit.Should().BeTrue();
        lista.Should().NotContain(o => o.Id == e.OtroOrganismo, "no es operable en FLIT");
        (await e.Organismos.EstablecerAsync(e.OtroOrganismo, true, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.OrganismoNoOperable);
        (await e.Organismos.EstablecerAsync(e.OtroOrganismo, false, e.Actor, ct)).Should().BeNull("apagar siempre se puede");

        // Habilitado y luego la empresa OT se desactiva: sigue en la lista (inactivo en FLIT) y el portal ya no lo ofrece.
        (await e.Organismos.EstablecerAsync(e.Organismo, true, e.Actor, ct)).Should().BeNull();
        (await e.Portal.ListarOrganismosAsync(ct)).Select(o => o.Id).Should().Contain(e.Organismo);
        (await e.Portal.OrganismoHabilitadoAsync(e.Organismo, ct)).Should().BeTrue();
        await e.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.tenants SET is_active = false WHERE id = {e.TenantOt}", ct);
        (await e.Organismos.ListarAsync(false, ct)).Single(o => o.Id == e.Organismo)
            .Should().Match<OrganismoPnView>(o => o.Habilitado && !o.ActivoEnFlit);
        (await e.Portal.ListarOrganismosAsync(ct)).Select(o => o.Id).Should().NotContain(e.Organismo);
        (await e.Portal.OrganismoHabilitadoAsync(e.Organismo, ct)).Should().BeFalse();
        (await e.Organismos.EstablecerAsync(e.Organismo, false, e.Actor, ct)).Should().BeNull("apagar siempre se puede");
    }

    [Fact]
    public async Task AC1_SoloSePuedenHabilitarLosTiposElegibles_ApagarSiempreSePuede_YElOrganismoSinTarifaSeAvisa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);

        var antes = await e.Tipos.ListarAsync(ct);
        antes.Single(t => t.Id == e.TipoElegible).Should().Match<TipoPnView>(t => t.Elegible && !t.Habilitado && !t.Ofrecible);
        // HU #14072: la lista trae TODOS los publicados; los no elegibles salen con su motivo y no se pueden habilitar.
        antes.Single(t => t.Id == e.TipoApagado).Should().Match<TipoPnView>(
            t => !t.Elegible && !t.Habilitado && !t.Ofrecible && t.Motivo.Contains("no está habilitado en FLIT 2"));
        antes.Single(t => t.Id == e.TipoNoElegiblePerfil).Should().Match<TipoPnView>(
            t => !t.Elegible && !t.Habilitado && !t.Ofrecible && t.Motivo == "El tipo exige leasing o locatario.");
        antes.Should().NotContain(t => t.Id == e.TipoArchivado || t.Id == e.TipoBorrador, "archivados y borradores no se listan");

        (await e.Tipos.EstablecerAsync(e.TipoNoElegiblePerfil, true, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.TipoNoElegible);
        (await e.Tipos.EstablecerAsync(e.TipoNoElegiblePerfil, false, e.Actor, ct)).Should().BeNull("apagar siempre se puede");
        (await e.Tipos.EstablecerAsync(e.TipoApagado, true, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.TipoNoElegible);
        (await e.Tipos.EstablecerAsync(Guid.NewGuid(), true, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.TipoNoElegible, "no es candidato");
        (await e.Tipos.EstablecerAsync(Guid.NewGuid(), false, e.Actor, ct)).Should().Be(ConfiguracionPnErrores.TipoNoExiste);
        (await e.Tipos.EstablecerAsync(e.TipoElegible, true, e.Actor, ct)).Should().BeNull();

        // Con el organismo habilitado y SIN tarifa, el organismo se avisa; con tarifa vigente (hoy), se cuenta el tipo.
        await e.Organismos.EstablecerAsync(e.Organismo, true, e.Actor, ct);
        // Línea base relativa: otras pruebas en paralelo también dejan tipos habilitados con tarifa general.
        var sinTarifa = (await e.Organismos.ListarAsync(true, ct)).Single(o => o.Id == e.Organismo).TiposConTarifaVigente;
        var hoy = e.Tarifas.HoyColombia();
        await e.TarifaAsync(e.TipoElegible, hoy, ct);
        (await e.Organismos.ListarAsync(true, ct)).Single(o => o.Id == e.Organismo).TiposConTarifaVigente.Should().Be(sinTarifa + 1);
        var despues = (await e.Tipos.ListarAsync(ct)).Single(t => t.Id == e.TipoElegible);
        despues.Ofrecible.Should().BeTrue();
        despues.OrganismosConTarifaVigente.Should().BeGreaterThanOrEqualTo(1);

        // Un tipo habilitado que deja de ser elegible sigue en la lista (para apagarlo) pero ya no es ofrecible.
        await e.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tramites.procedure_types SET wizard_enabled = false WHERE id = {e.TipoElegible}", ct);
        var sinWizard = (await e.Tipos.ListarAsync(ct)).Single(t => t.Id == e.TipoElegible);
        sinWizard.Should().Match<TipoPnView>(t => t.Habilitado && !t.Elegible && !t.Ofrecible);
        (await e.Tipos.EstablecerAsync(e.TipoElegible, false, e.Actor, ct)).Should().BeNull("apagar siempre se puede");
        (await e.Tipos.ListarAsync(ct)).Single(t => t.Id == e.TipoElegible).Should().Match<TipoPnView>(t => !t.Habilitado && !t.Elegible && !t.Ofrecible);
    }

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HU14072_UnTipoHabilitadoQueDejoDeEstarActivoEnFlitSigueEnLaListaParaApagarlo_ConActivoEnFlitFalso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var e = await Escenario.CrearAsync(factory, ct);

        (await e.Tipos.EstablecerAsync(e.TipoElegible, true, e.Actor, ct)).Should().BeNull();
        (await e.Tipos.ListarAsync(ct)).Single(t => t.Id == e.TipoElegible).ActivoEnFlit.Should().BeTrue();
        await e.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tramites.procedure_types SET is_active = false WHERE id = {e.TipoElegible}", ct);

        (await e.Tipos.ListarAsync(ct)).Single(t => t.Id == e.TipoElegible)
            .Should().Match<TipoPnView>(t => t.Habilitado && !t.ActivoEnFlit && !t.Elegible && !t.Ofrecible);
        (await e.Tipos.EstablecerAsync(e.TipoElegible, false, e.Actor, ct)).Should().BeNull("apagar siempre se puede");
        (await e.Tipos.ListarAsync(ct)).Should().NotContain(t => t.Id == e.TipoElegible, "ya no estÃ¡ habilitado ni activo");
    }
    private static GuardarParametrosPnCommand Comando(int? sesion, int? borrador, int? ciudadano, int? global) =>
        new(sesion, borrador, ciudadano, global, null, null, null, null, 0, Guid.NewGuid());

    private sealed class AuditoriaFila
    {
        public Guid? ChangedBy { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? Operation { get; set; }
    }

    private sealed class GrantFila
    {
        public bool IsEnabled { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        public FlitDbContext Db { get; }
        public ParametrosPnHandler Parametros { get; }
        public OrganismosPnHandler Organismos { get; }
        public CanalPuntoPnHandler Canal { get; }
        public IPersonaNaturalConfigRepository Config { get; }
        public TiposPnHandler Tipos { get; }
        public string CodigoOrganismo { get; private set; } = string.Empty;
        public string CodigoOtro { get; private set; } = string.Empty;
        public IPersonaNaturalTarifaRepository Tarifas { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid OtroOrganismo { get; } = Guid.NewGuid();
        public Guid TenantOt { get; } = Guid.NewGuid();
        public PersonaNaturalPortalReader Portal { get; }
        public Guid TipoElegible { get; } = Guid.NewGuid();
        public Guid TipoApagado { get; } = Guid.NewGuid();
        public Guid TipoNoElegiblePerfil { get; } = Guid.NewGuid();
        public Guid TipoArchivado { get; } = Guid.NewGuid();
        public Guid TipoBorrador { get; } = Guid.NewGuid();

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            var sp = scope.ServiceProvider;
            Db = sp.GetRequiredService<FlitDbContext>();
            Parametros = sp.GetRequiredService<ParametrosPnHandler>();
            Organismos = sp.GetRequiredService<OrganismosPnHandler>();
            Canal = sp.GetRequiredService<CanalPuntoPnHandler>();
            Config = sp.GetRequiredService<IPersonaNaturalConfigRepository>();
            Tipos = sp.GetRequiredService<TiposPnHandler>();
            Portal = sp.GetRequiredService<PersonaNaturalPortalReader>();
            Tarifas = sp.GetRequiredService<IPersonaNaturalTarifaRepository>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            // La empresa Persona Natural no la siembra ninguna migración: la prueba la asegura (y la retira si la creó).
            var e = new Escenario(host.ConEmpresaPersonaNatural().Services.CreateScope());
            var correo = $"pn-cfg-{e._sufijo.ToLowerInvariant()}@example.test";
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES ({e.Actor}, {correo}, {"Actor de prueba"}, 'active', now())", ct);
            foreach (var (id, n) in new[] { (e.Organismo, "A"), (e.OtroOrganismo, "B") })
            {
                var code = $"PNC{e._sufijo}{n}";
                if (n == "A") e.CodigoOrganismo = code;
                else e.CodigoOtro = code;
                await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                    VALUES ({id}, {code}, {"Organismo de prueba " + code}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                    """, ct);
            }

            // El organismo A es operable en FLIT (perfil OT + empresa OT activa); el B no tiene perfil.
            var taxId = "9" + e._sufijo;
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
                VALUES ({e.TenantOt}, {"OT de prueba " + e._sufijo}, {taxId}, 'FLIT', true, now())
                """, ct);
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES ({e.TenantOt}, {e.Organismo})
                """, ct);

            foreach (var (id, wizard, n, estado, perfil) in new[]
            {
                (e.TipoElegible, true, "A", "published", "{}"),
                (e.TipoApagado, false, "B", "published", "{}"),
                (e.TipoNoElegiblePerfil, true, "C", "published", """{"requiresLessee":true}"""),
                (e.TipoArchivado, true, "D", "archived", "{}"),
                (e.TipoBorrador, true, "E", "draft", "{}"),
            })
            {
                var code = $"PN_C_{e._sufijo}_{n}";
                await e.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO tramites.procedure_types (id, code, name, family, version, gate_profile, publication_status, published_at, is_active, wizard_enabled, created_at)
                    VALUES ({id}, {code}, {"Tipo de prueba " + code}, 'TRASPASO', 1, {perfil}::jsonb, {estado}, now(), true, {wizard}, now())
                    """, ct);
            }

            return e;
        }

        /// <summary>Derechos de tránsito y servicio FLIT del tipo, general, vigentes desde la fecha.</summary>
        /// <summary>Quita organismos, tipos y usuarios que esta prueba siembra (también un intento anterior si la limpieza no llegó a confirmarse).</summary>
        public async Task RetirarRestosDePruebaAsync(CancellationToken ct)
        {
            await using var tx = await Db.Database.BeginTransactionAsync(ct);
            await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica", ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM admin.persona_natural_tariffs
                 WHERE procedure_type_id IN (SELECT id FROM tramites.procedure_types WHERE name LIKE {"Tipo de prueba PN_C_%"})
                """, ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM admin.persona_natural_enabled_types
                 WHERE procedure_type_id IN (SELECT id FROM tramites.procedure_types WHERE name LIKE {"Tipo de prueba PN_C_%"})
                """, ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM admin.tenant_transit_office_grants
                 WHERE transit_office_id IN (SELECT id FROM catalogs.transit_offices WHERE name LIKE {"Organismo de prueba PNC%"})
                """, ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM admin.tenant_config_audit_logs
                 WHERE changed_by IN (SELECT id FROM identity.users WHERE email LIKE {"pn-cfg-%@example.test"})
                """, ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.users WHERE email LIKE {"pn-cfg-%@example.test"}", ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE name LIKE {"Tipo de prueba PN_C_%"}", ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM admin.transit_office_profiles
                 WHERE tenant_id IN (SELECT id FROM identity.tenants WHERE legal_name LIKE {"OT de prueba %"})
                """, ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.tenants WHERE legal_name LIKE {"OT de prueba %"}", ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE name LIKE {"Organismo de prueba PNC%"}", ct);
            await tx.CommitAsync(ct);
        }

        public async Task<bool> ExisteTablaCanalAsync(CancellationToken ct)
        {
            var nombre = (await Db.Database.SqlQuery<string>($"""
                SELECT COALESCE(to_regclass('admin.persona_natural_point_channel')::text, '') AS "Value"
                """).ToListAsync(ct)).FirstOrDefault();
            return !string.IsNullOrEmpty(nombre);
        }

        public async Task TarifaAsync(Guid tipo, DateOnly desde, CancellationToken ct)
        {
            foreach (var concepto in new[] { "DERECHOS_TRANSITO", "SERVICIO_FLIT" })
                await Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO admin.persona_natural_tariffs (concept, procedure_type_id, transit_office_id, amount_cop, valid_from)
                    VALUES ({concepto}, {tipo}, NULL, 1000, {desde})
                    """, ct);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                // Las versiones de tarifa son historial inmutable por trigger: solo un superusuario (la base de pruebas) puede retirarlas.
                var hayCanal = await ExisteTablaCanalAsync(CancellationToken.None);
                await using var tx = await Db.Database.BeginTransactionAsync();
                await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var orgs = new[] { Organismo, OtroOrganismo };
                var tipos = new[] { TipoElegible, TipoApagado, TipoNoElegiblePerfil, TipoArchivado, TipoBorrador };
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_tariffs WHERE procedure_type_id = ANY({tipos})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_enabled_types WHERE procedure_type_id = ANY({tipos})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_transit_office_grants WHERE transit_office_id = ANY({orgs})");
                if (hayCanal)
                    await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_point_channel WHERE transit_office_id = ANY({orgs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_config_audit_logs WHERE changed_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.users WHERE id = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = ANY({tipos})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.transit_office_profiles WHERE tenant_id = {TenantOt}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.tenants WHERE id = {TenantOt}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = ANY({orgs})");
                await tx.CommitAsync();
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}
