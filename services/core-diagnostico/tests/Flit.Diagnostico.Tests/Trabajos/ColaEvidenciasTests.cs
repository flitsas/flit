using System.Collections.Concurrent;
using System.Security.Cryptography;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Trabajos;

/// <summary>
/// HU #13850 (F07-H5) — cola del ZIP del lote (<see cref="ColaEvidencias"/>): la toma cruza compañías con
/// <c>FOR UPDATE SKIP LOCKED</c> (dos trabajadores nunca toman el mismo ZIP; una fila bloqueada por otra conexión se salta), pone
/// el lease con el reloj inyectado (<see cref="RelojManual"/>, sin esperas reales) y retoma un GENERANDO con el lease vencido; un
/// PENDIENTE en espera de reintento no se toma antes de tiempo. El trabajador hospedado solo se registra con la bandera.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Trabajos.ColaEvidenciasTests
/// </code>
/// </example>
public sealed class ColaEvidenciasTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    // Dos trabajadores en paralelo sobre ZIP de dos compañías: cada uno se toma una sola vez, con la compañía de su fila; la fila
    // que otra conexión tiene bloqueada no se toma (SKIP LOCKED, sin esperar).
    [Fact]
    public async Task Dos_trabajadores_no_toman_la_misma_evidencia()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var deA = await PendientesAsync(empresas.A, 4, ct);
        var deB = await PendientesAsync(empresas.B, 4, ct);
        var empresaDe = deA.ToDictionary(id => id, _ => empresas.A.Id).Concat(deB.ToDictionary(id => id, _ => empresas.B.Id)).ToDictionary();

        var tomadas = new ConcurrentBag<EvidenciaTomada>();
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            while (await TomarAsync(api, ct) is { } tomada)
                tomadas.Add(tomada);
        }, ct)));

        tomadas.Select(t => t.Id).Should().OnlyHaveUniqueItems("ningún ZIP se toma dos veces").And.BeEquivalentTo(empresaDe.Keys);
        tomadas.Should().OnlyContain(t => t.TenantId == empresaDe[t.Id] && t.Intentos == 1, "cada toma trae la compañía de su fila");

        var libre = (await PendientesAsync(empresas.A, 2, ct)).ToList();
        await using var otra = new NpgsqlConnection(api.BaseDeDatos);
        await otra.OpenAsync(ct);
        await using (var transaccion = await otra.BeginTransactionAsync(ct))
        {
            await using var bloqueo = new NpgsqlCommand("SELECT id FROM diagnostico.evidencias WHERE id = @id FOR UPDATE", otra, transaccion);
            bloqueo.Parameters.AddWithValue("id", libre[0]);
            await bloqueo.ExecuteScalarAsync(ct);

            (await TomarAsync(api, ct))!.Id.Should().Be(libre[1], "la bloqueada se salta sin esperar");
            (await TomarAsync(api, ct)).Should().BeNull();
            await transaccion.RollbackAsync(ct);
        }

        (await TomarAsync(api, ct))!.Id.Should().Be(libre[0], "liberada, se toma");
    }

    // Un GENERANDO con el lease vigente no se retoma; vencido el lease (el proceso murió), se retoma con un intento más.
    [Fact]
    public async Task Lease_vencido_se_retoma()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = (await PendientesAsync(empresa, 1, ct)).Single();

        var primera = await TomarAsync(api, ct);
        primera.Should().NotBeNull();
        primera!.Id.Should().Be(id);
        primera.Intentos.Should().Be(1);
        primera.LeaseHasta.Should().BeCloseTo(reloj.GetUtcNow() + Lease, TimeSpan.FromMilliseconds(1));
        var fila = await FilaAsync(empresa, id, ct);
        fila.Estado.Should().Be(EstadoEvidencia.Generando);
        fila.LeaseHasta.Should().Be(primera.LeaseHasta);

        reloj.Avanzar(Lease - TimeSpan.FromSeconds(1));
        (await TomarAsync(api, ct)).Should().BeNull("el lease sigue vigente");

        reloj.Avanzar(TimeSpan.FromSeconds(2));
        var segunda = await TomarAsync(api, ct);
        segunda.Should().NotBeNull("vencido el lease, otro trabajador lo retoma");
        segunda!.Id.Should().Be(id);
        segunda.Intentos.Should().Be(2);
        segunda.LeaseHasta.Should().BeAfter(primera.LeaseHasta);
    }

    // Un PENDIENTE devuelto a la cola con espera de reintento (lease_hasta en el futuro) no se toma hasta que pasa la espera.
    [Fact]
    public async Task Pendiente_en_espera_de_reintento_no_se_toma_antes_de_tiempo()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = (await PendientesAsync(empresa, 1, ct, esperaHasta: reloj.GetUtcNow().AddSeconds(30))).Single();

        (await TomarAsync(api, ct)).Should().BeNull("la espera del reintento no ha pasado");
        reloj.Avanzar(TimeSpan.FromSeconds(31));
        (await TomarAsync(api, ct))!.Id.Should().Be(id);
    }

    // El trabajador en segundo plano solo se registra con Diagnostico:Evidencias:Habilitado=true (como el de consultas).
    [Fact]
    public async Task Trabajador_de_evidencias_solo_se_registra_con_la_bandera()
    {
        await using (var apagado = await ApiDePrueba.SinBaseAsync())
            apagado.App.Services.GetServices<IHostedService>().Should().NotContain(s => s is TrabajadorEvidencias);

        await using var encendido = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration["Diagnostico:Evidencias:Habilitado"] = "true");
        encendido.App.Services.GetServices<IHostedService>().Should().ContainSingle(s => s is TrabajadorEvidencias);
    }

    private static async Task<EvidenciaTomada?> TomarAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<ColaEvidencias>(scope.ServiceProvider).TomarAsync(Lease, ct);
    }

    /// <summary>Un lote de la empresa con <paramref name="cantidad"/> ZIP PENDIENTE (huellas distintas, no vigentes).</summary>
    private static async Task<IReadOnlyList<Guid>> PendientesAsync(EmpresaDePrueba empresa, int cantidad, CancellationToken ct, DateTimeOffset? esperaHasta = null)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST591", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var ahora = DateTimeOffset.UtcNow;
        var filas = Enumerable.Range(0, cantidad).Select(i => new Evidencia
        {
            DiagnosticoId = lote.Id,
            Tipo = TipoEvidencia.Zip,
            Huella = Convert.ToHexStringLower(SHA256.HashData(Guid.NewGuid().ToByteArray())),
            Estado = EstadoEvidencia.Pendiente,
            Vigente = false,
            SolicitadoPor = Guid.NewGuid(),
            EmpresaNombre = "Empresa de prueba",
            LeaseHasta = esperaHasta,
            CreadoEn = ahora.AddMilliseconds(i),
        }).ToList();
        await using var db = empresa.Db();
        db.AddRange(filas);
        await db.SaveChangesAsync(ct);
        return [.. filas.Select(f => f.Id)];
    }

    private static async Task<Evidencia> FilaAsync(EmpresaDePrueba empresa, Guid id, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Evidencia>().AsNoTracking().SingleAsync(e => e.Id == id, ct);
    }
}
