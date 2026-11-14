using System.Net;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Diagnostico.Tests.Plataforma;
using Flit.Diagnostico.Tests.Saneamiento;
using FluentAssertions;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;
using VehiculoSembrado = Flit.Diagnostico.Tests.Tablero.VehiculoSembrado;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #14576 AC38 (plan §6.5, SP-11, SP-18) — PDF de deuda <c>GET …/deuda/pdf</c>: armazón de <c>PdfVehiculo</c> (membrete, tablas
/// FLIT, fuentes FLIT), encabezado con placa y fecha, dictamen si hay, una tabla por fuente con su fecha de consulta y el
/// saneamiento con sus renglones sin el contenido de las evidencias. El texto se lee con <see cref="PdfTextExtractor"/>.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Reportes.PdfDeudaTests
/// </code>
/// </example>
public sealed class PdfDeudaTests
{
    internal static PanelDeudaDto Panel(string placa = "TST731", SaneamientoDeudaDto? saneamiento = null) => new(
        new VehiculoDeudaDto(Guid.NewGuid(), placa),
        "CON_ANALISIS",
        new DictamenDeudaDto("BLOQUEADO", 2, [new MotivoDeudaDto("SIMIT_PROPIETARIO", "BLOQUEA", "El propietario tiene comparendos.")]),
        [
            new FuenteDeudaDto("SIMIT_PROPIETARIO", EstadosDeuda.ConDeuda, DeudaDePrueba.Dato,
                Persona: new PersonaDeudaDto("C", DeudaDePrueba.DocumentoPropietario, "****3040"), PazYSalvo: false,
                Comparendos: [new ComparendoDeudaDto("TST-C-0731", "2026-09-30", "450000.00", "PENDIENTE")], ComparendosTruncados: false,
                AcuerdosPago: [new AcuerdoDeudaDto("VIGENTE", "300000")]),
            new FuenteDeudaDto("IMPUESTO", EstadosDeuda.ConDeuda, DeudaDePrueba.Dato, MontoTotal: 1250000,
                Vigencias: [new VigenciaDeudaDto(2026, 1250000, false)]),
            new FuenteDeudaDto("SOAT", EstadosDeuda.NoVigente, DeudaDePrueba.Dato, Vence: "31/05/2026"),
            new FuenteDeudaDto("RTM", EstadosDeuda.SinHallazgo, DeudaDePrueba.Dato, Vence: "10/01/2027"),
            new FuenteDeudaDto("SIMIT_LOCATARIO", EstadosDeuda.NoAplica, MotivoNa: "SIN_DOCUMENTO_LOCATARIO"),
        ],
        saneamiento) { DiagnosticoNombre = "Lote deuda", DiagnosticoCreadoEn = DeudaDePrueba.Dato };

    // AC38 — encabezado con placa y fecha, dictamen, una sección por fuente (documento completo, valor como texto) y saneamiento
    // con sus renglones; ningún nombre de archivo de evidencia.
    [Fact]
    public void Pdf_trae_encabezado_dictamen_fuentes_y_saneamiento()
    {
        var saneamiento = new SaneamientoDeudaDto("EN_GESTION")
        {
            Renglones = [new RenglonDeudaDto("SIMIT_PROPIETARIO", "DEUDA", "PENDIENTE", null, "Pago en trámite", ConEvidencia: true)],
        };

        var texto = PdfTextExtractor.Extract(PdfDeuda.Generar(Panel(saneamiento: saneamiento), new DateTimeOffset(2026, 11, 14, 17, 30, 0, TimeSpan.Zero)));

        PdfTextExtractor.Contains(texto, "TST731").Should().BeTrue(texto);
        PdfTextExtractor.Contains(texto, "14/11/2026").Should().BeTrue("fecha de generación en hora Colombia");
        PdfTextExtractor.Contains(texto, "Bloqueado").Should().BeTrue();
        PdfTextExtractor.Contains(texto, "El propietario tiene comparendos.").Should().BeTrue();
        PdfTextExtractor.Contains(texto, DeudaDePrueba.DocumentoPropietario).Should().BeTrue("el PDF lleva el documento completo (SP-11)");
        PdfTextExtractor.Contains(texto, "450000.00").Should().BeTrue("el valor del comparendo va como texto");
        PdfTextExtractor.Contains(texto, "Con deuda").Should().BeTrue();
        PdfTextExtractor.Contains(texto, "No vigente").Should().BeTrue();
        PdfTextExtractor.Contains(texto, "Pago en trámite").Should().BeTrue();
        PdfTextExtractor.Contains(texto, "En gestión").Should().BeTrue();
    }

