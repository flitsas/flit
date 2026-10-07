using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// Bug #13304 (D4) — validación de la consulta RUNT que ICT envía para reutilizar al crear el borrador:
/// vigencia (default 24 h), coherencia placa/VIN y JSON legible. Fuera de eso no se consulta y se avisa.
/// <para>Uso de ejemplo: <c>IctOrchestrationService.ResolverPrecomputed(pv, "ABC123", null, 24, now)</c>
/// → <c>(snapshot, null)</c> o <c>(null, "vehicle_consultation_expired")</c>.</para>
/// </summary>
public sealed class IctPrecomputedVehicleResolverTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static PrecomputedVehicleConsultation Placa(string queriedPlate = "ABC123", double horasAtras = 1) => new()
    {
        SnapshotJson = PreflightVehicleSnapshotJson.Serialize(new PreflightVehicleSnapshot(
            [new PreflightCheckDto("gravamenes", "Gravámenes", "warn", "kyverum_runt", null)],
            [new HydratedField("runt_tiene_gravamenes", "SI", null)],
            ["kyverum_runt"])),
        ConsultedAt = Timestamp.FromDateTimeOffset(Ahora.AddHours(-horasAtras)),
        Provider = "kyverum_runt",
        Kind = "VehiclePlate",
        QueriedPlate = queriedPlate,
    };

    [Fact]
    public void Vigente_YDelMismoVehiculo_DevuelveElSnapshot()
    {
        var (snapshot, motivo) = IctOrchestrationService.ResolverPrecomputed(Placa(" abc 123 "), "ABC123", null, 24, Ahora);

        motivo.Should().BeNull();
        snapshot!.HydratedFields.Should().ContainSingle(f => f.FieldKey == "runt_tiene_gravamenes" && f.ValueText == "SI");
    }

    [Fact]
    public void Ausente_DevuelveMissing() =>
        IctOrchestrationService.ResolverPrecomputed(null, "ABC123", null, 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_missing");

    [Fact]
    public void MasViejaQueLaVigencia_DevuelveExpired() =>
        IctOrchestrationService.ResolverPrecomputed(Placa(horasAtras: 25), "ABC123", null, 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_expired");

    [Fact]
    public void VigenciaConfigurada_SeRespeta() =>
        IctOrchestrationService.ResolverPrecomputed(Placa(horasAtras: 3), "ABC123", null, 2, Ahora)
            .Motivo.Should().Be("vehicle_consultation_expired");

    [Fact]
    public void VigenciaNoPositiva_CaeAlDefaultDe24Horas() =>
        IctOrchestrationService.ResolverPrecomputed(Placa(horasAtras: 23), "ABC123", null, 0, Ahora)
            .Motivo.Should().BeNull();

    [Fact]
    public void OtraPlaca_DevuelveMismatch() =>
        IctOrchestrationService.ResolverPrecomputed(Placa("XYZ999"), "ABC123", null, 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_mismatch");

    [Fact]
    public void PorVinConVinDistinto_DevuelveMismatch()
    {
        var pv = Placa();
        pv.Kind = "VehicleVin";
        pv.QueriedPlate = string.Empty;
        pv.QueriedVin = "1HGCM82633A004352";

        IctOrchestrationService.ResolverPrecomputed(pv, "ABC123", "9BWZZZ377VT004251", 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_mismatch");
    }

    [Fact]
    public void JsonIlegible_DevuelveInvalid()
    {
        var pv = Placa();
        pv.SnapshotJson = "{no-es-json";

        IctOrchestrationService.ResolverPrecomputed(pv, "ABC123", null, 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_invalid");
    }

    [Fact]
    public void SinFechaOKindDesconocida_DevuelveInvalid()
    {
        var sinFecha = Placa();
        sinFecha.ConsultedAt = null;
        var kindRara = Placa();
        kindRara.Kind = "Conductor";

        IctOrchestrationService.ResolverPrecomputed(sinFecha, "ABC123", null, 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_invalid");
        IctOrchestrationService.ResolverPrecomputed(kindRara, "ABC123", null, 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_invalid");
    }
}
