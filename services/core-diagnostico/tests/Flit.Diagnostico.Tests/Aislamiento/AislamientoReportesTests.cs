using System.Net;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Backoffice;
using Flit.Diagnostico.Tests.Reportes.Catalogo;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #14579, BE-4 — AC «joins por tenant» (SP-4 a y b): una persona de B sembrada con el <c>vehiculo_id</c> de un vehículo de A (dato
/// corrupto, sembrado con los disparadores de FK apagados) no aparece en el reporte global ni en el de A; una compañía apagada no aporta
/// filas ni conteos al global. La prueba de arquitectura de <c>IgnoreQueryFilters</c> se corre aparte, sin editar. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~AislamientoReportesTests
/// </code>
/// </example>
public sealed class AislamientoReportesTests
{
    private const string DocumentoCruzado = "4455667788";

    [Fact]
    public async Task BE4_AC3_persona_de_otra_compania_con_el_vehiculo_de_A_no_aparece()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ReportesBackofficeTests.ConIdentidad(new EmpresasDePrueba([new(a, "Alfa", null), new(b, "Beta", null)])));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (ea, eb) = (new EmpresaDePrueba(api, a), new EmpresaDePrueba(api, b));
        var deA = await ReportesDePrueba.LoteAsync(ea, [new("RPA461", Propietarios: [new("CC", "1020304061")])], ct);
        await ReportesDePrueba.LoteAsync(eb, [new("RPB462")], ct);
        var cifrado = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Proteger(DocumentoCruzado);
        await using (var cx = new NpgsqlConnection(api.BaseDeDatos))
        {
            await cx.OpenAsync(ct);
            // Dato corrupto: la FK compuesta (tenant_id, vehiculo_id) lo impediría; se apaga solo en esta sesión.
            await using var cmd = new NpgsqlCommand(
                "SET session_replication_role = replica; " +
                "INSERT INTO diagnostico.personas_vehiculo (id, tenant_id, vehiculo_id, rol, orden, doc_tipo, doc_cifrado, origen, creado_en) " +
                "VALUES (@id, @tenant, @vehiculo, @rol, 9, 'CC', @cifrado, @origen, now()); SET session_replication_role = origin;", cx);
            cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
            cmd.Parameters.AddWithValue("tenant", b);
            cmd.Parameters.AddWithValue("vehiculo", deA.Vehiculos[0].Id);
            cmd.Parameters.AddWithValue("rol", Flit.Diagnostico.Api.Persistence.Configuraciones.CodigosDeEnum.Codigo(RolPersona.Propietario));
            cmd.Parameters.AddWithValue("cifrado", cifrado);
            cmd.Parameters.AddWithValue("origen", Flit.Diagnostico.Api.Persistence.Configuraciones.CodigosDeEnum.Codigo(OrigenDocumento.Proveedor));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        using var cliente = api.Cliente(ReportesBackofficeTests.TokenFlit(api, Guid.NewGuid()));
        var global = await cliente.GetAsync(ReportesBackofficeTests.Global("vehiculos", "&tamano=100"), ct);
        var deLaA = await cliente.GetAsync($"{ReportesBackofficeTests.Base}/vehiculos?{ReportesDePrueba.Rango()}&empresa={a}", ct);
        var compania = await api.Cliente(ReportesDePrueba.Token(api, a)).GetAsync(ReportesDePrueba.Ruta("vehiculos"), ct);

        foreach (var respuesta in new[] { global, deLaA, compania })
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
            texto.Should().Contain("RPA461").And.Contain("CC 1020304061");
            texto.Should().NotContain(DocumentoCruzado, "la persona es de B aunque apunte al vehículo de A: la unión compara tenant_id");
        }
    }

    [Fact]
    public async Task BE4_AC3_compania_apagada_no_aporta_filas_ni_conteos_al_global()
    {
        var (a, apagada) = (Guid.NewGuid(), Guid.NewGuid());
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ReportesBackofficeTests.ConIdentidad(new EmpresasDePrueba([new(a, "Alfa", null)])));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, a), [new("RPA471")], ct);
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, apagada), [new("RPC472"), new("RPC473")], ct);
        using var cliente = api.Cliente(ReportesBackofficeTests.TokenFlit(api, Guid.NewGuid()));

        foreach (var tipo in new[] { "vehiculos", "consultas", "consumo" })
        {
            var respuesta = await cliente.GetAsync(ReportesBackofficeTests.Global(tipo, "&tamano=100"), ct);
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
            (await ReportesDePrueba.JsonAsync(respuesta, ct)).GetProperty("total").GetInt32().Should().Be(1, tipo);
            texto.Should().NotContain("RPC47").And.NotContain(apagada.ToString());
        }
    }
}
