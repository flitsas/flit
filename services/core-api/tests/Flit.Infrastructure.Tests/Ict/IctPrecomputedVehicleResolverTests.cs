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

    // ── Revisión de seguridad (L-1, M-1) ──────────────────────────────────────────────────────

    [Fact]
    public void FechaMasDeCincoMinutosEnElFuturo_DevuelveInvalid()
    {
        var futura = Placa();
        futura.ConsultedAt = Timestamp.FromDateTimeOffset(Ahora.AddMinutes(6));

        IctOrchestrationService.ResolverPrecomputed(futura, "ABC123", null, 24, Ahora)
            .Motivo.Should().Be("vehicle_consultation_invalid");
    }

    [Fact]
    public void FechaDentroDelMargenDeRelojDeCincoMinutos_SeAcepta()
    {
        var casi = Placa();
        casi.ConsultedAt = Timestamp.FromDateTimeOffset(Ahora.AddMinutes(4));

        IctOrchestrationService.ResolverPrecomputed(casi, "ABC123", null, 24, Ahora)
            .Motivo.Should().BeNull();
    }

    [Fact]
    public void SnapshotConClavesAjenas_DescartaLasAjenasYConservaLasDeVehiculo()
    {
        // M-1 — un snapshot alterado en tránsito no puede sobrescribir OT, titular ni actores, ni inyectar
        // checks que el preflight de vehículo no emite (bloqueo, duplicidad, SIMIT).
        var pv = Placa();
        pv.SnapshotJson = PreflightVehicleSnapshotJson.Serialize(new PreflightVehicleSnapshot(
            [
                new PreflightCheckDto("gravamenes", "Gravámenes", "warn", "kyverum_runt", null),
                new PreflightCheckDto("soat", "SOAT", "ok", "kyverum_runt", null),
                new PreflightCheckDto("duplicidad", "Duplicidad", "ok", "system", null),
                new PreflightCheckDto("simit_vendedor", "SIMIT vendedor", "ok", "kyverum_runt", null),
            ],
            [
                new HydratedField("runt_tiene_gravamenes", "SI", null),
                new HydratedField("vehicle_brand", "MARCA", null),
                new HydratedField("transit_office_id", "00000000-0000-0000-0000-000000000001", null),
                new HydratedField("owner_document_number", "999", null),
                new HydratedField("comprador_nombre", "OTRO", null),
            ],
            ["kyverum_runt"]));

        var (snapshot, motivo) = IctOrchestrationService.ResolverPrecomputed(pv, "ABC123", null, 24, Ahora);

        motivo.Should().BeNull();
        snapshot!.HydratedFields.Select(f => f.FieldKey).Should().Equal("runt_tiene_gravamenes", "vehicle_brand");
        snapshot.Checks.Select(c => c.Key).Should().Equal("gravamenes", "soat");
        snapshot.Providers.Should().Equal("kyverum_runt");
    }

    // ── HU #13348: ICT consulta directo a core-consultas; el snapshot es su ResultadoConsulta en JSON ──

    private static PrecomputedVehicleConsultation DeConsultas(Flit.Consultas.Grpc.V1.ResultadoConsulta resultado)
    {
        var pv = Placa();
        pv.SnapshotJson = Google.Protobuf.JsonFormatter.Default.Format(resultado);
        return pv;
    }

    [Fact]
    public void HU13348_SnapshotDeConsultas_SeConvierteConLaMismaListaBlanca()
    {
        var pv = DeConsultas(new Flit.Consultas.Grpc.V1.ResultadoConsulta
        {
            Proveedor = "kyverum_runt",
            Chequeos =
            {
                new Flit.Consultas.Grpc.V1.Chequeo { Clave = "gravamenes", Etiqueta = "Gravámenes", Estado = Flit.Consultas.Grpc.V1.EstadoChequeo.Warn, Fuente = "kyverum_runt" },
                new Flit.Consultas.Grpc.V1.Chequeo { Clave = "duplicidad", Etiqueta = "Duplicidad", Estado = Flit.Consultas.Grpc.V1.EstadoChequeo.Ok, Fuente = "system" },
            },
            Campos =
            {
                new Flit.Consultas.Grpc.V1.Campo { Clave = "runt_tiene_gravamenes", ValorTexto = "SI" },
                new Flit.Consultas.Grpc.V1.Campo { Clave = "owner_document_number", ValorTexto = "999" },
            },
        });

        var (snapshot, motivo) = IctOrchestrationService.ResolverPrecomputed(pv, "ABC123", null, 24, Ahora);

        motivo.Should().BeNull();
        snapshot!.HydratedFields.Select(f => f.FieldKey).Should().Equal("runt_tiene_gravamenes");
        snapshot.Checks.Select(c => c.Key).Should().Equal("gravamenes");
        snapshot.Providers.Should().Equal("kyverum_runt");
    }

    [Fact]
    public void HU13348_SnapshotDeConsultasSinNadaDeVehiculo_EsComoSinConsulta()
    {
        var pv = DeConsultas(new Flit.Consultas.Grpc.V1.ResultadoConsulta
        {
            Proveedor = "kyverum_runt",
            Campos = { new Flit.Consultas.Grpc.V1.Campo { Clave = "owner_document_number", ValorTexto = "999" } },
        });

        IctOrchestrationService.ResolverPrecomputed(pv, "ABC123", null, 24, Ahora).Motivo.Should().Be("vehicle_consultation_missing");
    }

    [Theory]
    [InlineData("{\"proveedor\":\"\"}")]
    [InlineData("{\"chequeos\": 7}")]
    [InlineData("no es json")]
    public void HU13348_SnapshotIlegible_EsInvalido(string json)
    {
        var pv = Placa();
        pv.SnapshotJson = json;

        IctOrchestrationService.ResolverPrecomputed(pv, "ABC123", null, 24, Ahora).Motivo.Should().Be("vehicle_consultation_invalid");
    }
}
