using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
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
/// HU #11667 — <b>el chip de identidad del listado acredita la firma del baúl.</b>
///
/// <para><b>Qué se corrige.</b> La ruta de LOTE del listado no consultaba el baúl, así que el chip
/// podía contradecir al gate de radicación y al FUR: una parte jurídica que firma desde el baúl salía
/// como pendiente de identidad aunque el trámite se pudiera radicar. El comentario que justificaba la
/// omisión hablaba de un N+1 que ya no existe: el listado materializa las vigencias del baúl en UNA
/// consulta para todos los tenants —con la MISMA llave que la identidad— desde que se añadió la
/// columna «Firmado». Solo faltaba pasárselas al resolver.</para>
///
/// <para><b>Qué se ejercita.</b> El listado real (<see cref="ListProcedureInstancesHandler"/>) y, sobre
/// el MISMO trámite, la ruta per-instancia real (<see cref="GetWizardStateHandler"/>). Lo que se afirma
/// no es un valor aislado sino que <b>las dos rutas coinciden</b>, que es justo lo que fallaba. Se
/// comprueba además que el número de consultas no cambió.</para>
///
/// <para>Uso de ejemplo:
/// <c>var (fila, wizard) = await AmbasRutasAsync(tipoDocumento: "NIT", baulVigente: true);</c>
/// ⇒ <c>fila.CanSubmit == wizard.CanSubmit == true</c>.</para>
/// </summary>
public sealed class ChipIdentidadListadoBaulTests
{
    private const string Documento = "900123456";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    [Fact]
    public async Task ActorJuridicoConBaulVigenteYSinIdentidad_ElChipLoDaPorAprobado_YCoincideConLaRutaPerInstancia()
    {
        var (fila, wizard) = await AmbasRutasAsync("NIT", baulVigente: true);

        fila.CanSubmit.Should().BeTrue("la firma del baúl acredita a la parte jurídica (ADR-0025 D8)");
        fila.CanSubmit.Should().Be(wizard.CanSubmit, "el chip no puede contradecir al gate de radicación");
        fila.PasoActual.Should().Be(fila.TotalPasos);
    }

    [Fact]
    public async Task MecanismoIdentidadConBaulVigente_NoAprobadoPorBaul_EnLasDosRutas()
    {
        // Bug #11141 / HU #11660: el mecanismo elegido manda. Con «sello de validación de identidad»
        // seleccionado, la firma del baúl no se va a consumir y la biométrica sigue haciendo falta.
        var (fila, wizard) = await AmbasRutasAsync(
            "NIT", baulVigente: true, mecanismo: MecanismoFirma.Identidad);

        fila.CanSubmit.Should().BeFalse();
        fila.CanSubmit.Should().Be(wizard.CanSubmit);
    }

    [Fact]
    public async Task BaulVencido_NoAcredita()
    {
        // La consulta en lote trae también las firmas caducadas (la columna "Firmado" necesita
        // distinguirlas): acreditar por la mera existencia de la fila sería peor que no mirar el baúl.
        var (fila, wizard) = await AmbasRutasAsync("NIT", baulVigente: false);

        fila.CanSubmit.Should().BeFalse();
        fila.CanSubmit.Should().Be(wizard.CanSubmit);
    }

    [Fact]
    public async Task PersonaNatural_IgnoraElBaul()
    {
        // Control: con la MISMA clave vigente en el diccionario, un comprador con cédula no queda
        // acreditado. El baúl solo lo consume el representante legal de una persona jurídica.
        var (fila, wizard) = await AmbasRutasAsync("CC", baulVigente: true);

        fila.CanSubmit.Should().BeFalse();
        fila.CanSubmit.Should().Be(wizard.CanSubmit);
    }

    [Fact]
    public async Task NoIntroduceNingunaConsultaNueva()
    {
        // El coste del chip acreditado por baúl es CERO consultas: reutiliza el diccionario que el
        // listado ya traía para la columna "Firmado". Si alguien lo resuelve por fila, esto lo delata.
        var ct = TestContext.Current.CancellationToken;
        var instance = Tramite("NIT");
        Preparar(instance, baulVigente: true);

        await new ListProcedureInstancesHandler(_repo).HandleAsync(instance.TenantId, ct);

        await _repo.Received(1).ListWithSummaryGraphAsync(
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).ListVigenteApprovedIdentityKeysAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).ListFirmaBaulVigenciaKeysAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().FindVigenteApprovedByDocumentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Corre el listado y el asistente sobre el mismo trámite y devuelve las dos vistas.</summary>
    private async Task<(InstanceSummaryDto Fila, WizardStateDto Wizard)> AmbasRutasAsync(
        string tipoDocumento, bool baulVigente, string? mecanismo = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Tramite(tipoDocumento, mecanismo);
        Preparar(instance, baulVigente);

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(instance.TenantId, ct);

        // Ruta per-instancia: el baúl se resuelve por política, y solo devuelve firma si está vigente.
        var vault = new StubBaul(baulVigente ? FirmaVigente() : null);
        var (wizard, _) = await new GetWizardStateHandler(_repo, vaultPolicy: vault)
            .HandleAsync(instance.Id, instance.TenantId, ct);

        return (filas.Should().ContainSingle().Subject, wizard!);
    }

    private void Preparar(ProcedureInstance instance, bool baulVigente)
    {
        _repo.ListWithSummaryGraphAsync(
                instance.TenantId, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([instance]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        // Misma llave canónica que la identidad: tenant|TIPO|NÚMERO. El valor es la VIGENCIA.
        var actor = instance.Actors.First();
        var clave = BiometricRules.IdentidadKey(instance.TenantId, actor.DocumentType, actor.DocumentNumber);
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>(StringComparer.Ordinal) { [clave] = baulVigente });

        _repo.GetByIdWithWizardGraphAsync(
                instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
    }

    /// <summary>
    /// Matrícula inicial lista salvo por la identidad (VIN + preflight verde + adjuntos), para que el
    /// único gate que puede mover <c>CanSubmit</c> sea el de identidad.
    /// </summary>
    private static ProcedureInstance Tramite(string tipoDocumento, string? mecanismo = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
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
            DocumentType = tipoDocumento,
            DocumentNumber = Documento,
            FullName = "Renting SAS",
            Email = "renting@x.com",
            Phone = "3001234567",
            Metadata = ActorMetadataReader.Serialize(
                "Bogotá",
                "Calle 1 # 2-3",
                new ActorRepresentanteLegal("CC", "1090123456", "Ana Representante", "ana@x.com", null, mecanismo)),
        });
        foreach (var tipo in new[] { "factura", "aduana", "impronta" })
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
            });

        return instance;
    }

    private static SignatureVaultMatch FirmaVigente() => new(
        Guid.NewGuid(), "Renting SAS", "sig-hash", "vault/firma.png", "art-sha",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        Documento);

    /// <summary>Baúl que resuelve firma para cualquier documento (o ninguna): aísla el efecto de la regla.</summary>
    private sealed class StubBaul(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(match);
    }
}
