using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Queries.Domain.Documentos;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12791 (Épica #12760) — estado de vigencia y sello de tiempo de los consolidados en los
/// contratos de API del gestor (detalle, vista de red y listado).
/// <para>Uso de ejemplo:
/// <c>var (dto, _) = await new GetProcedureInstanceHandler(repo).HandleAsync(id, tenantId, ct);</c>
/// ⇒ <c>dto.ConsolidadoWizard.Estado == "vigente"</c>, <c>dto.ConsolidadoMaestro.Estado == "desactualizado"</c>.</para>
/// </summary>
public sealed class ConsolidadoVigenciaContratoTests
{
    private static readonly DateTimeOffset SelloWizard = new(2026, 9, 20, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SelloMaestro = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    // ── AC1 — detalle: wizard vigente + maestro desactualizado ───────────────────────────────

    [Fact]
    public async Task AC1_Detalle_WizardVigenteYMaestroDesactualizado_ExponeEstadoYSelloDeCadaUno()
    {
        var instance = Tramite(TramiteEstado.Entregado);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoWizardGeneradoEn = SelloWizard;
        instance.ConsolidadoMaestroVigente = false;
        instance.ConsolidadoMaestroGeneradoEn = SelloMaestro;
        PrepararDetalle(instance, Sources(("consolidado", "system"), ("consolidado_maestro", "system")));

        var (dto, error) = await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        dto!.ConsolidadoWizard.Should().Be(new ConsolidadoVigenciaDto("vigente", SelloWizard, "system", false, null));
        dto.ConsolidadoMaestro.Should().Be(new ConsolidadoVigenciaDto("desactualizado", SelloMaestro, "system", false, null));
    }

    [Fact]
    public async Task AC1_Detalle_SerializaElSubObjetoEnCamelCaseConSelloIso()
    {
        var instance = Tramite(TramiteEstado.Entregado);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoWizardGeneradoEn = SelloWizard;
        PrepararDetalle(instance, Sources(("consolidado", "system"), ("consolidado_maestro", "system")));

        var (dto, _) = await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, Web));
        var wizard = json.RootElement.GetProperty("consolidadoWizard");
        wizard.GetProperty("estado").GetString().Should().Be("vigente");
        wizard.GetProperty("generadoEn").GetDateTimeOffset().Should().Be(SelloWizard);
        wizard.GetProperty("origen").GetString().Should().Be("system");
        wizard.GetProperty("definitivo").GetBoolean().Should().BeFalse();
        wizard.GetProperty("modo").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("consolidadoMaestro").GetProperty("estado").GetString().Should().Be("desactualizado");
    }

    [Fact]
    public async Task AC1_VistaDeRed_TraeLosMismosCamposQueElDetallePropio()
    {
        // BuildDetailAsync es el ÚNICO armador del detalle (propio y red): si la vigencia se calculara
        // solo en el handler propio, la vista de red dejaría de ser equivalente.
        var instance = Tramite(TramiteEstado.Entregado);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoWizardGeneradoEn = SelloWizard;
        var scope = TenantScope.Group(Guid.NewGuid(), [instance.TenantId], GroupKind.Concesion);
        _repo.GetByIdWithDetailsAsync(instance.Id, scope, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetConsolidadoSourcesAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(Sources(("consolidado", "system")));

        var (red, _) = await new NetworkGetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, scope, TestContext.Current.CancellationToken);

        red!.Instance.ConsolidadoWizard!.Estado.Should().Be("vigente");
        red.Instance.ConsolidadoMaestro!.Estado.Should().Be("inexistente");
    }

    // ── AC2 — sin consolidado maestro ⇒ inexistente, sello null ──────────────────────────────

    [Fact]
    public async Task AC2_SinAdjuntoMaestro_Inexistente_AunqueLaBanderaYElSelloDigan_Otra_Cosa()
    {
        // La existencia la decide el ADJUNTO del tipo, no la bandera: una bandera arriba sin PDF
        // (p. ej. un adjunto limpiado a mano) no puede reportarse como vigente.
        var instance = Tramite(TramiteEstado.Entregado);
        instance.ConsolidadoMaestroVigente = true;
        instance.ConsolidadoMaestroGeneradoEn = SelloMaestro;
        PrepararDetalle(instance, Sources(("consolidado", "system")));

        var (dto, _) = await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        dto!.ConsolidadoMaestro.Should().Be(new ConsolidadoVigenciaDto("inexistente", null, null, false, null));
    }

    [Fact]
    public async Task AC2_RepositorioSinDiccionario_NoRevienta_YAmbosQuedanInexistentes()
    {
        var instance = Tramite(TramiteEstado.Borrador);
        PrepararDetalle(instance, null!);

        var (dto, _) = await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        dto!.ConsolidadoWizard!.Estado.Should().Be("inexistente");
        dto.ConsolidadoMaestro!.Estado.Should().Be("inexistente");
        dto.ConsolidadoMaestro.GeneradoEn.Should().BeNull();
    }

    [Fact]
    public async Task AC2_Detalle_ConsultaLosAdjuntosUnaSolaVez_ConElTenantDelTramite()
    {
        var instance = Tramite(TramiteEstado.Borrador);
        PrepararDetalle(instance, Sources());

        await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        await _repo.Received(1).GetConsolidadoSourcesAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AC2_Derivar_SinSource_EsInexistente_ConSelloNull()
    {
        ConsolidadoVigencia.Derivar(null, vigente: true, SelloWizard, esEstadoFinal: false, esMigrado: false)
            .Should().Be(new ConsolidadoVigenciaDto("inexistente", null, null, false, null));
    }

    // ── AC3 — listado del gestor sin N+1 ─────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_Listado_CadaFilaTraeLaVigenciaDelWizard_DesdeElGrafoYaCargado()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var vigente = Tramite(TramiteEstado.Entregado, tenant);
        vigente.ConsolidadoWizardVigente = true;
        vigente.ConsolidadoWizardGeneradoEn = SelloWizard;
        vigente.Attachments.Add(Adjunto("consolidado", "system"));
        var viejo = Tramite(TramiteEstado.Entregado, tenant);
        viejo.Attachments.Add(Adjunto("consolidado", "system"));
        var sinPdf = Tramite(TramiteEstado.Borrador, tenant);
        PrepararListado(tenant, [vigente, viejo, sinPdf]);

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(tenant, ct);

        filas.Single(f => f.Id == vigente.Id).ConsolidadoWizard
            .Should().Be(new ConsolidadoVigenciaDto("vigente", SelloWizard, "system", false, null));
        filas.Single(f => f.Id == viejo.Id).ConsolidadoWizard!.Estado.Should().Be("desactualizado");
        filas.Single(f => f.Id == sinPdf.Id).ConsolidadoWizard!.Estado.Should().Be("inexistente");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public async Task AC3_Listado_ElNumeroDeConsultasNoDependeDelNumeroDeFilas_SinN1(int filas)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var instancias = Enumerable.Range(0, filas).Select(_ =>
        {
            var i = Tramite(TramiteEstado.Entregado, tenant);
            i.Attachments.Add(Adjunto("consolidado", "system"));
            return i;
        }).ToList();
        PrepararListado(tenant, instancias);

        var result = await new ListProcedureInstancesHandler(_repo).HandleAsync(tenant, ct);

        result.Should().HaveCount(filas).And.OnlyContain(r => r.ConsolidadoWizard != null);
        // Nunca la lectura per-instancia de adjuntos del detalle: la vigencia sale del Include de
        // Attachments del grafo del listado (misma consulta dividida, número FIJO de comandos).
        await _repo.DidNotReceive().GetConsolidadoSourcesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        // Llamadas totales al repositorio: constantes (las consultas en lote del listado), no N.
        _repo.ReceivedCalls().Count().Should().Be(LlamadasDeUnListado(),
            "la vigencia no puede añadir una consulta por fila");
    }

    [Fact]
    public async Task AC3_ListadoFiltrado_TambienExponeLaVigencia()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var instance = Tramite(TramiteEstado.Entregado, tenant);
        instance.ConsolidadoWizardVigente = true;
        instance.Attachments.Add(Adjunto("consolidado", "user"));
        PrepararListado(tenant, [instance]);
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstance>)[instance], 1));

        var (items, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = tenant }, ct);

        items.Single().ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("vigente", null, "user", false, "cargado_por_usuario"));
    }

    // ── AC4 — casos especiales ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task AC4_EstadoFinal_DocumentacionDefinitiva(string estadoFinal)
    {
        // La transición a estado final baja la bandera, pero ese PDF es el definitivo: se reporta
        // vigente + definitivo, nunca «desactualizado» (la ruta de entrega no lo regenera).
        var instance = Tramite(estadoFinal);
        instance.ConsolidadoWizardVigente = false;
        instance.ConsolidadoWizardGeneradoEn = SelloWizard;
        PrepararDetalle(instance, Sources(("consolidado", "system")));

        var (dto, _) = await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        dto!.ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("vigente", SelloWizard, "system", true, "definitivo_estado_final"));
    }

    [Fact]
    public async Task AC4_SourceUser_OrigenUser_ModoCargadoPorUsuario()
    {
        var instance = Tramite(TramiteEstado.Entregado);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoWizardGeneradoEn = SelloWizard;
        PrepararDetalle(instance, Sources(("consolidado", "user")));

        var (dto, _) = await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        dto!.ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("vigente", SelloWizard, "user", false, "cargado_por_usuario"));
    }

    [Fact]
    public async Task AC4_MigradoV1EnEstadoFinal_MigradoSoloLectura_ConYSinPdf()
    {
        var instance = Tramite(TramiteEstado.Aprobado);
        instance.IsMigrated = true;
        PrepararDetalle(instance, Sources(("consolidado", "system")));

        var (dto, _) = await new GetProcedureInstanceHandler(_repo)
            .HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        dto!.ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("vigente", null, "system", true, "migrado_solo_lectura"));
        dto.ConsolidadoMaestro.Should().Be(
            new ConsolidadoVigenciaDto("inexistente", null, null, false, "migrado_solo_lectura"),
            "un migrado final sin PDF lleva el mismo modo que el error de la ruta de entrega");
    }

    [Fact]
    public void AC4_LosModosSonLosMismosLiteralesDeLaRutaDeEntrega()
    {
        ConsolidadoVigencia.ModoDefinitivoEstadoFinal.Should().Be(ConsolidadoEntregaModos.DefinitivoEstadoFinal);
        ConsolidadoVigencia.ModoMigradoSoloLectura.Should().Be(ConsolidadoEntregaModos.MigradoSoloLectura);
        ConsolidadoVigencia.ModoCargadoPorUsuario.Should().Be(ConsolidadoEntregaModos.CargadoPorUsuario);
        ConsolidadoEntregaModos.DefinitivoEstadoFinal.Should().Be("definitivo_estado_final");
        ConsolidadoEntregaModos.MigradoSoloLectura.Should().Be("migrado_solo_lectura");
        ConsolidadoEntregaModos.CargadoPorUsuario.Should().Be("cargado_por_usuario");
    }

    [Fact]
    public void AC4_MigradoNoFinal_NoSeMarcaComoMigrado()
    {
        // Igual que EntregarConsolidadoHandler: el modo migrado solo aplica en estado final.
        ConsolidadoVigencia.Derivar("system", vigente: false, null, esEstadoFinal: false, esMigrado: true)
            .Should().Be(new ConsolidadoVigenciaDto("desactualizado", null, "system", false, null));
    }

    // ── AC5 — contrato OpenAPI ───────────────────────────────────────────────────────────────

    [Fact]
    public void AC5_OpenApi_DeclaraElSchemaConsolidadoVigenciaTipado()
    {
        var bloque = Schema(Yaml(), "ConsolidadoVigencia");

        bloque.Should().MatchRegex(@"estado:\s*\n\s*type: string\s*\n\s*enum: \[vigente, desactualizado, inexistente\]");
        bloque.Should().MatchRegex(@"generadoEn:\s*\n\s*type: string\s*\n\s*format: date-time\s*\n\s*nullable: true");
        bloque.Should().MatchRegex(@"origen:\s*\n\s*type: string\s*\n\s*nullable: true\s*\n\s*enum: \[system, user, null\]");
        bloque.Should().MatchRegex(@"definitivo:\s*\n\s*type: boolean");
        bloque.Should().MatchRegex(@"modo:\s*\n\s*type: string\s*\n\s*nullable: true\s*\n\s*enum: \[definitivo_estado_final, migrado_solo_lectura, cargado_por_usuario, null\]");
        bloque.Should().Contain("required: [estado, generadoEn, origen, definitivo, modo]");
    }

    /// <summary>
    /// HU #12787 (F1) — el valor aditivo <c>radicado_fijo</c> de la entrega está en el enum de
    /// <c>ConsolidadoEntregaResponse.modo</c> junto a todos los modos que emite el handler.
    /// </summary>
    [Fact]
    public void HU12787_OpenApi_ConsolidadoEntregaResponse_DeclaraTodosLosModos_IncluidoRadicadoFijo()
    {
        var bloque = Schema(Yaml(), "ConsolidadoEntregaResponse");
        var modos = new[]
        {
            ConsolidadoEntregaModos.Vigente, ConsolidadoEntregaModos.Regenerado,
            ConsolidadoEntregaModos.DefinitivoEstadoFinal, ConsolidadoEntregaModos.MigradoSoloLectura,
            ConsolidadoEntregaModos.CargadoPorUsuario, ConsolidadoEntregaModos.SoloLectura,
            ConsolidadoEntregaModos.RadicadoFijo,
        };

        ConsolidadoEntregaModos.RadicadoFijo.Should().Be("radicado_fijo");
        bloque.Should().MatchRegex(@"modo:\s*\n\s*type: string\s*\n\s*nullable: true\s*\n\s*enum: \[" + string.Join(", ", modos) + @"\]");
    }

    [Fact]
    public void AC5_OpenApi_LasPropiedadesDelSchemaCoincidenConElDto()
    {
        // Paridad C# ↔ YAML: el serializador web emite camelCase; cada propiedad del record debe estar
        // declarada en el schema (y viceversa), o el contrato publicado miente.
        var bloque = Schema(Yaml(), "ConsolidadoVigencia");
        var enYaml = Regex.Matches(bloque, @"^ {8}(\w+):", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
        var enDto = typeof(ConsolidadoVigenciaDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet();

        enYaml.Should().BeEquivalentTo(enDto);
    }

    [Theory]
    [InlineData("PlateHistoryItem", "consolidadoWizard")]
    [InlineData("ProcedureInstanceDetail", "consolidadoWizard")]
    [InlineData("ProcedureInstanceDetail", "consolidadoMaestro")]
    [InlineData("OtClientProcedureResponse", "consolidadoWizard")]
    [InlineData("OtClientProcedureResponse", "consolidadoMaestro")]
    public void AC5_OpenApi_CadaSuperficieReferenciaElSchema(string schema, string campo)
    {
        var bloque = Schema(Yaml(), schema);

        // Bloque de la propiedad: desde "        campo:" hasta la siguiente clave hermana (8 espacios).
        var m = Regex.Match(bloque, "^ {8}" + campo + @":\n((?: {9,}.*\n|\n)*)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"{schema} debe declarar la propiedad {campo}");
        m.Groups[1].Value.Should().Contain(
            "$ref: \"#/components/schemas/ConsolidadoVigencia\"",
            $"{schema}.{campo} debe tiparse con ConsolidadoVigencia");
    }

    [Fact]
    public void AC5_OpenApi_OtClientProcedureResponse_DeclaraLaRadicacionQuipuxTipada()
    {
        // Ampliación para #12787 AC2: fecha de la última radicación exitosa + maestro enviado.
        var bloque = Schema(Yaml(), "OtClientProcedureResponse");

        bloque.Should().MatchRegex(@"quipuxRadicadoEn:\s*\n\s*type: string\s*\n\s*format: date-time\s*\n\s*nullable: true");
        bloque.Should().MatchRegex(@"quipuxMaestroAttachmentId:\s*\n\s*type: string\s*\n\s*format: uuid\s*\n\s*nullable: true");
    }

    [Fact]
    public void AC5_OpenApi_LosDtosCSharpTienenLosCamposQuePublicaElContrato()
    {
        typeof(ProcedureInstanceDetailDto).GetProperty("ConsolidadoWizard")!.PropertyType
            .Should().Be<ConsolidadoVigenciaDto>();
        typeof(ProcedureInstanceDetailDto).GetProperty("ConsolidadoMaestro")!.PropertyType
            .Should().Be<ConsolidadoVigenciaDto>();
        typeof(InstanceSummaryDto).GetProperty("ConsolidadoWizard")!.PropertyType
            .Should().Be<ConsolidadoVigenciaDto>();
        typeof(InstanceSummaryDto).GetProperty("ConsolidadoMaestro")
            .Should().BeNull("el listado solo expone el consolidado del wizard (el que muestra la fila)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static ProcedureInstance Tramite(string status, Guid? tenantId = null) => new()
    {
        ProcedureType = ProcedureTypeFixture.Matricula,
        Id = Guid.NewGuid(),
        TenantId = tenantId ?? Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstanceAttachment Adjunto(string tipo, string source) => new()
    {
        Id = Guid.NewGuid(),
        Tipo = tipo,
        Filename = tipo + ".pdf",
        Mimetype = "application/pdf",
        Source = source,
        UploadedAt = DateTimeOffset.UtcNow,
    };

    private static Dictionary<string, string> Sources(params (string Tipo, string Source)[] items) =>
        items.ToDictionary(i => i.Tipo, i => i.Source, StringComparer.OrdinalIgnoreCase);

    private void PrepararDetalle(ProcedureInstance instance, IReadOnlyDictionary<string, string> sources)
    {
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetConsolidadoSourcesAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(sources);
    }

    private void PrepararListado(Guid tenant, IReadOnlyList<ProcedureInstance> instancias)
    {
        _repo.ListWithSummaryGraphAsync(tenant, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns(instancias);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>());
    }

    /// <summary>Llamadas de un listado con UNA fila (línea base): cualquier N debe igualarla.</summary>
    private static int LlamadasDeUnListado()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var tenant = Guid.NewGuid();
        var i = Tramite(TramiteEstado.Entregado, tenant);
        repo.ListWithSummaryGraphAsync(tenant, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([i]);
        new ListProcedureInstancesHandler(repo).HandleAsync(tenant, CancellationToken.None).GetAwaiter().GetResult();
        return repo.ReceivedCalls().Count();
    }

    private static string Yaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "core-api.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("el contrato OpenAPI debe encontrarse desde el directorio de tests");
        return File.ReadAllText(Path.Combine(dir!.FullName, "contracts", "openapi", "core-api.v1.yaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>Bloque de <c>components.schemas.&lt;nombre&gt;</c>: desde su clave hasta el siguiente schema hermano.</summary>
    private static string Schema(string yaml, string nombre)
    {
        var m = Regex.Match(yaml, "^    " + nombre + ":\\n((?:(?:      .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"components.schemas.{nombre} debe existir en core-api.v1.yaml");
        return m.Groups[1].Value;
    }
}
