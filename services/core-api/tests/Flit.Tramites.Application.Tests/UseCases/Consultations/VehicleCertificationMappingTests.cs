using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #11303 (Feature #11301) — los mappers de vehículo leen los campos de SOAT y RTM que el RUNT ya
/// enviaba y no se estaban modelando, y producen el bundle canónico.
/// </summary>
/// <remarks>
/// Los JSON de estas pruebas son recortes literales de las tres consultas reales documentadas en
/// <c>docs/consulta-runt-nzs920-procesamiento.md</c> (solo las secciones <c>soat</c>, <c>rtm</c> y
/// <c>vehiculo</c> — sin datos del propietario). Son la evidencia de que el modelo anterior afirmaba
/// por escrito algo que el proveedor desmentía en cada respuesta.
/// </remarks>
public sealed class VehicleCertificationMappingTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Hoy = new(2026, 8, 7);

    private static KyverumRuntVehicleResponse Kyverum(string json) =>
        JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(json, WebJsonOptions)!;

    private static string? Field(ConsultationResult r, string key) =>
        r.HydratedFields.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    // Recorte literal de la consulta a NZS920: dos pólizas, la vigente y la del periodo anterior.
    private const string Nzs920Soat = """
    {
      "ok": true,
      "data": {
        "vehiculo": { "placa": "NZS920", "fechaRegistro": "2025-01-07T08:37:47.000-05:00" },
        "soat": [
          {
            "numSoat": "3488487200",
            "fechaExpedicion": "2025-12-20T00:00:00.000-05:00",
            "fechaExpediSoat": "2025-12-20T00:00:00.000-05:00",
            "fechaInicioPoliza": "2026-01-03T00:00:00.000-05:00",
            "fechaVencimSoat": "2027-01-02T00:00:00.000-05:00",
            "razonSocialAsegur": "AXA COLPATRIA SEGUROS SA",
            "estado": "VIGENTE"
          },
          {
            "numSoat": "40925769",
            "fechaExpediSoat": "2025-01-02T00:00:00.000-05:00",
            "fechaInicioPoliza": "2025-01-03T00:00:00.000-05:00",
            "fechaVencimSoat": "2026-01-02T00:00:00.000-05:00",
            "razonSocialAsegur": "SEGUROS GENERALES SURAMERICANA S.A.",
            "estado": "NO VIGENTE"
          }
        ]
      }
    }
    """;

    // Recorte literal de la consulta a LCL874: tres revisiones, todas APROBADA, la primera vigente.
    private const string Lcl874Rtm = """
    {
      "ok": true,
      "data": {
        "vehiculo": { "placa": "LCL874" },
        "rtm": [
          {
            "fechaExpedicionRvt": "2026-03-11T00:00:00.000-05:00",
            "fechaVencimientoRvt": "2027-03-11T00:00:00.000-05:00",
            "nombreCda": "IVESUR COLOMBIA BARRANQUILLA",
            "estadoRvt": "APROBADA",
            "tipoRevision": "REVISION TECNICO-MECANICO",
            "vigente": "SI",
            "numeCerti": "188327294"
          },
          {
            "fechaExpedicionRvt": "2025-03-12T00:00:00.000-05:00",
            "fechaVencimientoRvt": "2026-03-12T00:00:00.000-05:00",
            "nombreCda": "IVESUR COLOMBIA BARRANQUILLA",
            "estadoRvt": "APROBADA",
            "tipoRevision": "REVISION TECNICO-MECANICO",
            "vigente": "NO",
            "numeCerti": "180151310"
          },
          {
            "fechaExpedicionRvt": "2024-03-12T00:00:00.000-05:00",
            "fechaVencimientoRvt": "2025-03-12T00:00:00.000-05:00",
            "nombreCda": " CENTRO DE DIAGNOSTICO AUTOMOTOR EL DIAMANTE",
            "estadoRvt": "APROBADA",
            "tipoRevision": "REVISION TECNICO-MECANICO",
            "vigente": "NO",
            "numeCerti": "172361018"
          }
        ]
      }
    }
    """;

    // ── SOAT ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Kyverum_LeeLaPolizaYLasFechasQueElProveedorSiEnviaba()
    {
        // El DTO afirmaba por escrito que Kyverum «no trae póliza ni fechas de expedición». Estas tres
        // llaves tenían CERO filas en todo el ambiente; soat_expedicion, ninguna en absoluto.
        var result = KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Nzs920Soat), Hoy);

        Field(result, "soat_poliza").Should().Be("3488487200");
        Field(result, "soat_expedicion").Should().Be("2025-12-20T00:00:00.000-05:00");
        Field(result, "soat_vigencia").Should().Be("2026-01-03T00:00:00.000-05:00");
    }

    [Fact]
    public void Kyverum_LeeLaFechaDeMatriculaDesdeFechaRegistro()
    {
        // fechaMatricula llega null en las tres consultas; la fecha real está en fechaRegistro. Sin
        // esta llave, el bloque de RTM del certificado no puede evaluar si le aplica al vehículo.
        Field(KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Nzs920Soat), Hoy), "vehicle_registration_date")
            .Should().Be("2025-01-07T08:37:47.000-05:00");
    }

    [Fact]
    public void Kyverum_ElBundleTraeElHistoricoCompletoDePolizas()
    {
        var bundle = KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Nzs920Soat), Hoy).Certifications;

        bundle.Should().NotBeNull();
        bundle!.SoatHistory.Should().HaveCount(2, "el histórico ya vino en la misma respuesta pagada");

        var vigente = SoatSelection.PickCurrent(bundle.SoatHistory, Hoy)!;
        vigente.PolicyNumber.Value.Should().Be("3488487200");
        vigente.Insurer.Value.Should().Be("AXA COLPATRIA SEGUROS SA");
        vigente.IssuedOn.Value.Should().Be(new DateOnly(2025, 12, 20));
        vigente.ValidFrom.Value.Should().Be(new DateOnly(2026, 1, 3));
        vigente.ValidUntil.Value.Should().Be(new DateOnly(2027, 1, 2));
        vigente.Status.Value.Should().Be(VigencyStatus.Vigente);
    }

    [Fact]
    public void Kyverum_LasSeisCeldasDelSoatSalenLlenas()
    {
        // El criterio del expediente: seis celdas por tabla. Es la prueba que habría detectado el
        // defecto original, donde tres de estas seis no tenían una sola fila en la base.
        var vigente = SoatSelection.PickCurrent(
            KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Nzs920Soat), Hoy).Certifications!.SoatHistory,
            Hoy)!;

        new[]
        {
            vigente.PolicyNumber.ToDocumentText(),
            vigente.Insurer.ToDocumentText(),
            vigente.IssuedOn.ToDocumentText(),
            vigente.ValidFrom.ToDocumentText(),
            vigente.ValidUntil.ToDocumentText(),
            vigente.Status.ToDocumentText(),
        }.Should().AllSatisfy(celda => celda.Should().NotBeNullOrWhiteSpace());
    }

    // ── RTM ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Kyverum_LeeNumeroExpedicionYCdaDeLaRevision()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Lcl874Rtm), Hoy);

        Field(result, "rtm_numero").Should().Be("188327294");
        Field(result, "rtm_expedicion").Should().Be("2026-03-11T00:00:00.000-05:00");
        Field(result, "rtm_entidad").Should().Be("IVESUR COLOMBIA BARRANQUILLA");
    }

    [Fact]
    public void Kyverum_NoInventaLaVigenciaDeLaRtm()
    {
        // El RUNT no manda inicio de vigencia de la RTM. Ausente ⇒ celda en blanco (HU #10856), no un
        // valor deducido de la expedición.
        Field(KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Lcl874Rtm), Hoy), "rtm_vigencia")
            .Should().BeNull();
    }

    [Fact]
    public void Kyverum_LaVigenciaDeLaRtmSaleDeVigenteYNoDeEstadoRvt()
    {
        // Las tres revisiones son APROBADA; solo una está vigente. Si el estado saliera de estadoRvt,
        // el certificado afirmaría tres coberturas activas.
        var bundle = KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Lcl874Rtm), Hoy).Certifications!;

        bundle.RtmHistory.Should().HaveCount(3);
        bundle.RtmHistory.Count(r => r.Status.Value == VigencyStatus.Vigente).Should().Be(1);

        var actual = RtmSelection.PickCurrent(bundle.RtmHistory, Hoy)!;
        actual.CertificateNumber.Value.Should().Be("188327294");
        actual.Status.Value.Should().Be(VigencyStatus.Vigente);
    }

    [Fact]
    public void Kyverum_LimpiaElNombreSucioDelCda()
    {
        // El RUNT manda " CENTRO DE DIAGNOSTICO AUTOMOTOR EL DIAMANTE" con espacio inicial.
        var masAntigua = KyverumRuntVehicleResultMapper.MapVehicle(Kyverum(Lcl874Rtm), Hoy)
            .Certifications!.RtmHistory
            .Single(r => r.CertificateNumber.Value == "172361018");

        masAntigua.Cda.Value.Should().Be("CENTRO DE DIAGNOSTICO AUTOMOTOR EL DIAMANTE");
    }

    // ── Degradación ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SinSeccionesDeCertificacion_ElBundleEsNuloYNadaSeRompe()
    {
        // NZS920 no tiene sección rtm. Un bundle nulo degrada al camino anterior sin excepción.
        var result = KyverumRuntVehicleResultMapper.MapVehicle(
            Kyverum("""{ "ok": true, "data": { "vehiculo": { "placa": "ABC123" } } }"""), Hoy);

        result.Certifications.Should().BeNull();
        result.Checks.Should().NotBeEmpty("las verificaciones del preflight siguen igual");
    }

    [Fact]
    public void RespuestaVacia_NoLanza()
    {
        var act = () => KyverumRuntVehicleResultMapper.MapVehicle(Kyverum("""{ "ok": false }"""), Hoy);

        act.Should().NotThrow();
    }
}
