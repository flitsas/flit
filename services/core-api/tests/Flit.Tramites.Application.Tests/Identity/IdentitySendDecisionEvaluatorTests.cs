using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>HU #11263 — precedencia única de decisión de envío (CF-01 / ADR-0039).</summary>
public sealed class IdentitySendDecisionEvaluatorTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AC1_Baul_tiene_precedencia_y_no_envia()
    {
        var actor = ActorJuridicoConBaul();
        var ctx = new IdentitySendDecisionContext(
            Tenant, "NIT", "900123", actor,
            HasBaulFirmaActivaVigente: true,
            ValidationsForPerson: Array.Empty<ProcedureInstanceBiometricValidation>(),
            Now);

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.NoEnviar);
        d.Motivo.Should().Be(IdentitySendMotivo.CoberturaBaul);
        d.Origen.Should().Be(IdentitySendOrigen.Baul);
    }

    [Fact]
    public void AC1_Baul_no_aplica_si_mecanismo_es_sello_identidad()
    {
        var actor = ActorJuridicoConMecanismo(MecanismoFirma.Identidad);
        var vigente = AprobadaVigente("NIT", "900123");
        var ctx = new IdentitySendDecisionContext(
            Tenant, "NIT", "900123", actor,
            HasBaulFirmaActivaVigente: true, // hay firma, pero el gestor eligió sello → Aplica=false
            ValidationsForPerson: new[] { vigente },
            Now);

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        // Sin baúl aplicable, cae a identidad vigente
        d.Kind.Should().Be(IdentitySendDecisionKind.NoEnviar);
        d.Motivo.Should().Be(IdentitySendMotivo.IdentidadVigente);
    }

    [Fact]
    public void AC2_Identidad_aprobada_vigente_no_envia_y_devuelve_metadatos()
    {
        var v = AprobadaVigente("CC", "123");
        var ctx = Ctx("CC", "123", validations: new[] { v });

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.NoEnviar);
        d.Motivo.Should().Be(IdentitySendMotivo.IdentidadVigente);
        d.ValidationId.Should().Be(v.Id);
        d.Status.Should().Be(BiometricEstados.Aprobado);
        d.ValidatedAt.Should().Be(v.ValidatedAt);
        d.ValidUntil.Should().NotBeNull();
        d.Origen.Should().Be(IdentitySendOrigen.Tramite);
    }

    [Fact]
    public void AC2_Empata_documento_con_normalizacion_canonica()
    {
        var v = AprobadaVigente("cc", " 123 ");
        var ctx = Ctx("CC", "123", validations: new[] { v });

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.NoEnviar);
        d.Motivo.Should().Be(IdentitySendMotivo.IdentidadVigente);
    }

    [Fact]
    public void AC3_En_vuelo_con_enlace_vigente_no_envia()
    {
        var v = EnVuelo("CC", "1", expiresAt: Now.AddHours(2));
        var ctx = Ctx("CC", "1", validations: new[] { v });

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.NoEnviar);
        d.Motivo.Should().Be(IdentitySendMotivo.ValidacionEnVuelo);
        d.ValidationId.Should().Be(v.Id);
    }

    [Fact]
    public void AC4_En_vuelo_con_enlace_vencido_encauza_reenvio_sin_crear_fila()
    {
        var v = EnVuelo("CC", "1", expiresAt: Now.AddHours(-1));
        var ctx = Ctx("CC", "1", validations: new[] { v });

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.EncauzarReenvio);
        d.Motivo.Should().Be(IdentitySendMotivo.EnlaceVencidoReenvio);
        d.ValidationId.Should().Be(v.Id);
    }

    [Fact]
    public void AC5_Sin_cobertura_corresponde_enviar()
    {
        var ctx = Ctx("CC", "999", validations: Array.Empty<ProcedureInstanceBiometricValidation>());

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.Enviar);
        d.Motivo.Should().Be(IdentitySendMotivo.CorrespondeEnviar);
    }

    /// <summary>
    /// Reporte 2026-09-03 — una fila «en vuelo» HUÉRFANA (nunca se resolvió y quedó atrás cuando un
    /// intento POSTERIOR ya se expiró/rechazó) no puede seguir bloqueando el envío para siempre. El
    /// módulo de Identidad mostraba la fila NUEVA (expirada, la más reciente por <c>CreatedAt</c>) y el
    /// guard de envío seguía viendo bloqueado por la fila VIEJA (en vuelo) — dos superficies mirando
    /// filas distintas del mismo documento. Ahora el chequeo «en vuelo» solo mira la fila más reciente.
    /// </summary>
    [Fact]
    public void FilaEnVueloHuerfana_SuperadaPorUnIntentoPosteriorYaTerminal_CorrespondeEnviar()
    {
        var vieja = EnVuelo("CC", "1", expiresAt: Now.AddHours(2), createdAt: Now.AddDays(-10));
        var nueva = Expirada("CC", "1", createdAt: Now.AddDays(-1));
        var ctx = Ctx("CC", "1", validations: new[] { vieja, nueva });

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.Enviar);
        d.Motivo.Should().Be(IdentitySendMotivo.CorrespondeEnviar);
    }

    [Fact]
    public void FilaEnVueloEsLaMasReciente_SigueBloqueandoAunqueHayaUnaViejaExpirada()
    {
        // Control: si la fila en vuelo SÍ es la más reciente, el bloqueo sigue intacto — el fix no
        // desactiva la guarda, solo la ata a la fila correcta.
        var vieja = Expirada("CC", "1", createdAt: Now.AddDays(-10));
        var nueva = EnVuelo("CC", "1", expiresAt: Now.AddHours(2), createdAt: Now.AddDays(-1));
        var ctx = Ctx("CC", "1", validations: new[] { vieja, nueva });

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.NoEnviar);
        d.Motivo.Should().Be(IdentitySendMotivo.ValidacionEnVuelo);
        d.ValidationId.Should().Be(nueva.Id);
    }

    [Fact]
    public void DosFilasEnVuelo_EncauzaReenvioConLaMasReciente()
    {
        var vieja = EnVuelo("CC", "1", expiresAt: Now.AddHours(-5), createdAt: Now.AddDays(-2));
        var nueva = EnVuelo("CC", "1", expiresAt: Now.AddHours(-1), createdAt: Now.AddDays(-1));
        var ctx = Ctx("CC", "1", validations: new[] { vieja, nueva });

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Kind.Should().Be(IdentitySendDecisionKind.EncauzarReenvio);
        d.ValidationId.Should().Be(nueva.Id);
    }

    [Fact]
    public void Precedencia_baul_gana_sobre_identidad_vigente()
    {
        var actor = ActorJuridicoConBaul();
        var vigente = AprobadaVigente("NIT", "900123");
        var ctx = new IdentitySendDecisionContext(
            Tenant, "NIT", "900123", actor,
            HasBaulFirmaActivaVigente: true,
            ValidationsForPerson: new[] { vigente },
            Now);

        var d = IdentitySendDecisionEvaluator.Evaluate(ctx);

        d.Motivo.Should().Be(IdentitySendMotivo.CoberturaBaul);
    }

    private static IdentitySendDecisionContext Ctx(
        string tipo,
        string numero,
        IReadOnlyList<ProcedureInstanceBiometricValidation> validations,
        ProcedureInstanceActor? actor = null,
        bool hasBaul = false) =>
        new(Tenant, tipo, numero, actor, hasBaul, validations, Now);

    private static ProcedureInstanceBiometricValidation AprobadaVigente(string tipo, string numero) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = Guid.NewGuid(),
            DocumentType = tipo,
            DocumentNumber = numero,
            Status = BiometricEstados.Aprobado,
            ValidatedAt = Now.AddDays(-5),
            ValidUntil = Now.AddDays(25),
            ExpiresAt = Now.AddDays(-4),
            UpdatedAt = Now.AddDays(-5),
        };

    private static ProcedureInstanceBiometricValidation EnVuelo(
        string tipo, string numero, DateTimeOffset expiresAt, DateTimeOffset? createdAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = Guid.NewGuid(),
            DocumentType = tipo,
            DocumentNumber = numero,
            Status = BiometricEstados.EnProceso,
            ExpiresAt = expiresAt,
            CreatedAt = createdAt ?? Now.AddMinutes(-10),
            UpdatedAt = Now.AddMinutes(-10),
        };

    private static ProcedureInstanceBiometricValidation Expirada(
        string tipo, string numero, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = Guid.NewGuid(),
            DocumentType = tipo,
            DocumentNumber = numero,
            Status = BiometricEstados.Expirado,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    private static ProcedureInstanceActor ActorJuridicoConBaul() =>
        ActorJuridicoConMecanismo(null); // sin elección → Aplica=true (precedencia baúl)

    private static ProcedureInstanceActor ActorJuridicoConMecanismo(string? mecanismo)
    {
        var rl = new Dictionary<string, object?>
        {
            ["tipoDocumento"] = "CC",
            ["numeroDocumento"] = "1",
            ["nombreCompleto"] = "RL",
        };
        if (mecanismo is not null)
            rl["mecanismoFirma"] = mecanismo;

        var metadata = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object?> { ["representanteLegal"] = rl });

        return new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            DocumentType = "NIT",
            DocumentNumber = "900123",
            ActorType = "comprador",
            Metadata = metadata,
        };
    }
}
