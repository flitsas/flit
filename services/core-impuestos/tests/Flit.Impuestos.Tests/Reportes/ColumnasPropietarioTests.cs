using System.Net;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Liquidaciones;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Reportes;

/// <summary>
/// HU #14694 (M14; F4b-CF3, DA-7=b, Ley 1581) — columnas del propietario en los reportes y su exporte: nombre, tipo y número de
/// documento completos, sin enmascarar; nunca dirección, correo ni teléfono, ni como columna ni como dato, aunque el resultado
/// cifrado del proveedor los traiga en <c>validations</c>.
/// </summary>
/// <example>
/// <code>
/// var excel = await cliente.GetByteArrayAsync("/api/v1/impuestos/reportes/deuda/excel", ct);
/// </code>
/// </example>
public sealed class ColumnasPropietarioTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private const string Direccion = "CL 10 20 30";
    private const string Correo = "pedro@correo.example";
    private const string Telefono = "3001234567";

    private ApiDePrueba Api => fixture.Api;

    [Theory]
    [InlineData("detalle")]
    [InlineData("deuda")]
    public async Task El_propietario_sale_completo_y_sin_datos_de_contacto(string tipo)
    {
        var ct = Preparar();
        var empresa = await SembrarAsync(ct);
        var cliente = Api.Cliente(Api.Token(empresa: empresa, permisos: E.Exportar));

        using var json = await cliente.GetAsync($"{E.Reportes}/{tipo}", ct);
        using var excel = await cliente.GetAsync($"{E.Reportes}/{tipo}/excel", ct);

        var crudo = await json.Content.ReadAsStringAsync(ct);
        json.StatusCode.Should().Be(HttpStatusCode.OK, crudo);
        var texto = E.TextoDe(crudo);
        texto.Should().Contain("Propietario Prueba Uno").And.Contain("1000000002", "sin enmascarar (DA-7=b)");
        texto.Should().NotContain(Direccion).And.NotContain(Correo).And.NotContain(Telefono);
        texto.ToLowerInvariant().Should().NotContain("direccion").And.NotContain("correo").And.NotContain("telefono");
        excel.StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, filas) = EntornoMasiva.LeerXlsx(await excel.Content.ReadAsByteArrayAsync(ct));
        filas[0].Should().Contain(["Propietario", "Tipo de documento", "Número de documento"]);
        filas[0].Should().NotContain(c => c.Contains("Dirección", StringComparison.OrdinalIgnoreCase) || c.Contains("Correo", StringComparison.OrdinalIgnoreCase)
            || c.Contains("Teléfono", StringComparison.OrdinalIgnoreCase));
        var fila = filas.Single(f => f.Contains("PRO001"));
        fila.Should().Contain(["Propietario Prueba Uno", "CC", "1000000002"]);
        filas.SelectMany(f => f).Should().NotContain(c => c.Contains(Direccion) || c.Contains(Correo) || c.Contains(Telefono));
    }

    /// <summary>Una placa exitosa con deuda, propietario cifrado y un resultado cifrado que trae los datos de contacto en validations.</summary>
    private async Task<Guid> SembrarAsync(CancellationToken ct)
    {
        var empresa = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        var (_, items) = await E.SembrarAsync(Api, empresa, Flit.Impuestos.Api.Persistence.TipoLiquidacion.Individual, Guid.NewGuid(), ct, null,
            new ItemSemilla("PRO001", ResultadoStatus: "con_deuda", Total: 50m, Departamento: "25", DepartamentoNombre: "Cundinamarca",
                PropietarioNombre: "Propietario Prueba Uno", DocumentoTipo: "CC", DocumentoNumero: "1000000002", Vigencias: ["2025"]));
        var proteccion = Api.App.Services.GetRequiredService<Flit.Impuestos.Api.Plataforma.IProteccionImpuestos>();
        var resultado = proteccion.ProtegerResultado(
            "{\"result\":{\"validations\":[{\"field\":\"OwnerAddress\",\"sent\":\"" + Direccion + "\"},{\"field\":\"OwnerEmail\",\"sent\":\"" + Correo +
            "\"},{\"field\":\"OwnerPhone\",\"sent\":\"" + Telefono + "\"}]}}");
        await using var db = Api.Contexto(empresa);
        await db.Set<LiquidacionItem>().Where(i => i.Id == items[0]).ExecuteUpdateAsync(s => s.SetProperty(i => i.ResultadoCifrado, resultado), ct);
        return empresa;
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        return TestContext.Current.CancellationToken;
    }
}
