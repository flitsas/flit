using Flit.Analytics.Application.Scheduling;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests.Scheduling;

/// <summary>
/// Reportes 2.0 HU-D — validación del payload de informes programados (§4.7 del contrato):
/// nombre requerido ≤120, tipo/periodicidad/formato de vocabulario cerrado, coherencia
/// frequency ↔ dayOfWeek/dayOfMonth, hora 0..23 y 1..10 correos válidos. Mensajes en español.
/// </summary>
public sealed class ReportScheduleValidationTests
{
    private static ReportScheduleInput Valid(
        string? name = "Informe semanal OT",
        string? reportType = "ot",
        string? frequency = "weekly",
        int? dayOfWeek = 1,
        int? dayOfMonth = null,
        int? sendHour = 7,
        string? format = "pdf",
        IReadOnlyList<string>? recipients = null,
        bool? isActive = true) =>
        new(name, reportType, frequency, dayOfWeek, dayOfMonth, sendHour, format,
            recipients ?? ["gerencia@empresa.co"], isActive);

    [Fact]
    public void Payload_valido_normaliza_y_no_devuelve_error()
    {
        var (result, error) = SchedulingValidation.ValidateReportSchedule(Valid());

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Name.Should().Be("Informe semanal OT");
        result.Frequency.Should().Be("weekly");
        result.DayOfWeek.Should().Be(1);
        result.SendHour.Should().Be(7);
        result.Recipients.Should().ContainSingle().Which.Should().Be("gerencia@empresa.co");
    }

    [Fact]
    public void Nombre_vacio_devuelve_error_en_espanol()
    {
        var (result, error) = SchedulingValidation.ValidateReportSchedule(Valid(name: "  "));

        result.Should().BeNull();
        error.Should().Be("El nombre del informe es obligatorio.");
    }

    [Fact]
    public void Nombre_de_mas_de_120_caracteres_devuelve_error()
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(Valid(name: new string('a', 121)));

