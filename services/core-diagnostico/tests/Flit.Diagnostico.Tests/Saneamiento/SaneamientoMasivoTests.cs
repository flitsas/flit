using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Solicitud masiva <c>POST /saneamientos/solicitudes</c> (plan §6.1, S8; HU #14576 AC5, AC6, AC33 y AC34; SP-9): 1 a 500 ids de
/// uno o varios lotes, síncrona y en una transacción; los no elegibles se omiten con su motivo y los que el usuario no puede ver
/// salen como NO_ENCONTRADO, sin revelar que existen. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SaneamientoMasivoTests
/// </code>
/// </example>
public sealed class SaneamientoMasivoTests
{
    // AC5 + AC33 — dos lotes: elegibles, no terminado, ya solicitado, de otra compañía e inexistente.
    [Fact]
    public async Task HU14576_AC5_AC33_masivo_de_dos_lotes_con_omitidos_por_motivo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var uno = await SaneamientoDePrueba.LoteAsync(empresas.A, ["SAN201", "SAN202", "SAN203"], ct, abiertas: ["SAN202"]);
        var dos = await SaneamientoDePrueba.LoteAsync(empresas.A, ["SAN204"], ct);
        var deB = await SaneamientoDePrueba.LoteAsync(empresas.B, ["SAN205"], ct);
        await SaneamientoDePrueba.SembrarAsync(empresas.A, uno.Id, uno.Vehiculos[2].Id, EstadoSaneamiento.Saneado, ct);
        var inexistente = Guid.NewGuid();
        Guid[] ids = [uno.Vehiculos[0].Id, uno.Vehiculos[1].Id, uno.Vehiculos[2].Id, dos.Vehiculos[0].Id, deB.Vehiculos[0].Id, inexistente];

        var respuesta = await api.Cliente(SaneamientoDePrueba.Token(api, empresas.A.Id)).PostAsync(SaneamientoDePrueba.RutaMasivo, Cuerpo(ids), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        var json = JsonElement.Parse(texto);
        json.GetProperty("solicitados").GetInt32().Should().Be(2);
        json.GetProperty("omitidos").EnumerateArray()
            .Select(o => (o.GetProperty("vehiculo_id").GetGuid(), o.GetProperty("motivo").GetString()))
            .Should().Equal(
                (uno.Vehiculos[1].Id, "NO_TERMINADO"),
                (uno.Vehiculos[2].Id, "YA_SOLICITADO"),
                (deB.Vehiculos[0].Id, "NO_ENCONTRADO"),
                (inexistente, "NO_ENCONTRADO"));

        await using var db = empresas.A.Db();
        var creados = await db.Set<SaneamientoEntidad>().AsNoTracking().Where(s => s.Estado == EstadoSaneamiento.Solicitado).ToListAsync(ct);
        creados.Select(s => s.VehiculoId).Should().BeEquivalentTo([uno.Vehiculos[0].Id, dos.Vehiculos[0].Id]);
        (await db.Set<VehiculoDiagnostico>().AsNoTracking().Where(v => v.SaneamientoEstado == EstadoSaneamiento.Solicitado).Select(v => v.Id).ToListAsync(ct))
            .Should().BeEquivalentTo([uno.Vehiculos[0].Id, dos.Vehiculos[0].Id]);
        await using var dbB = empresas.B.Db();
        (await dbB.Set<SaneamientoEntidad>().CountAsync(ct)).Should().Be(0, "el vehículo de B no se toca");
    }

    // AC6 — lista vacía, 501 ids distintos, un elemento que no es uuid o un cuerpo sin lista: 400 LISTA_INVALIDA y nada escrito.
    [Fact]
    public async Task HU14576_AC6_lista_fuera_de_rango_es_400_LISTA_INVALIDA()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN206"], ct);
        using var cliente = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id));
        string[] cuerpos =
        [
            """{"vehiculo_ids":[]}""",
            JsonSerializer.Serialize(new { vehiculo_ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).Append(lote.Vehiculos[0].Id) }),
            $$"""{"vehiculo_ids":["{{lote.Vehiculos[0].Id}}","no-es-uuid"]}""",
            """{"vehiculo_ids":"x"}""",
            """{}""",
            """no es json""",
        ];

        foreach (var cuerpo in cuerpos)
        {
            var respuesta = await cliente.PostAsync(SaneamientoDePrueba.RutaMasivo, new StringContent(cuerpo, Encoding.UTF8, "application/json"), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, cuerpo.Length > 60 ? cuerpo[..60] : cuerpo);
            (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("LISTA_INVALIDA");
        }

        await using var db = empresa.Db();
        (await db.Set<SaneamientoEntidad>().CountAsync(ct)).Should().Be(0);
    }

    // SP-9 — los repetidos no cuentan para el tope: 500 distintos más 10 repetidos se aceptan.
    [Fact]
    public async Task SP9_ids_repetidos_no_cuentan_para_el_tope()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN207"], ct);
        var distintos = Enumerable.Range(0, 499).Select(_ => Guid.NewGuid()).Append(lote.Vehiculos[0].Id).ToList();
        var conRepetidos = distintos.Concat(Enumerable.Repeat(lote.Vehiculos[0].Id, 10)).ToArray();

        var respuesta = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id)).PostAsync(SaneamientoDePrueba.RutaMasivo, Cuerpo(conRepetidos), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await SaneamientoDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("solicitados").GetInt32().Should().Be(1);
        json.GetProperty("omitidos").GetArrayLength().Should().Be(499);
        json.GetProperty("omitidos").EnumerateArray().Should().OnlyContain(o => o.GetProperty("motivo").GetString() == "NO_ENCONTRADO");
    }

    // AC34 — con visibilidad PROPIOS, los vehículos de un lote de otro usuario salen NO_ENCONTRADO; los propios se solicitan.
    [Fact]
    public async Task HU14576_AC34_visibilidad_PROPIOS_omite_lotes_ajenos_como_NO_ENCONTRADO()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await using (var db = empresa.Db())
        {
            db.Add(new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios });
            await db.SaveChangesAsync(ct);
        }

        var yo = Guid.NewGuid();
        var propio = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN208"], ct, creador: yo);
        var ajeno = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN209"], ct, creador: Guid.NewGuid());

        var respuesta = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id, usuario: yo))
            .PostAsync(SaneamientoDePrueba.RutaMasivo, Cuerpo([propio.Vehiculos[0].Id, ajeno.Vehiculos[0].Id]), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await SaneamientoDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("solicitados").GetInt32().Should().Be(1);
        var omitido = json.GetProperty("omitidos").EnumerateArray().Single();
        omitido.GetProperty("vehiculo_id").GetGuid().Should().Be(ajeno.Vehiculos[0].Id);
        omitido.GetProperty("motivo").GetString().Should().Be("NO_ENCONTRADO");
        await using var lectura = empresa.Db();
        (await lectura.Set<SaneamientoEntidad>().SingleAsync(ct)).VehiculoId.Should().Be(propio.Vehiculos[0].Id);
    }

    private static StringContent Cuerpo(IEnumerable<Guid> ids) =>
        new(JsonSerializer.Serialize(new { vehiculo_ids = ids }), Encoding.UTF8, "application/json");
}
