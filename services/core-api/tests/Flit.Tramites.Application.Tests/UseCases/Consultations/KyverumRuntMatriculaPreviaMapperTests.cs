using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// Epic #12550 (HU #12648) — el mapper Kyverum expone el historial de matrícula como check informativo
/// <c>matricula_previa_runt</c> (siempre <c>ok</c>, veredicto en <c>Datos</c>) y completa
/// <c>transit_office_name</c> desde la solicitud de preasignación cuando el vehículo tiene placa pero
/// el bloque del vehículo no trae organismo. Fixtures: consultas por VIN reales del 2026-09-17.
/// </summary>
public sealed class KyverumRuntMatriculaPreviaMapperTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static KyverumRuntVehicleResponse Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Consultations", "Fixtures", "KyverumRunt", fixture);
        return JsonSerializer.Deserialize<KyverumRuntVehicleResponse>(File.ReadAllText(path), WebJsonOptions)!;
    }

    private static ConsultationCheck? Previa(ConsultationResult r) =>
        r.Checks.FirstOrDefault(c => c.Key == KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt);

    private static string? Dato(ConsultationCheck c, string etiqueta) =>
        c.Datos?.FirstOrDefault(d => d.Etiqueta == etiqueta)?.Valor;

    private static string? Field(ConsultationResult r, string key) =>
        r.HydratedFields.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    [Fact]
    public void PlacaPreasignada_VeredictoSinMatricula_YOrganismoDesdeLaSolicitud()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(Load("vehicle-vin-preasignado-wvt948.json"));

        var previa = Previa(result);
        previa.Should().NotBeNull();
        previa!.Status.Should().Be("ok", "el veredicto no es un hallazgo: lo convierte en bloqueo el preflight de matrícula");
        Dato(previa, KyverumRuntVehicleResultMapper.DatoVeredicto)
            .Should().Be(KyverumRuntVehicleResultMapper.VeredictoSinMatricula);
        Dato(previa, "Placa").Should().Be("WVT948");
        Dato(previa, "Organismo").Should().Be("STRIA TTEyTTO ENVIGADO");
        Dato(previa, "Placa preasignada").Should().Be("2026-09-16");

        Field(result, "plate").Should().Be("WVT948");
        Field(result, "vehicle_state").Should().Be("REGISTRADO");
        // vehiculo.organismoTransito llega nulo: el organismo sale de la entidad que preasignó la placa.
        Field(result, "transit_office_name").Should().Be("STRIA TTEyTTO ENVIGADO");
        // REGISTRADO ⇒ estado_vehiculo en fail (como siempre); el check nuevo no cambia el semáforo.
        result.Checks.Should().Contain(c => c.Key == "estado_vehiculo" && c.Status == "fail");
    }

    [Fact]
    public void Matriculado_VeredictoMatriculado_YElSemaforoNoSePoneRojoPorEso()
    {
        var result = KyverumRuntVehicleResultMapper.MapVehicle(Load("vehicle-vin-matriculado-wvn492.json"));

        var previa = Previa(result)!;
        previa.Status.Should().Be("ok");
        Dato(previa, KyverumRuntVehicleResultMapper.DatoVeredicto)
            .Should().Be(KyverumRuntVehicleResultMapper.VeredictoMatriculado);
        Dato(previa, "Organismo").Should().Be("STRIA DE TTOyTTE MEDELLIN");
        Dato(previa, "Matrícula inicial").Should().Be("2026-09-16");
        Field(result, "transit_office_name").Should().Be("STRIA DE TTOyTTE MEDELLIN");
        // Un traspaso consulta vehículos matriculados por definición: el historial NO puede pintar de
        // rojo esa consulta.
        result.Checks.Where(c => c.Key == KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt)
            .Should().OnlyContain(c => c.Status == "ok");
    }

    [Fact]
    public void SinHistorial_NoHayCheck_YElOrganismoQuedaComoVenga()
    {
        // La captura de referencia (julio) trae solicitudes sin tramitesRealizados: equivale a no tener
        // historial legible. Cae al respaldo por estado, que decide el preflight.
        var result = KyverumRuntVehicleResultMapper.MapVehicle(Load("vehicle-vin-tesla-qyq132.json"));

        Previa(result).Should().BeNull();
        Field(result, "transit_office_name").Should().Be("STRIA DE TTOyTTE MEDELLIN");
    }
}
