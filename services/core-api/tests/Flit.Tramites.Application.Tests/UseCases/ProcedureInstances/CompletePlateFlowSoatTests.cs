using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Validación del SOAT ante el RUNT al procesar (sub-estado <c>asignado</c> → <c>terminado</c>).
///
/// <para>Con la opción <b>apagada</b> (default), un SOAT que el RUNT no reporta vigente detiene el
/// avance. Con la opción <b>activa</b> el hallazgo solo se informa y el trámite continúa.</para>
/// </summary>
public sealed class CompletePlateFlowSoatTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ProcedureInstance Asignado(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial" ?? "matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000100",
            Status = TramiteEstado.Entregado,
            PlateFlowStatus = PlateFlowStatus.Asignado,
            ModalidadEntrada = "matricula_inicial",
            TipologiaCodigo = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        return instance;
    }

    /// <summary>Sin validador inyectado el handler avanza (no hay consulta que bloquee).</summary>
    [Fact]
    public async Task Procesar_sinValidador_avanzaATerminado()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        var sut = new CompletePlateFlowHandler(_repo);

        var (result, error, warning) = await sut.HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new CompletePlateFlowRequest(true, true), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result.Should().NotBeNull();
        instance.PlateFlowStatus.Should().Be(PlateFlowStatus.Terminado);
    }

    [Fact]
    public async Task Procesar_fueraDeAsignado_noAvanza()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        instance.PlateFlowStatus = PlateFlowStatus.Preasignado;
        var sut = new CompletePlateFlowHandler(_repo);

        var (result, error, _) = await sut.HandleAsync(
            instance.Id, instance.TenantId, null, new CompletePlateFlowRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be("plate_flow_not_asignado");
    }

    [Fact]
    public async Task Procesar_registraLosChecksDeSoatEImpuesto()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        var sut = new CompletePlateFlowHandler(_repo);

        await sut.HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new CompletePlateFlowRequest(true, false), Ct);

        instance.FieldValues.Should().Contain(f =>
            f.FieldKey == PlateFlowCheckFields.SoatPagado && f.ValueText == "true");
        instance.FieldValues.Should().Contain(f =>
            f.FieldKey == PlateFlowCheckFields.ImpuestoDepartamentalPagado && f.ValueText == "false");
    }

    /// <summary>El código de error es el que el endpoint traduce a la alerta bloqueante (409).</summary>
    [Fact]
    public void ElCodigoDeErrorDeSoatEsEstable()
    {
        CompletePlateFlowHandler.SoatNoVigente.Should().Be("soat_no_vigente");
        CompletePlateFlowHandler.SoatNoVigenteAdvertencia.Should().Be("soat_no_vigente_advertencia");
    }

    // ---------- Gate invertido: activo = no bloquea; apagado = bloquea ----------

    [Theory]
    [InlineData(/* opcionActiva */ false, /* vigente */ false, /* expectedBlock */ true)]
    [InlineData(/* opcionActiva */ true, /* vigente */ false, /* expectedBlock */ false)]
    [InlineData(/* opcionActiva */ false, /* vigente */ true, /* expectedBlock */ false)]
    [InlineData(/* opcionActiva */ true, /* vigente */ true, /* expectedBlock */ false)]
    public void Gate_activoNoBloquea_apagadoSiBloqueaCuandoSoatNoVigente(
        bool opcionActiva, bool soatVigente, bool expectedBlock)
    {
        CompletePlateFlowHandler
            .DebeBloquearPorSoatNoVigente(opcionActiva, consultaRespondio: true, soatVigente)
            .Should().Be(expectedBlock);
    }

    /// <summary>
    /// Dejar pasar no es callar: con la opción activa y SOAT no vigente hay que advertir. En el
    /// resto de combinaciones no hay nada que decir.
    /// </summary>
    [Theory]
    [InlineData(/* opcionActiva */ true, /* vigente */ false, /* expectedWarn */ true)]
    [InlineData(/* opcionActiva */ false, /* vigente */ false, /* expectedWarn */ false)]
    [InlineData(/* opcionActiva */ true, /* vigente */ true, /* expectedWarn */ false)]
    public void Advertencia_soloCuandoLaOpcionActivaDejaPasarUnSoatNoVigente(
        bool opcionActiva, bool soatVigente, bool expectedWarn)
    {
        CompletePlateFlowHandler
            .DebeAdvertirSoatNoVigente(opcionActiva, consultaRespondio: true, soatVigente)
            .Should().Be(expectedWarn);
    }

    [Fact]
    public void Advertencia_sinRespuestaDelRunt_noAdvierte()
    {
        CompletePlateFlowHandler
            .DebeAdvertirSoatNoVigente(
                permiteContinuarSinSoatVigente: true,
                consultaRespondio: false,
                soatVigente: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Gate_sinRespuestaDelRunt_nuncaBloquea()
    {
        // Proveedor caído / plantilla ausente: no convertir en bloqueo silencioso.
        CompletePlateFlowHandler
            .DebeBloquearPorSoatNoVigente(
                permiteContinuarSinSoatVigente: false,
                consultaRespondio: false,
                soatVigente: false)
            .Should().BeFalse();

        CompletePlateFlowHandler
            .DebeBloquearPorSoatNoVigente(
                permiteContinuarSinSoatVigente: false,
                consultaRespondio: true,
                soatVigente: null)
            .Should().BeFalse();
    }
}
