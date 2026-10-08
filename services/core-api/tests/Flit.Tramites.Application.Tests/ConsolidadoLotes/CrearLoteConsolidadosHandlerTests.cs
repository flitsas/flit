using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13373 (épica #13216) — <see cref="CrearLoteConsolidadosHandler"/> con dobles: validaciones (AC4), motor apagado
/// (AC8), lote activo (AC2), traducción de «no creado» (AC3), defensa de tenant (AC6) y armado del lote nuevo (AC1).
/// La atomicidad real y la purga (AC1/AC3/AC5/AC7) se prueban contra PostgreSQL en
/// <c>CrearLoteConsolidadosIntegrationTests</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var sut = new CrearLoteConsolidadosHandler(repo, new LoteSeleccionResolverPorOrigen([resolver]), cipher);
/// var r = await sut.HandleAsync(new CrearLoteConsolidadosCommand { Origen = "tramites", ... }, ct);
/// </code>
/// </remarks>
public sealed class CrearLoteConsolidadosHandlerTests
{
    private static readonly Guid TenantC = Guid.NewGuid();
    private static readonly Guid TenantD = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Organismo = Guid.NewGuid();
    private static readonly byte[] DekEnvuelta = [9, 8, 7, 6];

    private readonly FakeRepo _repo = new();
    private readonly FakeResolver _tramites = new(ConsolidadoExportOrigin.Tramites);
    private readonly FakeResolver _ot = new(ConsolidadoExportOrigin.OtBandeja);
    private readonly FakeResolver _sa = new(ConsolidadoExportOrigin.Superadmin);
    private readonly CrearLoteConsolidadosHandler _sut;

    public CrearLoteConsolidadosHandlerTests()
    {
        _sut = new CrearLoteConsolidadosHandler(
            _repo, new LoteSeleccionResolverPorOrigen([_tramites, _ot, _sa]), new FakeCipher());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<ProcedureInstanceRef> Refs(int n, Guid tenant, string prefijo = "R") =>
        Enumerable.Range(1, n).Select(i => new ProcedureInstanceRef(Guid.NewGuid(), tenant, $"{prefijo}{i:D5}", $"AB{i:D4}")).ToList();

    private static CrearLoteConsolidadosCommand Gestor(LoteSeleccion? seleccion = null, bool? confirma = true, string? tipo = null) => new()
    {
        Origen = ConsolidadoExportOrigin.Tramites,
        TenantId = TenantC,
        UsuarioId = Usuario,
        RolCodigo = "Radicador",
        TipoDocumento = tipo,
        Seleccion = seleccion ?? new SeleccionPorIds([Guid.NewGuid()]),
        ConfirmaEfectos = confirma,
    };

    // ── AC1 — creación correcta ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_FiltroMenosDosExcluidos_CongelaLos348EnOrden_ConDekEnvueltaYResumenMinimizado()
    {
        var refs = Refs(348, TenantC);
        _tramites.Devuelve = refs;
        var seleccion = new SeleccionPorFiltro(
            new TramitesLoteFiltro(new ProcedureInstanceListRequest { Placa = "ABC", Estados = ["radicado"] }),
            [Guid.NewGuid(), Guid.NewGuid()]);

        var r = await _sut.HandleAsync(Gestor(seleccion), Ct);

        r.Creado.Should().BeTrue();
        r.Lote!.Status.Should().Be(ConsolidadoExportStatus.EnCola);
        var nuevo = _repo.Creado!;
        nuevo.Items.Select(i => i.Id).Should().Equal(refs.Select(x => x.Id), "orden del listado");
        nuevo.TenantId.Should().Be(TenantC);
        nuevo.UsuarioId.Should().Be(Usuario);
        nuevo.RolCodigo.Should().Be("Radicador");
        nuevo.Origen.Should().Be(ConsolidadoExportOrigin.Tramites);
        nuevo.TipoDocumento.Should().Be(ConsolidadoExportDocumentType.Consolidado, "tipo por defecto del origen tramites");
        nuevo.ModoSeleccion.Should().Be(ConsolidadoExportSelectionMode.Filtro);
        nuevo.DekEnvuelta.Should().Equal(DekEnvuelta, "la DEK llega envuelta por el cipher, nunca en claro");
        nuevo.ExcluidosCount.Should().Be(2);
        nuevo.IdsCount.Should().BeNull();
        nuevo.ResumenFiltroJson.Should().Contain("\"excluidos\":{\"cantidad\":2}").And.NotContain("ABC");
        _tramites.Contexto.Should().Be(new LoteSeleccionContexto(TenantC, Usuario, null), "tenant y usuario del token");
    }

    [Fact]
    public async Task L3_TipoCodigoDelCatalogo_LlegaLiteralAlResumen_YSinTipoCodigoNoSeConsultaElCatalogo()
    {
        var tipos = Substitute.For<IProcedureTypeRepository>();
        tipos.ListAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(new List<Flit.Tramites.Domain.Entities.ProcedureType> { new() { Code = "TRASPASO_STANDARD" } });
        var resolvers = new LoteSeleccionResolverPorOrigen([_tramites, _ot, _sa]);
        var sut = new CrearLoteConsolidadosHandler(_repo, resolvers, new FakeCipher(), tiposDeTramite: tipos);
        _tramites.Devuelve = Refs(1, TenantC);

        var conTipo = await sut.HandleAsync(Gestor(new SeleccionPorFiltro(
            new TramitesLoteFiltro(new ProcedureInstanceListRequest { TipoCodigo = "traspaso_standard" }))), Ct);

        conTipo.Creado.Should().BeTrue();
        _repo.Creado!.ResumenFiltroJson.Should().Contain("\"tipoCodigo\":\"TRASPASO_STANDARD\"");
        await tipos.Received(1).ListAsync(null, null, Arg.Any<CancellationToken>());

        tipos.ClearReceivedCalls();
        var otroRepo = new FakeRepo();
        var sinTipo = await new CrearLoteConsolidadosHandler(otroRepo, resolvers, new FakeCipher(), tiposDeTramite: tipos).HandleAsync(Gestor(new SeleccionPorFiltro(
            new TramitesLoteFiltro(new ProcedureInstanceListRequest { Placa = "ABC" }))), Ct);

        sinTipo.Creado.Should().BeTrue();
        await tipos.DidNotReceiveWithAnyArgs().ListAsync(default, default, Ct);
    }

    [Fact]
    public async Task AC1_IdsDuplicados_DelResolver_SeCongelanUnaSolaVez()
    {
        var refs = Refs(3, TenantC);
        _tramites.Devuelve = [.. refs, refs[1]];

        var r = await _sut.HandleAsync(Gestor(new SeleccionPorIds(refs.Select(x => x.Id).ToList())), Ct);

        r.Creado.Should().BeTrue();
        _repo.Creado!.Items.Should().HaveCount(3, "un trámite por lote (uq_..._batch_instance)");
        _repo.Creado.ModoSeleccion.Should().Be(ConsolidadoExportSelectionMode.Ids);
        _repo.Creado.IdsCount.Should().Be(3);
    }

    // ── AC2 — lote activo ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_ConLoteActivo_DevuelveLoteActivoConSuId_SinResolverNiCrear()
    {
        var activo = Guid.NewGuid();
        _repo.Activo = activo;

        var r = await _sut.HandleAsync(Gestor(), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteActivo);
        r.LoteActivoId.Should().Be(activo);
        r.Lote.Should().BeNull();
        _tramites.Llamadas.Should().Be(0);
        _repo.Creado.Should().BeNull();
    }

    [Fact]
    public async Task AC2_CarreraDetectadaPorElRepositorio_TambienEsLoteActivo()
    {
        var activo = Guid.NewGuid();
        _tramites.Devuelve = Refs(1, TenantC);
        _repo.Resultado = new CrearLoteResultado(CrearLoteEstado.LoteActivo, LoteActivoId: activo);

        var r = await _sut.HandleAsync(Gestor(), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteActivo);
        r.LoteActivoId.Should().Be(activo);
    }

    // ── AC3 — la transacción falla ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_SiElRepositorioNoCrea_DevuelveLoteNoCreado()
    {
        _tramites.Devuelve = Refs(2, TenantC);
        _repo.Resultado = new CrearLoteResultado(CrearLoteEstado.NoCreado);

        var r = await _sut.HandleAsync(Gestor(), Ct);

        r.Creado.Should().BeFalse();
        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteNoCreado);
        r.Mensaje.Should().Be(CrearLoteConsolidadosHandler.MensajeNoDisponible);
    }

    // ── AC4 — confirmación y tipo ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task AC4_SinConfirmacion_DevuelveConfirmacionRequerida_YNoTocaLaBase(bool? confirma)
    {
        var r = await _sut.HandleAsync(Gestor(confirma: confirma), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.ConfirmacionRequerida);
        _repo.Llamadas.Should().Be(0);
        _tramites.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task AC4_MaestroEnOrigenTramites_EsTipoNoPermitido()
    {
        var r = await _sut.HandleAsync(Gestor(tipo: ConsolidadoExportDocumentType.ConsolidadoMaestro), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.TipoNoPermitido);
        _repo.Llamadas.Should().Be(0);
    }

    [Theory]
    [InlineData(ConsolidadoExportOrigin.Tramites, ConsolidadoExportDocumentType.Consolidado, true)]
    [InlineData(ConsolidadoExportOrigin.Tramites, ConsolidadoExportDocumentType.ConsolidadoMaestro, false)]
    [InlineData(ConsolidadoExportOrigin.Superadmin, ConsolidadoExportDocumentType.Consolidado, true)]
    [InlineData(ConsolidadoExportOrigin.Superadmin, ConsolidadoExportDocumentType.ConsolidadoMaestro, true)]
    [InlineData(ConsolidadoExportOrigin.OtBandeja, ConsolidadoExportDocumentType.ConsolidadoMaestro, true)]
    [InlineData(ConsolidadoExportOrigin.OtBandeja, ConsolidadoExportDocumentType.Consolidado, false)]
    [InlineData(ConsolidadoExportOrigin.Tramites, "otro", false)]
    public void AC4_ContratoDeTipoPorOrigen(string origen, string tipo, bool permitido) =>
        CrearLoteConsolidadosHandler.TipoPermitido(origen, tipo).Should().Be(permitido);

    [Fact]
    public async Task AC4_SinSeleccion_DevuelveSeleccionRequerida()
    {
        var r = await _sut.HandleAsync(Gestor() with { Seleccion = null }, Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.SeleccionRequerida);
    }

    [Fact]
    public async Task Tope_10001Ids_EsSeleccionExcedeTope_InclusoEnLaBandejaOt()
    {
        var ids = Enumerable.Range(0, LoteSeleccionTopes.MaxIds + 1).Select(_ => Guid.NewGuid()).ToList();
        var cmd = Gestor(new SeleccionPorIds(ids), tipo: ConsolidadoExportDocumentType.ConsolidadoMaestro) with
        {
            Origen = ConsolidadoExportOrigin.OtBandeja,
            OtTransitOfficeId = Organismo,
        };

        var r = await _sut.HandleAsync(cmd, Ct);

        r.Error.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        _ot.Llamadas.Should().Be(0);
        _repo.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task FiltroInvalidoDelResolver_SePropagaComoCodigo()
    {
        _tramites.Lanza = new LoteSeleccionInvalidaException(LoteSeleccionInvalidaException.CodigoFiltroInvalido, "campo x");

        var r = await _sut.HandleAsync(Gestor(), Ct);

        r.Error.Should().Be(LoteSeleccionInvalidaException.CodigoFiltroInvalido);
        _repo.Creado.Should().BeNull();
    }

    [Fact]
    public async Task SinCompania_EnOrigenTramites_FallaCerrado()
    {
        var r = await _sut.HandleAsync(Gestor() with { TenantId = null }, Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.SinCompania);
        _repo.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task SuperAdminConCompania_EsErrorDelLlamador()
    {
        var cmd = Gestor() with { Origen = ConsolidadoExportOrigin.Superadmin, TenantId = TenantC };

        var act = () => _sut.HandleAsync(cmd, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── AC6 — tenant del JWT ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_RefsDeOtraCompania_NoEntranNiCuentan()
    {
        var propios = Refs(3, TenantC);
        _tramites.Devuelve = [propios[0], .. Refs(2, TenantD, "D"), propios[1], propios[2]];

        var r = await _sut.HandleAsync(Gestor(), Ct);

        r.Creado.Should().BeTrue();
        _repo.Creado!.Items.Should().OnlyContain(i => i.TenantId == TenantC).And.HaveCount(3);
    }

    [Fact]
    public async Task SuperAdminAcotado_SoloCongelaLaCompaniaDelScope_YElLoteNoLlevaCompania()
    {
        _sa.Devuelve = [.. Refs(2, TenantC), .. Refs(1, TenantD, "D")];
        var cmd = Gestor() with { Origen = ConsolidadoExportOrigin.Superadmin, TenantId = null, ScopeTenantId = TenantC, RolCodigo = "SuperAdmin" };

        var r = await _sut.HandleAsync(cmd, Ct);

        r.Creado.Should().BeTrue();
        _repo.Creado!.TenantId.Should().BeNull();
        _repo.Creado.ScopeTenantId.Should().Be(TenantC);
        _repo.Creado.Items.Should().HaveCount(2);
        _sa.Contexto!.TenantId.Should().Be(TenantC, "el Super Admin resuelve con el scope, no con una compañía propia");
    }

    [Fact]
    public async Task BandejaOt_CongelaLasCompaniasCliente_YAuditaElOrganismo()
    {
        _ot.Devuelve = [.. Refs(2, TenantC), .. Refs(2, TenantD, "D")];
        var cmd = Gestor() with { Origen = ConsolidadoExportOrigin.OtBandeja, OtTransitOfficeId = Organismo, RolCodigo = "ot_admin" };

        var r = await _sut.HandleAsync(cmd, Ct);

        r.Creado.Should().BeTrue();
        _repo.Creado!.Items.Should().HaveCount(4);
        _repo.Creado.TipoDocumento.Should().Be(ConsolidadoExportDocumentType.ConsolidadoMaestro);
        _repo.Creado.OtTransitOfficeId.Should().Be(Organismo);
        _repo.Creado.ResumenFiltroJson.Should().Contain(Organismo.ToString("D"));
        _ot.Contexto.Should().Be(new LoteSeleccionContexto(TenantC, Usuario, Organismo));
    }

    // ── AC8 — motor apagado ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC8_MotorInactivo_DevuelveMotorInactivo_SinResolverNiCrear()
    {
        _repo.Settings = new ConsolidadoExportSettings { IsActive = false };
        _repo.Activo = Guid.NewGuid();

        var r = await _sut.HandleAsync(Gestor(), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.MotorInactivo);
        r.Mensaje.Should().Be("No se pudo completar la descarga, intente de nuevo");
        _tramites.Llamadas.Should().Be(0);
        _repo.Creado.Should().BeNull("no se crea ni se purga nada");
    }

    [Fact]
    public async Task AC8_SinFilaDeParametros_FallaCerrado()
    {
        _repo.Settings = null;

        var r = await _sut.HandleAsync(Gestor(), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.MotorInactivo);
    }

    // ── M1 — tope total configurable (max_items_per_batch) ─────────────────────────────────

    private static CrearLoteConsolidadosCommand DelOrigen(string origen, LoteSeleccion seleccion) => origen switch
    {
        ConsolidadoExportOrigin.Superadmin => Gestor(seleccion) with
        {
            Origen = origen,
            TenantId = null,
            ScopeTenantId = null,
            RolCodigo = "SuperAdmin",
        },
        ConsolidadoExportOrigin.OtBandeja => Gestor(seleccion, tipo: ConsolidadoExportDocumentType.ConsolidadoMaestro) with
        {
            Origen = origen,
            OtTransitOfficeId = Organismo,
            RolCodigo = "ot_admin",
        },
        _ => Gestor(seleccion),
    };

    private FakeResolver ResolverDe(string origen) => origen switch
    {
        ConsolidadoExportOrigin.Superadmin => _sa,
        ConsolidadoExportOrigin.OtBandeja => _ot,
        _ => _tramites,
    };

    [Theory]
    [InlineData(ConsolidadoExportOrigin.Tramites)]
    [InlineData(ConsolidadoExportOrigin.Superadmin)]
    [InlineData(ConsolidadoExportOrigin.OtBandeja)]
    public async Task M1_ModoFiltro_SeleccionResueltaSuperaElTope_ExcedeTopeConTotalYTope_SinCrearNada(string origen)
    {
        _repo.Settings = new ConsolidadoExportSettings { IsActive = true, MaxItemsPerBatch = 5 };
        ResolverDe(origen).Devuelve = Refs(6, TenantC);
        var seleccion = new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest()));

        var r = await _sut.HandleAsync(DelOrigen(origen, seleccion), Ct);

        r.Creado.Should().BeFalse();
        r.Error.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        r.Total.Should().Be(6);
        r.Tope.Should().Be(5);
        r.Mensaje.Should().Contain("6").And.Contain("5");
        _repo.Creado.Should().BeNull("ni lote, ni ítems, ni auditoría lote_creado");
        _repo.CrearLlamadas.Should().Be(0);
    }

    [Fact]
    public async Task M1_ModoIds_SeleccionResueltaSuperaElTope_ExcedeTope_SinCrear()
    {
        _repo.Settings = new ConsolidadoExportSettings { IsActive = true, MaxItemsPerBatch = 3 };
        var refs = Refs(4, TenantC);
        _tramites.Devuelve = refs;

        var r = await _sut.HandleAsync(Gestor(new SeleccionPorIds(refs.Select(x => x.Id).ToList())), Ct);

        r.Error.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        r.Total.Should().Be(4);
        r.Tope.Should().Be(3);
        _repo.CrearLlamadas.Should().Be(0);
    }

    [Fact]
    public async Task M1_ExclusionesEInterseccionDeSeguridadBajanDelTope_SeCrea()
    {
        // El filtro abarcaría 9, pero el resolver ya quitó 2 excluidos y 3 son de otra compañía (CF-15): quedan 4.
        _repo.Settings = new ConsolidadoExportSettings { IsActive = true, MaxItemsPerBatch = 4 };
        var propios = Refs(4, TenantC);
        _tramites.Devuelve = [.. propios, .. Refs(3, TenantD, "D")];
        var seleccion = new SeleccionPorFiltro(
            new TramitesLoteFiltro(new ProcedureInstanceListRequest()), [Guid.NewGuid(), Guid.NewGuid()]);

        var r = await _sut.HandleAsync(Gestor(seleccion), Ct);

        r.Creado.Should().BeTrue(r.Mensaje);
        r.Total.Should().BeNull();
        _repo.Creado!.Items.Select(i => i.Id).Should().Equal(propios.Select(p => p.Id));
    }

    [Fact]
    public async Task M1_TopeExactamenteIgual_SeCrea()
    {
        _repo.Settings = new ConsolidadoExportSettings { IsActive = true, MaxItemsPerBatch = 5 };
        var refs = Refs(5, TenantC);
        _tramites.Devuelve = [.. refs, refs[0]];

        var r = await _sut.HandleAsync(Gestor(new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest()))), Ct);

        r.Creado.Should().BeTrue(r.Mensaje);
        _repo.Creado!.Items.Should().HaveCount(5, "el duplicado no cuenta contra el tope");
    }

    [Fact]
    public void M1_ElTopePorDefectoDeLaEntidadEsElDelDdl() =>
        new ConsolidadoExportSettings().MaxItemsPerBatch.Should().Be(10_000);

    // ── Dobles ────────────────────────────────────────────────────────────────────────────

    private sealed class FakeRepo : IConsolidadoLoteRepository
    {
        public ConsolidadoExportSettings? Settings { get; set; } = new() { IsActive = true };
        public Guid? Activo { get; set; }
        public CrearLoteResultado? Resultado { get; set; }
        public NuevoLoteConsolidados? Creado { get; private set; }
        public int Llamadas { get; private set; }
        public int CrearLlamadas { get; private set; }

        public Task<ConsolidadoExportSettings?> ObtenerSettingsAsync(CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(Settings);
        }

        public Task<Guid?> ObtenerLoteActivoIdAsync(Guid usuarioId, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(Activo);
        }

        public Task<CrearLoteResultado> CrearAsync(NuevoLoteConsolidados nuevo, CancellationToken ct = default)
        {
            Llamadas++;
            CrearLlamadas++;
            if (Resultado is { Estado: not CrearLoteEstado.Creado } r)
                return Task.FromResult(r);
            Creado = nuevo;
            return Task.FromResult(new CrearLoteResultado(CrearLoteEstado.Creado, new ConsolidadoExportBatch
            {
                Id = Guid.NewGuid(),
                Status = ConsolidadoExportStatus.EnCola,
                TotalItems = nuevo.Items.Count,
                DekWrapped = nuevo.DekEnvuelta,
            }));
        }

        public Task<bool> PurgarAsync(Guid loteId, DateTimeOffset ahora, CancellationToken ct = default) =>
            throw new NotSupportedException();

        // HU #13376: el carril de ítems no participa en la creación del lote.
        public Task<ItemLoteReclamado?> ReclamarSiguienteItemAsync(string reclamante, int leaseSegundos, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> IniciarLotesSinItemsAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ObtenerLotesConCarrilTerminadoAsync(int maximo, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CierreCarrilResultado> CerrarCarrilAsync(Guid loteId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CancelarLoteResultado> CancelarAsync(CancelacionLote solicitud, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeResolver(string origen) : ILoteSeleccionResolver
    {
        public string Origen => origen;
        public IReadOnlyList<ProcedureInstanceRef> Devuelve { get; set; } = [];
        public LoteSeleccionInvalidaException? Lanza { get; set; }
        public LoteSeleccionContexto? Contexto { get; private set; }
        public int Llamadas { get; private set; }

        public Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
            LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default)
        {
            Llamadas++;
            Contexto = contexto;
            if (Lanza is not null)
                throw Lanza;
            return Task.FromResult(Devuelve);
        }
    }

    private sealed class FakeCipher : IConsolidadoLoteCipher
    {
        public byte[] GenerarDekEnvuelta() => [.. DekEnvuelta];

        public Task<ConsolidadoLoteCifradoResultado> CifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenEnClaro, Stream destinoCifrado, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<long> DescifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenCifrado, Stream destinoEnClaro, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
