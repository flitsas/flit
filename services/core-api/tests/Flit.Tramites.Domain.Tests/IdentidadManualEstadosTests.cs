using System;
using System.Linq;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #13283 (Feature #13280 A1, Épica #13202) — los estados y el proveedor manual existen como constantes y la
/// clasificación de vigencia los trata como "en curso": nunca vigentes hasta que la revisión apruebe.
/// </summary>
public sealed class IdentidadManualEstadosTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LasConstantesNuevasTienenElValorDeLaBd()
    {
        BiometricEstados.ManualActivo.Should().Be("manual_activo");
        BiometricEstados.PendienteRevisionManual.Should().Be("pendiente_revision_manual");
        BiometricProviders.Manual.Should().Be("manual");
        BiometricApprovalOrigins.Automatica.Should().Be("automatica");
        BiometricApprovalOrigins.Manual.Should().Be("manual");
    }

    [Fact]
    public void LasListasCompletasIncluyenLosValoresNuevosSinDuplicados()
    {
        BiometricEstados.Todos.Should().Contain([BiometricEstados.ManualActivo, BiometricEstados.PendienteRevisionManual]);
        BiometricEstados.Todos.Should().OnlyHaveUniqueItems().And.HaveCount(9);
        BiometricProviders.Todos.Should().OnlyHaveUniqueItems();
        BiometricProviders.Todos.Should().BeEquivalentTo(new[] { "mock", "kyverum", "migracion_v1", "manual" });
    }

    [Fact]
    public void ElEstadoMasLargoCabeEnElLimiteDeLaColumna()
    {
        // status es varchar(40) desde el DDL 130; 'pendiente_revision_manual' (25) no cabía en varchar(20).
        BiometricEstados.Todos.Max(e => e.Length).Should().BeLessThanOrEqualTo(40);
    }

    [Theory]
    [InlineData(BiometricEstados.ManualActivo)]
    [InlineData(BiometricEstados.PendienteRevisionManual)]
    public void LosEstadosManualesClasificanComoEnCurso_NuncaVigente(string estado)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Status = estado,
            Provider = BiometricProviders.Manual,
            // Aunque traigan fechas de aprobación, no son una aprobación: el estado manda.
            ValidatedAt = Now.AddDays(-1),
            ValidUntil = Now.AddDays(29),
        };

        IdentityVigenciaClassifier.Classify(v, Now).Should().Be(IdentityVigenciaEstados.EnCurso);
        BiometricRules.EsAprobadaVigente(v, Now).Should().BeFalse();
    }

    [Fact]
    public void UnaValidacionNuevaNaceSinOrigenNiDatosManuales()
    {
        var v = new ProcedureInstanceBiometricValidation();

        v.ApprovalOrigin.Should().BeNull();
        v.ManualActivatedBy.Should().BeNull();
        v.ConsentAt.Should().BeNull();
        v.ReviewedBy.Should().BeNull();
        v.RejectionReasonCode.Should().BeNull();
        v.Provider.Should().Be(BiometricProviders.Mock, "el default no cambia");
    }

    [Fact]
    public void Approve_NoCambiaDeComportamiento_NoEstampaOrigen()
    {
        var v = new ProcedureInstanceBiometricValidation { Status = BiometricEstados.Enviado };

        v.Approve(Now);

        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ApprovalOrigin.Should().BeNull("el origen lo estampan las HU de los flujos, no Approve");
    }
}
