using Flit.Modules.Quipux.Application.UseCases.ConsultarBandeja;
using Flit.Modules.Quipux.Domain.LogQx;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.ConsultarBandeja;

/// <summary>
/// Bandeja del LOG QX (HU #11786) sobre <see cref="ConsultarBandejaQuipuxHandler"/> con un
/// repositorio espía: se verifica la lógica que vive EN EL HANDLER — normalización de paginación,
/// ventana por defecto, tolerancia a filtros inválidos y cálculo de la espera.
/// </summary>
/// <remarks>
/// La consulta en sí NO se cubre aquí: <see cref="DbQuipuxBandejaRepository"/> usa SQL crudo porque
/// el predicado de elegibilidad depende del jsonb <c>external_refs</c>, y sobre EF InMemory devuelve
/// vacío a propósito. Los AC1 a AC3 (agregación por trámite, intentos, <c>sin_radicar</c> y la
/// antigüedad derivada de <c>procedure_instance_status_history</c>) se validan contra Postgres.
/// </remarks>
public sealed class ConsultarBandejaQuipuxTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Reloj fijo. Se escribe a mano en vez de traer <c>Microsoft.Extensions.TimeProvider.Testing</c>:
    /// ese paquete no está en la gestión central de versiones del repo, y añadirlo por un test sería
    /// tooling nuevo para algo que resuelven cinco líneas (misma decisión que en Flit.Admin.Tests).
    /// </summary>
    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    /// <summary>Repositorio espía: captura la query de dominio y devuelve lo que se le prepare.</summary>
    private sealed class SpyRepository : IQuipuxBandejaRepository
    {
        public QuipuxBandejaQuery? Recibida { get; private set; }

        public QuipuxBandejaPage Respuesta { get; set; } = new([], 0, []);

        public Task<QuipuxBandejaPage> SearchAsync(
            QuipuxBandejaQuery query, CancellationToken cancellationToken = default)
        {
            Recibida = query;
            return Task.FromResult(Respuesta);
        }

        // El catálogo del desplegable no pasa por este handler: lo sirve el endpoint directamente.
        public Task<IReadOnlyList<QuipuxTipoTramiteOpcion>> ListProcedureTypesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QuipuxTipoTramiteOpcion>>([]);
    }

    private static (ConsultarBandejaQuipuxHandler Handler, SpyRepository Repo) NewHandler()
    {
        var repo = new SpyRepository();
        var clock = new RelojFijo(Ahora);
        return (new ConsultarBandejaQuipuxHandler(repo, clock), repo);
    }

    private static ConsultarBandejaQuipuxQuery Query(
        DateTimeOffset? desde = null,
        DateTimeOffset? hasta = null,
        string? placa = null,
        string? instanceId = null,
        string? referencia = null,
        string? documento = null,
        string? estado = null,
        string? transitOfficeId = null,
        string? tenantId = null,
        string? procedureTypeId = null,
        string? familia = null,
        int? page = null,
        int? pageSize = null) =>
        new(desde, hasta, placa, instanceId, referencia, documento, estado,
            transitOfficeId, tenantId, procedureTypeId, familia, page, pageSize);

    private static QuipuxBandejaEntry Entry(
        string estado, DateTimeOffset? esperandoDesde = null, string? documento = null) =>
        new()
        {
            ProcedureInstanceId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000271",
            Plate = "ABC123",
            ProcedureTypeName = "Matrícula inicial",
            Estado = estado,
            ClientTenantName = "AutoFlota Antioquia S.A.S",
            TransitOfficeName = "Ibagué",
            DocumentoQx = documento,
            EsperandoDesde = esperandoDesde,
        };

    // ── AC1 — la bandeja responde sin filtros ────────────────────────────────

    [Fact]
    public async Task AC1_sin_filtros_aplica_la_ventana_por_defecto_y_no_exige_busqueda()
    {
        var (handler, repo) = NewHandler();

        var result = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        // Se consulta igual: la pantalla debe cargar con datos sin que nadie teclee nada.
        repo.Recibida.Should().NotBeNull();
        repo.Recibida!.Desde.Should().Be(Ahora.AddDays(-ConsultarBandejaQuipuxHandler.DefaultDiasVentana));
        repo.Recibida.Hasta.Should().BeNull();
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(ConsultarBandejaQuipuxHandler.DefaultPageSize);
    }

    [Fact]
    public async Task AC1_un_desde_explicito_respeta_el_rango_pedido()
    {
        var (handler, repo) = NewHandler();
        var desde = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(Query(desde: desde), TestContext.Current.CancellationToken);

        repo.Recibida!.Desde.Should().Be(desde);
    }

    [Fact]
    public async Task AC1_un_hasta_sin_desde_no_impone_la_ventana_por_defecto()
    {
        // Pedir "todo hasta tal fecha" es una consulta legítima: la ventana por defecto la
        // truncaría en silencio a los últimos 30 días y devolvería menos de lo pedido.
        var (handler, repo) = NewHandler();
        var hasta = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(Query(hasta: hasta), TestContext.Current.CancellationToken);

        repo.Recibida!.Desde.Should().BeNull();
        repo.Recibida.Hasta.Should().Be(hasta);
    }

    // ── AC4 — filtros combinables ────────────────────────────────────────────

    [Fact]
    public async Task AC4_todos_los_filtros_viajan_juntos_al_repositorio()
    {
        var (handler, repo) = NewHandler();
        var office = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var tipo = Guid.NewGuid();

        await handler.HandleAsync(
            Query(placa: " abc123 ", estado: QuipuxBandejaEstados.EnTramite,
                  transitOfficeId: office.ToString(), tenantId: tenant.ToString(),
                  procedureTypeId: tipo.ToString(), referencia: " TRM-271 "),
            TestContext.Current.CancellationToken);

        var q = repo.Recibida!;
        q.Placa.Should().Be("abc123");        // recortado; el repositorio normaliza a mayúsculas
        q.Estado.Should().Be(QuipuxBandejaEstados.EnTramite);
        q.TransitOfficeId.Should().Be(office);
        q.TenantId.Should().Be(tenant);
        q.ProcedureTypeId.Should().Be(tipo);
        q.ReferenceNumber.Should().Be("TRM-271");
    }

    // ── AC5 — documento QX ───────────────────────────────────────────────────

    [Fact]
    public async Task AC5_el_documento_viaja_como_filtro_y_vuelve_en_la_fila()
    {
        const string documento = "TESLA_MI_20260811_1220_LRWYGCFJ3TC767907";
        var (handler, repo) = NewHandler();
        repo.Respuesta = new QuipuxBandejaPage(
            [Entry(QuipuxBandejaEstados.EnTramite, documento: documento)], 1, []);

        var result = await handler.HandleAsync(
            Query(documento: "LRWYGCFJ3TC767907"), TestContext.Current.CancellationToken);

        repo.Recibida!.DocumentoQx.Should().Be("LRWYGCFJ3TC767907");
        result.Data.Should().ContainSingle().Which.DocumentoQx.Should().Be(documento);
    }

    // ── Filtro por tipo de trámite y por familia ─────────────────────────────

    [Fact]
    public async Task La_familia_viaja_normalizada_a_su_forma_canonica()
    {
        var (handler, repo) = NewHandler();

        await handler.HandleAsync(
            Query(familia: "  traspaso  "), TestContext.Current.CancellationToken);

        repo.Recibida!.Family.Should().Be("TRASPASO");
    }

    [Fact]
    public async Task Una_familia_inexistente_se_descarta_en_vez_de_vaciar_la_bandeja()
    {
        // Mandarla al repositorio devolvería cero filas, y la bandeja vacía se leería como «no hay
        // trámites de esa familia» cuando lo que pasó es que el valor no existe. Descartarla enseña
        // de más, y que sobren filas se nota; que falten, no.
        var (handler, repo) = NewHandler();

        await handler.HandleAsync(
            Query(familia: "VEHICULAR"), TestContext.Current.CancellationToken);

        repo.Recibida!.Family.Should().BeNull();
    }

    [Fact]
    public async Task El_tipo_concreto_y_la_familia_conviven_en_la_misma_consulta()
    {
        // El desplegable ofrece los dos niveles en un solo control, así que el handler tiene que
        // saber transportar ambos sin que uno anule al otro.
        var tipo = Guid.NewGuid();
        var (handler, repo) = NewHandler();

        await handler.HandleAsync(
            Query(procedureTypeId: tipo.ToString(), familia: "MATRICULAS"),
            TestContext.Current.CancellationToken);

        repo.Recibida!.ProcedureTypeId.Should().Be(tipo);
        repo.Recibida.Family.Should().Be("MATRICULAS");
    }

    // ── AC6 — contadores ─────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_los_contadores_del_repositorio_se_devuelven_tal_cual()
    {
        var (handler, repo) = NewHandler();
        repo.Respuesta = new QuipuxBandejaPage(
            [Entry(QuipuxBandejaEstados.Aprobado)],
            1,
            [new QuipuxBandejaContador(QuipuxBandejaEstados.Aprobado, 12),
             new QuipuxBandejaContador(QuipuxBandejaEstados.Fallido, 0)]);

        var result = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        result.Contadores.Should().HaveCount(2);
        result.Contadores.Should().ContainEquivalentOf(
            new QuipuxBandejaContadorView(QuipuxBandejaEstados.Aprobado, 12));
    }

    // ── AC7 — filtros que no casan con nada ──────────────────────────────────

    [Theory]
    [InlineData("no-es-un-uuid")]
    [InlineData("12345")]
    public async Task AC7_un_identificador_invalido_devuelve_vacio_sin_consultar(string instanceId)
    {
        var (handler, repo) = NewHandler();

        var result = await handler.HandleAsync(
            Query(instanceId: instanceId), TestContext.Current.CancellationToken);

        // No se ignora el filtro: ignorarlo mostraría resultados que nadie pidió.
        repo.Recibida.Should().BeNull();
        result.Data.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task AC7_un_estado_desconocido_devuelve_vacio_sin_consultar()
    {
        var (handler, repo) = NewHandler();

        var result = await handler.HandleAsync(
            Query(estado: "en_veremos"), TestContext.Current.CancellationToken);

        repo.Recibida.Should().BeNull();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task AC7_la_pagina_vacia_trae_los_siete_contadores_en_cero()
    {
        // Que falte el contador de "Fallido" obliga a adivinar si vale cero o si no se calculó.
        var (handler, _) = NewHandler();

        var result = await handler.HandleAsync(
            Query(estado: "inexistente"), TestContext.Current.CancellationToken);

        result.Contadores.Should().HaveCount(QuipuxBandejaEstados.Todos.Count);
        result.Contadores.Should().OnlyContain(c => c.Total == 0);
        result.Contadores.Select(c => c.Estado)
            .Should().BeEquivalentTo(QuipuxBandejaEstados.Todos);
    }

    // ── Antigüedad ───────────────────────────────────────────────────────────

    [Fact]
    public async Task La_espera_se_calcula_en_servidor_y_no_depende_del_reloj_del_navegador()
    {
        var (handler, repo) = NewHandler();
        repo.Respuesta = new QuipuxBandejaPage(
            [Entry(QuipuxBandejaEstados.EnTramite, esperandoDesde: Ahora.AddHours(-52))], 1, []);

        var result = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        result.Data.Single().HorasEsperando.Should().BeApproximately(52d, 0.001);
    }

    [Fact]
    public async Task Un_tramite_ya_resuelto_no_acumula_espera()
    {
        var (handler, repo) = NewHandler();
        repo.Respuesta = new QuipuxBandejaPage(
            [Entry(QuipuxBandejaEstados.Aprobado, esperandoDesde: null)], 1, []);

        var result = await handler.HandleAsync(Query(), TestContext.Current.CancellationToken);

        result.Data.Single().HorasEsperando.Should().BeNull();
    }

    // ── Paginación ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public async Task La_pagina_se_normaliza(int pedida, int esperada)
    {
        var (handler, repo) = NewHandler();

        var result = await handler.HandleAsync(
            Query(page: pedida), TestContext.Current.CancellationToken);

        result.Page.Should().Be(esperada);
        repo.Recibida!.Page.Should().Be(esperada);
    }

    [Theory]
    [InlineData(0, ConsultarBandejaQuipuxHandler.DefaultPageSize)]
    [InlineData(50, 50)]
    [InlineData(9999, ConsultarBandejaQuipuxHandler.MaxPageSize)]
    public async Task El_tamano_de_pagina_se_acota(int pedido, int esperado)
    {
        var (handler, repo) = NewHandler();

        var result = await handler.HandleAsync(
            Query(pageSize: pedido), TestContext.Current.CancellationToken);

        result.PageSize.Should().Be(esperado);
        repo.Recibida!.PageSize.Should().Be(esperado);
    }
}
