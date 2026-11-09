using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Application.Companies.TransitOffices.PortalDominio;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.PortalDominio;

/// <summary>Feature #14380 (Épica #14297) — reglas del dominio del portal: registro, verificación, certificado y entrega al punto.</summary>
public sealed class PortalDominioHandlerTests
{
    private static readonly Guid Organismo = Guid.Parse("07000000-0000-4000-8000-000000014381");
    private static readonly Guid IdDominio = Guid.Parse("d0000000-0000-4000-8000-000000014381");
    private static readonly Guid Actor = Guid.Parse("14381000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IPortalDominioRepository _repo = Substitute.For<IPortalDominioRepository>();
    private readonly IAdminAuditWriter _audit = Substitute.For<IAdminAuditWriter>();
    private readonly IDnsTxtResolver _dns = Substitute.For<IDnsTxtResolver>();
    private readonly DomainOptions _opciones = new() { Reserved = ["*.flitsas.online", "flitsas.online"], EdgeIp = "203.0.113.10" };
    private readonly DomainVerificationOptions _verificacion = new();

    private PortalDominioAdminHandler Admin(TimeProvider? reloj = null) =>
        new(_repo, new PortalDominioVerificador(_repo, _dns, _verificacion, reloj), _opciones, _audit);

    private static DominioDePortal Dominio(string estado = "pending", string host = "tramites.miorganismo.gov.co", string? motivo = null,
        DateTimeOffset? ultimaComprobacion = null, bool principal = false, Guid? id = null, Guid? organismo = null) =>
        new(id ?? IdDominio, organismo ?? Organismo, host, estado, motivo, "TOKEN-DE-VERIFICACION-0123456789", null, null, null, null, ultimaComprobacion, null, 0, null, Ahora, 3, principal);

    // ── B1 · registrar ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_UnHostValidoQuedaPendienteConInstruccionesDeDnsYToken()
    {
        _repo.RegistrarAsync(Organismo, "tramites.miorganismo.gov.co", Arg.Any<string>(), Actor, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(c => PortalDominioRegistro.Ok(Dominio() with { VerificationToken = c.ArgAt<string>(2) }));

        var r = await Admin().RegistrarAsync(Organismo, new RegistrarPortalDominioRequest("  Tramites.MiOrganismo.gov.co "), Actor, Ct);

        r.Error.Should().BeNull();
        r.Valor!.Estado.Should().Be("pendiente");
        r.Valor.Motivo.Should().BeNull();
        r.Valor.Dns.Should().BeEquivalentTo(new[]
        {
            new PortalDominioDnsDto("TXT", "_flit-verify.tramites.miorganismo.gov.co", r.Valor.Dns[0].Valor),
            new PortalDominioDnsDto("A", "tramites.miorganismo.gov.co", "203.0.113.10"),
        });
        r.Valor.Dns[0].Valor.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "256 bits en base64url, único por registro");
        await _audit.Received(1).WriteAsync(Arg.Is<AdminAuditEntry>(e => e.EntityName == "PortalDominio" && e.TargetEntityId == Organismo), Ct);
    }

    [Fact]
    public async Task SinIpDeBordeConfiguradaSoloSeEntregaElTxt()
    {
        _opciones.EdgeIp = string.Empty;
        _repo.RegistrarAsync(Organismo, Arg.Any<string>(), Arg.Any<string>(), Actor, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(PortalDominioRegistro.Ok(Dominio()));

        var r = await Admin().RegistrarAsync(Organismo, new RegistrarPortalDominioRequest("tramites.miorganismo.gov.co"), Actor, Ct);

        r.Valor!.Dns.Should().ContainSingle(d => d.Tipo == "TXT");
    }

    [Theory]
    [InlineData("https://tramites.miorganismo.gov.co")]
    [InlineData("tramites.miorganismo.gov.co/ruta")]
    [InlineData("sin-punto")]
    [InlineData("")]
    public async Task AC1_UnHostInvalidoSeRechazaConSuMotivoYNoEscribe(string host)
    {
        var r = await Admin().RegistrarAsync(Organismo, new RegistrarPortalDominioRequest(host), Actor, Ct);

        r.Error!.Code.Should().Be(PortalDominioErrores.Invalido);
        r.Error.Message.Should().Contain("sin https://");
        await _repo.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task AC2_UnHostReservadoSeRechazaConElMotivoExacto()
    {
        var r = await Admin().RegistrarAsync(Organismo, new RegistrarPortalDominioRequest("portal.flitsas.online"), Actor, Ct);

        r.Error!.Status.Should().Be(409);
        r.Error.Code.Should().Be(PortalDominioErrores.Reservado);
        r.Error.Message.Should().Be("El dominio portal.flitsas.online está reservado para FLIT y no se puede registrar. Use un dominio propio del organismo.");
        await _repo.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task AC2_UnHostYaRegistradoPorOtraRedOOrganismoSeRechazaConElMotivoExacto()
    {
        _repo.RegistrarAsync(Organismo, "tramites.miorganismo.gov.co", Arg.Any<string>(), Actor, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(PortalDominioRegistro.Falla(PortalDominioRegistroFallo.HostEnUso));

        var r = await Admin().RegistrarAsync(Organismo, new RegistrarPortalDominioRequest("tramites.miorganismo.gov.co"), Actor, Ct);

        r.Error!.Code.Should().Be(PortalDominioErrores.EnUso);
        r.Error.Message.Should().Be("El dominio tramites.miorganismo.gov.co ya está registrado por otro organismo o por una red de marca blanca. Use un dominio diferente.");
        await _audit.DidNotReceiveWithAnyArgs().WriteAsync(default!, Ct);
    }

    [Fact]
    public async Task AC3_UnOrganismoEnElTopeNoPuedeRegistrarOtroYElMotivoNombraElMaximo()
    {
        _opciones.PortalMaxPorOrganismo = 3;
        _repo.RegistrarAsync(Organismo, "otro.miorganismo.gov.co", Arg.Any<string>(), Actor, 3, Arg.Any<CancellationToken>())
            .Returns(PortalDominioRegistro.Falla(PortalDominioRegistroFallo.LimiteAlcanzado));

        var r = await Admin().RegistrarAsync(Organismo, new RegistrarPortalDominioRequest("otro.miorganismo.gov.co"), Actor, Ct);

        r.Error!.Status.Should().Be(409);
        r.Error.Code.Should().Be(PortalDominioErrores.Limite);
        r.Error.Message.Should().Be("El organismo ya tiene 3 dominios, el máximo permitido. Retire uno antes de registrar otro.");
    }

    [Fact]
    public async Task ElTopePorDefectoEsDiez()
    {
        _repo.RegistrarAsync(Organismo, Arg.Any<string>(), Arg.Any<string>(), Actor, 10, Arg.Any<CancellationToken>())
            .Returns(PortalDominioRegistro.Ok(Dominio()));

        (await Admin().RegistrarAsync(Organismo, new RegistrarPortalDominioRequest("tramites.miorganismo.gov.co"), Actor, Ct)).Error.Should().BeNull();
    }

    [Fact]
    public async Task SinDominiosElGetResponde200ConListaVaciaYUnIdInexistenteEs404()
    {
        _repo.ListarAsync(Organismo, Arg.Any<CancellationToken>()).Returns([]);

        (await Admin().ListarAsync(Organismo, Ct)).Should().BeEmpty();
        (await Admin().RetirarAsync(Organismo, IdDominio, Actor, Ct)).Error!.Code.Should().Be(PortalDominioErrores.NoRegistrado);
    }

    [Fact]
    public async Task ElListadoEntregaIdPrincipalYEstadoDeCadaDominioPorSeparado()
    {
        var otro = Guid.Parse("d0000000-0000-4000-8000-0000000143ff");
        _repo.ListarAsync(Organismo, Arg.Any<CancellationToken>()).Returns([
            Dominio("active", "a.miorganismo.gov.co", principal: true),
            Dominio("failed", "b.miorganismo.gov.co", motivo: "TXT_NOT_FOUND", id: otro)]);

        var lista = await Admin().ListarAsync(Organismo, Ct);

        lista.Select(d => (d.Id, d.Host, d.Principal, d.Estado)).Should().Equal(
            (IdDominio, "a.miorganismo.gov.co", true, "activo"), (otro, "b.miorganismo.gov.co", false, "fallido"));
        lista[1].Motivo.Should().Contain("No encontramos el registro TXT");
        lista[0].Motivo.Should().BeNull("un fallo de b no toca a a");
    }

    // ── Principal ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SoloUnDominioActivoPuedeSerPrincipalYSiNoElMotivoEsExacto()
    {
        _repo.ObtenerPorIdAsync(IdDominio, Arg.Any<CancellationToken>()).Returns(Dominio("verified"));

        var r = await Admin().PrincipalAsync(Organismo, IdDominio, Actor, Ct);

        r.Error!.Status.Should().Be(409);
        r.Error.Code.Should().Be(PortalDominioErrores.PrincipalNoActivo);
        r.Error.Message.Should().Be("Solo un dominio activo puede ser el principal. El dominio tramites.miorganismo.gov.co está verificado.");
        await _repo.DidNotReceiveWithAnyArgs().EstablecerPrincipalAsync(default, default, Ct);
    }

    [Fact]
    public async Task UnDominioActivoPasaAPrincipalYQuedaAuditado()
    {
        _repo.ObtenerPorIdAsync(IdDominio, Arg.Any<CancellationToken>()).Returns(Dominio("active"));
        _repo.EstablecerPrincipalAsync(IdDominio, Actor, Arg.Any<CancellationToken>()).Returns(Dominio("active", principal: true));

        var r = await Admin().PrincipalAsync(Organismo, IdDominio, Actor, Ct);

        r.Valor!.Principal.Should().BeTrue();
        await _audit.Received(1).WriteAsync(Arg.Is<AdminAuditEntry>(e => e.EntityName == "PortalDominio" && e.Operation == "update"), Ct);
    }

    // ── Alcance por id ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnIdDeOtroOrganismoDa403EnVerificarPrincipalYRetirarYNoEscribeNada()
    {
        var ajeno = Dominio("active", organismo: Guid.NewGuid());
        _repo.ObtenerPorIdAsync(IdDominio, Arg.Any<CancellationToken>()).Returns(ajeno);

        (await Admin().VerificarAsync(Organismo, IdDominio, Actor, Ct)).Error!.Status.Should().Be(403);
        (await Admin().PrincipalAsync(Organismo, IdDominio, Actor, Ct)).Error!.Status.Should().Be(403);
        (await Admin().RetirarAsync(Organismo, IdDominio, Actor, Ct)).Error!.Status.Should().Be(403);

        await _repo.DidNotReceiveWithAnyArgs().RetirarAsync(default, default, Ct);
        await _repo.DidNotReceiveWithAnyArgs().EstablecerPrincipalAsync(default, default, Ct);
        await _dns.DidNotReceiveWithAnyArgs().LookupAsync(default!, Ct);
    }
    // ── B2 · verificar ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_ConElTxtCorrectoElDominioPasaAVerificado()
    {
        _repo.ObtenerPorIdAsync(IdDominio, Arg.Any<CancellationToken>()).Returns(Dominio());
        _dns.LookupAsync("tramites.miorganismo.gov.co", Arg.Any<CancellationToken>())
            .Returns(new DnsTxtLookupResult(true, ["flit-verify=TOKEN-DE-VERIFICACION-0123456789"], null));
        _repo.AplicarComprobacionAsync(IdDominio, "verified", Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Dominio("verified"));

        var r = await Admin().VerificarAsync(Organismo, IdDominio, Actor, Ct);

        r.Valor!.Estado.Should().Be("verificado");
    }

    [Theory]
    [InlineData(false, "TXT_NOT_FOUND", "No encontramos el registro TXT _flit-verify.tramites.miorganismo.gov.co")]
    [InlineData(true, "TXT_MISMATCH", "su valor no coincide")]
    public async Task AC3_UnTxtAusenteOIncorrectoDejaElMotivoYNoActiva(bool hayTxt, string codigo, string fragmento)
    {
        _repo.ObtenerPorIdAsync(IdDominio, Arg.Any<CancellationToken>()).Returns(Dominio());
        _dns.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DnsTxtLookupResult(hayTxt, hayTxt ? ["otro-valor"] : [], null));
        _repo.AplicarComprobacionAsync(IdDominio, "failed", codigo, Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Dominio("failed", motivo: codigo));

        var r = await Admin().VerificarAsync(Organismo, IdDominio, Actor, Ct);

        r.Valor!.Estado.Should().Be("fallido");
        r.Valor.Motivo.Should().Contain(fragmento);
    }

    [Fact]
    public async Task UnaVerificacionMuyProntoEsperaYNoConsultaElDns()
    {
        var reloj = new FakeTimeProvider(Ahora);
        _repo.ObtenerPorIdAsync(IdDominio, Arg.Any<CancellationToken>()).Returns(Dominio(ultimaComprobacion: Ahora.AddSeconds(-10)));

        var r = await Admin(reloj).VerificarAsync(Organismo, IdDominio, Actor, Ct);

        r.Error!.Status.Should().Be(429);
        r.Error.EsperarSegundos.Should().Be(20);
        await _dns.DidNotReceiveWithAnyArgs().LookupAsync(default!, Ct);
    }

    // ── B2 · activar con el certificado ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AC2_VerificadoMasCertificadoPasaAActivoSinActivacionManual()
    {
        PortalDominioEstados.DecidirCertificado("verified", Ahora).Should().Be(new DomainCertificateDecision("active", Ahora));
        PortalDominioEstados.DecidirCertificado("active", Ahora).Should().Be(new DomainCertificateDecision("active", null), "renovación: solo fechas");
        PortalDominioEstados.DecidirCertificado("pending", Ahora).Should().BeNull();
        PortalDominioEstados.DecidirCertificado("failed", Ahora).Should().BeNull();
    }

    [Fact]
    public async Task AC2_LaSenalDelCertificadoActivaUnDominioVerificado()
    {
        _repo.ObtenerPorHostAsync("tramites.miorganismo.gov.co", Arg.Any<CancellationToken>()).Returns(Dominio("verified"));
        _repo.AplicarCertificadoAsync("tramites.miorganismo.gov.co", "active", Arg.Is<DateTimeOffset?>(a => a != null), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Dominio("active"));

        var r = await Certificados().EmitidoAsync("tramites.miorganismo.gov.co", Ahora, null, Ct);

        r.Should().Be(PortalDominioCertificadoResultado.Actualizado);
    }

    [Fact]
    public async Task UnDominioPendienteNoAdmiteCertificadoYUnHostAjenoNoEsDePortal()
    {
        _repo.ObtenerPorHostAsync("tramites.miorganismo.gov.co", Arg.Any<CancellationToken>()).Returns(Dominio("pending"));

        (await Certificados().EmitidoAsync("tramites.miorganismo.gov.co", Ahora, null, Ct)).Should().Be(PortalDominioCertificadoResultado.NoVerificado);
        (await Certificados().EmitidoAsync("otro.example.com", Ahora, null, Ct)).Should().Be(PortalDominioCertificadoResultado.NoEsDePortal);
        await _repo.DidNotReceiveWithAnyArgs().AplicarCertificadoAsync(default!, default!, default, default, default, default!, Ct);
    }

    // ── B4 · falla de emisión ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_B4_UnCertificadoQueNoSePudoEmitirDejaElMotivoYNoActiva()
    {
        _repo.ObtenerPorHostAsync("tramites.miorganismo.gov.co", Arg.Any<CancellationToken>()).Returns(Dominio("verified"));

        var r = await Certificados().FalloAsync("tramites.miorganismo.gov.co", "acme.sh --issue fallo\nlinea 2", Ct);

        r.Should().Be(PortalDominioCertificadoResultado.Actualizado);
        await _repo.Received(1).MarcarCertificadoFallidoAsync(
            "tramites.miorganismo.gov.co", "CERT_NOT_ISSUED: acme.sh --issue fallolinea 2", Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            PortalDominioCertificadoHandler.Trabajo, Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().AplicarCertificadoAsync(default!, default!, default, default, default, default!, Ct);
    }

    [Fact]
    public async Task UnaRenovacionFallidaNoTumbaUnDominioActivo()
    {
        _repo.ObtenerPorHostAsync("tramites.miorganismo.gov.co", Arg.Any<CancellationToken>()).Returns(Dominio("active"));

        await Certificados().FalloAsync("tramites.miorganismo.gov.co", null, Ct);

        await _repo.DidNotReceiveWithAnyArgs().MarcarCertificadoFallidoAsync(default!, default!, default, default, default!, Ct);
    }

    [Fact]
    public void ElEstadoFallidoPorCertificadoSeExplicaEnEspanol()
    {
        var dto = PortalDominioEstados.ADto(Dominio("failed", motivo: "CERT_NOT_ISSUED: x"), "203.0.113.10");

        dto.Estado.Should().Be("fallido");
        dto.Motivo.Should().Contain("No se pudo emitir el certificado");
    }

    // ── B3 · entrega al punto ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_B3_SoloUnDominioActivoEntregaElCodigoDelOrganismo()
    {
        _repo.ObtenerOrganismoActivoPorHostAsync("tramites.miorganismo.gov.co", Arg.Any<CancellationToken>())
            .Returns(new PortalDominioOrganismo(Organismo, "OT01"));

        (await Punto().PorHostAsync("Tramites.MiOrganismo.gov.co", Ct)).Should().Be(new PortalDominioPuntoDto(true, "OT01", "tramites.miorganismo.gov.co"));
        (await Punto().PorHostAsync("pendiente.miorganismo.gov.co", Ct)).Should().Be(PortalDominioPuntoDto.Ninguno);
        (await Punto().PorHostAsync("https://malo", Ct)).Should().Be(PortalDominioPuntoDto.Ninguno);
    }

    [Fact]
    public async Task EnlaceQrYCorreosObtienenElDominioActivoPorCodigoYAnteUnaFallaSeResponderSinDominio()
    {
        _repo.ObtenerHostActivoPorCodigoAsync("ot01", Arg.Any<CancellationToken>()).Returns("tramites.miorganismo.gov.co");
        (await Punto().PorCodigoAsync("ot01", Ct)).Should().Be(new PortalDominioPuntoDto(true, "OT01", "tramites.miorganismo.gov.co"));

        _repo.ObtenerHostActivoPorCodigoAsync("falla", Arg.Any<CancellationToken>()).Returns<string?>(_ => throw new InvalidOperationException("bd"));
        (await Punto().PorCodigoAsync("falla", Ct)).Should().Be(PortalDominioPuntoDto.Ninguno);
        (await Punto().PorCodigoAsync(new string('x', 21), Ct)).Should().Be(PortalDominioPuntoDto.Ninguno);
    }

    private PortalDominioCertificadoHandler Certificados() =>
        new(_repo, _verificacion, NullLogger<PortalDominioCertificadoHandler>.Instance, new FakeTimeProvider(Ahora));

    private PortalDominioPuntoHandler Punto() => new(_repo, NullLogger<PortalDominioPuntoHandler>.Instance);

    private sealed class FakeTimeProvider(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
