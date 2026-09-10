using Flit.Admin.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #11504 — el agregado <see cref="AdminIdentityValidation"/> lleva conteo de intentos y solo
/// terminaliza el rechazo cuando se agotan (preventiva del Bug #11503 para el camino admin, que hoy no
/// tiene ningún llamador de <c>ReconcileAsync</c> real contra Kyverum). Cubre AC1 (no terminaliza con
/// cupo), AC2 (terminaliza al agotar), AC3 (aprobación tras rechazos no agotados conserva el histórico) y
/// dedup (mismo intento repetido no duplica el conteo) — todo a nivel de <b>dominio puro</b>, sin
/// servicio ni proveedor.
/// </summary>
public sealed class AdminIdentityValidationAttemptsTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000ee01");
    private static readonly Guid Representative = Guid.Parse("77777777-0000-4000-8000-00000000ee02");
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Uso de ejemplo:
    /// var v = AdminIdentityValidation.CreateSent(tenant, subjectType, subjectRef, "CC", "123", "Juan",
    ///     "juan@x.co", AdminIdentityProviders.Kyverum, null, "kv-1", null, "en_proceso", "{}", now);
    /// var changed = v.RegisterFailedAttempt(now, attemptKey: "2026-08-13T10:00:00Z", providerStatus: "rechazado_intento");
    /// </summary>
    private static AdminIdentityValidation NewSent() =>
        AdminIdentityValidation.CreateSent(
            Tenant, AdminIdentitySubjectTypes.LegalRepresentative, Representative, "CC", "123456789",
            "Juan Perez", "juan@x.co", AdminIdentityProviders.Kyverum, "url", "kv-1", "enc", "en_proceso",
            "{}", Now);

    // ── AC1 — rechazo con intentos disponibles: incrementa, no terminaliza ──────────────────────────

    [Fact]
    public void RegisterFailedAttempt_WithAvailableAttempts_IncrementsWithoutTerminalizing()
    {
        var v = NewSent();

        var changed = v.RegisterFailedAttempt(Now, "attempt-1", "rechazado_intento");

        changed.Should().BeTrue();
        v.Attempts.Should().Be(1);
        v.MaxAttempts.Should().Be(AdminIdentityRules.KyverumMaxIntentos);
        v.Status.Should().NotBe(AdminIdentityEstados.Rechazado);
        // Elegible para reconciliaciones posteriores: no quedó en estado terminal.
        v.Status.Should().BeOneOf(AdminIdentityEstados.Enviado, AdminIdentityEstados.EnProceso);
    }

    // ── AC2 — al agotar el máximo, terminaliza en rechazado ─────────────────────────────────────────

    [Fact]
    public void RegisterFailedAttempt_ReachingMaxAttempts_TerminalizesRejected()
    {
        var v = NewSent();

        v.RegisterFailedAttempt(Now, "attempt-1", "rechazado_intento");
        v.RegisterFailedAttempt(Now, "attempt-2", "rechazado_intento");
        var changed = v.RegisterFailedAttempt(Now, "attempt-3", "rechazado_intento");

        changed.Should().BeTrue();
        v.Attempts.Should().Be(3);
        v.Status.Should().Be(AdminIdentityEstados.Rechazado);

        // Terminal: una reconciliación posterior es no-op (excluida de reconciliaciones futuras).
        var again = v.RegisterFailedAttempt(Now, "attempt-4", "rechazado_intento");
        again.Should().BeFalse();
        v.Attempts.Should().Be(3); // no siguió contando tras terminalizar
    }

    // ── AC3 — aprobación tras rechazos no agotados conserva el histórico de intentos ───────────────

    [Fact]
    public void Approve_AfterNonExhaustedAttempts_KeepsAttemptsHistory()
    {
        var v = NewSent();
        v.RegisterFailedAttempt(Now, "attempt-1", "rechazado_intento");
        v.RegisterFailedAttempt(Now, "attempt-2", "rechazado_intento");

        var transitioned = v.Approve(Now.AddMinutes(5), "cert-123");

        transitioned.Should().BeTrue();
        v.Status.Should().Be(AdminIdentityEstados.Aprobado);
        v.Attempts.Should().Be(2); // conserva el histórico consumido
        v.CertificateHash.Should().Be("cert-123");
    }

    // ── Dedup — el MISMO intento reportado dos veces no duplica el conteo ──────────────────────────

    [Fact]
    public void RegisterFailedAttempt_SameAttemptKeyTwice_IncrementsOnlyOnce()
    {
        var v = NewSent();

        var first = v.RegisterFailedAttempt(Now, "same-attempt-key", "rechazado_intento");
        var second = v.RegisterFailedAttempt(Now.AddMinutes(2), "same-attempt-key", "rechazado_intento");

        first.Should().BeTrue();
        second.Should().BeFalse(); // poll repetido del MISMO intento: no-op
        v.Attempts.Should().Be(1);
        v.Status.Should().NotBe(AdminIdentityEstados.Rechazado);
    }

    [Fact]
    public void RegisterFailedAttempt_DifferentAttemptKeys_IncrementsEachOne()
    {
        var v = NewSent();

        v.RegisterFailedAttempt(Now, "attempt-1", "rechazado_intento");
        v.RegisterFailedAttempt(Now, "attempt-1", "rechazado_intento"); // repetido: no cuenta
        v.RegisterFailedAttempt(Now, "attempt-2", "rechazado_intento"); // nuevo: sí cuenta

        v.Attempts.Should().Be(2);
    }

    /// <summary>
    /// Sin clave de intento no se puede distinguir un intento NUEVO de la re-lectura del mismo, así que
    /// no se cuenta: contar aquí agotaría los intentos a base de pollings y congelaría la validación —
    /// justo el modo de fallo del Bug #11503 que esta HU previene. La dirección segura del error es
    /// tardar (la validación sigue abierta hasta que el proveedor la cierre o expire), nunca congelar.
    /// </summary>
    [Fact]
    public void RegisterFailedAttempt_WithoutAttemptKey_DoesNotCount()
    {
        var v = NewSent();

        v.RegisterFailedAttempt(Now, attemptKey: null, "rechazado_intento");
        v.RegisterFailedAttempt(Now.AddMinutes(2), attemptKey: null, "rechazado_intento");
        v.RegisterFailedAttempt(Now.AddMinutes(4), attemptKey: null, "rechazado_intento");

        v.Attempts.Should().Be(0);
        v.Status.Should().NotBe(AdminIdentityEstados.Rechazado);
        v.ProviderStatus.Should().Be("rechazado_intento"); // la traza sí se refresca
    }

    // ── AC6 — el conteo vive en el agregado admin, no depende del cliente Kyverum ──────────────────

    /// <summary>
    /// AC6: esta prueba NO usa <c>IKyverumVerifyClient</c> ni ningún adaptador de proveedor — solo el
    /// agregado de dominio. Demuestra que la terminalización por conteo es una responsabilidad PROPIA de
    /// <see cref="AdminIdentityValidation"/>: corregir únicamente <c>KyverumVerifyClient</c> (Bug #11503,
    /// ya hecho en esta rama) NO habría bastado para el camino admin, porque antes de esta HU el agregado
    /// no tenía ningún campo de conteo ni método que lo aplicara — un cliente corregido que reportara
    /// <c>rechazado_intento</c> repetidamente se habría quedado sin ningún mecanismo que lo terminalizara.
    /// </summary>
    [Fact]
    public void RegisterFailedAttempt_TerminalizationLogic_IsSelfContainedInDomain_NotInKyverumClient()
    {
        var v = NewSent();

        // Simula 3 reportes de intento fallido con claves DISTINTAS (como si vinieran de un cliente Kyverum
        // ya corregido, reportando rechazado_intento en cada intento real del sujeto). Ningún tipo del
        // cliente Kyverum ni del proveedor participa en esta prueba: el agregado decide solo.
        for (var i = 1; i <= AdminIdentityRules.KyverumMaxIntentos; i++)
        {
            v.RegisterFailedAttempt(Now, $"attempt-{i}", "rechazado_intento");
        }

        v.Status.Should().Be(AdminIdentityEstados.Rechazado);
        v.Attempts.Should().Be(AdminIdentityRules.KyverumMaxIntentos);
    }
}
