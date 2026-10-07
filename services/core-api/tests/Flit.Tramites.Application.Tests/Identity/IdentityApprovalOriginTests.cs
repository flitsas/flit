using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// HU #13303 (Épica #13202) — el origen de la aprobación se estampa en el punto único de aprobación
/// (<see cref="IdentityValidationResultApplier"/>): la de Kyverum/mock queda <c>automatica</c>; la del flujo manual la sella
/// <c>SellarAprobacionManual</c> después y <c>manual</c> gana siempre. El DTO del detalle de Identidad lo expone solo si aprobó.
/// </summary>
public sealed class IdentityApprovalOriginTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();

    private static ProcedureInstanceBiometricValidation Fila(string provider, string status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Name = "Persona de prueba",
        DocumentType = "CC",
        DocumentNumber = "900123456",
        Email = "persona@example.test",
        Status = status,
        Provider = provider,
        TokenHash = new string('a', 64),
        ExpiresAt = Now.AddHours(5),
    };

    private static IdentityValidationTerminalResult Resultado(bool aprobada) =>
        new(aprobada, aprobada ? "approved" : "rejected", "{}", 95);

    [Theory]
    [InlineData(BiometricProviders.Kyverum, BiometricEstados.EnProceso)]
    [InlineData(BiometricProviders.Mock, BiometricEstados.Enviado)]
    public async Task Aprobacion_de_Kyverum_o_mock_deja_origen_automatica(string provider, string status)
    {
        var v = Fila(provider, status);

        var aplicado = await new IdentityValidationResultApplier(_events).ApplyAsync(v, Resultado(true), Now, Ct);

        aplicado.Should().BeTrue();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ApprovalOrigin.Should().Be(BiometricApprovalOrigins.Automatica);
    }

    [Fact]
    public async Task Rechazo_no_estampa_origen()
    {
        var v = Fila(BiometricProviders.Kyverum, BiometricEstados.EnProceso);

        await new IdentityValidationResultApplier(_events).ApplyAsync(v, Resultado(false), Now, Ct);

        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.ApprovalOrigin.Should().BeNull();
    }

    [Fact]
    public async Task Aprobacion_manual_pasa_por_el_applier_y_manual_gana_al_sellar()
    {
        var v = Fila(BiometricProviders.Manual, BiometricEstados.PendienteRevisionManual);

        await new IdentityValidationResultApplier(_events).ApplyAsync(v, Resultado(true), Now, Ct);
        v.ApprovalOrigin.Should().BeNull("el applier no marca como automática una aprobación del flujo manual");

        v.SellarAprobacionManual(Guid.NewGuid(), Now);
        v.ApprovalOrigin.Should().Be(BiometricApprovalOrigins.Manual);
    }

    [Fact]
    public async Task Manual_gana_aunque_el_origen_ya_fuera_automatica()
    {
        var v = Fila(BiometricProviders.Manual, BiometricEstados.PendienteRevisionManual);
        v.ApprovalOrigin = BiometricApprovalOrigins.Automatica;

        await new IdentityValidationResultApplier(_events).ApplyAsync(v, Resultado(true), Now, Ct);
        v.SellarAprobacionManual(Guid.NewGuid(), Now);

        v.ApprovalOrigin.Should().Be(BiometricApprovalOrigins.Manual);
    }

    [Theory]
    [InlineData(BiometricEstados.Aprobado, BiometricApprovalOrigins.Automatica, BiometricApprovalOrigins.Automatica)]
    [InlineData(BiometricEstados.Aprobado, BiometricApprovalOrigins.Manual, BiometricApprovalOrigins.Manual)]
    [InlineData(BiometricEstados.Aprobado, null, null)] // aprobada anterior al campo
    [InlineData(BiometricEstados.Rechazado, BiometricApprovalOrigins.Automatica, null)]
    [InlineData(BiometricEstados.EnProceso, null, null)]
    public async Task El_detalle_de_identidad_expone_approvalOrigin_solo_si_esta_aprobada(
        string status, string? origen, string? esperado)
    {
        var v = Fila(BiometricProviders.Kyverum, status);
        v.ApprovalOrigin = origen;
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        repo.ListLinkedProceduresByIdentityDocumentsAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());

        var (dto, error) = await new GetPrevalidacionDetailHandler(repo).HandleAsync(v.TenantId, v.Id, Ct);

        error.Should().BeNull();
        dto!.ApprovalOrigin.Should().Be(esperado);
    }

    [Theory]
    [InlineData(BiometricProviders.Manual, BiometricEstados.Rechazado, "imagen_borrosa", "imagen_borrosa")]
    [InlineData(BiometricProviders.Manual, BiometricEstados.Aprobado, "imagen_borrosa", null)] // solo si está rechazada
    [InlineData(BiometricProviders.Manual, BiometricEstados.Rechazado, null, null)] // rechazada sin código
    [InlineData(BiometricProviders.Kyverum, BiometricEstados.Rechazado, "imagen_borrosa", null)] // solo proveedor manual
    [InlineData(BiometricProviders.Mock, BiometricEstados.Rechazado, "imagen_borrosa", null)]
    public async Task El_detalle_de_identidad_expone_rejectionReasonCode_solo_en_rechazada_manual(
        string provider, string status, string? codigo, string? esperado)
    {
        var v = Fila(provider, status);
        v.RejectionReasonCode = codigo;
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        repo.ListLinkedProceduresByIdentityDocumentsAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<(string, string)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<LinkedProcedureSummary>>());

        var (dto, error) = await new GetPrevalidacionDetailHandler(repo).HandleAsync(v.TenantId, v.Id, Ct);

        error.Should().BeNull();
        dto!.RejectionReasonCode.Should().Be(esperado);
    }
}
