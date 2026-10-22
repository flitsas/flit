using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #13846 (F07-H1, F07-C8) — <see cref="HuellaEvidencia"/>: SHA-256 hex de un JSON canónico (claves ordenadas) con la versión
/// de la plantilla, el tipo, la cabecera, el encabezado y, por celda, fuente, estado, motivo de N/A, tipo de error, fecha del dato,
/// versión del tablero y el SHA-256 del payload normalizado. Sin base. Datos inventados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~HuellaEvidenciaTests
/// </code>
/// </example>
public sealed class HuellaEvidenciaTests
{
    private static readonly Guid Lote = Guid.Parse("0199f000-0000-7000-8000-000000000001");
    private static readonly Guid VehiculoA = Guid.Parse("0199f000-0000-7000-8000-0000000000a1");
    private static readonly Guid VehiculoB = Guid.Parse("0199f000-0000-7000-8000-0000000000b2");
    private static readonly DateTimeOffset Dato = new(2026, 10, 22, 14, 30, 15, 123, TimeSpan.Zero);

    // AC1 (F07-C8) — la misma entrada en otro orden (celdas, claves del encabezado, alcance y claves del payload) da la misma huella.
    [Fact]
    public void Misma_entrada_misma_huella_sin_importar_el_orden()
    {
        var una = Entrada(
            celdas:
            [
                Celda(VehiculoA, "RUNT", payload: """{"placa":"TST101","marca":"MARCA PRUEBA","datos":{"b":2,"a":1}}"""),
                Celda(VehiculoB, "SIMIT_VEHICULO", payload: """{"semaforo":"VERDE","cantidad_comparendos":0}"""),
                Celda(VehiculoA, "SIMIT_VEHICULO", estado: "ERROR", tipoError: "TIMEOUT", payload: null),
            ],
            encabezado: new Dictionary<string, string?> { ["empresa"] = "Empresa Prueba", ["creador"] = "Ana Prueba" },
            alcance: ["RUNT", "SIMIT_VEHICULO"]);
        var otra = Entrada(
            celdas:
            [
                Celda(VehiculoA, "SIMIT_VEHICULO", estado: "ERROR", tipoError: "TIMEOUT", payload: null),
                Celda(VehiculoB, "SIMIT_VEHICULO", payload: """{ "cantidad_comparendos": 0, "semaforo": "VERDE" }"""),
                Celda(VehiculoA, "RUNT", payload: """{"datos":{"a":1,"b":2},"marca":"MARCA PRUEBA","placa":"TST101"}"""),
            ],
            encabezado: new Dictionary<string, string?> { ["creador"] = "Ana Prueba", ["empresa"] = "Empresa Prueba" },
            alcance: ["SIMIT_VEHICULO", "RUNT"]);

        var huella = HuellaEvidencia.Calcular(una);

        huella.Should().MatchRegex("^[0-9a-f]{64}$", "SHA-256 en hexadecimal minúsculo (char(64))");
        HuellaEvidencia.Calcular(otra).Should().Be(huella);
        HuellaEvidencia.Calcular(una).Should().Be(huella, "es determinista");
        // La fecha del dato se compara al microsegundo (la precisión de timestamptz): lo que vuelve de la base huellea igual.
        HuellaEvidencia.Calcular(una with { Celdas = [.. una.Celdas.Select(c => c with { DatoObtenidoEn = c.DatoObtenidoEn?.ToOffset(TimeSpan.FromHours(-5)) })] })
            .Should().Be(huella, "el mismo instante en otra zona horaria es el mismo dato");
    }

