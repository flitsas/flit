using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Comparendos.Api.Modulos.CambioContraventor;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Transporte lento que cuenta los correos por destinatario; puede hacer que el primero al OT falle de forma transitoria.</summary>
public sealed class EnvioContado : IEnvioCorreo
{
    private int _caidasOt;

    public ConcurrentDictionary<string, int> Enviados { get; } = new(StringComparer.Ordinal);

    /// <summary>Cuántos correos a la radicación del OT fallan con «proveedor no disponible» antes de salir.</summary>
    public void CaerOt(int veces) => _caidasOt = veces;

    public async Task<ResultadoEnvioCorreo> EnviarAsync(Guid? tenantId, CorreoParaEnviar correo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(correo);
        await Task.Delay(150, ct);
        if (correo.DestinatarioEmail.StartsWith("radicacion@", StringComparison.Ordinal) && Interlocked.Decrement(ref _caidasOt) >= 0)
            return new ResultadoEnvioCorreo("PROVEEDOR_NO_DISPONIBLE", "Sin conexión", false, null);
        Enviados.AddOrUpdate(correo.DestinatarioEmail, 1, (_, n) => n + 1);
        return new ResultadoEnvioCorreo(ResultadoEnvioCorreo.Enviado, null, false, Guid.NewGuid().ToString());
    }
}

public sealed class ApiConEnvioContado : ApiConFuente
{
    public EnvioContado Envio { get; } = new();

    protected override void Reemplazar(IServiceCollection services)
    {
        base.Reemplazar(services);
        services.AddScoped<IEnvioCorreo>(_ => Envio);
    }
}

/// <summary>Bug #14270 (CC-02): una solicitud de cambio de contraventor mandaba dos veces el correo al OT y dos avisos 4.</summary>
public sealed class CorreosDuplicadosTests(ApiConEnvioContado api) : IClassFixture<ApiConEnvioContado>
{
    [Fact]
    public async Task DespachosSimultaneos_DelMismoCorreo_EnvianUnaSolaVez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (comparendo, ot) = await SolicitudConOtCaidoAsync("CCD-1", "doble@example.test", ct);
        var despachador = api.App.Services.GetRequiredService<DespachadorCorreos>();

        // La llamada directa (el botón) y el ciclo del despachador compiten por el mismo correo.
        await Task.WhenAll(despachador.DespacharAsync(ot, ct), despachador.DespacharAsync(ot, ct), despachador.DespacharPendientesAsync(ct));
        await despachador.DespacharPendientesAsync(ct);

        api.Envio.Enviados.GetValueOrDefault("radicacion@movilidad.test").Should().Be(1);
        api.Envio.Enviados.GetValueOrDefault("doble@example.test").Should().Be(1);
        var intentos = await api.EnDbAsync(db => db.ComunicacionIntentos.Where(i => i.ComunicacionId == ot).OrderBy(i => i.Numero).Select(i => i.Numero).ToListAsync(ct));
        intentos.Should().Equal(1, 2);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.ComparendoId == comparendo && c.Tipo == TipoComunicacion.Aviso4, ct))).Should().Be(1);
    }

    [Fact]
    public async Task ReaccionesSimultaneas_AlMismoCorreoDelOt_CreanUnSoloAviso4()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (comparendo, ot) = await SolicitudConOtCaidoAsync("CCD-2", "reaccion@example.test", ct);

        // Dos caminos cierran a la vez el mismo correo al OT como enviado y reaccionan.
        async Task ReaccionarAsync()
        {
            await using var scope = api.App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
            var reaccion = scope.ServiceProvider.GetServices<IReaccionCorreo>().OfType<ReaccionCambioContraventor>().Single();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var c = await db.Comunicaciones.SingleAsync(x => x.Id == ot, ct);
                c.Estado = EstadoComunicacion.Enviado;
                await reaccion.AlTerminarAsync(c, new ResultadoEnvioCorreo(ResultadoEnvioCorreo.Enviado, null, false, null), ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }

        await Task.WhenAll(ReaccionarAsync(), ReaccionarAsync());

        (await api.EnDbAsync(db => db.AvisosConductor.CountAsync(a => a.ComparendoId == comparendo && a.TipoAviso == TipoAviso.Aviso4, ct))).Should().Be(1);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(c => c.ComparendoId == comparendo && c.Tipo == TipoComunicacion.Aviso4, ct))).Should().Be(1);
    }

    /// <summary>Solicitud de cambio cuyo correo al OT quedó PENDIENTE por una caída transitoria; el reloj ya pasó la espera.</summary>
    private async Task<(Guid Comparendo, Guid ComunicacionOt)> SolicitudConOtCaidoAsync(string numero, string correoConductor, CancellationToken ct)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, cambio: true);
        var id = (await IngresarAsync(api, empresa, propietarioEsElNit: true, Item(numero))).Values.Single();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa,
            [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.ContraventorDocumentos, Permisos.CambioContraventorSolicitar, Permisos.RepresentantesGestionar]));
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = correoConductor, declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        foreach (var tipo in new[] { "Cedula", "Licencia" })
        {
            using var pdf = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", "d.pdf" } };
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/documentos?tipo={tipo}", pdf, ct)).EnsureSuccessStatusCode();
        }

        await RepresentanteDelNitAsync(api, cliente, empresa, ct);
        api.Envio.CaerOt(1);
        (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct)).EnsureSuccessStatusCode();
        var ot = await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.ComparendoId == id && c.Tipo == TipoComunicacion.CambioContraventorOt).Select(c => c.Id).SingleAsync(ct));
        (await api.EnDbAsync(db => db.Comunicaciones.SingleAsync(c => c.Id == ot, ct))).Estado.Should().Be(EstadoComunicacion.Pendiente);
        api.Reloj.Ahora = api.Reloj.Ahora.AddMinutes(2);
        return (id, ot);
    }
}
