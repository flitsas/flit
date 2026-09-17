using System;
using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #12571 (Feature #12565) — gates de la solicitud de revocatoria (AC1-AC5). Usa
/// <see cref="BusinessDayCalculator"/> real (simple, sin festivos) porque es determinístico y sin IO;
/// no hace falta un fake para estos escenarios.
/// </summary>
public sealed class RevocationRequestGateTests
{
    private static readonly BusinessDayCalculator Calendar = new();

    // Lunes 2026-09-14 09:00 UTC — aprobación de referencia para todos los escenarios.
    private static readonly DateTimeOffset ApprovedAt = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private static GateResult Evaluate(
        string tramiteStatus = "aprobado",
        string? origin = null,
        bool isMigrated = false,
        DateTimeOffset? approvedAt = null,
        int? windowBusinessDays = null,
        bool hasActiveRequest = false,
        DateTimeOffset? now = null) =>
        RevocationRequestGate.Evaluate(
            tramiteStatus,
            origin,
            isMigrated,
            approvedAt ?? ApprovedAt,
            windowBusinessDays,
            hasActiveRequest,
            now ?? ApprovedAt,
            Calendar);

    // ── AC1 — sin solicitud activa, ventana no configurada o vigente ⇒ permite crear ──────────

    [Fact]
    public void AC1_SinVentanaConfigurada_Permite()
    {
        Evaluate(windowBusinessDays: null, hasActiveRequest: false, now: ApprovedAt.AddDays(400))
            .Ok.Should().BeTrue();
    }

    [Fact]
    public void AC1_VentanaConfiguradaYVigente_Permite()
    {
        // Ventana de 3 días hábiles desde el lunes: vence el jueves. Se solicita el miércoles: dentro.
        Evaluate(windowBusinessDays: 3, hasActiveRequest: false, now: ApprovedAt.AddDays(2))
            .Ok.Should().BeTrue();
    }

    // ── AC2 — ventana configurada y vencida ⇒ 422 ventana_vencida ──────────────────────────────

    [Fact]
    public void AC2_VentanaVencida_Bloquea422()
    {
        // Ventana de 1 día hábil desde el lunes: vence el martes 09:00. Se solicita el viernes: tarde.
        var result = Evaluate(windowBusinessDays: 1, now: ApprovedAt.AddDays(4));

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(RevocationRequestGate.VentanaVencida);
    }

    [Fact]
    public void AC2_ExactamenteEnElLimite_Permite()
    {
        // El límite (vence) es inclusive: now == vence todavía permite.
        var vence = Calendar.AddBusinessDays(ApprovedAt, 2);

        Evaluate(windowBusinessDays: 2, now: vence).Ok.Should().BeTrue();
    }

    [Fact]
    public void AC2_UnSegundoDespuesDelLimite_Bloquea()
    {
        var vence = Calendar.AddBusinessDays(ApprovedAt, 2);

        var result = Evaluate(windowBusinessDays: 2, now: vence.AddSeconds(1));

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(RevocationRequestGate.VentanaVencida);
    }

    // ── AC3 — trámite no creado en FLIT ⇒ 422 origen_no_soportado ──────────────────────────────

    [Fact]
    public void AC3_OrigenIct_Bloquea422()
    {
        var result = Evaluate(origin: "ict");

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(RevocationRequestGate.OrigenNoSoportado);
    }

    [Fact]
    public void AC3_IsMigrated_Bloquea422()
    {
        // Migrado de V1: aunque origin sea null, IsMigrated manda (mismo criterio que TramiteFuente.Desde).
        var result = Evaluate(origin: null, isMigrated: true);

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(RevocationRequestGate.OrigenNoSoportado);
    }

    [Fact]
    public void AC3_OrigenDashboard_NoBloquea()
    {
        Evaluate(origin: null, isMigrated: false).Ok.Should().BeTrue();
    }

    // ── AC4 — ya hay una solicitud activa ⇒ 409 solicitud_activa_existente ─────────────────────

    [Fact]
    public void AC4_SolicitudActivaExistente_Bloquea409()
    {
        var result = Evaluate(hasActiveRequest: true);

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(RevocationRequestGate.SolicitudActivaExistente);
    }

    // ── AC5 — reintento tras rechazo con ventana vigente (o sin límite) ⇒ permite ──────────────

    [Fact]
    public void AC5_ReintentoTrasRechazoConVentanaVigente_Permite()
    {
        // Una solicitud "rechazada" NO es activa: el llamador (repositorio) no la reporta como
        // hasActiveRequest=true. La ventana se sigue contando desde la aprobación ORIGINAL.
        Evaluate(windowBusinessDays: 5, hasActiveRequest: false, now: ApprovedAt.AddDays(3))
            .Ok.Should().BeTrue();
    }

    [Fact]
    public void AC5_ReintentoTrasRechazoSinLimite_Permite()
    {
        Evaluate(windowBusinessDays: null, hasActiveRequest: false, now: ApprovedAt.AddDays(1000))
            .Ok.Should().BeTrue();
    }

    [Fact]
    public void AC5_ReintentoTrasRechazoConVentanaYaVencida_Bloquea()
    {
        // La ventana corre desde la aprobación ORIGINAL, no desde el rechazo: un reintento tardío
        // sigue bloqueado igual que una primera solicitud tardía (AC2).
        var result = Evaluate(windowBusinessDays: 1, hasActiveRequest: false, now: ApprovedAt.AddDays(10));

        result.Ok.Should().BeFalse();
        result.Code.Should().Be(RevocationRequestGate.VentanaVencida);
    }

    // ── Precondición de estado — el gate asume Aprobado; el resto es responsabilidad del endpoint ──

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    public void EstadoDistintoDeAprobado_LanzaInvalidOperationException(string estado)
    {
        var act = () => RevocationRequestGate.Evaluate(
            estado, null, false, ApprovedAt, null, false, ApprovedAt, Calendar);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CalculadorNulo_LanzaArgumentNullException()
    {
        var act = () => RevocationRequestGate.Evaluate(
            TramiteEstado.Aprobado, null, false, ApprovedAt, 5, false, ApprovedAt, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
