using System.Reflection;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Persistence.Siembra;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Configuracion;

/// <summary>
/// HU #14694 (M08, F3-CF3, F5a-CF4, CO-03, CO-04; AC-BE-79) — copia de la configuración en la cabecera del lote: el máximo
/// vigente (operativo si hay, si no el contractual), los departamentos habilitados (null = todos) y la concurrencia, en el jsonb
/// <c>{maxRegistros, departamentosHabilitados, concurrencia}</c>. Sin configuración guardada se usan los valores por defecto
/// sembrados (500, todos). La copia no cambia si la configuración cambia después. El modelo no tiene parámetro de contacto.
/// </summary>
/// <example>
/// <code>
/// var copia = CopiaConfiguracion.De(configuracionOnull, concurrencia: 2);
/// liquidacion.ConfigCopia = copia.ComoJson();
/// </code>
/// </example>
public sealed class CopiaConfiguracionTests(ApiConfiguracionFixture fixture) : IClassFixture<ApiConfiguracionFixture>
{
    [Fact]
    public void Sin_configuracion_guardada_usa_los_valores_por_defecto_sembrados()
    {
        var copia = CopiaConfiguracion.De(null, concurrencia: 2);

        copia.MaxRegistros.Should().Be(ConfiguracionPorDefecto.MaxRegistrosLoteContractual).And.Be(500);
        copia.DepartamentosHabilitados.Should().BeNull("null = todos los activos de /fuentes");
        copia.Concurrencia.Should().Be(2);
    }

    [Fact]
    public void Los_valores_por_defecto_son_500_todos_los_departamentos_sin_operativo_ni_correos()
    {
        var empresa = Guid.NewGuid();
        var ahora = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        var fila = ConfiguracionPorDefecto.Nueva(empresa, ahora);

        fila.TenantId.Should().Be(empresa);
        fila.MaxRegistrosLoteContractual.Should().Be(500);
        fila.DepartamentosHabilitados.Should().BeNull();
        fila.MaxRegistrosLoteOperativo.Should().BeNull();
        fila.CorreosAviso.Should().BeEmpty();
        fila.ActualizadoEn.Should().Be(ahora);
    }

    [Fact]
    public void Con_operativo_copia_el_operativo_y_sin_el_copia_el_contractual()
    {
        var conOperativo = new ConfiguracionEmpresa { MaxRegistrosLoteContractual = 800, MaxRegistrosLoteOperativo = 300, DepartamentosHabilitados = ["05"] };
        var sinOperativo = new ConfiguracionEmpresa { MaxRegistrosLoteContractual = 800 };

        CopiaConfiguracion.De(conOperativo, 2).MaxRegistros.Should().Be(300);
        CopiaConfiguracion.De(sinOperativo, 2).MaxRegistros.Should().Be(800);
        CopiaConfiguracion.De(conOperativo, 2).DepartamentosHabilitados.Should().Equal("05");
    }

    [Fact]
    public void Un_operativo_mayor_que_el_contractual_guardado_a_mano_no_pasa_del_contractual()
    {
        var fila = new ConfiguracionEmpresa { MaxRegistrosLoteContractual = 100, MaxRegistrosLoteOperativo = 900 };

        CopiaConfiguracion.De(fila, 2).MaxRegistros.Should().Be(100);
    }

    [Fact]
    public void El_json_lleva_exactamente_las_tres_claves_del_contrato_y_se_lee_igual()
    {
        var copia = CopiaConfiguracion.De(new ConfiguracionEmpresa { MaxRegistrosLoteContractual = 800, MaxRegistrosLoteOperativo = 300, DepartamentosHabilitados = ["05", "25"] }, 2);

        var json = copia.ComoJson();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("maxRegistros", "departamentosHabilitados", "concurrencia");
        doc.RootElement.GetProperty("maxRegistros").GetInt32().Should().Be(300);
        doc.RootElement.GetProperty("departamentosHabilitados").EnumerateArray().Select(d => d.GetString()).Should().Equal("05", "25");
        doc.RootElement.GetProperty("concurrencia").GetInt32().Should().Be(2);
        CopiaConfiguracion.DesdeJson(json).Should().BeEquivalentTo(copia);
        CopiaConfiguracion.DesdeJson(CopiaConfiguracion.De(null, 3).ComoJson()).DepartamentosHabilitados.Should().BeNull();
    }

    [Fact]
    public void La_copia_no_cambia_si_la_configuracion_cambia_despues()
    {
        var fila = new ConfiguracionEmpresa { MaxRegistrosLoteContractual = 800, DepartamentosHabilitados = ["05"] };
        var copia = CopiaConfiguracion.De(fila, 2);

        fila.MaxRegistrosLoteContractual = 10;
        fila.DepartamentosHabilitados[0] = "99";

        copia.MaxRegistros.Should().Be(800);
        copia.DepartamentosHabilitados.Should().Equal("05");
    }

    [Theory]
    [InlineData(null, "25", true)]
    [InlineData(new[] { "05", "25" }, "25", true)]
    [InlineData(new[] { "05" }, "25", false)]
    [InlineData(new string[0], "05", false)]
    public void DepartamentoHabilitado_null_es_todos_y_una_lista_es_la_lista(string[]? habilitados, string departamento, bool esperado)
    {
        var copia = CopiaConfiguracion.De(new ConfiguracionEmpresa { MaxRegistrosLoteContractual = 10, DepartamentosHabilitados = habilitados }, 1);

        copia.DepartamentoHabilitado(departamento).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_concurrencia_menor_que_1_no_se_copia(int concurrencia)
    {
        var act = () => CopiaConfiguracion.De(null, concurrencia);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void El_modelo_de_configuracion_no_tiene_ningun_parametro_de_contacto_del_propietario()
    {
        Type[] tipos =
        [
            typeof(ConfiguracionEmpresa), typeof(CopiaConfiguracion), typeof(ConfiguracionDto), typeof(ConfiguracionContractualDto),
            typeof(ConfiguracionOperativaDto), typeof(ConfiguracionOperativaEntrada), typeof(ConfiguracionContractualEntrada),
        ];
        string[] prohibidos = ["contact", "owner", "propietario", "telefono", "phone", "celular", "direccion", "address"];

        var nombres = tipos.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).Select(p => p.Name).ToList();

        nombres.Should().NotBeEmpty();
        nombres.Where(n => prohibidos.Any(p => n.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Should().BeEmpty("CO-04 (DA-9a): la configuración no tiene parámetro de contacto del propietario");
    }

    [Fact]
    public async Task CopiaAsync_lee_la_configuracion_de_la_compania_o_los_valores_por_defecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = fixture.Api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var conFila = Guid.NewGuid();
        var sinFila = Guid.NewGuid();
        await using (var db = api.Contexto(conFila))
        {
            db.Add(new ConfiguracionEmpresa
            {
                TenantId = conFila,
                MaxRegistrosLoteContractual = 700,
                MaxRegistrosLoteOperativo = 250,
                DepartamentosHabilitados = ["25"],
                ActualizadoEn = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        using var scope = api.App.Services.CreateScope();
        var servicio = scope.ServiceProvider.GetRequiredService<ServicioConfiguracion>();

        var guardada = await servicio.CopiaAsync(conFila, 2, ct);
        var porDefecto = await servicio.CopiaAsync(sinFila, 2, ct);

        guardada.Should().BeEquivalentTo(new CopiaConfiguracion(250, ["25"], 2));
        porDefecto.Should().BeEquivalentTo(new CopiaConfiguracion(500, null, 2), "AC-BE-79: el primer lote usa los valores sembrados");
    }
}
