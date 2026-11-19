using System.Net;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Reportes;
using Flit.Diagnostico.Tests.Reportes.Catalogo;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Backoffice.ReportesBackofficeTests;

namespace Flit.Diagnostico.Tests.Backoffice;

/// <summary>
/// HU #14579, BE-4 — AC «exportación global» (contrato-api §5; SP-7, SP-8, SP-10, SP-11, SP-12, SP-14): B2/B3
/// <c>/backoffice/reportes/{tipo}/global/excel|pdf</c> y B5/B6 <c>/backoffice/reportes/{tipo}/excel|pdf?empresa=</c> reutilizan el
/// exportador de BE-3. La global lleva el sufijo <c>-global</c> en el nombre y audita <c>accesos_reporte</c> (XLSX/PDF, empresas del
/// archivo) más <c>lecturas_backoffice</c> por empresa del archivo en un guardado; aplican 422 sin descifrar ni auditar, 429 por usuario y
/// las cabeceras de descarga segura; sin <c>reportes.descargar</c> o desde una compañía cliente, 403. PostgreSQL real. Datos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ExportacionesBackofficeTests
/// </code>
/// </example>
public sealed partial class ExportacionesBackofficeTests
{
    [GeneratedRegex(@"^diagnostico-reporte-[a-z]+-\d{8}-\d{8}(-global)?\.(xlsx|pdf)$")]
    private static partial Regex NombreArchivo();

    private static string Archivo(string tipo, string formato, Guid? empresa = null) => empresa is { } e
        ? $"{Base}/{tipo}/{formato}?{ReportesDePrueba.Rango()}&empresa={e}"
        : $"{Base}/{tipo}/global/{formato}?{ReportesDePrueba.Rango()}";

