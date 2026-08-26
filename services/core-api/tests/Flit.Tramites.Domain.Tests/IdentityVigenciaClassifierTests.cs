using System;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #11751 (ADR-0050) — clasificación de la validación MÁS RECIENTE de una persona en los CUATRO
/// estados de vigencia que expone la consulta admin por documento: sin_validacion, en_curso,
/// aprobada_vigente, vencida.
///
/// <para>Uso de ejemplo:
/// <c>IdentityVigenciaClassifier.Classify(latest, DateTimeOffset.UtcNow)</c> → uno de
/// <see cref="IdentityVigenciaEstados"/>.</para>
/// </summary>
public sealed class IdentityVigenciaClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    // ── sin_validacion ─────────────────────────────────────────────────────

    [Fact]
    public void SinFila_ClasificaComoSinValidacion()
    {
        IdentityVigenciaClassifier.Classify(null, Now).Should().Be(IdentityVigenciaEstados.SinValidacion);
    }

    [Fact]
    public void Rechazada_ClasificaComoSinValidacion()
    {
        // Decisión de diseño (HU #11751): rechazada se trata igual que ausencia de fila — misma
        // acción (prevalidar de nuevo), y el ADR-0050 exige exactamente cuatro estados.
        var v = new ProcedureInstanceBiometricValidation { Status = BiometricEstados.Rechazado };

        IdentityVigenciaClassifier.Classify(v, Now).Should().Be(IdentityVigenciaEstados.SinValidacion);
    }

    [Fact]
    public void ErrorEnvio_ClasificaComoSinValidacion()
    {
        var v = new ProcedureInstanceBiometricValidation { Status = BiometricEstados.ErrorEnvio };

        IdentityVigenciaClassifier.Classify(v, Now).Should().Be(IdentityVigenciaEstados.SinValidacion);
    }

    // ── en_curso ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BiometricEstados.Enviado)]
    [InlineData(BiometricEstados.EnProceso)]
    [InlineData(BiometricEstados.PendienteEnvio)]
    public void NoTerminal_ClasificaComoEnCurso(string status)
    {
        var v = new ProcedureInstanceBiometricValidation { Status = status };

        IdentityVigenciaClassifier.Classify(v, Now).Should().Be(IdentityVigenciaEstados.EnCurso);
    }

    // ── aprobada_vigente / vencida ─────────────────────────────────────────

    [Fact]
    public void AprobadaDentroDeVentana_ClasificaComoAprobadaVigente()
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Status = BiometricEstados.Aprobado,
            ValidatedAt = Now.AddDays(-1),
            ValidUntil = Now.AddDays(29),
        };

        IdentityVigenciaClassifier.Classify(v, Now).Should().Be(IdentityVigenciaEstados.AprobadaVigente);
    }

    [Fact]
    public void AprobadaFueraDeVentana_ClasificaComoVencida()
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Status = BiometricEstados.Aprobado,
            ValidatedAt = Now.AddDays(-40),
            ValidUntil = Now.AddDays(-10),
        };

        IdentityVigenciaClassifier.Classify(v, Now).Should().Be(IdentityVigenciaEstados.Vencida);
    }

    [Fact]
    public void Expirada_ClasificaComoVencida()
    {
        var v = new ProcedureInstanceBiometricValidation { Status = BiometricEstados.Expirado };

        IdentityVigenciaClassifier.Classify(v, Now).Should().Be(IdentityVigenciaEstados.Vencida);
    }
}
