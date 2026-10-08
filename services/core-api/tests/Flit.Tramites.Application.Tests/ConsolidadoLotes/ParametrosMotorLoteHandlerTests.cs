using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13420 (épica #13216) — <see cref="ObtenerParametrosMotorLoteHandler"/>, <see cref="ActualizarParametrosMotorLoteHandler"/>
/// y <see cref="ConsolidadoExportSettingsRangos"/> sobre un repositorio doble: obligatorios, rangos idénticos a los CHECK
/// del DDL 133 (extremos dentro y fuera), lease &gt; timeout, traducción de conflicto/no encontrado/CHECK de la base, y
/// que nada se escribe con un error. La escritura real (row_version, audit_logs) la cubre
/// <c>ParametrosMotorLoteIntegrationTests</c> (PostgreSQL).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await new ActualizarParametrosMotorLoteHandler(repo).HandleAsync(Valido() with { MaxItemsPerBatch = 5000 }, ct);
/// r.Estado.Should().Be(ParametrosMotorLoteEstado.Actualizado);
/// </code>
/// </remarks>
public sealed class ParametrosMotorLoteHandlerTests
{
    private static readonly Guid Usuario = Guid.Parse("13420000-0000-4000-8000-0000000000a1");
    private static readonly DateTimeOffset Ahora = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly IConsolidadoExportSettingsRepository _repo = Substitute.For<IConsolidadoExportSettingsRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ActualizarParametrosMotorLoteHandler Handler() => new(_repo, new RelojFijo(Ahora));

    /// <summary>Los valores sembrados por el DDL 133, con row_version 3.</summary>
    private static ActualizarParametrosMotorLoteCommand Valido() =>
        new(10_000, 500, 250, 2, 300, 600, 3, 30, 1200, 1800, 3, 24, true, 3, Usuario);

    private static ConsolidadoExportSettings Fila(int tope = 10_000, long rowVersion = 3) => new()
    {
        Id = Guid.NewGuid(),
        MaxItemsPerBatch = tope,
        MaxPdfsPerPart = 500,
        MaxMbPerPart = 250,
        ItemSlots = 2,
        ItemTimeoutSeconds = 300,
        ItemLeaseSeconds = 600,
        MaxItemAttempts = 3,
        RetryDelaySeconds = 30,
        PartTimeoutSeconds = 1200,
        PartLeaseSeconds = 1800,
        MaxPartAttempts = 3,
        RetentionHours = 24,
        IsActive = true,
        UpdatedAt = Ahora,
        UpdatedBy = Usuario,
        RowVersion = rowVersion,
    };

    private void Responde(ActualizarSettingsResultado r) =>
        _repo.ActualizarAsync(Arg.Any<ConsolidadoExportSettingsValores>(), Arg.Any<long>(), Arg.Any<Guid?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(r);

    // ── AC1 — GET ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Obtener_ProyectaTodosLosCampos_ElNombreDeQuienCambio_YLosLimites()
    {
        _repo.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(new ConsolidadoExportSettingsLeidos(Fila(), "Ana Admin"));

        var dto = await new ObtenerParametrosMotorLoteHandler(_repo).HandleAsync(Ct);

        dto.Should().NotBeNull();
        dto!.MaxItemsPerBatch.Should().Be(10_000);
        dto.MaxPdfsPerPart.Should().Be(500);
        dto.MaxMbPerPart.Should().Be(250);
        dto.ItemSlots.Should().Be(2);
        dto.ItemTimeoutSeconds.Should().Be(300);
        dto.ItemLeaseSeconds.Should().Be(600);
        dto.MaxItemAttempts.Should().Be(3);
        dto.RetryDelaySeconds.Should().Be(30);
        dto.PartTimeoutSeconds.Should().Be(1200);
        dto.PartLeaseSeconds.Should().Be(1800);
        dto.MaxPartAttempts.Should().Be(3);
        dto.RetentionHours.Should().Be(24);
        dto.IsActive.Should().BeTrue();
        dto.UpdatedAt.Should().Be(Ahora);
        dto.UpdatedBy.Should().Be(Usuario);
        dto.UpdatedByName.Should().Be("Ana Admin");
        dto.RowVersion.Should().Be(3);
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("maxItemsPerBatch", 1, 32_766, null));
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("retryDelaySeconds", 5, 3600, null));
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("itemLeaseSeconds", null, 7200, "itemTimeoutSeconds"));
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("partLeaseSeconds", null, 14_400, "partTimeoutSeconds"));
        dto.Limites.Should().HaveCount(12, "los diez rangos de una columna y las dos reglas de lease");
    }

    [Fact]
    public async Task AC1_Obtener_SinFila_DevuelveNull()
    {
        _repo.ObtenerAsync(Arg.Any<CancellationToken>()).Returns((ConsolidadoExportSettingsLeidos?)null);

        (await new ObtenerParametrosMotorLoteHandler(_repo).HandleAsync(Ct)).Should().BeNull();
    }

    // ── AC2 — guardar ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Actualizar_TopeA5000_EscribeConElRowVersionLeido_ElUsuario_YLaHora()
    {
        Responde(new ActualizarSettingsResultado(ActualizarSettingsEstado.Actualizado,
            new ConsolidadoExportSettingsLeidos(Fila(tope: 5000, rowVersion: 4), "Ana Admin")));

        var r = await Handler().HandleAsync(Valido() with { MaxItemsPerBatch = 5000 }, Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Actualizado);
        r.Parametros!.MaxItemsPerBatch.Should().Be(5000);
        r.Parametros.RowVersion.Should().Be(4);
        r.Errores.Should().BeEmpty();
        await _repo.Received(1).ActualizarAsync(
            Arg.Is<ConsolidadoExportSettingsValores>(v => v.MaxItemsPerBatch == 5000 && v.IsActive && v.ItemLeaseSeconds == 600),
            3, Usuario, Ahora, Arg.Any<CancellationToken>());
    }

    // ── AC3 — rangos ───────────────────────────────────────────────────────────────────

    public static TheoryData<string, int> FueraDeRango => new()
    {
        { "maxItemsPerBatch", 0 },
        { "maxItemsPerBatch", 32_767 },
        { "maxPdfsPerPart", 0 },
        { "maxPdfsPerPart", 5001 },
        { "maxMbPerPart", 9 },
        { "maxMbPerPart", 2049 },
        { "itemSlots", 0 },
        { "itemSlots", 7 },
        { "itemTimeoutSeconds", 0 },
        { "maxItemAttempts", 0 },
        { "maxItemAttempts", 11 },
        { "retryDelaySeconds", 4 },
        { "partTimeoutSeconds", 0 },
        { "maxPartAttempts", 0 },
        { "maxPartAttempts", 11 },
        { "retentionHours", 0 },
        { "retentionHours", 169 },
    };

    [Theory]
    [MemberData(nameof(FueraDeRango))]
    public async Task AC3_ValorFueraDeRango_Invalido_ConElCampo_YNoEscribe(string campo, int valor)
    {
        var r = await Handler().HandleAsync(Con(Valido(), campo, valor), Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Invalido);
        r.Errores.Keys.Should().Equal(campo);
        await _repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    public static TheoryData<string, int> ExtremosDentro => new()
    {
        { "maxItemsPerBatch", 1 },
        { "maxItemsPerBatch", 32_766 },
        { "maxPdfsPerPart", 1 },
        { "maxPdfsPerPart", 5000 },
        { "maxMbPerPart", 10 },
        { "maxMbPerPart", 2048 },
        { "itemSlots", 1 },
        { "itemSlots", 6 },
        { "maxItemAttempts", 1 },
        { "maxItemAttempts", 10 },
        { "retryDelaySeconds", 5 },
        { "maxPartAttempts", 10 },
        { "retentionHours", 1 },
        { "retentionHours", 168 },
    };

    [Theory]
    [MemberData(nameof(ExtremosDentro))]
    public void AC3_LosExtremosDelCheck_SonValidos(string campo, int valor)
    {
        var c = Con(Valido(), campo, valor);
        ConsolidadoExportSettingsRangos.Validar(Valores(c)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(300, 300)]
    [InlineData(300, 299)]
    public async Task AC3_LeaseDeItemMenorOIgualQueSuTimeout_Invalido_EnItemLeaseSeconds(int timeout, int lease)
    {
        var r = await Handler().HandleAsync(Valido() with { ItemTimeoutSeconds = timeout, ItemLeaseSeconds = lease }, Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Invalido);
        r.Errores.Keys.Should().Equal("itemLeaseSeconds");
        r.Errores["itemLeaseSeconds"].Single().Should().Contain("itemTimeoutSeconds");
        await _repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    [Fact]
    public async Task AC3_LeaseDeParteIgualASuTimeout_Invalido_EnPartLeaseSeconds()
    {
        var r = await Handler().HandleAsync(Valido() with { PartTimeoutSeconds = 1800, PartLeaseSeconds = 1800 }, Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Invalido);
        r.Errores.Keys.Should().Equal("partLeaseSeconds");
    }

    [Fact]
    public async Task AC3_CamposAusentes_Invalido_UnErrorPorCampo_IncluidosIsActiveYRowVersion()
    {
        var r = await Handler().HandleAsync(
            new ActualizarParametrosMotorLoteCommand(null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, Usuario), Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Invalido);
        r.Errores.Keys.Should().BeEquivalentTo(
            "maxItemsPerBatch", "maxPdfsPerPart", "maxMbPerPart", "itemSlots", "itemTimeoutSeconds", "itemLeaseSeconds",
            "maxItemAttempts", "retryDelaySeconds", "partTimeoutSeconds", "partLeaseSeconds", "maxPartAttempts",
            "retentionHours", "isActive", "rowVersion");
        await _repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    [Fact]
    public async Task AC3_CheckRechazadoPorLaBase_Invalido_EnElCampoDeLaRestriccion()
    {
        Responde(new ActualizarSettingsResultado(ActualizarSettingsEstado.RechazadoPorLaBase,
            Restriccion: "ck_consolidado_export_settings_part_lease"));

        var r = await Handler().HandleAsync(Valido(), Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Invalido);
        r.Errores.Keys.Should().Equal("partLeaseSeconds");
    }

    // ── AC4 — concurrencia ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_RowVersionDesactualizado_Conflicto()
    {
        Responde(new ActualizarSettingsResultado(ActualizarSettingsEstado.Conflicto));

        var r = await Handler().HandleAsync(Valido(), Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Conflicto);
        r.Parametros.Should().BeNull();
    }

    [Fact]
    public async Task Actualizar_SinFila_NoEncontrado()
    {
        Responde(new ActualizarSettingsResultado(ActualizarSettingsEstado.NoEncontrado));

        (await Handler().HandleAsync(Valido(), Ct)).Estado.Should().Be(ParametrosMotorLoteEstado.NoEncontrado);
    }

    // ── AC6 — apagar ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_ApagarElMotor_EscribeIsActiveFalse()
    {
        var apagada = Fila();
        apagada.IsActive = false;
        Responde(new ActualizarSettingsResultado(ActualizarSettingsEstado.Actualizado,
            new ConsolidadoExportSettingsLeidos(apagada, null)));

        var r = await Handler().HandleAsync(Valido() with { IsActive = false }, Ct);

        r.Parametros!.IsActive.Should().BeFalse();
        await _repo.Received(1).ActualizarAsync(Arg.Is<ConsolidadoExportSettingsValores>(v => !v.IsActive),
            Arg.Any<long>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ── Security L1 — topes de los tiempos del motor ────────────────────────────────────

    [Fact]
    public async Task L1_Obtener_LimitesPublicanLosTopesDeTiemposYLeases()
    {
        _repo.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(new ConsolidadoExportSettingsLeidos(Fila(), null));

        var dto = await new ObtenerParametrosMotorLoteHandler(_repo).HandleAsync(Ct);

        dto!.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("itemTimeoutSeconds", 1, 3600, null));
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("retryDelaySeconds", 5, 3600, null));
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("partTimeoutSeconds", 1, 7200, null));
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("itemLeaseSeconds", null, 7200, "itemTimeoutSeconds"));
        dto.Limites.Should().ContainEquivalentOf(new ParametroMotorLoteLimiteDto("partLeaseSeconds", null, 14_400, "partTimeoutSeconds"));
        dto.Limites.Should().OnlyContain(l => l.Maximo != null, "ningún parámetro del motor queda sin tope");
    }

    /// <summary>Un tiempo por encima de su tope (el lease se sube a su máximo para que el único error sea el del rango).</summary>
    [Theory]
    [InlineData("itemTimeoutSeconds", 3601, "Debe estar entre 1 y 3600.")]
    [InlineData("retryDelaySeconds", 3601, "Debe estar entre 5 y 3600.")]
    [InlineData("partTimeoutSeconds", 7201, "Debe estar entre 1 y 7200.")]
    [InlineData("retryDelaySeconds", int.MaxValue, "Debe estar entre 5 y 3600.")]
    public async Task L1_TiempoPorEncimaDeSuTope_Invalido_ConElMensajeDelRango_YNoEscribe(string campo, int valor, string mensaje)
    {
        var c = Con(Valido() with { ItemLeaseSeconds = 7200, PartLeaseSeconds = 14_400 }, campo, valor);

        var r = await Handler().HandleAsync(c, Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Invalido);
        r.Errores.Keys.Should().Equal(campo);
        r.Errores[campo].Should().Equal(mensaje);
        await _repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    [Theory]
    [InlineData("itemLeaseSeconds", 7201, "Debe ser menor o igual que 7200.")]
    [InlineData("partLeaseSeconds", 14_401, "Debe ser menor o igual que 14400.")]
    [InlineData("partLeaseSeconds", int.MaxValue, "Debe ser menor o igual que 14400.")]
    public async Task L1_LeasePorEncimaDeSuTope_Invalido_EnElCampoDelLease_YNoEscribe(string campo, int valor, string mensaje)
    {
        var c = campo == "itemLeaseSeconds" ? Valido() with { ItemLeaseSeconds = valor } : Valido() with { PartLeaseSeconds = valor };

        var r = await Handler().HandleAsync(c, Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Invalido);
        r.Errores.Keys.Should().Equal(campo);
        r.Errores[campo].Should().Equal(mensaje);
        await _repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    [Fact]
    public void L1_LosTopesSonInclusivos_TodosEnSuMaximo_EsValido()
    {
        var c = Valido() with
        {
            ItemTimeoutSeconds = 3600,
            ItemLeaseSeconds = 7200,
            RetryDelaySeconds = 3600,
            PartTimeoutSeconds = 7200,
            PartLeaseSeconds = 14_400,
        };

        ConsolidadoExportSettingsRangos.Validar(Valores(c)).Should().BeEmpty();
    }

    [Fact]
    public void Aplicar_CopiaLosTreceValores_ALaFila()
    {
        var fila = Fila();
        var v = new ConsolidadoExportSettingsValores(5000, 100, 64, 4, 120, 240, 5, 10, 600, 900, 2, 48, false);

        ConsolidadoExportSettingsRangos.Aplicar(fila, v);

        fila.Should().BeEquivalentTo(new
        {
            MaxItemsPerBatch = 5000,
            MaxPdfsPerPart = 100,
            MaxMbPerPart = 64,
            ItemSlots = (short)4,
            ItemTimeoutSeconds = 120,
            ItemLeaseSeconds = 240,
            MaxItemAttempts = (short)5,
            RetryDelaySeconds = 10,
            PartTimeoutSeconds = 600,
            PartLeaseSeconds = 900,
            MaxPartAttempts = (short)2,
            RetentionHours = 48,
            IsActive = false,
        });
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private static ConsolidadoExportSettingsValores Valores(ActualizarParametrosMotorLoteCommand c) => new(
        c.MaxItemsPerBatch!.Value, c.MaxPdfsPerPart!.Value, c.MaxMbPerPart!.Value, c.ItemSlots!.Value,
        c.ItemTimeoutSeconds!.Value, c.ItemLeaseSeconds!.Value, c.MaxItemAttempts!.Value, c.RetryDelaySeconds!.Value,
        c.PartTimeoutSeconds!.Value, c.PartLeaseSeconds!.Value, c.MaxPartAttempts!.Value, c.RetentionHours!.Value,
        c.IsActive!.Value);

    private static ActualizarParametrosMotorLoteCommand Con(ActualizarParametrosMotorLoteCommand c, string campo, int valor) =>
        campo switch
        {
            "maxItemsPerBatch" => c with { MaxItemsPerBatch = valor },
            "maxPdfsPerPart" => c with { MaxPdfsPerPart = valor },
            "maxMbPerPart" => c with { MaxMbPerPart = valor },
            "itemSlots" => c with { ItemSlots = valor },
            // Con el timeout en 0 el lease (600) sigue siendo mayor: el único error es el del rango.
            "itemTimeoutSeconds" => c with { ItemTimeoutSeconds = valor },
            "maxItemAttempts" => c with { MaxItemAttempts = valor },
            "retryDelaySeconds" => c with { RetryDelaySeconds = valor },
            "partTimeoutSeconds" => c with { PartTimeoutSeconds = valor },
            "maxPartAttempts" => c with { MaxPartAttempts = valor },
            "retentionHours" => c with { RetentionHours = valor },
            _ => throw new ArgumentOutOfRangeException(nameof(campo)),
        };

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