        error.Should().Be("El nombre no puede superar los 120 caracteres.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dashboard")]
    public void Tipo_de_informe_fuera_del_vocabulario_devuelve_error(string? reportType)
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(Valid(reportType: reportType));

        error.Should().Contain("tipo de informe");
    }

    [Fact]
    public void Frecuencia_desconocida_devuelve_error()
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(
            Valid(frequency: "hourly", dayOfWeek: null));

        error.Should().Be("La periodicidad debe ser una de: daily, weekly, monthly.");
    }

    [Fact]
    public void Daily_con_dia_de_semana_devuelve_error_de_coherencia()
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(
            Valid(frequency: "daily", dayOfWeek: 1));

        error.Should().Be("Un informe diario no debe indicar día de la semana ni día del mes.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(7)]
    public void Weekly_requiere_dayOfWeek_entre_0_y_6(int? dayOfWeek)
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(
            Valid(frequency: "weekly", dayOfWeek: dayOfWeek));

        error.Should().Contain("día de la semana");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(29)]
    public void Monthly_requiere_dayOfMonth_entre_1_y_28(int? dayOfMonth)
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(
            Valid(frequency: "monthly", dayOfWeek: null, dayOfMonth: dayOfMonth));

        error.Should().Contain("día del mes");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Hora_fuera_de_rango_devuelve_error(int sendHour)
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(Valid(sendHour: sendHour));

        error.Should().Be("La hora de envío debe estar entre 0 y 23 (hora de Bogotá).");
    }

    [Fact]
    public void Hora_ausente_usa_el_default_7()
    {
        var (result, error) = SchedulingValidation.ValidateReportSchedule(Valid(sendHour: null));

        error.Should().BeNull();
        result!.SendHour.Should().Be(SchedulingValidation.DefaultSendHour);
    }

    [Fact]
    public void Formato_desconocido_devuelve_error()
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(Valid(format: "csv"));

        error.Should().Be("El formato debe ser 'excel' o 'pdf'.");
    }

    [Fact]
    public void Email_invalido_devuelve_error_en_espanol_con_el_correo()
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(
            Valid(recipients: ["gerencia@empresa.co", "no-es-un-correo"]));

        error.Should().Be("El correo 'no-es-un-correo' no es una dirección válida.");
    }

    [Fact]
    public void Sin_destinatarios_devuelve_error()
    {
        var (_, error) = SchedulingValidation.ValidateReportSchedule(Valid(recipients: []));

        error.Should().Be("Debe indicar al menos un destinatario de correo.");
    }

    [Fact]
    public void Mas_de_10_destinatarios_devuelve_error()
    {
        var recipients = Enumerable.Range(1, 11).Select(i => $"user{i}@empresa.co").ToList();

        var (_, error) = SchedulingValidation.ValidateReportSchedule(Valid(recipients: recipients));

        error.Should().Be("No puede indicar más de 10 destinatarios.");
    }

    [Fact]
    public void Destinatarios_duplicados_se_deduplican_sin_error()
    {
        var (result, error) = SchedulingValidation.ValidateReportSchedule(
            Valid(recipients: ["a@b.co", " A@B.CO "]));

        error.Should().BeNull();
        result!.Recipients.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------
    // Reportes 2.0 (HU-D, segunda ola) — informe tipo "consulta"
    // ------------------------------------------------------------------

    [Fact]
    public void Tipo_consulta_valido_normaliza_savedQueryId_y_scope()
    {
        var id = Guid.NewGuid();
        var input = Valid(reportType: "consulta", format: "excel") with
        {
            SavedQueryId = id,
            SavedQueryScope = "empresa",
        };

        var (result, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().BeNull();
        result!.SavedQueryId.Should().Be(id);
        result.SavedQueryScope.Should().Be("empresa");
    }

    [Fact]
    public void Tipo_consulta_sin_savedQueryId_devuelve_error()
    {
        var input = Valid(reportType: "consulta", format: "excel") with { SavedQueryScope = "empresa" };

        var (_, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().Be("Un informe de tipo 'consulta' requiere indicar savedQueryId.");
    }

    [Fact]
    public void Tipo_consulta_sin_scope_devuelve_error()
    {
        var input = Valid(reportType: "consulta", format: "excel") with { SavedQueryId = Guid.NewGuid() };

        var (_, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().Be("El alcance de la consulta debe ser 'empresa', 'ot' o 'superadmin'.");
    }

    [Fact]
    public void Tipo_consulta_con_scope_desconocido_devuelve_error()
    {
        var input = Valid(reportType: "consulta", format: "excel") with
        {
            SavedQueryId = Guid.NewGuid(),
            SavedQueryScope = "otro",
        };

        var (_, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().Be("El alcance de la consulta debe ser 'empresa', 'ot' o 'superadmin'.");
    }

    [Fact]
    public void Tipo_consulta_con_scope_superadmin_valido_normaliza_sin_error()
    {
        var id = Guid.NewGuid();
        var input = Valid(reportType: "consulta", format: "excel") with
        {
            SavedQueryId = id,
            SavedQueryScope = "superadmin",
        };

        var (result, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().BeNull();
        result!.SavedQueryScope.Should().Be("superadmin");
    }

    [Fact]
    public void Tipo_consulta_en_pdf_devuelve_error()
    {
        var input = Valid(reportType: "consulta", format: "pdf") with
        {
            SavedQueryId = Guid.NewGuid(),
            SavedQueryScope = "empresa",
        };

        var (_, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().Be("Un informe de tipo 'consulta' solo se entrega en formato Excel.");
    }

    [Fact]
    public void SavedQueryId_en_un_tipo_que_no_es_consulta_devuelve_error()
    {
        var input = Valid(reportType: "resumen") with { SavedQueryId = Guid.NewGuid() };

        var (_, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().Be("savedQueryId y el alcance de la consulta solo aplican a informes de tipo 'consulta'.");
    }

    // ------------------------------------------------------------------
    // Reportes 2.0 (HU-D, tercera ola) — 3 tipos propios del organismo (alcance OT)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("ot_analisis")]
    [InlineData("ot_informe")]
    [InlineData("ot_revisores")]
    public void Tipos_de_alcance_ot_estan_en_el_vocabulario_y_no_exigen_saved_query(string reportType)
    {
        var (result, error) = SchedulingValidation.ValidateReportSchedule(Valid(reportType: reportType));

        error.Should().BeNull();
        result!.ReportType.Should().Be(reportType);
        result.SavedQueryId.Should().BeNull();
        result.SavedQueryScope.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Reportes 2.0 (HU-D, tercera ola) — consulta guardada del organismo (savedQueryScope="ot")
    // ------------------------------------------------------------------

    [Fact]
    public void Tipo_consulta_con_scope_ot_valido_normaliza_sin_error()
    {
        var id = Guid.NewGuid();
        var input = Valid(reportType: "consulta", format: "excel") with
        {
            SavedQueryId = id,
            SavedQueryScope = "ot",
        };

        var (result, error) = SchedulingValidation.ValidateReportSchedule(input);

        error.Should().BeNull();
        result!.SavedQueryId.Should().Be(id);
        result.SavedQueryScope.Should().Be("ot");
    }
}
