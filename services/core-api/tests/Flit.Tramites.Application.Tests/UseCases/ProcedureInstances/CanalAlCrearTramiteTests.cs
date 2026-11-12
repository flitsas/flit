using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14318 (Feature #14315, Épica #14296) — el servidor asigna el canal del trámite al crearlo y lo muestra en el detalle.
/// Cubre en el handler de creación (AC1 a AC4 y AC6, sin base de datos): qué canal y qué OT del canal llegan al INSERT, el
/// rechazo de combinaciones incoherentes y que el JSON de la API no puede traer el canal; y en el detalle (AC10) el canal con
/// su nombre y el OT del punto con el suyo. Los INSERT contra PostgreSQL real están en <c>CanalAlCrearTramitePostgresTests</c>.
/// <para>Uso de ejemplo: <c>Crear(new(..., Channel: "pn_punto", ChannelTransitOfficeId: ot))</c> deja la instancia con ese canal y ese OT.</para>
/// </summary>
public sealed class CanalAlCrearTramiteTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly List<ProcedureInstance> _insertadas = [];
    private readonly ProcedureType _tipo = new()
    {
        Id = Guid.NewGuid(),
        Code = "TRASPASO_STANDARD",
        Name = "Traspaso",
        Family = "TRASPASO",
        PublicationStatus = PublicationStatus.Published,
        WizardEnabled = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CanalAlCrearTramiteTests()
    {
        _typeRepo.GetByIdAsync(_tipo.Id, Arg.Any<CancellationToken>()).Returns(_tipo);
        _typeRepo.GetByCodePublishedAsync(_tipo.Code, Arg.Any<CancellationToken>()).Returns(_tipo);
        _repo.AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var instancia = call.Arg<ProcedureInstance>();
                instancia.ReferenceNumber = "1";
                _insertadas.Add(instancia);
                return Task.FromResult(AddProcedureInstanceOutcome.Created);
            });
    }

    private CreateProcedureInstanceHandler Handler() => new(_repo, _typeRepo);

    private CreateProcedureInstanceRequest Solicitud(string? canal = null, Guid? ot = null)
    {
        var baseRequest = new CreateProcedureInstanceRequest(Guid.NewGuid(), _tipo.Id, Guid.NewGuid(), null);
        return canal is null ? baseRequest : baseRequest with { Channel = canal, ChannelTransitOfficeId = ot };
    }

    // ── AC1: sin canal explícito, el alta es del producto Trámites ───────────────────────────────

    [Fact]
    public async Task AC1_SinCanalExplicito_ElTramiteNaceConCanalTramitesYSinOt()
    {
        var (resultado, error) = await Handler().HandleAsync(Solicitud(), Ct);

        error.Should().BeNull();
        resultado.Should().NotBeNull();
        var insertada = _insertadas.Should().ContainSingle().Which;
        insertada.Channel.Should().Be(TramiteCanal.Tramites);
        insertada.ChannelTransitOfficeId.Should().BeNull();
    }

    /// <summary>
    /// AC1 por la consulta del paso 1 (<see cref="CreateProcedureInstanceFromConsultaHandler"/>, que también usa la carga
    /// masiva): llega al INSERT con canal Trámites y sin OT. Se corta al insertar para no armar el resto del flujo.
    /// </summary>
    [Fact]
    public async Task AC1_PorConsulta_ElTramiteNaceConCanalTramitesYSinOt()
    {
        _repo.FindTramitesByPlacaAsync(Arg.Any<Guid>(), "ABC123", Arg.Is(Guid.Empty), Arg.Any<CancellationToken>()).Returns([]);
        _repo.AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>())
            .Returns<Task<AddProcedureInstanceOutcome>>(call =>
            {
                _insertadas.Add(call.Arg<ProcedureInstance>());
                throw new CorteDePruebaException();
            });
        var transitOfficeResolver = Substitute.For<ITransitOfficeResolver>();
        var sut = new CreateProcedureInstanceFromConsultaHandler(
            _repo,
            Handler(),
            new PatchFieldValuesHandler(_repo),
            new RunPreflightHandler(
                _repo,
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationProviderRegistry>(),
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationProviderChainResolver>(),
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationTenantOverrideProvider>(),
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationRestrictionPolicy>(),
                transitOfficeResolver),
            Substitute.For<IPreflightPreviewStore>(),
            transitOfficeResolver,
            typeRepo: _typeRepo);

        var llamar = async () => await sut.HandleAsync(
            new CreateFromConsultaRequest(
                Guid.NewGuid(), Guid.NewGuid(), "traspaso", Vin: null, Plate: "ABC123",
                OwnerDocumentType: "CC", OwnerDocumentNumber: "1", PreviewToken: null),
            Ct);

        await llamar.Should().ThrowAsync<CorteDePruebaException>();
        var insertada = _insertadas.Should().ContainSingle().Which;
        insertada.Channel.Should().Be(TramiteCanal.Tramites);
        insertada.ChannelTransitOfficeId.Should().BeNull();
    }

    private sealed class CorteDePruebaException : Exception;

    /// <summary>
    /// AC8 (regla que no cambia) — Traspaso y Matrícula Inicial siguen bloqueando un segundo trámite en proceso del mismo
    /// vehículo: la regla mira estado y vehículo, nunca el canal, así que un trámite del portal bloquea al del punto y al revés.
    /// </summary>
    [Theory]
    [InlineData("traspaso")]
    [InlineData("matricula_inicial")]
    public async Task AC8_TraspasoYMatriculaInicial_SiguenBloqueandoUnSegundoTramiteEnProceso(string modalidad)
    {
        var tenant = Guid.NewGuid();
        var ot = Guid.NewGuid();
        _repo.FindTramitesByPlacaAsync(tenant, "ABC123", Guid.Empty, Arg.Any<CancellationToken>())
            .Returns([new PlacaTramiteExistente(Guid.NewGuid(), TramiteEstado.Borrador, "ABC123")]);
        _repo.FindTramitesByVinAsync(tenant, Arg.Any<string>(), Guid.Empty, Arg.Any<CancellationToken>())
            .Returns([new VinTramiteExistente(Guid.NewGuid(), TramiteEstado.Borrador, 1, null, "1HGCM82633A004352")]);
        var resolver = Substitute.For<ITransitOfficeResolver>();
        resolver.ResolveEnabledByIdAsync(tenant, ot, Arg.Any<CancellationToken>())
            .Returns(new ResolvedTransitOffice(ot, "99999000", "OT PRUEBA", "99999", "MUNICIPIO"));
        _typeRepo.GetByCodePublishedAsync("MATRICULA_NUEVA", Arg.Any<CancellationToken>())
            .Returns(new ProcedureType
            {
                Id = Guid.NewGuid(), Code = "MATRICULA_NUEVA", Name = "Matrícula", Family = "MATRICULAS",
                PublicationStatus = PublicationStatus.Published, WizardEnabled = true, CreatedAt = DateTimeOffset.UtcNow,
            });
        var sut = new CreateProcedureInstanceFromConsultaHandler(
            _repo,
            Handler(),
            new PatchFieldValuesHandler(_repo),
            new RunPreflightHandler(
                _repo,
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationProviderRegistry>(),
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationProviderChainResolver>(),
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationTenantOverrideProvider>(),
                Substitute.For<Flit.Tramites.Application.UseCases.Consultations.IConsultationRestrictionPolicy>(),
                resolver),
            Substitute.For<IPreflightPreviewStore>(),
            resolver,
            typeRepo: _typeRepo);

        var (resultado, error, existente, _) = await sut.HandleAsync(
            new CreateFromConsultaRequest(
                tenant, Guid.NewGuid(), modalidad,
                Vin: modalidad == "matricula_inicial" ? "1HGCM82633A004352" : null,
                Plate: modalidad == "traspaso" ? "ABC123" : null,
                OwnerDocumentType: "CC", OwnerDocumentNumber: "1", PreviewToken: null,
                TransitOfficeId: ot),
            Ct);

        error.Should().Be("DUPLICATE_ACTIVE_PROCEDURE");
        resultado.Should().BeNull();
        existente.Should().NotBeNull();
        _insertadas.Should().BeEmpty();
    }

    /// <summary>
    /// AC8 (lo que cambia) — dos altas del mismo vehículo, una del portal web y otra del punto, no se estorban en el INSERT:
    /// cada una conserva su canal. El bloqueo de arriba es de la creación por consulta; el handler de creación no mira
    /// duplicados, así que el tipo que permite simultáneos (o el trámite ya terminado) crea cada uno con lo suyo.
    /// </summary>
    [Fact]
    public async Task AC8_DosTramitesDelMismoVehiculo_PorWebYPorPunto_ConservanCadaUnoSuCanal()
    {
        var ot = Guid.NewGuid();
        var porWeb = Solicitud(TramiteCanal.PersonaNaturalWeb);
        var porPunto = Solicitud(TramiteCanal.PersonaNaturalOt, ot);

        await Handler().HandleAsync(porWeb, Ct);
        await Handler().HandleAsync(porPunto, Ct);

        _insertadas.Should().HaveCount(2);
        _insertadas[0].Channel.Should().Be("pn_web");
        _insertadas[0].ChannelTransitOfficeId.Should().BeNull();
        _insertadas[1].Channel.Should().Be("pn_punto");
        _insertadas[1].ChannelTransitOfficeId.Should().Be(ot);
    }

    // ── AC2, AC3 y AC4: el canal que decide el llamador llega al mismo INSERT ────────────────────

    [Fact]
    public async Task AC2_PortalWeb_ElTramiteNaceConCanalPnWebYSinOt()
    {
        await Handler().HandleAsync(Solicitud(TramiteCanal.PersonaNaturalWeb), Ct);

        var insertada = _insertadas.Should().ContainSingle().Which;
        insertada.Channel.Should().Be("pn_web");
        insertada.ChannelTransitOfficeId.Should().BeNull();
    }

    [Fact]
    public async Task AC3_PuntoDelOrganismo_ElTramiteNaceConCanalPnPuntoYElOtDelPunto()
    {
        var ot = Guid.NewGuid();

        await Handler().HandleAsync(Solicitud(TramiteCanal.PersonaNaturalOt, ot), Ct);

        var insertada = _insertadas.Should().ContainSingle().Which;
        insertada.Channel.Should().Be("pn_punto");
        insertada.ChannelTransitOfficeId.Should().Be(ot);
    }

    [Fact]
    public async Task AC4_Ict_ElTramiteNaceConCanalIntegracionYSinOt()
    {
        await Handler().HandleAsync(Solicitud(TramiteCanal.Integracion), Ct);

        var insertada = _insertadas.Should().ContainSingle().Which;
        insertada.Channel.Should().Be("integracion");
        insertada.ChannelTransitOfficeId.Should().BeNull();
    }

    // ── Defensa: combinaciones que la base rechazaría con un CHECK ───────────────────────────────

    [Theory]
    [InlineData("pn_punto", false)]  // pn_punto sin OT
    [InlineData("pn_web", true)]     // OT con un canal que no es pn_punto
    [InlineData("tramites", true)]
    [InlineData("integracion", true)]
    [InlineData("web", false)]       // fuera del catálogo
    [InlineData("TRAMITES", false)]
    [InlineData("", false)]
    public async Task UnCanalIncoherente_SeRechazaAntesDeInsertar(string canal, bool conOt)
    {
        var (resultado, error) = await Handler().HandleAsync(Solicitud(canal, conOt ? Guid.NewGuid() : null), Ct);

        error.Should().Be("invalid_request");
        resultado.Should().BeNull();
        _insertadas.Should().BeEmpty();
        await _repo.DidNotReceive().AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>());
    }

    // ── AC6: el cliente no impone el canal ───────────────────────────────────────────────────────

    [Fact]
    public void AC6_ElCuerpoJsonNoPuedeTraerElCanalNiElOtDelCanal()
    {
        var ot = Guid.NewGuid();
        var json = $$"""
            {
              "tenantId": "{{Guid.NewGuid()}}",
              "procedureTypeId": "{{Guid.NewGuid()}}",
              "createdByUserId": "{{Guid.NewGuid()}}",
              "channel": "pn_punto",
              "channelTransitOfficeId": "{{ot}}",
              "origin": "persona_natural"
            }
            """;

        var request = JsonSerializer.Deserialize<CreateProcedureInstanceRequest>(json, JsonSerializerOptions.Web);

        request.Should().NotBeNull();
        request!.Channel.Should().Be(TramiteCanal.Tramites, "el canal no se lee del JSON: lo fija el servidor");
        request.ChannelTransitOfficeId.Should().BeNull("el OT del canal tampoco se lee del JSON");
    }

    // ── AC10: el detalle trae el canal y el OT del canal ─────────────────────────────────────────

    [Theory]
    [InlineData(null, "tramites")]
    [InlineData("", "tramites")]
    [InlineData("tramites", "tramites")]
    [InlineData("pn_web", "pn_web")]
    [InlineData("integracion", "integracion")]
    [InlineData("aplicacion", "aplicacion")]
    public async Task AC10_ElDetalleTraeElCanal_YSinDatoSeLeeComoTramites(string? guardado, string esperado)
    {
        var instancia = Instancia(guardado, null);

        var (detalle, error) = await Detalle(instancia);

        error.Should().BeNull();
        detalle!.Channel.Should().Be(esperado);
        detalle.ChannelTransitOfficeId.Should().BeNull();
        detalle.ChannelTransitOfficeName.Should().BeNull();
        await _repo.DidNotReceive().GetTransitOfficeNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC10_ElDetalleDelPuntoTraeElOtDelCanalConSuNombre_AunqueElOrganismoDelTramiteSeaOtro()
    {
        var otDelPunto = Guid.NewGuid();
        var instancia = Instancia("pn_punto", otDelPunto);
        instancia.TransitOfficeId = Guid.NewGuid(); // el organismo del trámite cambió al enviarlo al OT
        _repo.GetTransitOfficeNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [otDelPunto] = "Organismo de prueba" });

        var (detalle, _) = await Detalle(instancia);

        detalle!.Channel.Should().Be("pn_punto");
        detalle.ChannelTransitOfficeId.Should().Be(otDelPunto);
        detalle.ChannelTransitOfficeName.Should().Be("Organismo de prueba");
    }

    [Fact]
    public async Task AC10_ElJsonDelDetalleExponeLosCuatroCamposDelCanal()
    {
        var ot = Guid.NewGuid();
        _repo.GetTransitOfficeNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [ot] = "Organismo de prueba" });
        var (detalle, _) = await Detalle(Instancia("pn_punto", ot));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detalle, JsonSerializerOptions.Web));

        json.RootElement.GetProperty("channel").GetString().Should().Be("pn_punto");
        json.RootElement.GetProperty("channelTransitOfficeId").GetGuid().Should().Be(ot);
        json.RootElement.GetProperty("channelTransitOfficeName").GetString().Should().Be("Organismo de prueba");
    }

    private async Task<(ProcedureInstanceDetailDto? Result, string? Error)> Detalle(ProcedureInstance instancia)
    {
        _repo.GetByIdWithDetailsAsync(instancia.Id, instancia.TenantId, Arg.Any<CancellationToken>()).Returns(instancia);
        return await new GetProcedureInstanceHandler(_repo).HandleAsync(instancia.Id, instancia.TenantId, Ct);
    }

    private static ProcedureInstance Instancia(string? canal, Guid? ot) => new()
    {
        ProcedureType = ProcedureTypeFixture.Matricula,
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
        Channel = canal,
        ChannelTransitOfficeId = ot,
    };
}
