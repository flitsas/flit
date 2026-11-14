using System.Net;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Panel de deuda de la gestión FLIT (security-previa SP-7, opción a; HU #14576): <c>GET /backoffice/saneamientos/{sid}/deuda?empresa=</c>
/// en el grupo B, auditado por el filtro de empresa explícita, sin caché, con la misma proyección del panel de la compañía (SP-11).
/// Ninguna ruta del cliente acepta <c>?empresa=</c>. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~DeudaBackofficeTests
/// </code>
/// </example>
public sealed class DeudaBackofficeTests
{
    // SP-7 — el panel de la gestión responde con la deuda del vehículo del saneamiento, deja la lectura auditada y no se guarda en caché.
    [Fact]
    public async Task HU14576_SP7_panel_de_deuda_del_backoffice_auditado()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBD101", EstadoSaneamiento.EnGestion, ct);
        await Tablero.Tablero.PayloadAsync(empresa, lote.Celda("SBD101", Fuente.SimitVehiculo),
            """{"semaforo":"ROJO","cantidad_comparendos":1,"comparendos":[{"numero":"SBD-C-1","fecha":"2026-09-30","valor":"468500.50","estado":"PENDIENTE"}]}""", ct);
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(GestionDePrueba.TokenFlit(api, usuario: flit)).GetAsync(GestionDePrueba.Ruta(s.Id, empresa.Id, "/deuda"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = await GestionDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("vehiculo").GetProperty("id").GetGuid().Should().Be(lote.Vehiculos[0].Id);
        var simit = json.GetProperty("fuentes").EnumerateArray().Single(f => f.GetProperty("fuente").GetString() == "SIMIT_VEHICULO");
        simit.GetProperty("estado").GetString().Should().Be("CON_DEUDA");
        json.GetProperty("saneamiento").GetProperty("estado").GetString().Should().Be("EN_GESTION");
        DeudaDePrueba.Claves(json).Should().NotContain("nombre", "SP-11: la persona va sin nombre");
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        (await db.Set<LecturaBackoffice>().IgnoreQueryFilters().AsNoTracking().CountAsync(l => l.UsuarioId == flit && l.EmpresaConsultada == empresa.Id, ct))
            .Should().Be(1, "la lectura entre compañías queda auditada");
    }

    // SP-7 — el sid de otra empresa es 404; un vehículo sin terminar es 409; la ruta del cliente con ?empresa= y el token de FLIT no da datos.
    [Fact]
    public async Task HU14576_SP7_404_entre_empresas_409_sin_terminar_y_cliente_sin_empresa_explicita()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (DosEmpresas.En(api).A, DosEmpresas.En(api).B);
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(a, "SBD102", EstadoSaneamiento.EnGestion, ct);
        var abierto = await SaneamientoDePrueba.LoteAsync(a, ["SBD103"], ct, abiertas: ["SBD103"]);
        var sinTerminar = await SaneamientoDePrueba.SembrarAsync(a, abierto.Id, abierto.Vehiculos[0].Id, EstadoSaneamiento.EnGestion, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        var ajena = await cliente.GetAsync(GestionDePrueba.Ruta(s.Id, b.Id, "/deuda"), ct);
        var noTerminado = await cliente.GetAsync(GestionDePrueba.Ruta(sinTerminar.Id, a.Id, "/deuda"), ct);
        var rutaCliente = await cliente.GetAsync($"{DeudaDePrueba.RutaPanel(lote.Id, lote.Vehiculos[0].Id)}?empresa={a.Id}", ct);

        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ajena.Content.ReadAsStringAsync(ct)).Should().NotContain("SBD102");
        noTerminado.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SaneamientoDePrueba.CodigoAsync(noTerminado, ct)).Should().Be("VEHICULO_NO_TERMINADO");
        ((int)rutaCliente.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        (await rutaCliente.Content.ReadAsStringAsync(ct)).Should().NotContain("SBD102", "la ruta del cliente no acepta ?empresa=");
    }
}
