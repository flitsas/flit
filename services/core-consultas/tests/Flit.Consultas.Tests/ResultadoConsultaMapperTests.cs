using Flit.Consultas.Grpc.Mapping;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13346 — la ida y vuelta por gRPC no cambia el resultado: lo que Trámites recibe de core-consultas es lo mismo
/// que habría recibido en proceso (incluidos null frente a "", certificaciones y respuesta cruda).
/// </summary>
public sealed class ResultadoConsultaMapperTests
{
    private static readonly DateTimeOffset Consultado = new(2026, 10, 6, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void IdaYVuelta_DevuelveElMismoResultado()
    {
        var original = new ConsultationResult(
            "kyverum_runt",
            "yellow",
            [
                new ConsultationCheck("soat", "SOAT", "ok", "kyverum_runt", null),
                new ConsultationCheck("comparendos", "Comparendos", "warn", "verifik_simit", "Tiene 2 comparendos",
                    [new FineDetail("C-1", "2026-01-02", 1234567.89m, "Bogotá", null, "C29"), new FineDetail(null, null, null, null, "PENDIENTE", null)],
                    [new CheckDato("Placa", "ABC123"), new CheckDato("Estado", "")]),
                new ConsultationCheck("provider", "Consulta RUNT", "error", "verifik", ""),
            ],
            [
                new HydratedField("vehicle_year", "2020", null),
                new HydratedField("runt_gravamenes", null, "[{\"acreedor\":\"Banco\"}]"),
                new HydratedField("runt_tiene_prendas", "", null),
            ],
            FromCache: true,
            QueriedAt: Consultado,
            Certifications: new CertificationBundle(
                [new SoatCertification(new CertifiedNumber("123", "0123"), new CertifiedName("SURA", null), new CertifiedDate(new DateOnly(2026, 1, 1), "01/01/2026"),
                    new CertifiedDate(new DateOnly(2026, 1, 2), null), CertifiedDate.Empty, new CertifiedStatus(VigencyStatus.Vigente, "VIGENTE"))],
                [new RtmCertification(CertifiedNumber.Empty, new CertifiedName(null, "CDA X"), CertifiedDate.Empty, CertifiedDate.Empty,
                    new CertifiedDate(null, "sin fecha"), new CertifiedStatus(VigencyStatus.Vencido, null), "PERIODICA")],
                [new MerchantRegistration("900123456", new CertifiedName("ACME SAS", "ACME S.A.S."), CertifiedNumber.Empty, CertifiedStatus.Empty,
                    CertifiedDate.Empty, CertifiedDate.Empty, CertifiedName.Empty, CertifiedName.Empty, CertifiedName.Empty, CertifiedName.Empty,
                    [new LegalRepresentative("Ana", "CC", "123", null, null)])],
                new VehicleRegistrationFacts(new CertifiedDate(new DateOnly(2019, 5, 3), "2019-05-03"))),
            RawPayload: new RawProviderPayload("kyverum_runt", "vehicle", "ABC123", "{\"placa\":\"ABC123\"}", Consultado));

        var vuelta = ResultadoConsultaMapper.FromProto(ResultadoConsultaMapper.ToProto(original, incluirCruda: true));

        vuelta.Should().BeEquivalentTo(original, o => o.WithStrictOrdering());
    }

    [Fact]
    public void SinCrudaNiCertificaciones_LleganNull()
    {
        var original = new ConsultationResult("verifik", "green", [], []);

        var vuelta = ResultadoConsultaMapper.FromProto(ResultadoConsultaMapper.ToProto(original with { RawPayload = new RawProviderPayload("verifik", "vehicle", null, "{}", Consultado) }, incluirCruda: false));

        vuelta.RawPayload.Should().BeNull();
        vuelta.Certifications.Should().BeNull();
        vuelta.QueriedAt.Should().BeNull();
    }

    [Fact]
    public void HU13348_Avaluos_IdaYVuelta_SinCambios()
    {
        var original = new SuggestedCommercialValue(98_000_000, "base_gravable",
        [
            AvaluoResult.Ok("base_gravable", 98_000_000),
            AvaluoResult.Ok("mercado_libre", 112_000_000, muestras: 12),
            AvaluoResult.NoData("fasecolda"),
            AvaluoResult.Error("otra", "caída"),
        ]);

        var vuelta = ResultadoConsultaMapper.FromProto(ResultadoConsultaMapper.ToProto(original));

        vuelta.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void HU13348_Avaluos_SinValor_LlegaNull()
    {
        var vuelta = ResultadoConsultaMapper.FromProto(ResultadoConsultaMapper.ToProto(new SuggestedCommercialValue(null, null, [])));

        vuelta.Sugerido.Should().BeNull();
        vuelta.FuentePrincipal.Should().BeNull();
        vuelta.Sources.Should().BeEmpty();
    }
}