    [Fact]
    public async Task BE4_AC5_exportacion_global_con_sufijo_y_doble_auditoria()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var api = await ArrancarAsync(new EmpresasDePrueba([new(a, "Alfa", null), new(b, "Beta", null)]));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, a), [new("RPA501", Propietarios: [new("CC", "1020304501")])], ct);
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, b), [new("RPB502"), new("RPB503")], ct);
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, c), [new("RPC504", Propietarios: [new("CC", "1020304504")])], ct);
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(TokenDescarga(api, flit)).GetAsync(Archivo("vehiculos", "excel"), ct);

        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes[..Math.Min(400, bytes.Length)]));
        var nombre = respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"');
        NombreArchivo().IsMatch(nombre).Should().BeTrue(nombre);
        nombre.Should().EndWith("-global.xlsx");
        respuesta.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue();
        var texto = string.Join('\n', ExcelDePrueba.Leer(bytes).Hojas["Reporte"].SelectMany(f => f));
        texto.Should().Contain("RPA501").And.Contain("CC 1020304501").And.Contain("Alfa").And.Contain("RPB503");
        texto.Should().NotContain("RPC504").And.NotContain("1020304504");

        var acceso = (await AccesosAsync(api, flit, ct)).Should().ContainSingle().Which;
        acceso.Alcance.Should().Be(AlcanceReporte.BackofficeGlobal);
        acceso.Formato.Should().Be(FormatoReporte.Xlsx);
        acceso.Filas.Should().Be(3);
        acceso.Empresas.Should().Be(2);
        var lecturas = await LecturasAsync(api, flit, ct);
        lecturas.Select(l => l.EmpresaConsultada).Should().BeEquivalentTo([a, b]);
        lecturas.Select(l => l.Fecha).Append(acceso.Fecha).Distinct().Should().ContainSingle("un solo SaveChanges");
    }

    [Fact]
    public async Task BE4_AC5_exportacion_por_compania_en_pdf_sin_sufijo()
    {
        var a = Guid.NewGuid();
        await using var api = await ArrancarAsync(new EmpresasDePrueba([new(a, "Alfa", null)]));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, a), [new("RPA511")], ct);
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(TokenDescarga(api, flit)).GetAsync(Archivo("consultas", "pdf", a), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Content.Headers.ContentType!.ToString().Should().Be("application/pdf");
        var nombre = respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"');
        NombreArchivo().IsMatch(nombre).Should().BeTrue(nombre);
        nombre.Should().NotContain("-global");
        var acceso = (await AccesosAsync(api, flit, ct)).Should().ContainSingle().Which;
        acceso.Alcance.Should().Be(AlcanceReporte.BackofficeEmpresa);
        acceso.Formato.Should().Be(FormatoReporte.Pdf);
        acceso.EmpresaConsultada.Should().Be(a);
        (await LecturasAsync(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().Equal(a);
    }

    // 422 sin descifrar ni auditar; 429 si el mismo usuario ya exporta (el freno es el de BE-3, compartido).
    [Fact]
    public async Task BE4_AC5_tope_422_y_freno_429_en_la_global()
    {
        var a = Guid.NewGuid();
        await using var api = await ArrancarAsync(new EmpresasDePrueba([new(a, "Alfa", null)]), new Dictionary<string, string?> { ["Diagnostico:Reportes:MaximoFilasPdf"] = "1" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, a), [new("RPA521", Propietarios: [new("CC", "1020304521")]), new("RPA522", Propietarios: [new("CC", "1020304522")])], ct);
        var protector = ReportesDePrueba.Protector(api);
        var flit = Guid.NewGuid();

        protector.Reiniciar();
        var grande = await api.Cliente(TokenDescarga(api, flit)).GetAsync(Archivo("vehiculos", "pdf"), ct);

        grande.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await grande.Content.ReadAsStringAsync(ct));
        (await ReportesDePrueba.CodigoAsync(grande, ct)).Should().Be("REPORTE_DEMASIADO_GRANDE");
        protector.Llamadas.Should().Be(0, "el conteo va antes de descifrar");
        (await AccesosAsync(api, flit, ct)).Should().BeEmpty();
        (await LecturasAsync(api, flit, ct)).Should().BeEmpty();

        using var compuerta = new SemaphoreSlim(0);
        protector.Compuerta = compuerta;
        using var primera = api.Cliente(TokenDescarga(api, flit));
        using var otra = api.Cliente(TokenDescarga(api, flit));
        var lenta = primera.GetAsync(Archivo("vehiculos", "excel"), ct);
        await protector.PrimerDescifrado.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        var segunda = await otra.GetAsync(Archivo("vehiculos", "excel", a), ct);
        compuerta.Release(100);
        (await lenta).StatusCode.Should().Be(HttpStatusCode.OK);

        segunda.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ReportesDePrueba.CodigoAsync(segunda, ct)).Should().Be("EXPORTACION_EN_CURSO");
        segunda.Headers.RetryAfter.Should().NotBeNull();
    }

    // AC «autorización» de las exportaciones: un cliente con los claims armados a mano y un FLIT sin reportes.descargar: 403.
    [Fact]
    public async Task BE4_AC6_exportaciones_exigen_backoffice_y_reportes_descargar()
    {
        var identidad = new EmpresasDePrueba([]);
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: ConIdentidad(identidad));
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Token(roles: ["admin_diagnostico"],
            permisos: [Permisos.BackofficeVer, Permisos.VolumetriaVer, Permisos.ReportesDescargar, Permisos.ReportesVer], tipoEmpresa: "CLIENTE");
        var sinDescargar = TokenFlit(api, Guid.NewGuid());

        foreach (var token in new[] { cliente, sinDescargar })
        {
            foreach (var ruta in new[] { Archivo("vehiculos", "excel"), Archivo("vehiculos", "pdf"), Archivo("consumo", "excel", Guid.NewGuid()), Archivo("vehiculos", "pdf", Guid.NewGuid()) })
                (await api.Cliente(token).GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, ruta);
        }

        identidad.Llamadas.Should().Be(0);
    }

    private static string TokenDescarga(ApiDePrueba api, Guid usuario) =>
        api.Token(permisos: [Permisos.BackofficeVer, Permisos.ReportesDescargar], roles: ["operador"], tipoEmpresa: "FLIT", sujeto: usuario.ToString());

    /// <summary>La API con el protector de prueba de BE-3 (cuenta y retiene descifrados), la lista de Identidad y configuración extra.</summary>
    private static Task<ApiDePrueba> ArrancarAsync(EmpresasDePrueba identidad, IDictionary<string, string?>? configuracion = null) =>
        ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.AddSingleton<IProtectorDocumentoPersona>(sp =>
                new ProtectorDePrueba(new ProtectorDocumentoPersona(sp.GetRequiredService<IDataProtectionProvider>())));
            b.Services.AddScoped<IEmpresasPlataforma>(_ => identidad);
            if (configuracion is not null)
                b.Configuration.AddInMemoryCollection(configuracion);
        });
}
