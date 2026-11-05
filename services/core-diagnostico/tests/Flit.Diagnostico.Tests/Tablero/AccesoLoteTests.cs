using System.Net;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13845 (F08-H1), AC1 y AC3 (F08-C3) — el listado de lotes y la lectura individual del tablero usan la MISMA regla de
/// <c>AccesoLote</c>: para cada usuario, con EMPRESA y con PROPIOS, los lotes que trae <c>GET /diagnosticos</c> son exactamente
/// aquellos cuya grilla responde 200 (el resto, 404).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~AccesoLoteTests
/// </code>
/// </example>
public sealed class AccesoLoteTests
{
    [Fact]
    public async Task Lista_y_lectura_individual_aplican_la_misma_regla()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var empresa = empresas.A;
        var usuario1 = ListadoDePrueba.TokenUsuario(api, empresa.Id);
        var usuario2 = ListadoDePrueba.TokenUsuario(api, empresa.Id);
        var admin = ListadoDePrueba.TokenAdmin(api, empresa.Id);
        var t = ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00");
        var lotes = new[]
        {
            await ListadoDePrueba.LoteAsync(empresa, "Del usuario 1", t, ct, Tablero.Usuario(usuario1)),
            await ListadoDePrueba.LoteAsync(empresa, "Del usuario 2", t.AddMinutes(1), ct, Tablero.Usuario(usuario2)),
            await ListadoDePrueba.LoteAsync(empresa, "Del admin", t.AddMinutes(2), ct, Tablero.Usuario(admin), estado: EstadoDiagnostico.Creado),
        };
        var deB = await ListadoDePrueba.LoteAsync(empresas.B, "De la otra compañía", t, ct, Tablero.Usuario(usuario1));

        async Task Comparar(string visibilidad)
        {
            foreach (var token in new[] { usuario1, usuario2, admin })
            {
                var listados = ListadoDePrueba.Ids(await ListadoDePrueba.JsonAsync(await api.Cliente(token).GetAsync(ListadoDePrueba.Ruta, ct), ct));
                var legibles = new List<Guid>();
                foreach (var lote in lotes.Append(deB))
                {
                    var grilla = await api.Cliente(token).GetAsync(Tablero.RutaGrilla(lote.Id), ct);
                    grilla.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
                    if (grilla.StatusCode == HttpStatusCode.OK)
                        legibles.Add(lote.Id);
                }

                listados.Should().BeEquivalentTo(legibles, $"{visibilidad}: lista y lectura individual coinciden para cada usuario");
                listados.Should().NotContain(deB.Id, "nunca otra compañía, aunque el creador coincida");
            }
        }

        await Comparar("EMPRESA (sin fila)");
        ListadoDePrueba.Ids(await ListadoDePrueba.JsonAsync(await api.Cliente(usuario1).GetAsync(ListadoDePrueba.Ruta, ct), ct))
            .Should().HaveCount(3, "con EMPRESA el usuario ve los tres");

        await SembrarAsync(empresa, new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios }, ct);
        await Comparar("PROPIOS");
        ListadoDePrueba.Ids(await ListadoDePrueba.JsonAsync(await api.Cliente(usuario1).GetAsync(ListadoDePrueba.Ruta, ct), ct))
            .Should().Equal(lotes[0].Id);
        ListadoDePrueba.Ids(await ListadoDePrueba.JsonAsync(await api.Cliente(admin).GetAsync(ListadoDePrueba.Ruta, ct), ct))
            .Should().HaveCount(3);
    }
}
