using System.Text.Json;
using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Certifications;

/// <summary>
/// HU #11308 (Feature #11301) — <b>guardián de cobertura en tiempo de ejecución</b>.
/// </summary>
/// <remarks>
/// Sustituye, para las certificaciones, al guardián estático de <c>FieldValueContractGuardTests</c>,
/// que es un <c>grep</c> sobre el código fuente: le basta con que <i>algún</i> productor declarado
/// mencione la llave como literal. Por eso nunca vio que el proveedor primario no la producía en la
/// ruta real, y seis de las doce celdas del certificado acabaron con cero filas en todo el ambiente
/// después de desplegar un Feature entero dedicado a llenarlas.
///
/// <para>Esta prueba recorre la <b>cadena completa</b> —respuesta real del proveedor → mapper →
/// ingesta → lector documental— y exige que las celdas lleguen no vacías al final. Es la prueba que
/// <b>sí</b> habría detectado el defecto original: con el DTO anterior de Kyverum falla en la primera
/// aserción.</para>
///
/// <para>Los JSON son recortes literales de las consultas reales documentadas en
/// <c>docs/consulta-runt-nzs920-procesamiento.md</c>, sin datos del propietario.</para>
/// </remarks>
public sealed class CertificationCoverageGuardTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Instancia = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 8, 7);

    /// <summary>Consulta real a NZS920 (SOAT) y LCL874 (RTM), en una sola respuesta.</summary>
    private const string RespuestaRealKyverum = """
    {
      "ok": true,
      "data": {
        "vehiculo": { "placa": "NZS920", "fechaRegistro": "2019-01-07T08:37:47.000-05:00" },
        "soat": [
          {
            "numSoat": "3488487200",
            "fechaExpediSoat": "2025-12-20T00:00:00.000-05:00",
            "fechaInicioPoliza": "2026-01-03T00:00:00.000-05:00",
            "fechaVencimSoat": "2027-01-02T00:00:00.000-05:00",
            "razonSocialAsegur": "AXA COLPATRIA SEGUROS SA",
            "estado": "VIGENTE"
          }
        ],
        "rtm": [
          {
            "fechaExpedicionRvt": "2026-03-11T00:00:00.000-05:00",
            "fechaVencimientoRvt": "2027-03-11T00:00:00.000-05:00",
            "nombreCda": "IVESUR COLOMBIA BARRANQUILLA",
            "estadoRvt": "APROBADA",
            "tipoRevision": "REVISION TECNICO-MECANICO",
            "vigente": "SI",
            "numeCerti": "188327294"
          }
        ]
      }
    }
    """;

    /// <summary>
    /// Recorre proveedor → mapper → ingesta → lector, y devuelve lo que vería el generador del PDF.
    /// </summary>
    private static async Task<CertificationView> CadenaCompletaAsync(string respuestaProveedor)
    {
        var repositorio = new AlmacenEnMemoria();

        var resultado = KyverumRuntVehicleResultMapper.MapVehicle(
            JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(respuestaProveedor, WebJsonOptions)!,
            Hoy);

        resultado.Certifications.Should().NotBeNull(
            "el mapper debe producir el bundle canónico; sin él no hay nada que persistir");

        await new CertificationIngestionService(repositorio).IngestAsync(
            Instancia, Tenant,
            resultado.Certifications!,
            new CertificationProvenance(
                CertificationSourceKind.Consultation, resultado.Provider,
                new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.FromHours(-5)),
                MapperVersion: KyverumRuntVehicleResultMapper.MapperVersion),
            cancellationToken: TestContext.Current.CancellationToken);

        // El lector recibe las llaves que el mapper también escribió en field_values, tal como en
        // producción: así el respaldo entra en juego si la tabla no cubriera algo.
        var fieldValues = resultado.HydratedFields
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        return await new CertificationReader(repositorio).ForDocumentsAsync(
            Instancia, Tenant, fieldValues, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LasSeisCeldasDelSoatLleganNoVaciasAlDocumento()
    {
        var certs = await CadenaCompletaAsync(RespuestaRealKyverum);

        certs.Soat.Should().NotBeNull();
        var celdas = new Dictionary<string, string?>
        {
            ["N° Póliza"] = certs.Soat!.PolicyNumber.ToDocumentText(),
            ["Entidad"] = certs.Soat.Insurer.ToDocumentText(),
            ["Expedición"] = certs.Soat.IssuedOn.ToDocumentText(),
            ["Vigencia"] = certs.Soat.ValidFrom.ToDocumentText(),
            ["Vencimiento"] = certs.Soat.ValidUntil.ToDocumentText(),
            ["Estado"] = certs.Soat.Status.ToDocumentText(),
        };

        celdas.Where(c => string.IsNullOrWhiteSpace(c.Value)).Select(c => c.Key)
            .Should().BeEmpty("son las seis celdas de la tabla de SOAT del expediente");
    }

    [Fact]
    public async Task LasSeisCeldasDeLaRtmLleganNoVaciasSalvoLaQueElRuntNoManda()
    {
        var certs = await CadenaCompletaAsync(RespuestaRealKyverum);

        certs.Rtm.Should().NotBeNull();
        certs.Rtm!.CertificateNumber.ToDocumentText().Should().NotBeNullOrWhiteSpace();
        certs.Rtm.Cda.ToDocumentText().Should().NotBeNullOrWhiteSpace();
        certs.Rtm.IssuedOn.ToDocumentText().Should().NotBeNullOrWhiteSpace();
        certs.Rtm.ValidUntil.ToDocumentText().Should().NotBeNullOrWhiteSpace();
        certs.Rtm.Status.ToDocumentText().Should().NotBeNullOrWhiteSpace();

        // La sexta —inicio de vigencia de la revisión— el RUNT NO la manda. Queda en blanco a
        // propósito y NO se deduce de la expedición: el certificado no afirma lo que nadie dijo.
        certs.Rtm.ValidFrom.ToDocumentText().Should().BeNull();
    }

    [Fact]
    public async Task LaFechaDeMatriculaLlegaYHabilitaLaReglaDeAntiguedadDeLaRtm()
    {
        // Sin esta llave el bloque de RTM del certificado queda permanentemente en "no aplica".
        var certs = await CadenaCompletaAsync(RespuestaRealKyverum);

        certs.Vehicle.FechaMatricula.Value.Should().Be(new DateOnly(2019, 1, 7));
        RtmSelection.Applies(certs.Vehicle, Hoy).Should().BeTrue();
    }

    [Fact]
    public async Task LaProcedenciaLlegaAlPieDeCadaTabla()
    {
        // El texto fijo del certificado afirma una consulta al RUNT que puede no haber ocurrido.
        var certs = await CadenaCompletaAsync(RespuestaRealKyverum);

        certs.SoatFrom.Should().NotBeNull();
        certs.SoatFrom!.Source.Should().Be(CertificationSourceKind.Consultation);
        certs.SoatFrom.ProviderKey.Should().Be("kyverum_runt");
        certs.SoatFrom.MapperVersion.Should().Be(KyverumRuntVehicleResultMapper.MapperVersion,
            "es lo que permite reprocesar las filas de un mapper corregido");
        certs.SoatFrom.ToDocumentFooter("RUNT 2.0").Should().Contain("2026/08/07");
    }

    [Fact]
    public async Task LaCondicionDeEmisionSeCumpleConDatosReales()
    {
        // D8 — al menos una celda de SOAT o RTM con dato.
        (await CadenaCompletaAsync(RespuestaRealKyverum)).HasSoatOrRtmData.Should().BeTrue();
    }

    [Fact]
    public async Task UnaRespuestaSinCertificaciones_NoEmiteYNoRompe()
    {
        var repositorio = new AlmacenEnMemoria();
        var vacia = KyverumRuntVehicleResultMapper.MapVehicle(
            JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(
                """{ "ok": true, "data": { "vehiculo": { "placa": "ABC123" } } }""", WebJsonOptions)!,
            Hoy);

        var certs = await new CertificationReader(repositorio).ForDocumentsAsync(
            Instancia, Tenant,
            vacia.HydratedFields.ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase),
            TestContext.Current.CancellationToken);

        certs.HasSoatOrRtmData.Should().BeFalse();
        certs.MerchantByNit.Should().BeEmpty();
    }

    /// <summary>
    /// Almacén en memoria con el MISMO comportamiento de upsert por llave natural que el real: sin
    /// eso, la prueba no ejercitaría de verdad el paso de ingesta.
    /// </summary>
    private sealed class AlmacenEnMemoria : ICertificationRepository
    {
        private readonly Dictionary<string, StoredSoatPolicy> _soat = [];
        private readonly Dictionary<string, StoredRtmInspection> _rtm = [];
        private readonly Dictionary<string, StoredMerchantRegistration> _merchants = [];

        public Task<CertificationSnapshot> LoadAsync(Guid tenantId, Guid instanceId, CancellationToken ct) =>
            Task.FromResult(new CertificationSnapshot(
                [.. _soat.Values], [.. _rtm.Values], [.. _merchants.Values]));

        public Task<Guid?> SaveRawPayloadAsync(
            Guid tenantId, Guid instanceId, RawProviderPayload? payload, CancellationToken ct) =>
            Task.FromResult<Guid?>(payload is null ? null : Guid.NewGuid());

        public Task UpsertSoatPoliciesAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredSoatPolicy> policies, CancellationToken ct)
        {
            foreach (var p in policies)
                _soat[p.Certification.NaturalKey()] = p;
            return Task.CompletedTask;
        }

        public Task UpsertRtmInspectionsAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredRtmInspection> inspections, CancellationToken ct)
        {
            foreach (var r in inspections)
                _rtm[r.Certification.NaturalKey()] = r;
            return Task.CompletedTask;
        }

        public Task UpsertMerchantRegistrationsAsync(
            Guid tenantId, Guid instanceId, IReadOnlyList<StoredMerchantRegistration> registrations, CancellationToken ct)
        {
            foreach (var m in registrations)
                _merchants[m.Registration.Nit] = m;
            return Task.CompletedTask;
        }

        public Task<int> FreezeAsync(Guid tenantId, Guid instanceId, DateTimeOffset frozenAt, CancellationToken ct) =>
            Task.FromResult(0);
    }
}
