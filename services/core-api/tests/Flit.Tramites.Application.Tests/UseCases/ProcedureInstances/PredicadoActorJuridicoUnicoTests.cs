using System.Text.RegularExpressions;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11661 — <b>un solo predicado de "actor jurídico" en el dominio de firma.</b>
///
/// <para><b>Qué se fija.</b> La ruta del baúl (la que decide si una parte cuenta como identidad
/// aprobada sin biométrica) pregunta por el tipo de documento a través de
/// <see cref="FirmaBaulCobertura.EsJuridico"/> y de nadie más. Hasta esta HU
/// <c>IdentityApprovalResolver</c> llevaba una copia local con la misma regla; ADR-0039 señala
/// exactamente esa duplicación como el origen del Bug #11141.</para>
///
/// <para><b>Por qué se prueba por el asistente y no por el resolver.</b> El resolver es
/// <c>internal</c> y no hay <c>InternalsVisibleTo</c> hacia este ensamblado. El camino real que lo
/// consume —el gate de identidad del asistente— sí es público, y es además donde el defecto se vería.</para>
///
/// <para>Uso de ejemplo:
/// <c>new GetWizardStateHandler(repo, vaultPolicy: new StubBaul(match)).HandleAsync(id, tenant, ct)</c>
/// ⇒ el paso de identidad queda <c>complete</c> si la parte jurídica está cubierta por el baúl.</para>
/// </summary>
public sealed class PredicadoActorJuridicoUnicoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    // ── Comportamiento: "N" (código RUNT del NIT) debe pesar igual que "NIT" ──────────────────

    [Theory]
    [InlineData("NIT")]
    [InlineData("N")]
    [InlineData("nit")]
    public async Task ActorJuridico_CubiertoPorElBaul_CuentaComoIdentidadAprobada(string tipoDocumento)
    {
        // Algunos proveedores de consulta devuelven "N" donde el resto del sistema escribe "NIT". Si el
        // predicado dejara fuera esa variante, la misma empresa quedaría aprobada o bloqueada según por
        // dónde entraron sus datos.
        var ct = TestContext.Current.CancellationToken;
        var (handler, _) = Asistente(tipoDocumento, conBaul: true);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("complete");
        result.Blockers.Should().NotContain(TramiteEstadoErrores.IdentidadNoAprobada);
        result.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task PersonaNatural_NoEntraPorLaRutaDelBaul_AunqueElBaulResuelvaFirma()
    {
        // Control que da sentido al anterior: con la MISMA política de baúl (que resuelve firma para
        // cualquier documento), un comprador con cédula no queda aprobado. El baúl solo lo consume el
        // representante legal de una persona jurídica (ADR-0025 §4).
        var ct = TestContext.Current.CancellationToken;
        var (handler, _) = Asistente("CC", conBaul: true);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
        result.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task ActorJuridico_SinFirmaEnElBaul_NoQuedaAprobado()
    {
        // Ser jurídico no basta: sin firma vigente que plasmar, la identidad sigue haciendo falta.
        var ct = TestContext.Current.CancellationToken;
        var (handler, _) = Asistente("NIT", conBaul: false);

        var (result, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
        result.CanSubmit.Should().BeFalse();
    }

    // ── Contrato: no puede quedar ninguna otra copia del predicado ────────────────────────────

    /// <summary>
    /// Reconoce la forma en que se escribe la copia: comparar un valor contra el literal <c>"N"</c>
    /// con <c>StringComparison</c>. Es justo la línea que distingue a este predicado de cualquier otra
    /// comparación de tipos de documento del módulo.
    /// </summary>
    private static readonly Regex ComparacionConNit = new(
        @"string\.Equals\([^)]*,\s*""N""\s*,\s*StringComparison",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ElPredicadoDeActorJuridicoVive_SoloEn_FirmaBaulCobertura()
    {
        var raiz = Path.Combine(
            RepoRoot(), "services", "core-api", "src", "Flit.Tramites.Application");
        Directory.Exists(raiz).Should().BeTrue("el proyecto de aplicación de trámites debe existir");

        var infractores = Directory
            .EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !string.Equals(
                    Path.GetFileName(f), "FirmaBaulCobertura.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => ComparacionConNit.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetFileName(f))
            .ToList();

        infractores.Should().BeEmpty(
            "el predicado de actor jurídico debe consumirse desde FirmaBaulCobertura.EsJuridico; "
            + "una segunda copia es la forma en que nació el Bug #11141");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "services", "core-api", "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("No se encontró la raíz del repositorio desde el directorio de test.");
    }

    /// <summary>
    /// Trámite de matrícula inicial listo salvo por la identidad: VIN, preflight en verde y checklist
    /// documental completo. Así el único gate que puede mover el resultado es el de identidad.
    /// </summary>
    private (GetWizardStateHandler Handler, ProcedureInstance Instance) Asistente(
        string tipoDocumentoComprador, bool conBaul)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
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
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

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
