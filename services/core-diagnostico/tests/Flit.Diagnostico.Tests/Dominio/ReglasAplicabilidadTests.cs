using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13733 (F03-H5) — reglas de no aplica, evaluadas tras completar RUNT y antes de liberar las demás celdas: impuesto con
/// servicio público → <c>SERVICIO_PUBLICO</c>; impuesto con departamento nulo o sin fila soportada → <c>DEPARTAMENTO_NO_SOPORTADO</c>;
/// RUNT y SIMIT del locatario sin documento (ni proveedor ni declarado) → <c>SIN_DOCUMENTO_LOCATARIO</c>. Regla pura.
/// </summary>
/// <example>
/// <code>
/// var motivo = ReglasAplicabilidad.Evaluar(Fuente.ImpuestoDepartamental, new HechosAplicabilidad("PUBLICO", "05", true, null));
/// // motivo == MotivoNa.ServicioPublico
/// </code>
/// </example>
public sealed class ReglasAplicabilidadTests
{
    private static HechosAplicabilidad Hechos(string? servicio = "PARTICULAR", string? departamento = "05", bool soportado = true, OrigenDocumento? locatario = null) =>
        new(servicio, departamento, soportado, locatario);

    // AC1 (F03-C9) — impuesto de un vehículo de servicio público: NA SERVICIO_PUBLICO aunque el departamento esté soportado.
    [Theory]
    [InlineData("PUBLICO")]
    [InlineData("Público ")]
    [InlineData("SERVICIO PUBLICO")]
    public void Servicio_publico_deja_el_impuesto_NA(string servicio)
    {
        ReglasAplicabilidad.Evaluar(Fuente.ImpuestoDepartamental, Hechos(servicio)).Should().Be(MotivoNa.ServicioPublico);
        ReglasAplicabilidad.EsServicioPublico(servicio).Should().BeTrue();
    }

    // AC2 (F03-C9) — departamento sin fila soportada (o desconocido): NA DEPARTAMENTO_NO_SOPORTADO.
    [Theory]
    [InlineData("05", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void Departamento_no_soportado_deja_el_impuesto_NA(string? departamento, bool soportado) =>
        ReglasAplicabilidad.Evaluar(Fuente.ImpuestoDepartamental, Hechos(departamento: departamento, soportado: soportado))
            .Should().Be(MotivoNa.DepartamentoNoSoportado);

    // AC2 — departamento soportado y servicio particular: el impuesto sí aplica.
    [Fact]
    public void Departamento_soportado_deja_el_impuesto_aplicable()
    {
        ReglasAplicabilidad.Evaluar(Fuente.ImpuestoDepartamental, Hechos()).Should().BeNull();
        ReglasAplicabilidad.EsServicioPublico("PARTICULAR").Should().BeFalse();
        ReglasAplicabilidad.EsServicioPublico(null).Should().BeFalse();
    }

    // AC3 (F03-C6) — sin documento del locatario: RUNT_LOCATARIO y SIMIT_LOCATARIO en NA SIN_DOCUMENTO_LOCATARIO.
    [Theory]
    [InlineData(Fuente.RuntLocatario)]
    [InlineData(Fuente.SimitLocatario)]
    public void Sin_documento_de_locatario_deja_sus_celdas_NA(Fuente fuente) =>
        ReglasAplicabilidad.Evaluar(fuente, Hechos(locatario: null)).Should().Be(MotivoNa.SinDocumentoLocatario);

    // AC3 — locatario declarado por el cliente o hidratado del proveedor: sus celdas aplican.
    [Theory]
    [InlineData(Fuente.RuntLocatario, OrigenDocumento.DeclaradoCliente)]
    [InlineData(Fuente.SimitLocatario, OrigenDocumento.DeclaradoCliente)]
    [InlineData(Fuente.RuntLocatario, OrigenDocumento.Proveedor)]
    [InlineData(Fuente.SimitLocatario, OrigenDocumento.Proveedor)]
    public void Locatario_declarado_o_de_proveedor_deja_sus_celdas_aplicables(Fuente fuente, OrigenDocumento origen) =>
        ReglasAplicabilidad.Evaluar(fuente, Hechos(locatario: origen)).Should().BeNull();

    // Las demás fuentes no tienen regla de no aplica: ni el servicio público ni la falta de locatario las tocan.
    [Theory]
    [InlineData(Fuente.Runt)]
    [InlineData(Fuente.RuntPropietario)]
    [InlineData(Fuente.SimitVehiculo)]
    [InlineData(Fuente.SimitPropietario)]
    [InlineData(Fuente.Avaluo)]
    public void Otras_fuentes_siempre_aplican(Fuente fuente) =>
        ReglasAplicabilidad.Evaluar(fuente, Hechos("PUBLICO", null, false, null)).Should().BeNull();
}
