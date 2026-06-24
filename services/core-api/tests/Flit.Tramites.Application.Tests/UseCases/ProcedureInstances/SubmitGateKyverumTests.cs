using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// AC5 (HU #10233): el SubmitGate exige identidad APROBADA del comprador. La regla es agnóstica al
/// proveedor (evalúa <c>Estado == aprobado</c>), por lo que una validación Kyverum aprobada satisface el
/// gate y una en proceso lo bloquea con <see cref="SubmitGate.IdentidadRequerida"/>.
/// </summary>
public sealed class SubmitGateKyverumTests
{
    private static ProcedureInstance Matricula(string biometriaEstado, string provider = BiometricProviders.Kyverum)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            Parte = "comprador",
            Estado = biometriaEstado,
            Provider = provider,
            KyverumVerificationId = provider == BiometricProviders.Kyverum ? "kyv_1" : null,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return instance;
    }

    [Fact]
    public void Gate_KyverumApproved_DoesNotRequireIdentidad()
    {
        var errors = SubmitGate.Evaluate(Matricula(BiometricEstados.Aprobado));
        errors.Should().NotContain(SubmitGate.IdentidadRequerida);
    }

    [Fact]
    public void Gate_KyverumInProgress_RequiresIdentidad()
    {
        var errors = SubmitGate.Evaluate(Matricula(BiometricEstados.EnProceso));
        errors.Should().Contain(SubmitGate.IdentidadRequerida);
    }
}
