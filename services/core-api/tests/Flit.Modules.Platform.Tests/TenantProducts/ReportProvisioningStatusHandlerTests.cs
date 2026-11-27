using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1; flit.identidad.v1.AprovisionamientoProductoService) — el reporte del estado de
/// aprovisionamiento que envía el servicio del producto: valida el contrato (estados del plan §3.1, acciones, largos,
/// versión ≥ 1), normaliza vacíos a <c>null</c>, guarda <c>firma_confirmada</c> solo en LISTO y delega la monotonía en el
/// almacén (solo versiones mayores).
/// <para>Uso de ejemplo: <c>await handler.HandleAsync(new(tenantId, "impuestos", "LISTO", "", "", "", false, 3))</c>.</para>
/// </summary>
public sealed class ReportProvisioningStatusHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ITenantProductProvisioningStore _store = Substitute.For<ITenantProductProvisioningStore>();

    public ReportProvisioningStatusHandlerTests()
    {
        _store.GuardarSiEsMasNuevoAsync(Arg.Any<TenantProductProvisioning>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ProvisioningGuardado(true, true, ci.Arg<TenantProductProvisioning>().Version));
    }

    private ReportProvisioningStatusHandler Handler() => new(_store, new Reloj(Ahora));

    private static ReportProvisioningStatusCommand Reporte(
        string? estado = "LISTO", string? motivo = "", string? mensaje = "", string? accion = "", bool firma = false, long version = 3, string producto = "impuestos") =>
        new(TenantId, producto, estado, motivo, mensaje, accion, firma, version);

    [Fact]
    public async Task Listo_GuardaElReporte_ConFirmaSinConfirmarYVaciosComoNull()
    {
        var r = await Handler().HandleAsync(Reporte(), Ct);

        r.Should().Be(new ProvisioningGuardado(true, true, 3));
        await _store.Received(1).GuardarSiEsMasNuevoAsync(
            new TenantProductProvisioning(TenantId, "impuestos", "LISTO", null, null, null, false, 3, Ahora),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConMotivoMensajeYAccion_LosGuarda_YFirmaNullFueraDeListo()
    {
        await Handler().HandleAsync(Reporte("ERROR", "EMPRESA_YA_REGISTRADA", "La empresa ya existe en el proveedor", "REINTENTAR", firma: true, version: 9), Ct);

        await _store.Received(1).GuardarSiEsMasNuevoAsync(
            new TenantProductProvisioning(TenantId, "impuestos", "ERROR", "EMPRESA_YA_REGISTRADA", "La empresa ya existe en el proveedor", "REINTENTAR", null, 9, Ahora),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("PENDIENTE")]
    [InlineData("LISTO")]
    [InlineData("LISTO_SIN_WEBHOOK")]
    [InlineData("ERROR")]
    [InlineData("INCIERTO")]
    [InlineData("REQUIERE_CREDENCIALES")]
    [InlineData("REQUIERE_REVISION_NIT")]
    [InlineData("SUSPENDIDO")]
    public async Task LosOchoEstadosDelPlan_SeAceptan(string estado)
    {
        (await Handler().HandleAsync(Reporte(estado), Ct)).Aplicado.Should().BeTrue();
    }

    [Theory]
    [InlineData("REINTENTAR")]
    [InlineData("CARGAR_SECRETO")]
    [InlineData("CARGAR_CREDENCIALES")]
    [InlineData("CORREGIR_NIT")]
    public async Task LasCuatroAcciones_SeAceptan(string accion)
    {
        (await Handler().HandleAsync(Reporte("ERROR", accion: accion), Ct)).Aplicado.Should().BeTrue();
    }

    [Fact]
    public async Task VersionNoMayor_NoSeAplica_YDevuelveLaGuardada()
    {
        _store.GuardarSiEsMasNuevoAsync(Arg.Any<TenantProductProvisioning>(), Arg.Any<CancellationToken>())
            .Returns(new ProvisioningGuardado(true, false, 12));

        (await Handler().HandleAsync(Reporte(version: 5), Ct)).Should().Be(new ProvisioningGuardado(true, false, 12));
    }

    public static TheoryData<ReportProvisioningStatusCommand> Invalidos() => new()
    {
        Reporte(estado: null),
        Reporte(estado: ""),
        Reporte(estado: "listo"),
        Reporte(estado: "ACTIVO"),
        Reporte(accion: "BORRAR"),
        Reporte(version: 0),
        Reporte(version: -1),
        Reporte(mensaje: new string('x', 301)),
        Reporte(motivo: new string('M', 65)),
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public async Task ReporteFueraDelContrato_SeRechaza_SinGuardar(ReportProvisioningStatusCommand reporte)
    {
        var act = () => Handler().HandleAsync(reporte, Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProvisioningReportInvalid);
        await _store.DidNotReceiveWithAnyArgs().GuardarSiEsMasNuevoAsync(default!, Ct);
    }

    [Fact]
    public async Task MensajeDe300_SeAcepta()
    {
        (await Handler().HandleAsync(Reporte("ERROR", mensaje: new string('x', 300)), Ct)).Aplicado.Should().BeTrue();
    }

    [Theory]
    [InlineData("diagnostico")]
    [InlineData("tramites")]
    public async Task ProductoSinAprovisionamiento_SeRechaza(string producto)
    {
        var act = () => Handler().HandleAsync(Reporte(producto: producto), Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductWithoutProvisioning);
        await _store.DidNotReceiveWithAnyArgs().GuardarSiEsMasNuevoAsync(default!, Ct);
    }

    [Fact]
    public async Task EmpresaInexistente_SeRechazaConTenantNotFound()
    {
        _store.GuardarSiEsMasNuevoAsync(Arg.Any<TenantProductProvisioning>(), Arg.Any<CancellationToken>())
            .Returns(new ProvisioningGuardado(false, false, 0));

        var act = () => Handler().HandleAsync(Reporte(), Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.TenantNotFound);
    }

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