    // SP-18 — el nombre del archivo solo lleva [A-Z0-9] de la placa. AP-3: si no queda nada, deuda-sin-placa.pdf (antes
    // deuda-vehiculo.pdf), nunca con espacios.
    [Theory]
    [InlineData("TST731", "deuda-TST731.pdf")]
    [InlineData("ab\"c;1\r\n", "deuda-ABC1.pdf")]
    [InlineData("--", "deuda-sin-placa.pdf")]
    public void Nombre_del_archivo_reduce_la_placa(string placa, string esperado) =>
        PdfDeuda.NombreArchivo(placa).Should().Be(esperado);

    // AP-3 — placa vacía, nula o solo con espacios: deuda-sin-placa.pdf, sin espacios.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" - / ")]
    public void HU14576_AP3_sin_placa_el_PDF_se_llama_deuda_sin_placa(string? placa)
    {
        var nombre = PdfDeuda.NombreArchivo(placa!);

        nombre.Should().Be("deuda-sin-placa.pdf");
        nombre.Should().NotContain(" ");
    }

    // AC38/SP-18/SP-11 por HTTP — 200 application/pdf, adjunto deuda-{placa}.pdf, cuatro cabeceras de descarga segura; sin el
    // nombre de la persona que traía el payload; sin reportes.descargar 403; otra compañía 404.
    [Fact]
    public async Task Http_pdf_con_cabeceras_seguras_403_y_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await TableroAyudas.SembrarAsync(empresas.A, ct, 
            vehiculos: [new VehiculoSembrado("TST732", [new(Fuente.SimitPropietario, EstadoCelda.Completado, 1)])]);
        var vehiculo = lote.Vehiculos.Single();
        var persona = await DeudaDePrueba.PersonaAsync(api, empresas.A, vehiculo.Id, RolPersona.Propietario, 1, DeudaDePrueba.DocumentoPropietario, ct);
        await TableroAyudas.PayloadAsync(empresas.A, lote.Celda("TST732", Fuente.SimitPropietario), DeudaDePrueba.PayloadPersonas(
            (persona.Id, 1, """{"semaforo":"ROJO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0732","valor":"99000","estado":"PENDIENTE"}],"nombre":"PERSONA FICTICIA ZQX"}""")), ct);

        var respuesta = await api.Cliente(DeudaDePrueba.TokenDescargar(api, empresas.A.Id)).GetAsync(DeudaDePrueba.RutaPdf(lote.Id, vehiculo.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        respuesta.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be("deuda-TST732.pdf");
        DeudaDePrueba.CabecerasSeguras(respuesta);
        var texto = PdfTextExtractor.Extract(await respuesta.Content.ReadAsByteArrayAsync(ct));
        PdfTextExtractor.Contains(texto, "PERSONA FICTICIA ZQX").Should().BeFalse("el nombre nunca sale (SP-11)");
        PdfTextExtractor.Contains(texto, "99000").Should().BeTrue();

        (await api.Cliente(DeudaDePrueba.TokenVer(api, empresas.A.Id)).GetAsync(DeudaDePrueba.RutaPdf(lote.Id, vehiculo.Id), ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Cliente(DeudaDePrueba.TokenDescargar(api, empresas.B.Id)).GetAsync(DeudaDePrueba.RutaPdf(lote.Id, vehiculo.Id), ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
