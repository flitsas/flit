using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// El gate del mandatario al aprobar admite las DOS formas de firmar (ajuste tras validación manual).
///
/// <para>Miraba solo <c>IdentityVigente</c>, así que devolvía <c>mandatario_identidad_requerida</c> a un
/// mandatario que tenía su firma del baúl vigente y podía firmar perfectamente: el organismo no podía
/// aprobar el trámite y la única salida era validarle una identidad que no hacía falta. Son alternativas,
/// no requisitos acumulativos — la misma precedencia que ya aplica el generador del mandato.</para>
/// </summary>
public sealed class MandatoApprovalFirmaBaulTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OfficeId = Guid.NewGuid();
    private static readonly Guid SignerId = Guid.NewGuid();

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IMandateSignerDirectory _directory = Substitute.For<IMandateSignerDirectory>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();

    [Fact]
    public async Task ConFirmaDelBaulVigente_ElOrganismoPuedeAprobar_AunqueNoTengaIdentidad()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Seed(identidadVigente: false);
        ConFirmaDelBaul();

        var decision = await Handler().CheckAsync(id, Tenant, null, null, ct);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        decision.MandateSignerId.Should().Be(SignerId);
    }

    [Fact]
    public async Task SinNingunaDeLasDos_SeSigueExigiendoQueConsigaConQueFirmar()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Seed(identidadVigente: false);

        var decision = await Handler().CheckAsync(id, Tenant, null, null, ct);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.IdentidadRequerida);
        decision.MandateSignerId.Should().BeNull();
    }

    [Fact]
    public async Task ConIdentidadVigente_SigueAprobandoSinConsultarElBaul()
    {
        // No regresión: la vía que ya funcionaba no cambia, y ni siquiera se pregunta por el baúl —
        // resolverlo cuando ya hay identidad sería una consulta inútil por trámite aprobado.
        var ct = TestContext.Current.CancellationToken;
        var id = Seed(identidadVigente: true);

        var decision = await Handler().CheckAsync(id, Tenant, null, null, ct);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        await _vault.DidNotReceive().ResolveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuienFirmaAMano_NoNecesitaNiBaulNiIdentidad()
    {
        // El documento le deja la línea y él la suscribe en papel. Exigirle una de las dos vías
        // bloquearía un mandato que se firma justamente porque no las tiene.
        var ct = TestContext.Current.CancellationToken;
        var id = Seed(identidadVigente: false, firmaFisica: true);

        var decision = await Handler().CheckAsync(id, Tenant, null, null, ct);

        decision.Outcome.Should().Be(MandatoApprovalOutcome.Resolved);
        // Ni siquiera se consulta el baúl: la marca de firma física ya resuelve.
        await _vault.DidNotReceive().ResolveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LaFirmaDelMandatarioSeBuscaEnElTenantDeLaGestora_NoEnElDelOrganismo()
    {
        // Misma convención que el generador del mandato (HU #11030). Resolverla contra el organismo la
        // buscaría donde no está y el gate volvería a bloquear a quien sí puede firmar.
        var ct = TestContext.Current.CancellationToken;
        var id = Seed(identidadVigente: false);
        ConFirmaDelBaul();

        await Handler().CheckAsync(id, Tenant, null, null, ct);

        await _vault.Received(1).ResolveAsync(Tenant, "CC", "70111222", Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private MandatoApprovalHandler Handler() => new(_repo, _directory, _vault);

    private void ConFirmaDelBaul() =>
        _vault.ResolveAsync(Tenant, "CC", "70111222", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Carlos Ruiz", "hash", "vault/f.png", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "70111222"));

    /// <summary>Trámite con mandato ya generado (que es lo que hace exigible al firmante) y un mandatario.</summary>
    private Guid Seed(bool identidadVigente, bool firmaFisica = false)
    {
        var id = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Entregado,
            ModalidadEntrada = "matricula_inicial",
            TransitOfficeId = OfficeId,
            MandateSignerId = SignerId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = id,
            Tipo = "mandato",
            Filename = "mandato.pdf",
            UploadedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithFurGraphAsync(id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);

        _directory.GetCandidatesAsync(OfficeId, Tenant, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<MandateSignerCandidate>>(
            [
                new MandateSignerCandidate(
                    SignerId, "Carlos Ruiz", "70111222", null, identidadVigente,
                    SignatureVaultId: null, TipoDocumento: "CC", FirmaFisica: firmaFisica),
            ]);
        return id;
    }
}
