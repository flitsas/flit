using Flit.Impuestos.Domain.Liquidaciones;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Dominio;

/// <summary>
/// HU #14694 (M05) — clase de cada código de error de la consulta según la tabla §9 de la guía del API: corregir datos (no
/// reintentar), depende del mensaje (<c>portal_rejected</c>) o reintentable como consulta nueva. <c>pdf_download_failed</c> y
/// <c>s3_error</c> no son errores: acompañan un resultado parcial. Un código que la tabla no conoce se trata como «depende».
/// </summary>
/// <example>
/// <code>
/// ClasificacionErrores.De("rate_limited"); // ClaseError.Reintentable
/// </code>
/// </example>
public sealed class ClaseErrorTests
{
    [Theory]
    [InlineData("invalid_params", ClaseError.CorregirDatos)]
    [InlineData("owner_validation_failed", ClaseError.CorregirDatos)]
    [InlineData("vehicle_data_required", ClaseError.CorregirDatos)]
    [InlineData("portal_rejected", ClaseError.Depende)]
    [InlineData("captcha_blocked", ClaseError.Reintentable)]
    [InlineData("auth_failed", ClaseError.Reintentable)]
    [InlineData("rate_limited", ClaseError.Reintentable)]
    [InlineData("timeout", ClaseError.Reintentable)]
    [InlineData("http_status", ClaseError.Reintentable)]
    [InlineData("proxy_failed", ClaseError.Reintentable)]
    [InlineData("source_inactive", ClaseError.Reintentable)]
    [InlineData("adapter_unavailable", ClaseError.Reintentable)]
    [InlineData("parse_error", ClaseError.Reintentable)]
    [InlineData("unexpected", ClaseError.Reintentable)]
    [InlineData("worker_lost", ClaseError.Reintentable)]
    public void Tabla_9_de_la_guia(string codigo, ClaseError clase)
    {
        ClasificacionErrores.De(codigo).Should().Be(clase);
    }

    [Fact]
    public void La_tabla_tiene_exactamente_los_15_codigos_de_error_de_la_guia()
    {
        ClasificacionErrores.Tabla.Should().HaveCount(15);
        ClasificacionErrores.Tabla.Keys.Should().NotContain(["pdf_download_failed", "s3_error"], "esos acompañan un resultado parcial");
    }

    [Theory]
    [InlineData("pdf_download_failed", true)]
    [InlineData("s3_error", true)]
    [InlineData("rate_limited", false)]
    [InlineData(null, false)]
    public void Codigos_de_resultado_parcial(string? codigo, bool parcial)
    {
        ClasificacionErrores.EsParcial(codigo).Should().Be(parcial);
    }

    [Theory]
    [InlineData("algo_nuevo_del_api")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("RATE_LIMITED")]
    public void Codigo_desconocido_es_depende(string? codigo)
    {
        ClasificacionErrores.De(codigo).Should().Be(ClaseError.Depende, "sin regla conocida, decide una persona");
    }
}
