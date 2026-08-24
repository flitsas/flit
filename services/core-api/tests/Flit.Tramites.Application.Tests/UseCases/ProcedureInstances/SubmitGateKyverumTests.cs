using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// AC5 (HU #10233): el SubmitGate exige identidad APROBADA del comprador. La regla es agnóstica al
/// proveedor (evalúa <c>Estado == aprobado</c>), por lo que una validación Kyverum aprobada satisface el
/// gate y una en proceso lo bloquea con <see cref="SubmitGate.IdentidadNoAprobada"/>.
/// </summary>
public sealed class SubmitGateKyverumTests
{
    private static ProcedureInstance Matricula(string biometriaEstado, string provider = BiometricProviders.Kyverum)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = biometriaEstado,
            Provider = provider,
            KyverumVerificationId = provider == BiometricProviders.Kyverum ? "kyv_1" : null,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return instance;
    }

    // El gate ahora recibe el set de partes con identidad vigente aprobada PER-PERSONA (HU #10350): la
    // resolución por documento/vigencia vive en IdentityApprovalResolver/FindVigenteApprovedByDocument, no en
    // el gate. Aquí se prueba que el gate exige/omite identidad_no_aprobada según ese set.
    private static HashSet<string> Aprobadas(params string[] partes) =>
        new(partes, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Gate_CompradorAprobado_DoesNotRequireIdentidad()
    {
        var errors = SubmitGate.Evaluate(Matricula(BiometricEstados.Aprobado), Aprobadas("comprador"));
        errors.Should().NotContain(SubmitGate.IdentidadNoAprobada);
    }

    [Fact]
    public void Gate_CompradorSinIdentidad_RequiresIdentidad()
    {
        var errors = SubmitGate.Evaluate(Matricula(BiometricEstados.EnProceso), Aprobadas());
        errors.Should().Contain(SubmitGate.IdentidadNoAprobada);
    }
}
