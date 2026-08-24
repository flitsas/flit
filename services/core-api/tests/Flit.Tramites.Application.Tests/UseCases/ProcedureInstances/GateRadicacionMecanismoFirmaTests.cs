using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11660 — <b>el gate de radicación respeta el mecanismo de firma elegido.</b>
///
/// <para><b>El defecto.</b> La ruta que decide si una parte cuenta como identidad aprobada daba por
/// buena cualquier firma de baúl vigente, sin mirar si el gestor había elegido justamente el otro
/// mecanismo. Con «sello de validación de identidad» seleccionado y una firma de baúl al día, el
/// trámite se radicaba <i>sin que la biométrica se hubiera hecho</i>: el generador de documentos —que
/// sí consulta el mecanismo desde el Bug #11141— iba a estampar un sello de identidad que no existía.</para>
///
/// <para>ADR-0039 prescribe este cambio y nombra el Bug #11141 como causa: el gate debe consumir
/// <see cref="FirmaBaulCobertura.Aplica"/>, que es el predicado único e incluye el mecanismo.</para>
///
/// <para><b>Por dónde se prueba.</b> El resolver es <c>internal</c>; el gate de identidad del
/// asistente es el consumidor público de esa misma resolución y el sitio donde el gestor ve el
/// veredicto (<c>canSubmit</c>).</para>
///
/// <para>Uso de ejemplo:
/// <c>Asistente(mecanismo: MecanismoFirma.Identidad, conBaul: true, conIdentidadVigente: false)</c>
/// ⇒ <c>CanSubmit == false</c>.</para>
/// </summary>
public sealed class GateRadicacionMecanismoFirmaTests
{
    private const string RlTipoDocumento = "CC";
    private const string RlDocumento = "1090123456";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    [Fact]
    public async Task EligiendoIdentidad_ConFirmaDeBaulVigente_NoAprueba()
    {
        // El caso del Bug #11141 llevado al gate: tener firma no es ir a usarla. Si el mecanismo elegido
        // es el sello de identidad, la parte solo se acredita con la biométrica hecha.
        var ct = TestContext.Current.CancellationToken;
        var handler = Asistente(MecanismoFirma.Identidad, conBaul: true, conIdentidadVigente: false);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task EligiendoElBaul_ConFirmaVigente_Aprueba()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = Asistente(MecanismoFirma.Baul, conBaul: true, conIdentidadVigente: false);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.Blockers.Should().NotContain(TramiteEstadoErrores.IdentidadNoAprobada);
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task SinEleccionExplicita_ConFirmaVigente_Aprueba()
    {
        // Sin elección manda la precedencia del baúl (HU #11031). El cambio no puede endurecer el gate
        // para la mayoría de trámites, que nunca eligen mecanismo.
        var ct = TestContext.Current.CancellationToken;
        var handler = Asistente(mecanismo: null, conBaul: true, conIdentidadVigente: false);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task EligiendoIdentidad_ConValidacionBiometricaVigente_Aprueba()
    {
        // La contrapartida: quien eligió el sello y ya validó su identidad radica sin estorbo. El gate
        // se endurece solo para el camino que no acredita nada.
        var ct = TestContext.Current.CancellationToken;
        var handler = Asistente(MecanismoFirma.Identidad, conBaul: true, conIdentidadVigente: true);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.Blockers.Should().NotContain(TramiteEstadoErrores.IdentidadNoAprobada);
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task EligiendoIdentidad_SinBaulNiValidacion_NoAprueba()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = Asistente(MecanismoFirma.Identidad, conBaul: false, conIdentidadVigente: false);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
        result.CanSubmit.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Matrícula inicial completa salvo por la identidad (VIN, preflight verde, checklist documental),
    /// con un comprador jurídico cuyo representante legal lleva el mecanismo indicado.
    /// </summary>
    private GetWizardStateHandler Asistente(string? mecanismo, bool conBaul, bool conIdentidadVigente)
    {
        var now = DateTimeOffset.UtcNow;
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = now,
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
            CreatedAt = now,
        });
        instance.Actors.Add(CompradorJuridico(mecanismo));
        foreach (var tipo in new[] { "factura", "aduana", "impronta" })
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
            });

        if (conIdentidadVigente)
            instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                PartyRole = "comprador",
                Name = "Ana Representante",
                DocumentType = RlTipoDocumento,
                DocumentNumber = RlDocumento,
                Email = "rep@empresa.com",
                Status = BiometricEstados.Aprobado,
                Provider = BiometricProviders.Kyverum,
                TokenHash = "hash",
                ValidatedAt = now.AddDays(-1),
                ValidUntil = now.AddDays(29),
                ExpiresAt = now.AddHours(1),
                CreatedAt = now.AddDays(-1),
            });

        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        return conBaul
            ? new GetWizardStateHandler(_repo, vaultPolicy: new StubBaul(FirmaVigente()))
            : new GetWizardStateHandler(_repo);
    }

    private static ProcedureInstanceActor CompradorJuridico(string? mecanismo)
    {
        var rl = new Dictionary<string, object?>
        {
            ["tipoDocumento"] = RlTipoDocumento,
            ["numeroDocumento"] = RlDocumento,
            ["nombreCompleto"] = "Ana Representante",
            ["email"] = "rep@empresa.com",
        };
        if (mecanismo is not null)
            rl["mecanismoFirma"] = mecanismo;

        return new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "Empresa Compradora SAS",
            Email = "contacto@empresa.com",
            Phone = "3001234567",
            PersonType = ActorPersonTypes.Juridical,
            Metadata = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["ciudad"] = "Bogotá",
                ["direccion"] = "Calle 1 # 2-3",
                ["representanteLegal"] = rl,
            }),
        };
    }

    private static SignatureVaultMatch FirmaVigente() => new(
        Guid.NewGuid(), "Ana Representante", "sig-hash", "vault/firma.png", "art-sha",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        RlDocumento);

    /// <summary>Baúl con firma vigente para el representante legal del trámite.</summary>
    private sealed class StubBaul(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(match);
    }
}
