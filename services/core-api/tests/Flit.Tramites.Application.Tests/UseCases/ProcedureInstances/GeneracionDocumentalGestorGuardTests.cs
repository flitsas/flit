using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11051 — gate que aplican los endpoints del GESTOR antes de generar documentación. Con el trámite
/// aprobado o anulado la documentación del expediente es definitiva y no se regenera desde el gestor.
/// El sistema (aprobación del OT, asignación de placa, identidad validada, transiciones) NO pasa por
/// este gate a propósito: regenera en estado final por diseño (HU #10996).
/// </summary>
public sealed class GeneracionDocumentalGestorGuardTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GeneracionDocumentalGestorGuard _guard;

    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public GeneracionDocumentalGestorGuardTests()
    {
        _guard = new GeneracionDocumentalGestorGuard(_repo);
    }

    private void Arrange(string status)
    {
        var instance = new ProcedureInstance
        {
            Id = Id,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-011051",
            Status = status,
            ModalidadEntrada = "traspaso",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdAsync(Id, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    // AC1 — trámite aprobado: el gestor no puede generar ni regenerar.
    [Fact]
    public async Task Aprobado_DevuelveElCodigoDeBloqueo()
    {
        Arrange(TramiteEstado.Aprobado);

        var error = await _guard.CheckAsync(Id, TenantId, TestContext.Current.CancellationToken);

        error.Should().Be(TramiteEstadoErrores.GeneracionBloqueadaEstadoFinal);
    }

    // AC2 — trámite anulado: mismo criterio.
    [Fact]
    public async Task Anulado_DevuelveElCodigoDeBloqueo()
    {
        Arrange(TramiteEstado.Anulado);

        var error = await _guard.CheckAsync(Id, TenantId, TestContext.Current.CancellationToken);

        error.Should().Be(TramiteEstadoErrores.GeneracionBloqueadaEstadoFinal);
    }

    // AC4 — estados en proceso: la generación procede como hasta ahora.
    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    public async Task EstadoNoFinal_NoBloquea(string status)
    {
        Arrange(status);

        var error = await _guard.CheckAsync(Id, TenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
    }

    // El trámite de otro tenant (o inexistente) no debe reportarse como "bloqueado por estado": es 404.
    [Fact]
    public async Task InstanciaInexistente_DevuelveNotFound()
    {
        _repo.GetByIdAsync(Id, TenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var error = await _guard.CheckAsync(Id, TenantId, TestContext.Current.CancellationToken);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
    }

    // Lectura ligera: el gate solo necesita el status, así que NO debe cargar los grafos del expediente
    // (checklist/adjuntos/actores) por cada intento de generación.
    [Fact]
    public async Task NoCargaGrafosDelExpediente()
    {
        Arrange(TramiteEstado.Borrador);

        await _guard.CheckAsync(Id, TenantId, TestContext.Current.CancellationToken);

        await _repo.Received(1).GetByIdAsync(Id, TenantId, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithWizardGraphAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
