using Flit.Analytics.Application.Scheduling;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests.Scheduling;

/// <summary>
/// Reportes 2.0 HU-D — validación del payload de reglas de alerta (§4.7 del contrato):
/// métrica/operador de vocabulario cerrado, umbral obligatorio, ventana 5..43200,
/// cooldown 5..10080 (defaults 1440/240) y 1..10 correos válidos. Mensajes en español.
/// </summary>
public sealed class AlertRuleValidationTests
{
    private static AlertRuleInput Valid(
        string? name = "Rechazo OT alto",
        string? metric = "rejection_rate_pct",
        string? @operator = "gt",
        decimal? threshold = 25.0m,
        int? windowMinutes = 1440,
        int? cooldownMinutes = 240,
        IReadOnlyList<string>? recipients = null,
        bool? isActive = true) =>
        new(name, metric, @operator, threshold, windowMinutes, cooldownMinutes,
            recipients ?? ["alertas@empresa.co"], isActive);

    [Fact]
    public void Payload_valido_normaliza_y_no_devuelve_error()
    {
        var (result, error) = SchedulingValidation.ValidateAlertRule(Valid());

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Metric.Should().Be("rejection_rate_pct");
        result.Operator.Should().Be("gt");
        result.Threshold.Should().Be(25.0m);
        result.WindowMinutes.Should().Be(1440);
        result.CooldownMinutes.Should().Be(240);
    }

    [Fact]
    public void Nombre_vacio_devuelve_error_en_espanol()
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(Valid(name: null));

        error.Should().Be("El nombre de la alerta es obligatorio.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cpu_usage")]
    public void Metrica_fuera_del_vocabulario_devuelve_error(string? metric)
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(Valid(metric: metric));

        error.Should().Contain("métrica");
    }

    [Theory]
    [InlineData("ict_stuck_in_validation")]
    [InlineData("ict_novelty_rate_pct")]
    [InlineData("ict_webhook_delivery_failures")]
    [InlineData("ict_jobs_out_of_sla")]
    public void Metricas_ict_estan_en_el_vocabulario(string metric)
    {
        // HU5 / E1: las métricas de observabilidad ICT son válidas para crear reglas de alerta.
        var (result, error) = SchedulingValidation.ValidateAlertRule(Valid(metric: metric));

        error.Should().BeNull();
        result!.Metric.Should().Be(metric);
    }

    [Theory]
    [InlineData("ot_rejection_rate_pct")]
    [InlineData("ot_stuck_count")]
    public void Metricas_de_alcance_ot_estan_en_el_vocabulario(string metric)
    {
        // Reportes 2.0 HU-D (tercera ola): alertas del propio Organismo de Tránsito.
        var (result, error) = SchedulingValidation.ValidateAlertRule(Valid(metric: metric));

        error.Should().BeNull();
        result!.Metric.Should().Be(metric);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("eq")]
    public void Operador_fuera_del_vocabulario_devuelve_error(string? @operator)
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(Valid(@operator: @operator));

        error.Should().Be("El operador debe ser uno de: gt, gte, lt, lte.");
    }

    [Fact]
    public void Umbral_ausente_devuelve_error()
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(Valid(threshold: null));

        error.Should().Be("El umbral es obligatorio.");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(43_201)]
    public void Ventana_fuera_de_rango_devuelve_error(int window)
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(Valid(windowMinutes: window));

        error.Should().Be("La ventana de evaluación debe estar entre 5 y 43200 minutos.");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10_081)]
    public void Cooldown_fuera_de_rango_devuelve_error(int cooldown)
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(Valid(cooldownMinutes: cooldown));

        error.Should().Be("El periodo de enfriamiento debe estar entre 5 y 10080 minutos.");
    }

    [Fact]
    public void Ventana_y_cooldown_ausentes_usan_defaults()
    {
        var (result, error) = SchedulingValidation.ValidateAlertRule(
            Valid(windowMinutes: null, cooldownMinutes: null));

        error.Should().BeNull();
        result!.WindowMinutes.Should().Be(SchedulingValidation.DefaultWindowMinutes);
        result.CooldownMinutes.Should().Be(SchedulingValidation.DefaultCooldownMinutes);
    }

    [Fact]
    public void Email_invalido_devuelve_error_en_espanol()
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(
            Valid(recipients: ["sin-arroba.co"]));

        error.Should().Be("El correo 'sin-arroba.co' no es una dirección válida.");
    }

    [Fact]
    public void Sin_destinatarios_devuelve_error()
    {
        var (_, error) = SchedulingValidation.ValidateAlertRule(Valid(recipients: []));

        error.Should().Be("Debe indicar al menos un destinatario de correo.");
    }
}
