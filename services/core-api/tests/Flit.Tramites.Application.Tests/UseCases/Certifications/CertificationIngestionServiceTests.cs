using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Certifications;

/// <summary>
/// HU #11304 (Feature #11301) — punto único de escritura del almacén canónico: fusión con
/// precedencia, marcado de la vigente y guardado del payload crudo sanitizado.
/// </summary>
public sealed class CertificationIngestionServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Instancia = Guid.NewGuid();
    private static readonly DateTimeOffset Ayer = new(2026, 8, 6, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset Hoy = new(2026, 8, 7, 10, 0, 0, TimeSpan.FromHours(-5));

    private static SoatCertification Poliza(
        string numero, string? aseguradora = "AXA COLPATRIA SEGUROS SA",
        string vence = "2027-01-02", string? desde = "2026-01-03") =>
        CertificationFactory.Soat(numero, aseguradora, "2025-12-20", desde, vence, "VIGENTE");

    // ── Fusión y precedencia ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnaConsultaSinAseguradora_NoBorraLaQueYaEstaba()
    {
        // La regla que convierte la reconsulta en una operación segura. Sin ella, un proveedor que
        // esta vez no manda un campo vacía la celda del certificado.
        var repo = new FakeCertificationRepository();
        repo.Seed(new StoredSoatPolicy(
            Poliza("POL-1"),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Ayer)));

        var service = new CertificationIngestionService(repo);

        await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle([Poliza("POL-1", aseguradora: null)], []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            cancellationToken: TestContext.Current.CancellationToken);

        repo.SoatWrites.Single().Single().Certification.Insurer.Value
            .Should().Be("AXA COLPATRIA SEGUROS SA");
    }

    [Fact]
    public async Task UnaCorreccionManual_SobreviveALaReconsulta()
    {
        // D2. Hoy este dato se pierde en silencio en la siguiente consulta.
        var repo = new FakeCertificationRepository();
        repo.Seed(new StoredSoatPolicy(
            Poliza("POL-1", aseguradora: "SEGUROS CORREGIDOS S.A."),
            new CertificationProvenance(CertificationSourceKind.User, "manual", Ayer)));

        var service = new CertificationIngestionService(repo);

        await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle([Poliza("POL-1", aseguradora: "LO QUE DIGA EL RUNT")], []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            cancellationToken: TestContext.Current.CancellationToken);

        repo.SoatWrites.Single().Single().Certification.Insurer.Value
            .Should().Be("SEGUROS CORREGIDOS S.A.");
    }

    [Fact]
    public async Task UnaConsulta_SiMejoraLoQueHabiaPuestoElOcr()
    {
        var repo = new FakeCertificationRepository();
        repo.Seed(new StoredSoatPolicy(
            Poliza("POL-1", aseguradora: "AXA COLPATRlA"),   // errata típica de OCR
            new CertificationProvenance(CertificationSourceKind.Ocr, "ocr", Ayer)));

        var service = new CertificationIngestionService(repo);

        await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle([Poliza("POL-1")], []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            cancellationToken: TestContext.Current.CancellationToken);

        repo.SoatWrites.Single().Single().Certification.Insurer.Value
            .Should().Be("AXA COLPATRIA SEGUROS SA");
    }

    // ── Selección de la vigente ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SoloUnaPolizaQuedaMarcadaComoVigente()
    {
        var repo = new FakeCertificationRepository();
        var service = new CertificationIngestionService(repo);

        await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle(
                [
                    Poliza("VIEJA", vence: "2026-01-02", desde: "2025-01-03"),
                    Poliza("VIGENTE", vence: "2027-01-02", desde: "2026-01-03"),
                ],
                []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            cancellationToken: TestContext.Current.CancellationToken);

        var escritas = repo.SoatWrites.Single();
        escritas.Should().HaveCount(2, "el histórico completo se persiste");
        escritas.Where(p => p.IsCurrent).Should().ContainSingle()
            .Which.Certification.PolicyNumber.Value.Should().Be("VIGENTE");
    }

    [Fact]
    public async Task LaVigenteSeDecideContraElHistoricoYaGuardado()
    {
        // Si el proveedor solo devuelve la póliza nueva, la anterior no debe quedar marcada como
        // vigente por inercia — ni la nueva ganar sin comparar con lo que ya había.
        var repo = new FakeCertificationRepository();
        repo.Seed(new StoredSoatPolicy(
            Poliza("LARGA", vence: "2028-01-01", desde: "2026-01-01"),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Ayer),
            IsCurrent: true));

        var service = new CertificationIngestionService(repo);

        await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle([Poliza("CORTA", vence: "2026-09-01", desde: "2026-01-03")], []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            cancellationToken: TestContext.Current.CancellationToken);

        repo.SoatWrites.Single().Single().IsCurrent
            .Should().BeFalse("la ya guardada cubre más allá y sigue siendo la vigente");
    }

    // ── Payload crudo ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElPayloadCrudoSeGuardaYQuedaEnlazadoALaFila()
    {
        var repo = new FakeCertificationRepository();
        var service = new CertificationIngestionService(repo);

        await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle([Poliza("POL-1")], []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            new RawProviderPayload("kyverum_runt", RawProviderPayload.VehicleSubject, "NZS920",
                """{"ok":true,"data":{"soat":[]}}""", Hoy),
            cancellationToken: TestContext.Current.CancellationToken);

        repo.SavedPayloads.Should().ContainSingle();
        repo.SoatWrites.Single().Single().Provenance.RawPayloadId
            .Should().Be(repo.LastPayloadId, "la fila apunta a la evidencia que la produjo");
    }

    [Fact]
    public async Task ElPayloadSeSanitizaAntesDeGuardarse()
    {
        var repo = new FakeCertificationRepository();
        var service = new CertificationIngestionService(repo);

        await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle([Poliza("POL-1")], []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            new RawProviderPayload("kyverum_runt", RawProviderPayload.VehicleSubject, "NZS920",
                """{"authorization":"Bearer secreto","data":{"apiKey":"k-123","placa":"NZS920"}}""", Hoy),
            TestContext.Current.CancellationToken);

        var guardado = repo.SavedPayloads.Single().PayloadJson;

        guardado.Should().NotContain("Bearer secreto");
        guardado.Should().NotContain("k-123");
        guardado.Should().Contain("NZS920", "lo que sirve para reprocesar se conserva tal cual");
    }

    [Fact]
    public async Task UnPayloadIlegible_NoSeGuardaYNoRompeLaIngesta()
    {
        var repo = new FakeCertificationRepository();
        var service = new CertificationIngestionService(repo);

        var act = async () => await service.IngestAsync(
            Instancia, Tenant,
            CertificationBundle.ForVehicle([Poliza("POL-1")], []),
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            new RawProviderPayload("kyverum_runt", RawProviderPayload.VehicleSubject, "NZS920",
                "esto no es json", Hoy),
            TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        repo.SavedPayloads.Should().BeEmpty();
        repo.SoatWrites.Should().ContainSingle("la certificación se persiste igual");
    }

    // ── Degradación ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnBundleVacio_NoEscribeNada()
    {
        var repo = new FakeCertificationRepository();
        var service = new CertificationIngestionService(repo);

        var escritas = await service.IngestAsync(
            Instancia, Tenant, CertificationBundle.Empty,
            new CertificationProvenance(CertificationSourceKind.Consultation, "kyverum_runt", Hoy),
            cancellationToken: TestContext.Current.CancellationToken);

        escritas.Should().Be(0);
        repo.SoatWrites.Should().BeEmpty();
        repo.SavedPayloads.Should().BeEmpty();
    }

    /// <summary>Doble en memoria del puerto de persistencia. Registra lo que se le pidió escribir.</summary>
    private sealed class FakeCertificationRepository : ICertificationRepository
    {
        private readonly List<StoredSoatPolicy> _seededSoat = [];

        public List<IReadOnlyList<StoredSoatPolicy>> SoatWrites { get; } = [];
        public List<IReadOnlyList<StoredRtmInspection>> RtmWrites { get; } = [];
        public List<IReadOnlyList<StoredMerchantRegistration>> MerchantWrites { get; } = [];
        public List<RawProviderPayload> SavedPayloads { get; } = [];
        public Guid? LastPayloadId { get; private set; }

        public void Seed(StoredSoatPolicy policy) => _seededSoat.Add(policy);

        public Task<CertificationSnapshot> LoadAsync(Guid tenantId, Guid instanceId, CancellationToken ct) =>
            Task.FromResult(new CertificationSnapshot(_seededSoat, [], []));

        public Task<Guid?> SaveRawPayloadAsync(
            Guid tenantId, Guid instanceId, RawProviderPayload? payload, CancellationToken ct)
        {
            if (payload is null)
                return Task.FromResult<Guid?>(null);

            SavedPayloads.Add(payload);
            LastPayloadId = Guid.NewGuid();
            return Task.FromResult(LastPayloadId);
        }

        public Task UpsertSoatPoliciesAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredSoatPolicy> policies, CancellationToken ct)
        {
            SoatWrites.Add(policies);
            return Task.CompletedTask;
        }

        public Task UpsertRtmInspectionsAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredRtmInspection> inspections, CancellationToken ct)
        {
            RtmWrites.Add(inspections);
            return Task.CompletedTask;
        }

        public Task UpsertMerchantRegistrationsAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredMerchantRegistration> registrations, CancellationToken ct)
        {
            MerchantWrites.Add(registrations);
            return Task.CompletedTask;
        }

        public Task<int> FreezeAsync(Guid tenantId, Guid instanceId, DateTimeOffset frozenAt, CancellationToken ct) =>
            Task.FromResult(0);
    }
}
