using System;
using System.Collections.Generic;
using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class VinPolicyEvaluatorTests
{
    [Fact]
    public void IsMatriculaInicial_SinTipologia_True()
    {
        VinPolicyEvaluator.IsMatriculaInicial(null).Should().BeTrue();
        VinPolicyEvaluator.IsMatriculaInicial("").Should().BeTrue();
        VinPolicyEvaluator.IsMatriculaInicial(TramiteTipologiaCatalog.CodigoMatriculaInicial).Should().BeTrue();
    }

    [Fact]
    public void IsMatriculaInicial_ConTipologiaTraspaso_False()
    {
        VinPolicyEvaluator.IsMatriculaInicial(TramiteTipologiaCatalog.CodigoTraspasoStandard).Should().BeFalse();
    }

    [Fact]
    public void EvaluarConflicto_SinFilas_Null()
    {
        VinPolicyEvaluator.EvaluarConflicto([]).Should().BeNull();
    }

    [Fact]
    public void EvaluarConflicto_SoloRechazados_Null_PermiteReintentar()
    {
        var existentes = new List<VinTramiteExistente>
        {
            new(Guid.NewGuid(), "rechazado", Paso: 3, Placa: "X", Vin: "ABC123"),
        };
        VinPolicyEvaluator.EvaluarConflicto(existentes).Should().BeNull();
    }

    [Fact]
    public void EvaluarConflicto_BorradorExistente_Duplicado()
    {
        var id = Guid.NewGuid();
        var existentes = new List<VinTramiteExistente>
        {
            new(id, "borrador", Paso: 2, Placa: "QTP710", Vin: "VIN1"),
        };
        var c = VinPolicyEvaluator.EvaluarConflicto(existentes);
        c.Should().NotBeNull();
        c!.Code.Should().Be(VinConflictCode.TramiteDuplicado);
        c.ExistingTramite.Id.Should().Be(id);
        c.Message.Should().Contain("QTP710");
    }

    [Fact]
    public void EvaluarConflicto_Aprobado_BloqueoDePorVida()
    {
        // Fix HU #10876: el estado real de "completado" en TramiteEstado es "aprobado" (no
        // "completado", que nunca se persiste — TramiteEstado.cs:14).
        var existentes = new List<VinTramiteExistente>
        {
            new(Guid.NewGuid(), TramiteEstado.Aprobado, Paso: 5, Placa: "ZZZ", Vin: "VIN2"),
        };
        var c = VinPolicyEvaluator.EvaluarConflicto(existentes);
        c!.Code.Should().Be(VinConflictCode.TramiteMatriculaCompletada);
        c.Message.Should().Contain("una vez");
    }

    [Fact]
    public void EvaluarConflicto_SoloAnulados_Null_PermiteReintentar()
    {
        // AC4 (HU #10876): anulado es estado final y libera la llave, igual que rechazado.
        var existentes = new List<VinTramiteExistente>
        {
            new(Guid.NewGuid(), TramiteEstado.Anulado, Paso: 2, Placa: "X", Vin: "ABC123"),
        };
        VinPolicyEvaluator.EvaluarConflicto(existentes).Should().BeNull();
    }

    [Fact]
    public void EvaluarConflicto_IgnoraRechazadoYPrioriza1erBloqueante()
    {
        // Ordenado por recencia desc: el rechazado se ignora, el borrador (1er bloqueante) define.
        var existentes = new List<VinTramiteExistente>
        {
            new(Guid.NewGuid(), "rechazado", 1, null, "V"),
            new(Guid.NewGuid(), "borrador", 2, null, "V"),
        };
        VinPolicyEvaluator.EvaluarConflicto(existentes)!.Code.Should().Be(VinConflictCode.TramiteDuplicado);
    }

    [Fact]
    public void EvaluarConflicto_IgnoraRechazadoYAnulado_Prioriza1erBloqueante()
    {
        // Ordenado por recencia desc: rechazado y anulado se ignoran, el preparado (1er bloqueante) define.
        var existentes = new List<VinTramiteExistente>
        {
            new(Guid.NewGuid(), TramiteEstado.Rechazado, 1, null, "V"),
            new(Guid.NewGuid(), TramiteEstado.Anulado, 2, null, "V"),
            new(Guid.NewGuid(), TramiteEstado.Preparado, 3, null, "V"),
        };
        VinPolicyEvaluator.EvaluarConflicto(existentes)!.Code.Should().Be(VinConflictCode.TramiteDuplicado);
    }
}