    // AC2/AC3 (F07-C8) — subir la versión de una celda (reproceso, llenado manual) cambia la huella; también su estado o su payload.
    [Fact]
    public void Cambio_de_version_de_celda_cambia_la_huella()
    {
        var base_ = Entrada(celdas: [Celda(VehiculoA, "RUNT", version: 5), Celda(VehiculoA, "SIMIT_VEHICULO", version: 6)]);
        var huella = HuellaEvidencia.Calcular(base_);

        HuellaEvidencia.Calcular(base_ with { Celdas = [base_.Celdas[0], base_.Celdas[1] with { VersionTablero = 7 }] })
            .Should().NotBe(huella, "version_tablero sube con todo cambio de celda");
        HuellaEvidencia.Calcular(base_ with { Celdas = [base_.Celdas[0], base_.Celdas[1] with { Estado = "ERROR", TipoError = "TIMEOUT" }] })
            .Should().NotBe(huella);
        HuellaEvidencia.Calcular(base_ with { Celdas = [base_.Celdas[0], base_.Celdas[1] with { Payload = """{"semaforo":"ROJO"}""" }] })
            .Should().NotBe(huella);
        HuellaEvidencia.Calcular(base_ with { Celdas = [base_.Celdas[0], base_.Celdas[1] with { DatoObtenidoEn = Dato.AddSeconds(1) }] })
            .Should().NotBe(huella);
        HuellaEvidencia.Calcular(base_ with { Encabezado = new Dictionary<string, string?> { ["empresa"] = "Otra Empresa" } })
            .Should().NotBe(huella, "los nombres resueltos del encabezado salen en el archivo");
        HuellaEvidencia.Calcular(base_ with { Tipo = TipoEvidencia.Xlsx }).Should().NotBe(huella);
    }

    // AC1 (F07-C8) — una plantilla nueva del renderizador invalida todo lo generado con la anterior.
    [Fact]
    public void Cambio_de_version_de_plantilla_cambia_la_huella()
    {
        var entrada = Entrada(celdas: [Celda(VehiculoA, "RUNT")]);

        HuellaEvidencia.Calcular(entrada with { VersionPlantilla = 2 }).Should().NotBe(HuellaEvidencia.Calcular(entrada));
    }

    // R-20 — el origen y el usuario manual no entran en la huella: una celda manual con el mismo dato huellea igual que la de API.
    [Fact]
    public void Origen_y_usuario_manual_no_entran_en_la_huella()
    {
        var api = CeldaConsulta(OrigenCelda.Api, null);
        var manual = CeldaConsulta(OrigenCelda.Manual, Guid.NewGuid());

        var deApi = CeldaHuella.Desde(api);
        CeldaHuella.Desde(manual).Should().Be(deApi);
        deApi.Fuente.Should().Be("SIMIT_VEHICULO");
        deApi.Estado.Should().Be("COMPLETADO");
        deApi.VersionTablero.Should().Be(9);
    }

    private static CeldaConsulta CeldaConsulta(OrigenCelda origen, Guid? usuario) => new()
    {
        Id = Guid.Parse("0199f000-0000-7000-8000-0000000000c3"),
        DiagnosticoId = Lote,
        VehiculoId = VehiculoA,
        Fuente = Fuente.SimitVehiculo,
        Estado = EstadoCelda.Completado,
        Origen = origen,
        UsuarioManualId = usuario,
        Payload = """{"semaforo":"VERDE"}""",
        DatoObtenidoEn = Dato,
        VersionTablero = 9,
    };

    private static EntradaHuella Entrada(CeldaHuella[] celdas, Dictionary<string, string?>? encabezado = null, string[]? alcance = null) => new(
        VersionPlantilla: 1,
        Tipo: TipoEvidencia.Pdf,
        Cabecera: new CabeceraHuella(Lote, "Flota de prueba", "SIMPLE", null, alcance ?? ["RUNT", "SIMIT_VEHICULO"]),
        Encabezado: encabezado ?? new Dictionary<string, string?> { ["empresa"] = "Empresa Prueba" },
        Celdas: celdas);

    private static CeldaHuella Celda(
        Guid vehiculo, string fuente, string estado = "COMPLETADO", string? tipoError = null, long version = 1, string? payload = """{"ok":true}""") =>
        new(vehiculo, fuente, estado, null, tipoError, Dato, version, payload);
}
