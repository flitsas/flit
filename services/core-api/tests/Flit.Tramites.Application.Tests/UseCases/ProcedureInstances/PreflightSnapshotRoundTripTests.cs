using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// El snapshot del pre-vuelo se GUARDA como JSON y se vuelve a leer: lo que el panel muestra no es
/// lo que el proveedor acaba de responder, sino lo que quedó persistido.
///
/// <para>Por eso el respaldo de los checks (<c>Datos</c>) tiene que sobrevivir ese viaje. Si se
/// perdiera, la tarjeta en verde volvería a quedar vacía —el síntoma exacto que este respaldo vino a
/// resolver— y el fallo sería invisible: no hay error, solo un hueco.</para>
/// </summary>
public sealed class PreflightSnapshotRoundTripTests
{
    [Fact]
    public void LosDatosDelProveedorSobrevivenAlGuardadoYLectura()
    {
        var checks = new List<PreflightCheckDto>
        {
            new("soat", "SOAT", "ok", "kyverum_runt", null, null,
                [
                    new CheckDato("Vigente hasta", "2027/01/23"),
                    new CheckDato("Póliza", "3506349600"),
                    new CheckDato("Aseguradora", "AXA COLPATRIA SEGUROS SA"),
                ]),
        };

        // Mismo par serializar/deserializar que usa el guardado del snapshot
        // (`JsonSerializer` con opciones por defecto, en ambos extremos).
        var json = JsonSerializer.Serialize(checks);
        var leidos = JsonSerializer.Deserialize<List<PreflightCheckDto>>(json)!;

        leidos.Should().ContainSingle();
        leidos[0].Datos.Should().NotBeNull();
        leidos[0].Datos!.Should().HaveCount(3);
        leidos[0].Datos![0].Etiqueta.Should().Be("Vigente hasta");
        leidos[0].Datos![0].Valor.Should().Be("2027/01/23");
    }

    [Fact]
    public void UnSnapshotAnteriorAlRespaldoSeLeeSinRomper()
    {
        // Los expedientes con un pre-vuelo ya corrido no traen la llave: se leen con `Datos` en null
        // y la tarjeta cae al mensaje de siempre, como antes de este cambio.
        var json = """
            [{"Key":"soat","Label":"SOAT","Status":"ok","Source":"kyverum_runt","Message":null}]
            """;

        var leidos = JsonSerializer.Deserialize<List<PreflightCheckDto>>(json)!;

        leidos.Should().ContainSingle();
        leidos[0].Datos.Should().BeNull();
    }

    // ── Bug #13304 — snapshot de vehículo que viaja opaco por ICT ───────────────
    // Uso de ejemplo: PreflightVehicleSnapshotJson.Serialize(PreflightVehicleSnapshot.FromConsultation(r))
    // en la consulta gRPC y PreflightVehicleSnapshotJson.TryDeserialize(json, out var s) al crear el borrador.

    private static ConsultationResult ResultadoConTodo() => new(
        "kyverum_runt",
        "yellow",
        [
            new ConsultationCheck("gravamenes", "Gravámenes", "warn", "kyverum_runt", "Prenda vigente",
                Datos: [new CheckDato("Acreedor", "BANCO DE PRUEBA S.A."), new CheckDato("Tipo", "PRENDA")]),
            new ConsultationCheck("soat", "SOAT", "ok", "kyverum_runt", null),
            new ConsultationCheck("simit_multas", "Multas", "warn", "kyverum_runt", "1 comparendo",
                Details: [new FineDetail("C-001", "2026-01-02", 123456.78m, "BOGOTA", "PENDIENTE", "C02")]),
        ],
        [
            new HydratedField("runt_tiene_gravamenes", "SI", null),
            new HydratedField("runt_gravamenes", null, "[{\"acreedor\":\"BANCO DE PRUEBA S.A.\"}]"),
            new HydratedField("vehicle_brand", "MARCA", null),
        ]);

    [Fact]
    public void Bug13304_SnapshotDeVehiculo_IdaYVuelta_ConservaChecksDetallesDatosYValueJson()
    {
        var original = PreflightVehicleSnapshot.FromConsultation(ResultadoConTodo());

        var json = PreflightVehicleSnapshotJson.Serialize(original);
        var ok = PreflightVehicleSnapshotJson.TryDeserialize(json, out var vuelta);

        ok.Should().BeTrue();
        vuelta.Should().NotBeNull();
        vuelta!.Should().BeEquivalentTo(original, o => o.WithStrictOrdering());
        vuelta!.Providers.Should().Equal("kyverum_runt");
        vuelta.Checks[0].Datos.Should().HaveCount(2);
        vuelta.Checks[2].Details![0].Valor.Should().Be(123456.78m);
        vuelta.HydratedFields.Single(f => f.FieldKey == "runt_gravamenes").ValueJson
            .Should().Be("[{\"acreedor\":\"BANCO DE PRUEBA S.A.\"}]");
        json.Should().NotContain("RawPayload", "el snapshot no lleva la respuesta cruda del proveedor")
            .And.NotContain("Certifications");
    }

    [Fact]
    public void Bug13304_FromConsultation_MismoMapeoQueElPreflight()
    {
        var r = ResultadoConTodo();

        var snapshot = PreflightVehicleSnapshot.FromConsultation(r);

        snapshot.Checks.Select(c => (c.Key, c.Label, c.Status, c.Source, c.Message))
            .Should().Equal(r.Checks.Select(c => (c.Key, c.Label, c.Status, c.Source, c.Message)));
        snapshot.HydratedFields.Should().Equal(r.HydratedFields);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-json")]
    [InlineData("{\"checks\":null}")]
    [InlineData("[]")]
    public void Bug13304_TryDeserialize_TextoInvalido_DevuelveFalseSinLanzar(string? json)
    {
        var ok = PreflightVehicleSnapshotJson.TryDeserialize(json, out var snapshot);

        ok.Should().BeFalse();
        snapshot.Should().BeNull();
    }
}
