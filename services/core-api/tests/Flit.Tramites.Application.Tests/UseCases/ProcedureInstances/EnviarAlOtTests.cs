using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// «Enviar al OT» (ADR-0059, HU #12597 AC4/AC5): <c>asignado → entregado</c> por el ciclo de vida, con
/// la validación del SOAT ante el RUNT.
///
/// <para>Con la opción <b>apagada</b> (default), un SOAT que el RUNT no reporta vigente detiene el
/// avance. Con la opción <b>activa</b> el hallazgo solo se informa y el trámite continúa.</para>
/// </summary>
public sealed class EnviarAlOtTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITramiteLifecycleService _lifecycle = Substitute.For<ITramiteLifecycleService>();
    private TramiteTransitionCommand? _comandoRecibido;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EnviarAlOtHandler Sut() => new(_repo, _lifecycle);

    private ProcedureInstance Asignado(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial" ?? "matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000100",
            Status = TramiteEstado.Asignado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithDetailsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        // El ciclo de vida real se prueba en TramiteLifecycleServiceTests; aquí solo interesa QUÉ orden
        // recibe y que el handler devuelva la instancia transicionada.
        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _comandoRecibido = call.Arg<TramiteTransitionCommand>();
                instance.Status = _comandoRecibido.ToStatus;
                return TramiteTransitionOutcome.Ok(instance);
            });
        return instance;
    }

    /// <summary>AC4 — sin validador inyectado el handler envía (no hay consulta que bloquee).</summary>
    [Fact]
    public async Task Enviar_sinValidador_pasaAEntregadoPorElCicloDeVida()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        var usuario = Guid.NewGuid();

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, usuario, new EnviarAlOtRequest(true, true), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result.Should().NotBeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        _comandoRecibido.Should().NotBeNull();
        _comandoRecibido!.ToStatus.Should().Be(TramiteEstado.Entregado);
        _comandoRecibido.Actor.Should().Be(TramiteActor.Gestor);
        _comandoRecibido.ChangedByUserId.Should().Be(usuario);
    }

    /// <summary>AC5 — en preasignacion no hay placa y en entregado ya se envió: 422 transicion_no_permitida.</summary>
    [Theory]
    [InlineData("preasignacion")]
    [InlineData("entregado")]
    [InlineData("preparado")]
    public async Task Enviar_fueraDeAsignado_noAvanzaNiTocaElCicloDeVida(string status)
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        instance.Status = status;

        var (result, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, null, new EnviarAlOtRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
        _comandoRecibido.Should().BeNull();
    }

    [Fact]
    public async Task Enviar_siElCicloDeVidaDeniega_propagaElCodigo()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(TramiteTransitionOutcome.Fail(TramiteEstadoErrores.TransicionSoloGestor, "x"));

        var (result, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, null, new EnviarAlOtRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.TransicionSoloGestor);
    }

    [Fact]
    public async Task Enviar_registraLosChecksDeSoatEImpuestoAntesDeTransicionar()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());

        await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(true, false), Ct);

        instance.FieldValues.Should().Contain(f =>
            f.FieldKey == EnvioOtCheckFields.SoatPagado && f.ValueText == "true");
        instance.FieldValues.Should().Contain(f =>
            f.FieldKey == EnvioOtCheckFields.ImpuestoDepartamentalPagado && f.ValueText == "false");
    }

    /// <summary>El código de error es el que el endpoint traduce a la alerta bloqueante (409).</summary>
    [Fact]
    public void ElCodigoDeErrorDeSoatEsEstable()
    {
        EnviarAlOtHandler.SoatNoVigente.Should().Be("soat_no_vigente");
        EnviarAlOtHandler.SoatNoVigenteAdvertencia.Should().Be("soat_no_vigente_advertencia");
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
        EnviarAlOtHandler
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
        EnviarAlOtHandler
            .DebeAdvertirSoatNoVigente(opcionActiva, consultaRespondio: true, soatVigente)
            .Should().Be(expectedWarn);
    }

    [Fact]
    public void Advertencia_sinRespuestaDelRunt_noAdvierte()
    {
        EnviarAlOtHandler
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
        EnviarAlOtHandler
            .DebeBloquearPorSoatNoVigente(
                permiteContinuarSinSoatVigente: false,
                consultaRespondio: false,
                soatVigente: false)
            .Should().BeFalse();

        EnviarAlOtHandler
            .DebeBloquearPorSoatNoVigente(
                permiteContinuarSinSoatVigente: false,
                consultaRespondio: true,
                soatVigente: null)
            .Should().BeFalse();
    }

    /// <summary>
    /// HU #12116 — al pasar a entregado, «Enviar al OT» reintenta la firma automática de impronta
    /// (idempotente si ya se firmó al asignar la placa). Como en Submit, NO se stubea
    /// <c>GetByIdWithFurGraphAsync</c>: el handler de firma recibe <c>null</c> y termina best-effort;
    /// lo que importa es que SE INVOCÓ tras la transición y que el envío no cambia su respuesta.
    /// </summary>
    [Fact]
    public async Task Enviar_trasEntregar_invocaFirmaImprontaSinAlterarLaRespuesta()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        var firmaImpronta = new FirmarImprontaManualSiListaHandler(
            _repo, Substitute.For<IExpedienteConsolidadoMerger>(), Substitute.For<IAttachmentStorage>(),
            Substitute.For<IImprontaManualStamper>());
        var sut = new EnviarAlOtHandler(_repo, _lifecycle, firmaImpronta: firmaImpronta);

        var (result, error, warning) = await sut.HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        await _repo.Received(1).GetByIdWithFurGraphAsync(
            instance.Id, instance.TenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>HU #12116 — si el ciclo de vida rechaza la transición, la firma ni se intenta.</summary>
    [Fact]
    public async Task Enviar_siLaTransicionFalla_noIntentaFirmarLaImpronta()
    {
        var instance = Asignado(Guid.NewGuid(), Guid.NewGuid());
        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(TramiteTransitionOutcome.Fail(TramiteEstadoErrores.TransicionNoPermitida));
        var firmaImpronta = new FirmarImprontaManualSiListaHandler(
            _repo, Substitute.For<IExpedienteConsolidadoMerger>(), Substitute.For<IAttachmentStorage>(),
            Substitute.For<IImprontaManualStamper>());
        var sut = new EnviarAlOtHandler(_repo, _lifecycle, firmaImpronta: firmaImpronta);

        var (result, error, _) = await sut.HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
        await _repo.DidNotReceive().GetByIdWithFurGraphAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
