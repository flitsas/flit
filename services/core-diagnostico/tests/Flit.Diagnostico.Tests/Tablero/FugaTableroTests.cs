using System.Net;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// HU #13743 (F04-H1), AC6 y AC7 (F04-C12) — visibilidad del tablero: un diagnóstico de otra compañía responde 404 sin ningún
/// dato en la grilla, el historial y el cierre del panel; con <c>visibilidad_usuario=PROPIOS</c> el usuario solo ve los lotes
/// que creó (404 en los de otro usuario de su compañía) y el admin de la compañía ve todos. La fuga de los GET nuevos la cubren
/// además sus escenarios en <see cref="EscenariosDeFuga"/>.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~FugaTableroTests
/// </code>
/// </example>
public sealed class FugaTableroTests
{
    // AC6 (F04-C12) — un usuario de A pide la grilla (y el historial y el cierre) de un diagnóstico de B: 404 sin datos.
    [Fact]
    public async Task Grilla_de_otra_compania_responde_404()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var deB = await Tablero.SembrarAsync(empresas.B, ct, nombre: "Lote secreto de B", vehiculos:
            [new("TST171", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        var propio = await Tablero.SembrarAsync(empresas.A, ct, vehiculos: [new("TST172", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        var respuestas = new[]
        {
            await empresas.A.Cliente().GetAsync(Tablero.RutaGrilla(deB.Id), ct),
            await empresas.A.Cliente().GetAsync(Tablero.RutaHistorial(deB.Id), ct),
            await empresas.A.Cliente().PostAsync(Tablero.RutaCierrePanel(deB.Id), null, ct),
        };

        foreach (var respuesta in respuestas)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{respuesta.RequestMessage!.RequestUri}: {cuerpo}");
            cuerpo.Should().NotContainAny(["Lote secreto de B", "TST171", deB.Vehiculos.Single().Id.ToString(), empresas.B.Id.ToString()]);
        }

        (await Cabeceras.LeerAsync(api, empresas.B.Id, deB.Id, ct)).Estado.Should().Be(EstadoDiagnostico.EnProceso, "el cierre de A no tocó el lote de B");
        (await empresas.A.Cliente().GetAsync(Tablero.RutaGrilla(propio.Id), ct)).StatusCode.Should().Be(HttpStatusCode.OK, "A sí ve su propio lote");
    }

    // AC7 (F04-C12, R-21) — con PROPIOS el usuario no ve el lote de otro usuario de su compañía (404); el creador y el admin sí.
    [Fact]
    public async Task Visibilidad_PROPIOS_oculta_lotes_de_otro_usuario()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios }, ct);
        var creador = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        var otro = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        var admin = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]);
        var lote = await Tablero.SembrarAsync(empresa, ct, creador: Tablero.Usuario(creador), nombre: "Lote del creador", vehiculos:
            [new("TST181", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);

        foreach (var ruta in new[] { Tablero.RutaGrilla(lote.Id), Tablero.RutaHistorial(lote.Id) })
        {
            var ajena = await api.Cliente(otro).GetAsync(ruta, ct);
            var cuerpo = await ajena.Content.ReadAsStringAsync(ct);
            ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{ruta}: {cuerpo}");
            cuerpo.Should().NotContainAny(["Lote del creador", "TST181"]);
            (await api.Cliente(creador).GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"el creador ve {ruta}");
            (await api.Cliente(admin).GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"el admin ve {ruta}");
        }

        (await api.Cliente(otro).PostAsync(Tablero.RutaCierrePanel(lote.Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Cabeceras.LeerAsync(api, empresa.Id, lote.Id, ct)).Estado.Should().Be(EstadoDiagnostico.EnProceso);
    }

    // Permiso: sin diagnostico.diagnosticos.ver no hay grilla, historial ni cierre (403).
    [Fact]
    public async Task Sin_permiso_de_ver_responde_403()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST191", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        using var cliente = api.Cliente(api.Token(empresa: empresa.Id, permisos: [Permisos.ConfiguracionVer]));

        (await cliente.GetAsync(Tablero.RutaGrilla(lote.Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.GetAsync(Tablero.RutaHistorial(lote.Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.PostAsync(Tablero.RutaCierrePanel(lote.Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
