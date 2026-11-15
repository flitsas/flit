using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Estadisticas.EstadisticasDePrueba;

namespace Flit.Diagnostico.Tests.Estadisticas;

/// <summary>
/// HU #14579, BE-1 — AC «sin PII» (SP-16): con una placa, un VIN y un documento sembrados, el JSON de E1, E2 y E3 no los contiene en
/// ninguna profundidad (ni el creador del lote). Se recorre el árbol entero, claves y valores. PostgreSQL real. Datos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EstadisticasSinPiiTests
/// </code>
/// </example>
public sealed class EstadisticasSinPiiTests
{
    private const string Placa = "PII123";
    private const string Vin = "9ZZPII00000000123";
    private const string Documento = "1020304050";

    [Fact]
    public async Task BE1_AC7_E1_E2_y_E3_no_llevan_placa_VIN_documento_ni_usuario()
    {
        var a = Guid.NewGuid();
        var identidad = new EmpresasDeUna(a);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = new EmpresaDePrueba(api, a);
        var creador = Guid.NewGuid();
        var lote = await LoteAsync(empresa, Dia(0), ct, creador: creador, placas: [Placa]);
        await using (var db = empresa.Db())
        {
            var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == lote.Vehiculos[0].Id, ct);
            vehiculo.Vin = Vin;
            vehiculo.PropietarioDocTipo = "C";
            vehiculo.PropietarioDocCifrado = [9, 9, 9];
            var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == lote.Celdas[0].Id, ct);
            celda.Payload = $$"""{"documento_propietario":"{{Documento}}","placa":"{{Placa}}"}""";
            await db.SaveChangesAsync(ct);
        }

        var flit = api.Token(permisos: [Permisos.BackofficeVer, Permisos.VolumetriaVer], roles: ["operador"], tipoEmpresa: "FLIT");
        var respuestas = new[]
        {
            await empresa.Cliente().GetAsync(Ruta(Hoy, Hoy), ct),
            await api.Cliente(flit).GetAsync(Ruta(Hoy, Hoy, RutaGlobal), ct),
            await api.Cliente(flit).GetAsync(Ruta(Hoy, Hoy, $"{RutaEmpresa}?empresa={a}"), ct),
        };

        foreach (var respuesta in respuestas)
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
            var hojas = Hojas(JsonDocument.Parse(texto).RootElement).ToList();
            hojas.Should().NotBeEmpty();
            hojas.Should().NotContain(h => h.Contains(Placa, StringComparison.OrdinalIgnoreCase)
                || h.Contains(Vin, StringComparison.OrdinalIgnoreCase)
                || h.Contains(Documento, StringComparison.Ordinal)
                || h.Contains(creador.ToString(), StringComparison.OrdinalIgnoreCase));
            texto.Should().NotContain("usuario").And.NotContain("placa").And.NotContain("vin\"");
            JsonDocument.Parse(texto).RootElement.GetProperty("resumen").GetProperty("vehiculos").GetInt32().Should().Be(1, "el vehículo sí cuenta");
        }
    }

    /// <summary>Todas las claves y valores escalares del árbol JSON.</summary>
    private static IEnumerable<string> Hojas(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    yield return p.Name;
                    foreach (var h in Hojas(p.Value))
                        yield return h;
                }

                break;
            case JsonValueKind.Array:
                foreach (var h in e.EnumerateArray().SelectMany(Hojas))
                    yield return h;
                break;
            default:
                yield return e.ToString();
                break;
        }
    }

    private sealed class EmpresasDeUna(Guid empresa) : IEmpresasPlataforma
    {
        public Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EmpresaPlataforma>?>([new EmpresaPlataforma(empresa, "Compañía ficticia", "900000009")]);
    }
}
