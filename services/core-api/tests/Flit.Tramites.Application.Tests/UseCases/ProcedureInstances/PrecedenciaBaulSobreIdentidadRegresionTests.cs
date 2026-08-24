using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11754 (ADR-0050) — regresión de la precedencia D8 del ADR-0025: el BAÚL de firmas precede a la
/// identidad biométrica. Este ADR-0050 mueve la fuente de verdad de la identidad ADMINISTRATIVA
/// (representantes legales, mandatarios) de <c>admin.admin_identity_validations</c> al módulo Identidad,
/// pero <b>NO TOCA</b> <see cref="IdentityApprovalResolver"/> ni <see cref="FirmaBaulCobertura"/>: esta
/// suite fija ese límite con un test que debe seguir en verde exactamente igual antes y después del
/// cambio de fuente (HU #11751/#11752/#11753), porque ejercita un camino distinto (comprador/vendedor
/// de un trámite, no el directorio de mandatarios).
///
/// <para>Uso de ejemplo:
/// <c>new GetWizardStateHandler(repo, vaultPolicy: new StubBaul(match)).HandleAsync(id, tenant, ct)</c>
/// con <c>repo.FindVigenteApprovedByDocumentAsync(...)</c> devolviendo <c>null</c> (SIN identidad
/// vigente) ⇒ el paso de identidad queda <c>complete</c> igual, porque el baúl solo basta.</para>
/// </summary>
public sealed class PrecedenciaBaulSobreIdentidadRegresionTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    [Fact]
    public async Task ActorJuridico_ConBaulVigente_YSinIdentidadVigente_QuedaAprobado()
    {
        // El núcleo de D8: firma de baúl vigente + CERO identidad vigente (repo.FindVigenteApprovedByDocumentAsync
        // devuelve null explícitamente) sigue resolviendo "aprobado". Si alguna vez la resolución de
        // identidad administrativa se enrutara por error hacia este camino, o si el cambio de fuente
        // (HU #11751/52) alterara esta consulta, este test lo detecta.
        var ct = TestContext.Current.CancellationToken;
        var (handler, _) = Asistente(tipoDocumentoComprador: "NIT", conBaul: true, conIdentidadVigente: false);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.Blockers.Should().NotContain(TramiteEstadoErrores.IdentidadNoAprobada);
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task ActorJuridico_SinBaul_ConIdentidadVigente_QuedaAprobadoPorElCaminoNormal()
    {
        // Control: sin baúl, el paso 2 (identidad biométrica vigente) sigue funcionando igual — el
        // cambio de fuente de HU #11751/52 no toca esta consulta tampoco.
        var ct = TestContext.Current.CancellationToken;
        var (handler, _) = Asistente(tipoDocumentoComprador: "NIT", conBaul: false, conIdentidadVigente: true);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task ActorJuridico_SinBaulNiIdentidad_NoQuedaAprobado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (handler, _) = Asistente(tipoDocumentoComprador: "NIT", conBaul: false, conIdentidadVigente: false);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
        result.CanSubmit.Should().BeFalse();
    }

    // ── Helpers (mismo fixture que PredicadoActorJuridicoUnicoTests) ───────────

    private (GetWizardStateHandler Handler, ProcedureInstance Instance) Asistente(
        string tipoDocumentoComprador, bool conBaul, bool conIdentidadVigente)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000002",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "vin",
            ValueText = "1HGCM82633A004352",
            Source = "user",
        });
        instance.PreflightSnapshots.Add(new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(),
            Overall = "green",
            Checks = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = tipoDocumentoComprador,
            DocumentNumber = "900123456",
            FullName = "Renting SAS",
            Email = "renting@x.com",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize("Bogotá", "Calle 1 # 2-3", null),
        });
        foreach (var tipo in new[] { "factura", "aduana", "impronta" })
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
            });

        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);

        // Punto central del test: el repo de identidad biométrica devuelve o no una fila vigente, con
        // total independencia de la fuente de identidad ADMINISTRATIVA que HU #11751/52 cambia.
        var identidadVigente = conIdentidadVigente
            ? new ProcedureInstanceBiometricValidation
            {
                Status = BiometricEstados.Aprobado,
                DocumentType = tipoDocumentoComprador,
                DocumentNumber = "900123456",
                ValidatedAt = DateTimeOffset.UtcNow,
                ValidUntil = DateTimeOffset.UtcNow.AddDays(10),
            }
            : null;
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(identidadVigente);

        var handler = conBaul
            ? new GetWizardStateHandler(_repo, vaultPolicy: new StubBaul(FirmaVigente()))
            : new GetWizardStateHandler(_repo);
        return (handler, instance);
    }

    private static SignatureVaultMatch FirmaVigente() => new(
        Guid.NewGuid(), "Renting SAS", "sig-hash", "vault/firma.png", "art-sha",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        "900123456");

    /// <summary>Baúl que resuelve firma para cualquier documento: aísla el efecto del predicado.</summary>
    private sealed class StubBaul(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(match);
    }
}
