using System.Text.Json;
using Flit.Diagnostico.Api.Persistence.Siembra;
using Flit.Diagnostico.Domain.Dictamen;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Siembra de <c>diagnostico.reglas_dictamen</c> (corte 4, ADR-0077). El proyector lee
/// <see cref="CatalogoReglasDictamen.Filas"/>; <c>condicion</c> repite el código de la fila y no es un motor.
/// </summary>
public sealed class SiembraReglasDictamenTests
{
    [Fact]
    public void Proyecta_42_filas_de_la_version_1_y_54_de_la_version_2()
    {
        // Punto 2 (2026-11-11): la siembra suma v2 sin tocar v1; los dictámenes ya emitidos conservan su versión.
        var filas = ProyectorReglasDictamen.Proyectar();

        filas.Should().HaveCount(96);
        filas.Count(f => f.CatalogoVersion == 1).Should().Be(42);
        filas.Count(f => f.CatalogoVersion == CatalogoReglasDictamen.Version).Should().Be(54);
        filas.Should().OnlyContain(f => f.Vigente);
    }

    [Fact]
    public void La_clave_catalogo_version_tipo_y_codigo_no_se_repite()
    {
        var filas = ProyectorReglasDictamen.Proyectar();

        filas.Select(f => (f.CatalogoVersion, f.TipoTramiteCodigo, f.Codigo)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Efecto_y_plantilla_son_los_de_Filas()
    {
        var filas = ProyectorReglasDictamen.Proyectar();

        filas.Should().HaveCount(CatalogoReglasDictamen.Historico.Count);
        foreach (var esperada in CatalogoReglasDictamen.Historico)
        {
            var fila = filas.Should().ContainSingle(f =>
                f.CatalogoVersion == esperada.CatalogoVersion
                && f.TipoTramiteCodigo == esperada.TipoTramiteCodigo
                && f.Codigo == esperada.Codigo).Subject;

            fila.Efecto.Should().Be(esperada.Efecto);
            fila.PlantillaTexto.Should().Be(esperada.PlantillaTexto);
        }
    }

    [Fact]
    public void Condicion_es_un_objeto_cuyo_codigo_es_el_de_la_fila()
    {
        var filas = ProyectorReglasDictamen.Proyectar();

        foreach (var fila in filas)
        {
            using var json = JsonDocument.Parse(fila.Condicion);
            json.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            json.RootElement.GetProperty("codigo").GetString().Should().Be(fila.Codigo);
            json.RootElement.EnumerateObject().Should().ContainSingle().Which.Name.Should().Be("codigo");
        }
    }

    [Fact]
    public void Soat_y_rtm_bloquean_salvo_rtm_unilateral_y_prenda_impuesto_y_fuente_advierten_en_los_tres_tramites()
    {
        var filas = ProyectorReglasDictamen.Proyectar().Where(f => f.CatalogoVersion == CatalogoReglasDictamen.Version).ToList();

        CatalogoReglasDictamen.TiposTramite.Should().HaveCount(3);
        foreach (var tipo in CatalogoReglasDictamen.TiposTramite)
        {
            filas.Should().ContainSingle(f => f.TipoTramiteCodigo == tipo && f.Codigo == "SOAT_NO_VIGENTE")
                .Which.Efecto.Should().Be("BLOQUEA");
            // Ola 2026-11-11 (V4): en el unilateral la RTM no se valida (art. 5.3.2.2) y advierte.
            filas.Should().ContainSingle(f => f.TipoTramiteCodigo == tipo && f.Codigo == "RTM_NO_VIGENTE")
                .Which.Efecto.Should().Be(tipo == "TRASPASO_UNILATERAL" ? "ADVIERTE" : "BLOQUEA");
            filas.Should().ContainSingle(f => f.TipoTramiteCodigo == tipo && f.Codigo == "PRENDA")
                .Which.Efecto.Should().Be("ADVIERTE");
            filas.Should().ContainSingle(f => f.TipoTramiteCodigo == tipo && f.Codigo == "IMPUESTO")
                .Which.Efecto.Should().Be("ADVIERTE");
            filas.Should().ContainSingle(f => f.TipoTramiteCodigo == tipo && f.Codigo == "FUENTE_PENDIENTE")
                .Which.Efecto.Should().Be("ADVIERTE");
        }
    }

    [Fact]
    public void El_id_de_la_misma_clave_es_estable_en_dos_llamadas()
    {
        var primera = ProyectorReglasDictamen.Proyectar();
        var segunda = ProyectorReglasDictamen.Proyectar();

        primera.Should().HaveCount(96);
        segunda.Should().HaveCount(96);
        foreach (var fila in primera)
        {
            var otra = segunda.Should().ContainSingle(f =>
                f.CatalogoVersion == fila.CatalogoVersion
                && f.TipoTramiteCodigo == fila.TipoTramiteCodigo
                && f.Codigo == fila.Codigo).Subject;

            otra.Id.Should().Be(fila.Id);
            fila.Id.Should().NotBe(Guid.Empty);
        }

        // RFC 4122 §4.3, espacio DNS, nombre flit.diagnostico.reglas_dictamen/1/TRASPASO_STANDARD/SOAT_NO_VIGENTE.
        primera.Should().ContainSingle(f =>
                f.CatalogoVersion == 1 && f.TipoTramiteCodigo == "TRASPASO_STANDARD" && f.Codigo == "SOAT_NO_VIGENTE")
            .Which.Id.Should().Be(Guid.Parse("d981f4f1-ad6f-590f-9fe7-5578c1db7471"));
    }
}
