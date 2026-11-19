using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #14563, hallazgo L3 de seguridad tras la decisión D23: el SuperAdmin de una compañía que no es FLIT (o sin claim
/// <c>tenant_type</c>) pasa la operación FLIT y lee datos de otra compañía con <c>?empresa=</c>. Esa lectura entre compañías
/// queda auditada en <c>diagnostico.lecturas_backoffice</c> igual que la del operador FLIT: exactamente una fila nueva con el
/// usuario, la empresa leída y la ruta, y con el <c>tenant_id</c> de la compañía del lector (la del token, no la leída).
/// Postgres real; la auditoría se lee sin filtro de empresa para ver la fila tal como quedó. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SuperAdminLecturaAuditadaTests
/// </code>
/// </example>
public sealed class SuperAdminLecturaAuditadaTests
{
    // Casos 1 y 2 — SuperAdmin de una compañía CLIENTE, o sin tenant_type, lee las versiones del dictamen (trae ajuste_motivo,
    // @pii:low) de otra compañía: 200 y una sola fila de auditoría con su usuario, la empresa leída, la ruta y su propio tenant_id.
    [Theory]
    [InlineData("CLIENTE")]
    [InlineData(null)]
    public async Task SuperAdmin_no_FLIT_lee_versiones_del_dictamen_de_otra_compania_y_queda_auditado(string? tipoEmpresa)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var leida = DosEmpresas.En(api).A;
        var lote = await SembrarConDictamenAsync(leida, ct);
        var vid = lote.Vehiculos[0].Id;
        var lector = Guid.CreateVersion7();
        var token = api.Token(empresa: lector, permisos: [], roles: ["SuperAdmin"], tipoEmpresa: tipoEmpresa);
        var usuario = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);
        var ruta = $"{Program.Prefijo}/backoffice/diagnosticos/{lote.Id}/vehiculos/{vid}/dictamen/versiones?empresa={leida.Id}";
        var antes = await LecturasAsync(api, ct);

        var respuesta = await api.Cliente(token).GetAsync(ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var nuevas = (await LecturasAsync(api, ct)).Except(antes).ToList();
        nuevas.Should().ContainSingle($"una lectura del SuperAdmin (tenant_type={tipoEmpresa ?? "(sin claim)"}) es una fila de auditoría")
            .Which.Should().Be(new Lectura(lector, leida.Id, usuario, ruta), "la fila lleva el tenant_id del lector, no el de la empresa leída");
    }

    // Caso 3 — la otra lectura de la operación FLIT: el formulario del llenado manual de una celda de otra compañía.
    [Fact]
    public async Task SuperAdmin_CLIENTE_lee_el_formulario_del_llenado_manual_de_otra_compania_y_queda_auditado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var leida = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(leida, ct, vehiculos: [new("TST911", [new(Fuente.Runt, EstadoCelda.Error, 1)])]);
        var celda = lote.Celda("TST911", Fuente.Runt);
        var lector = Guid.CreateVersion7();
        var token = api.Token(empresa: lector, permisos: [], roles: ["SuperAdmin"], tipoEmpresa: "CLIENTE");
        var usuario = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);
        var ruta = $"{Program.Prefijo}/backoffice/consultas/{celda.Id}/manual?empresa={leida.Id}";
        var antes = await LecturasAsync(api, ct);

        var respuesta = await api.Cliente(token).GetAsync(ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var nuevas = (await LecturasAsync(api, ct)).Except(antes).ToList();
        nuevas.Should().ContainSingle().Which.Should().Be(new Lectura(lector, leida.Id, usuario, ruta));
    }

    private sealed record Lectura(Guid TenantId, Guid EmpresaConsultada, Guid UsuarioId, string Ruta);

    /// <summary>Toda la auditoría de la base efímera, sin filtro de empresa (conexión directa con el dueño de la base).</summary>
    private static async Task<List<Lectura>> LecturasAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            "SELECT tenant_id, empresa_consultada, usuario_id, ruta FROM diagnostico.lecturas_backoffice", conexion);
        await using var lector = await comando.ExecuteReaderAsync(ct);
        var filas = new List<Lectura>();
        while (await lector.ReadAsync(ct))
            filas.Add(new Lectura(lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetString(3)));
        return filas;
    }

    private static async Task<Tablero.LoteSembrado> SembrarConDictamenAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST910", [new(Fuente.Runt, EstadoCelda.Completado, 1)])],
            modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_STANDARD");
        await using var db = empresa.Db();
        db.Add(new DictamenVehiculo
        {
            VehiculoId = lote.Vehiculos[0].Id,
            Version = 1,
            Vigente = true,
            Veredicto = "VIABLE",
            Alertamientos = "[]",
            CatalogoVersion = CatalogoReglasDictamen.Version,
            RedactadoPor = "PLANTILLA",
            EstadoRedaccion = "PENDIENTE",
            CreadoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return lote;
    }
}
