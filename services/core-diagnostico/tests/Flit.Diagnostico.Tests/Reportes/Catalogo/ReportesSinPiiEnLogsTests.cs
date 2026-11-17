using System.Net;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-2 (SP-9) — con un proveedor de logs que captura todo: generar páginas de los cuatro tipos y forzar un documento que no
/// se descifra (su excepción trae un documento en el mensaje) no deja en ninguna línea el documento, la placa ni el VIN sembrados; el
/// fallo queda registrado solo con el tipo de la excepción. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReportesSinPiiEnLogsTests
/// </code>
/// </example>
public sealed class ReportesSinPiiEnLogsTests
{
    [Fact]
    public async Task BE2_paginas_y_error_forzado_no_dejan_PII_en_logs()
    {
        var logs = new CapturaLogs();
        await using var api = await ReportesDePrueba.ArrancarAsync(logs: logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa,
            [new("RPT401", Vin: "9ZZPII00000000401", Propietarios: [new("CC", "1020304050")], CifradoIlegible: true)], ct);
        using var cliente = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));

        foreach (var tipo in new[] { "vehiculos", "consultas", "saneamientos", "derivaciones" })
        {
            var respuesta = await cliente.GetAsync(ReportesDePrueba.Ruta(tipo), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, $"{tipo}: {await respuesta.Content.ReadAsStringAsync(ct)}");
        }

        var documento = await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", "documento=1"), ct);
        documento.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReportesDePrueba.CodigoAsync(documento, ct)).Should().Be("FILTRO_INVALIDO");

        var lineas = logs.Lineas;
        lineas.Should().Contain(l => l.Contains("CryptographicException", StringComparison.Ordinal), "el fallo de descifrado se registra con el tipo de la excepción");
        lineas.Should().Contain(l => l.Contains("Acceso a reporte registrado", StringComparison.Ordinal));
        foreach (var pii in new[] { "RPT401", "9ZZPII00000000401", "1020304050", ProtectorDePrueba.DocumentoEnElError })
            lineas.Should().NotContain(l => l.Contains(pii, StringComparison.Ordinal), $"SP-9: «{pii}» no va a los logs");
    }
}
