using Flit.Admin.Tests.Identity;
using Flit.Api.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU 15 (Features #14248/#14249) — de punta a punta del creador por tipo, contra Postgres real: lo que el portal entrega
/// (campos, adjuntos, prenda, destino, partes) tiene que quedar en el trámite de FLIT 2 con las MISMAS claves y valores que deja el
/// asistente del gestor. Los tipos de transformación, blindaje, causal y levantamiento se identifican por su código, así que esas
/// pruebas usan el tipo sembrado y se omiten si la base no lo tiene o no lo tiene encendido; destino, prenda, leasing y
/// simultáneos se prueban con tipos propios (códigos únicos, retirados al terminar).
/// </summary>
public sealed class PersonaNaturalTiposPostgresTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(_ => { }).ConEmpresaPersonaNatural();

    // ── Declaraciones de los tipos sembrados ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CambioDeColor_GuardaElColorNuevoYLaBanderaCambio_ConElSoporteComoAdjuntoDelPortal()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.UsarTipoSembradoAsync("CAMBIO_COLOR", ct);
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000001")],
            campos: new() { ["vehicle_color"] = "ROJO CEREZA" },
            adjuntos: [e.Adjunto("soporte_cambio_color", "aa11")]);
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        var campos = await e.CamposAsync(ct);
        campos.Should().Contain("vehicle_color", "ROJO CEREZA").And.Contain("cambio_color", "true", "el servidor deriva la bandera que enciende el asistente");
        (await e.AdjuntosAsync(ct)).Should().ContainSingle().Which.Should().Match<ProcedureInstanceAttachment>(a => a.Tipo == "soporte_cambio_color" && a.Source == "portal");
        (await e.ActoresAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task CambioDeColor_ElMismoArchivoParaDosDocumentos_SeRegistraUnaVezPorTipo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.UsarTipoSembradoAsync("CAMBIO_COLOR", ct);
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000001")],
            campos: new() { ["vehicle_color"] = "ROJO CEREZA" },
            adjuntos: [e.Adjunto("tarjeta_propiedad", "same"), e.Adjunto("soporte_cambio_color", "same")]);
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        var adjuntos = await e.AdjuntosAsync(ct);
        adjuntos.Select(a => a.Tipo).Should().BeEquivalentTo("tarjeta_propiedad", "soporte_cambio_color");
        adjuntos.Select(a => a.Sha256).Distinct().Should().ContainSingle("es el mismo archivo registrado para dos tipos");
    }

    [Fact]
    public async Task Blindaje_ElDesmonteDejaLaBanderaDerivadaEnFalso_YLosNivelesEnVerdadero()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.UsarTipoSembradoAsync("BLINDAJE", ct);
        var desmonte = e.Solicitud(
            partes: [e.Parte("comprador", "2000000002")],
            campos: new() { ["blindaje_nivel"] = "DESMONTE", ["blindaje"] = "true" },
            adjuntos: [e.Adjunto("certificado_blindaje", "bb22")]);
        await e.SembrarValidacionesAsync(desmonte, ct);

        var r = await e.ProcesarAsync(new PortalFalso(desmonte), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        var campos = await e.CamposAsync(ct);
        campos.Should().Contain("blindaje_nivel", "DESMONTE").And.Contain("blindaje", "false", "la bandera se deriva de la opción: lo que mande el navegador no manda");
        (await e.AdjuntosAsync(ct)).Should().ContainSingle().Which.Tipo.Should().Be("certificado_blindaje");
    }

    [Fact]
    public async Task CancelacionDeMatricula_GuardaLaCausalYSusDocumentos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.UsarTipoSembradoAsync("CANCELACION_MATRICULA", ct);
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000003")],
            campos: new() { ["cancelacion_causal"] = "PERDIDA_TOTAL_ACCIDENTE" },
            adjuntos: [e.Adjunto("certificado_dijin", "c1"), e.Adjunto("certificado_aseguradora_perito", "c2"), e.Adjunto("certificado_autoridad_administrativa", "c3")]);
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        if (r.Codigo == "procedure_family_blocked")
            Assert.Skip("La empresa Persona Natural de esta base tiene bloqueada la familia MATRICULAS (configuración del ambiente).");
        r.Creada.Should().BeTrue(r.Codigo);
        (await e.CamposAsync(ct)).Should().Contain("cancelacion_causal", "PERDIDA_TOTAL_ACCIDENTE");
        (await e.AdjuntosAsync(ct)).Select(a => a.Tipo).Should().BeEquivalentTo("certificado_dijin", "certificado_aseguradora_perito", "certificado_autoridad_administrativa");
    }

    [Theory]
    [InlineData("LEVANTAMIENTO_PRENDA", "levantar")]
    [InlineData("PRENDA_INSCRIPCION", "registrar")]
    public async Task UnTipoPrendarioDeAccionUnica_FijaLaDecision_AunqueElPortalMandeOtra(string tipo, string esperada)
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.UsarTipoSembradoAsync(tipo, ct);
        var solicitud = e.Solicitud(partes: [e.Parte("comprador", "2000000004")], prenda: new PrendaPortal(true, "Banco Uno", "900123456", "sin_prenda"));
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        (await e.PrendasAsync(ct)).Should().ContainSingle().Which.Should().Match<ProcedureInstancePrenda>(
            p => p.Decision == esperada && p.AcreedorNombre == "Banco Uno" && p.AcreedorDocumento == "900123456");
    }

    [Theory]
    [InlineData("LEVANTAMIENTO_PRENDA", "paz_salvo_prenda", "prenda_levantamiento")]
    [InlineData("PRENDA_INSCRIPCION", "inscripcion_prenda", "prenda_registro")]
    public async Task UnTipoPrendarioDeAccionUnica_RadicadoDesdeElPortal_PasaElGateDePrendaDeFlit2(string tipo, string deLaMatriz, string deLaDecision)
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.UsarTipoSembradoAsync(tipo, ct);
        var levanta = tipo == "LEVANTAMIENTO_PRENDA";
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000014")],
            adjuntos: [e.Adjunto(deLaMatriz, "pr01"), e.Adjunto(deLaDecision, "pr02")],
            prenda: new PrendaPortal(true, "Banco Uno", "900123456", null, levanta ? "Secretaría de Tránsito de Cali" : "ignorada"));
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        var prendas = await e.PrendasAsync(ct);
        prendas.Should().ContainSingle().Which.LevantamientoEntidad.Should().Be(levanta ? "Secretaría de Tránsito de Cali" : null);
        var docTipos = (await e.AdjuntosAsync(ct)).Select(a => a.Tipo).ToList();
        docTipos.Should().Contain([deLaMatriz, deLaDecision]);
        Flit.Tramites.Domain.Tramites.Services.PrendaGate.EvaluateAccionUnica(prendas.ToList(), docTipos).Should().BeNull();
    }

    [Fact]
    public async Task LevantamientoRadicadoSinEntidad_NoPasaElGateDePrenda_ComoEnFlit2()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.UsarTipoSembradoAsync("LEVANTAMIENTO_PRENDA", ct);
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000015")],
            adjuntos: [e.Adjunto("paz_salvo_prenda", "pr03"), e.Adjunto("prenda_levantamiento", "pr04")],
            prenda: new PrendaPortal(true, "Banco Uno", "900123456"));
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        var prendas = await e.PrendasAsync(ct);
        Flit.Tramites.Domain.Tramites.Services.PrendaGate.EvaluateAccionUnica(prendas.ToList(), (await e.AdjuntosAsync(ct)).Select(a => a.Tipo).ToList())
            .Should().Be(Flit.Tramites.Domain.Tramites.Estados.TramiteEstadoErrores.PrendaEntidadLevantamientoRequerida);
    }

    // ── Destino, prenda, leasing y simultáneos con tipos propios ─────────────────────────────────────────────────────

    [Fact]
    public async Task TrasladoDeCuenta_EscribeLasCuatroClavesDelDestino_ConLosDatosDelCatalogo_NoLosDelNavegador()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct, perfil: """{"requiresBuyer":true,"requiresDestinationTransitOffice":true}""", familia: "OTROS");
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000005")],
            campos: new() { ["transit_office_destino_id"] = e.Destino.ToString(), ["transit_office_destino_name"] = "NOMBRE FALSO" });
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        var campos = await e.CamposAsync(ct);
        campos.Should().Contain("transit_office_destino_id", e.Destino.ToString());
        campos.Should().Contain("transit_office_destino_code", e.CodigoDestino);
        campos.Should().Contain("transit_office_destino_name", e.NombreDestino, "el nombre sale del catálogo, no del navegador");
        campos.Should().Contain("transit_office_destino_city", "Municipio de prueba");
        campos["transit_office_id"].Should().Be(e.Organismo.ToString(), "el organismo del trámite sigue siendo el de origen");
    }

    [Fact]
    public async Task TrasladoDeCuenta_ConUnDestinoQueNoExiste_SeRechazaYElBorradorQuedaAnulado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct, perfil: """{"requiresBuyer":true,"requiresDestinationTransitOffice":true}""", familia: "OTROS");
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000006")],
            campos: new() { ["transit_office_destino_id"] = Guid.NewGuid().ToString() });
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeFalse();
        r.Codigo.Should().Be("organismo_destino_invalido");
        r.Motivo.Should().Contain("destino");
        (await e.TramitesAsync(ct)).Should().ContainSingle().Which.Status.Should().Be("anulado");
    }

    [Theory]
    [InlineData("registrar", true, "registrar")]
    [InlineData("levantar", true, "levantar")]
    [InlineData("sin_prenda", false, "sin_prenda")]
    [InlineData(null, true, "registrar")]
    [InlineData(null, false, "sin_prenda")]
    public async Task LaDecisionDePrenda_SeRegistraEnFlit2_ConLaMismaDecisionDelPortal(string? decision, bool tiene, string esperada)
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000007"), e.Parte("vendedor", "2000000008")],
            prenda: new PrendaPortal(tiene, tiene ? "Banco Dos" : null, tiene ? "800123456" : null, decision));
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        (await e.PrendasAsync(ct)).Should().ContainSingle().Which.Decision.Should().Be(esperada);
    }

    [Fact]
    public async Task Leasing_SoloSeCapturaAlLocatario_YElDocumentoDelPropietarioDelDiagnosticoNoSePisa()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct, perfil: """{"requiresBuyer":true,"requiresLessee":true}""", familia: "OTROS");
        var solicitud = e.Solicitud(
            partes: [e.Parte("locatario", "2000000009")],
            campos: new() { ["owner_document_type"] = "CC", ["owner_document_number"] = "1111111111" });
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        (await e.ActoresAsync(ct)).Should().Be(1);
        (await e.CamposAsync(ct)).Should().Contain("owner_document_number", "1111111111", "en un leasing el titular capturado es el locatario, no el propietario inscrito");
    }

    [Fact]
    public async Task UnSimultaneoEnUnTraspaso_GuardaElValorNuevoYLaBandera_PorqueElTipoAcumulaComplementos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        var solicitud = e.Solicitud(
            partes: [e.Parte("comprador", "2000000010"), e.Parte("vendedor", "2000000011")],
            campos: new() { ["vehicle_color"] = "AZUL", ["cambio_color"] = "true" },
            adjuntos: [e.Adjunto("soporte_cambio_color", "dd44")]);
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeTrue(r.Codigo);
        (await e.CamposAsync(ct)).Should().Contain("vehicle_color", "AZUL").And.Contain("cambio_color", "true");
        (await e.AdjuntosAsync(ct)).Should().ContainSingle().Which.Tipo.Should().Be("soporte_cambio_color");
    }

    [Fact]
    public async Task UnTipoDeOtrosSinDeclaraciones_RechazaUnaTransformacionAjena_ComoEnFlit2()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct, perfil: """{"requiresBuyer":true,"allowsComplementaryTransformations":false}""", familia: "OTROS");
        var solicitud = e.Solicitud(partes: [e.Parte("comprador", "2000000012")], campos: new() { ["cambio_color"] = "true" });
        await e.SembrarValidacionesAsync(solicitud, ct);

        var r = await e.ProcesarAsync(new PortalFalso(solicitud), e.Evento(), ct);

        r.Creada.Should().BeFalse("PatchFieldValues no admite complementos en la familia OTROS (ADR-0050): el creador respeta esa regla");
        r.Codigo.Should().Be("complemento_no_admitido");
    }

    [Fact]
    public async Task ListarOrganismosDestino_SoloDevuelveLosHabilitadosParaLaEmpresaPnYOperables_FiltraPorTextoYResuelvePorId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.HacerOperableAsync(e.Destino, ct);
        var lector = e.Servicio<Flit.Infrastructure.PersonaNatural.PersonaNaturalPortalReader>();

        // D3 (PO, HU #14259): operable pero SIN grant de la empresa Persona Natural => no aparece (y guardarlo daría organismo_destino_invalido).
        (await lector.ListarOrganismosDestinoAsync(null, e.Destino, 10, ct)).Should().BeEmpty("es operable pero la empresa Persona Natural no tiene grant sobre él");
        (await lector.ListarOrganismosDestinoAsync(e.CodigoDestino, null, 10, ct)).Should().BeEmpty();

        // Con el grant de la empresa (el mismo que valida FLIT 2 al radicar) y operable: sale.
        await e.DarGrantAsync(e.Destino, ct);
        (await lector.ListarOrganismosDestinoAsync(null, e.Destino, 10, ct)).Should().ContainSingle().Which.Nombre.Should().Be(e.NombreDestino);
        (await lector.ListarOrganismosDestinoAsync(null, e.Organismo, 10, ct)).Should().BeEmpty("tiene grant del portal pero no es operable en FLIT 2");

        // Por texto: nombre, código o municipio; un comodín del usuario no se interpreta.
        (await lector.ListarOrganismosDestinoAsync(e.CodigoDestino.ToLowerInvariant(), null, 10, ct)).Select(o => o.Id).Should().Equal(e.Destino);
        (await lector.ListarOrganismosDestinoAsync("Municipio de prueba", null, 50, ct)).Select(o => o.Id).Should().Contain(e.Destino).And.NotContain(e.Organismo);
        (await lector.ListarOrganismosDestinoAsync("%", null, 50, ct)).Select(o => o.Id).Should().NotContain(e.Destino);
        (await lector.ListarOrganismosDestinoAsync("no existe " + Guid.NewGuid(), null, 10, ct)).Should().BeEmpty();

        // El límite siempre se acota.
        (await lector.ListarOrganismosDestinoAsync(null, null, 100_000, ct)).Count.Should().BeLessThanOrEqualTo(Flit.Infrastructure.PersonaNatural.PersonaNaturalPortalReader.LimiteDestinoMaximo);
        (await lector.ListarOrganismosDestinoAsync(null, null, 0, ct)).Count.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task LaDecisionDePrendaDeCadaCaso_SeResuelveSinTocarFlit2()
    {
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(true, "B", "1", "levantar"), "TRASPASO_STANDARD").Should().Be("levantar");
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(true, "B", "1", "LEVANTAR"), "TRASPASO_STANDARD").Should().Be("levantar");
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(false, null, null, "sin_prenda"), "TRASPASO_STANDARD").Should().Be("sin_prenda");
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(true, "B", "1", "omitir"), "TRASPASO_STANDARD").Should().Be("registrar", "una decisión que el portal no ofrece cae al comportamiento anterior");
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(true, "B", "1"), "TRASPASO_STANDARD").Should().Be("registrar");
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(false, null, null), "TRASPASO_STANDARD").Should().Be("sin_prenda");
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(true, "B", "1", "sin_prenda"), "LEVANTAMIENTO_PRENDA").Should().Be("levantar");
        PersonaNaturalCreador.DecisionDePrenda(new PrendaPortal(true, "B", "1", "levantar"), "PRENDA_INSCRIPCION").Should().Be("registrar");
        await Task.CompletedTask;
    }

    private sealed class PortalFalso(SolicitudPortal solicitud) : IPersonaNaturalPortalClient
    {
        public Task<LecturaSolicitudPortal> ObtenerSolicitudParaRadicarAsync(Guid solicitudId, Guid tenantId, CancellationToken ct) =>
            Task.FromResult(new LecturaSolicitudPortal(solicitud, null));
    }

    /// <summary>Organismos y tipo de una prueba, con códigos únicos; se retiran al terminar (incluidos los hijos del trámite).</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly string _sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        private readonly List<string> _solicitudRefs = [];
        private readonly Guid _tipoPropio = Guid.NewGuid();

        public FlitDbContext Db { get; }

        public Guid SolicitudId { get; } = Guid.NewGuid();
        public Guid Organismo { get; } = Guid.NewGuid();
        public Guid Destino { get; } = Guid.NewGuid();
        public Guid Tipo { get; private set; }
        public string CodigoTipo { get; private set; } = string.Empty;
        public string CodigoDestino => "PT" + _sufijo + "D";
        public string NombreDestino => "Organismo de prueba " + CodigoDestino;

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            Db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        }

        public static async Task<Escenario> CrearAsync(
            WebApplicationFactory<Program> host, CancellationToken ct, string perfil = """{"requiresBuyer":true,"requiresSeller":true}""", string familia = "TRASPASO")
        {
            var e = new Escenario(host.Services.CreateScope());
            e.Tipo = e._tipoPropio;
            e.CodigoTipo = "PN_T15_" + e._sufijo;
            foreach (var (id, n) in new[] { (e.Organismo, "O"), (e.Destino, "D") })
            {
                var code = $"PT{e._sufijo}{n}";
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
                VALUES ({e.Tipo}, {e.CodigoTipo}, {"Tipo de prueba " + e.CodigoTipo}, {familia}, 1, {perfil}::jsonb, 'published', now(), true, true, now())
                """, ct);
            return e;
        }

        /// <summary>Usa el tipo sembrado de FLIT 2 (identificado por su código). Si la base no lo tiene encendido, omite la prueba.</summary>
        public async Task UsarTipoSembradoAsync(string codigo, CancellationToken ct)
        {
            var fila = await Db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM tramites.procedure_types
                 WHERE code = {codigo} AND is_active AND wizard_enabled AND publication_status = 'published'
                """).ToListAsync(ct);
            if (fila.Count == 0)
                Assert.Skip($"La base no tiene el tipo {codigo} publicado y con el wizard encendido.");
            Tipo = fila[0];
            CodigoTipo = codigo;
            // Los tipos sembrados validan que el organismo sea operable en FLIT 2 (ValidateOtOperability).
            await HacerOperableAsync(Organismo, ct);
        }

        public RadicacionSolicitadaDatos Evento() =>
            new(SolicitudId, "PN-2026-000001", CodigoTipo, 1, "huella", Organismo, [], null, "llave-" + SolicitudId, Guid.NewGuid());

        public SolicitudPortal Solicitud(
            IReadOnlyList<ParteSolicitudPortal>? partes = null,
            Dictionary<string, string>? campos = null,
            IReadOnlyList<AdjuntoSolicitudPortal>? adjuntos = null,
            PrendaPortal? prenda = null) =>
            new(SolicitudId, "PN-2026-000001", CodigoTipo, 1, "huella", Organismo, "Organismo", null, null,
                campos ?? new Dictionary<string, string>(), new DatosSolicitudPortal(null, prenda), partes ?? [], adjuntos ?? [], "llave-" + SolicitudId, Guid.NewGuid().ToString());

        public ParteSolicitudPortal Parte(string rol, string documento) =>
            new(rol, 1, "CC", documento, "Persona de prueba", $"p{documento}@correo.test", "3001112233", "Calle 1", Guid.NewGuid(), "aprobada");

        public AdjuntoSolicitudPortal Adjunto(string tipo, string sha) =>
            new(Guid.NewGuid(), tipo, $"portal/{SolicitudId}/{sha}", (sha + SolicitudId.ToString("N")).PadRight(64, '0')[..64], 1024, "application/pdf", $"{tipo}.pdf");

        private readonly Guid _tenantOt = Guid.NewGuid();
        private bool _operable;

        public Task DarGrantAsync(Guid organismo, CancellationToken ct) =>
            Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO admin.tenant_transit_office_grants (tenant_id, transit_office_id, is_enabled, source) VALUES ({PlatformTenants.PersonaNatural}, {organismo}, true, 'CLIENT')", ct);

        public T Servicio<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

        /// <summary>Perfil OT con empresa activa: el criterio de «operable en FLIT 2» (el mismo de <c>OtOperabilityGate</c>).</summary>
        public async Task HacerOperableAsync(Guid organismo, CancellationToken ct)
        {
            _operable = true;
            await Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
                VALUES ({_tenantOt}, {"OT de prueba " + _sufijo}, {"8" + _sufijo}, 'FLIT', true, now())
                """, ct);
            await Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES ({_tenantOt}, {organismo})", ct);
        }

        public async Task<ResultadoRadicacionPortal> ProcesarAsync(IPersonaNaturalPortalClient portal, RadicacionSolicitadaDatos evento, CancellationToken ct)
        {
            var radicador = new PersonaNaturalRadicacionHandler(_scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), portal);
            return await radicador.ProcesarAsync(evento, ct);
        }

        public async Task<List<ProcedureInstance>> TramitesAsync(CancellationToken ct)
        {
            var externa = SolicitudId.ToString();
            Db.ChangeTracker.Clear();
            return await Db.Set<ProcedureInstance>().AsNoTracking().Where(p => p.ExternalRef == externa).ToListAsync(ct);
        }

        private async Task<Guid> TramiteIdAsync(CancellationToken ct) => (await TramitesAsync(ct)).Single().Id;

        public async Task<Dictionary<string, string?>> CamposAsync(CancellationToken ct)
        {
            var id = await TramiteIdAsync(ct);
            return await Db.Set<ProcedureInstanceFieldValue>().IgnoreQueryFilters().AsNoTracking()
                .Where(f => f.ProcedureInstanceId == id).ToDictionaryAsync(f => f.FieldKey, f => f.ValueText, ct);
        }

        public async Task<List<ProcedureInstanceAttachment>> AdjuntosAsync(CancellationToken ct)
        {
            var id = await TramiteIdAsync(ct);
            return await Db.Set<ProcedureInstanceAttachment>().IgnoreQueryFilters().AsNoTracking().Where(a => a.ProcedureInstanceId == id).ToListAsync(ct);
        }

        public async Task<List<ProcedureInstancePrenda>> PrendasAsync(CancellationToken ct)
        {
            var id = await TramiteIdAsync(ct);
            return await Db.Set<ProcedureInstancePrenda>().IgnoreQueryFilters().AsNoTracking().Where(p => p.ProcedureInstanceId == id && p.Estado == Flit.Tramites.Domain.Tramites.ValueObjects.PrendaEstado.Vigente).ToListAsync(ct);
        }

        public async Task<int> ActoresAsync(CancellationToken ct)
        {
            var id = await TramiteIdAsync(ct);
            return await Db.Set<ProcedureInstanceActor>().IgnoreQueryFilters().AsNoTracking().CountAsync(a => a.ProcedureInstanceId == id, ct);
        }

        public async Task SembrarValidacionesAsync(SolicitudPortal solicitud, CancellationToken ct)
        {
            _solicitudRefs.Add(SolicitudId.ToString());
            foreach (var p in solicitud.Partes)
            {
                var ahora = DateTimeOffset.UtcNow;
                var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.ValidacionId!.Value.ToString())));
                await Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var tx = await Db.Database.BeginTransactionAsync(ct);
                    await Db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_tenant_id', {PlatformTenants.PersonaNatural.ToString()}, true)", ct);
                    await Db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO tramites.procedure_instance_biometric_validations
                            (id, tenant_id, portal_solicitud_ref, portal_parte, name, document_type, document_number, email, registered_email,
                             status, token_hash, expires_at, provider, validated_at, valid_until, created_at, attempts, max_attempts)
                        VALUES ({p.ValidacionId!.Value}, {PlatformTenants.PersonaNatural}, {SolicitudId.ToString()}, {p.Rol}, 'Persona de prueba', 'CC', {p.NumeroDocumento}, 'p@correo.test', 'p@correo.test',
                                'aprobado', {hash}, {ahora.AddHours(24)}, 'mock', {ahora}, {ahora.AddDays(29)}, {ahora}, 0, 5)
                        """, ct);
                    await tx.CommitAsync(ct);
                });
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                var orgs = new[] { Organismo, Destino };
                var refs = _solicitudRefs.ToArray();
                var externa = SolicitudId.ToString();
                await using var tx = await Db.Database.BeginTransactionAsync();
                await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                var instancias = $"SELECT id FROM tramites.procedure_instances WHERE tenant_id = '{PlatformTenants.PersonaNatural}' AND external_ref = '{externa}'";
                foreach (var tabla in new[]
                {
                    "procedure_instance_field_values", "procedure_instance_attachments", "procedure_instance_prenda", "procedure_instance_actors",
                    "procedure_instance_commercial", "procedure_instance_events", "procedure_instance_status_history", "procedure_state_change_outbox",
                })
                {
#pragma warning disable EF1002 // identificadores fijos y guids propios: no hay entrada de usuario
                    await Db.Database.ExecuteSqlRawAsync($"DELETE FROM tramites.{tabla} WHERE procedure_instance_id IN ({instancias})");
#pragma warning restore EF1002
                }

                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_instance_biometric_validations WHERE tenant_id = {PlatformTenants.PersonaNatural} AND portal_solicitud_ref = ANY({refs})");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_instances WHERE tenant_id = {PlatformTenants.PersonaNatural} AND external_ref = {externa}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_transit_office_grants WHERE transit_office_id = ANY({orgs})");
                if (_operable)
                {
                    await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.transit_office_profiles WHERE tenant_id = {_tenantOt}");
                    await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.tenants WHERE id = {_tenantOt}");
                }

                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tramites.procedure_types WHERE id = {_tipoPropio}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM catalogs.transit_offices WHERE id = ANY({orgs})");
                await tx.CommitAsync();
            }
            catch (Exception)
            {
                // Retiro best-effort: la base de la prueba es de desarrollo y los códigos son únicos.
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}
