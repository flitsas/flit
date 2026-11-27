using System.Net;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14711 (P5; impuestos-events.v1 1.2.0) — el trabajo <c>impuestos.credencial_diagnostico.actualizada</c> que deja la
/// plataforma cuando el alta de Diagnóstico cambió la llave: si la credencial de la compañía en Impuestos es una copia de
/// Diagnóstico o tiene otro prefijo, se recopia por <c>ObtenerCredencialImpuestoParaCopia</c> y se reemplaza (conservando el
/// secreto del webhook); si es propia con el mismo prefijo, no se toca. Idempotente.
/// </summary>
/// <example>
/// <code>
/// await EntornoAprovisionamiento.EntregarAsync(api, ConsumidorCredencialDiagnosticoTests.Actualizada(empresa), ct);
/// </code>
/// </example>
public sealed class ConsumidorCredencialDiagnosticoTests
{
    private const string LlaveVieja = "ViEjAaaa-llave-anterior-de-prueba-0123456789";
    private const string Secreto = ClienteApiLiquidacionTests.WebhookSecret;

    /// <summary>Misma llave de Diagnóstico en prefijo (8 caracteres) que <see cref="EntornoAprovisionamiento.LlaveDiagnostico"/>.</summary>
    private const string MismoPrefijo = "DiAgNoStIcO-OTRA-llave-propia-0123456789-z";

    public static EventEnvelope Actualizada(Guid empresa, Guid? enLosDatos = null, string producto = "diagnostico", long version = 4) =>
        EntornoAprovisionamiento.Sobre(
            ConsumidorCredencialDiagnostico.Tipo, empresa,
            new { tenantId = enLosDatos ?? empresa, productCode = producto, version, occurredAt = DateTimeOffset.UtcNow });

    private static IdentidadDePrueba ConLlaveDeDiagnostico() =>
        new() { Llave = new LlaveDiagnostico(true, EntornoAprovisionamiento.LlaveDiagnostico, 4) };

    private static async Task<CredencialEmpresa> FilaAsync(Api.ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct);
    }

    [Fact]
    public async Task CopiaDeDiagnostico_SeRecopiaLaLlaveVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = ConLlaveDeDiagnostico();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, LlaveVieja, null, OrigenCredencial.CopiaDiagnostico, ct);

        (await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa), ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
        var fila = await FilaAsync(api, empresa, ct);
        fila.Origen.Should().Be(OrigenCredencial.CopiaDiagnostico);
        fila.Version.Should().Be(2);
        identidad.LlamadasLlave.Should().Be(1);
    }

    [Fact]
    public async Task PropiaConOtroPrefijo_SeReemplaza_ConservandoElSecretoDelWebhook()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), ConLlaveDeDiagnostico());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, LlaveVieja, Secreto, OrigenCredencial.Alta, ct);

        await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa), ct);

        var credencial = await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct);
        credencial!.ApiKey.Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
        credencial.WebhookSecret.Should().Be(Secreto);
        (await FilaAsync(api, empresa, ct)).Origen.Should().Be(OrigenCredencial.CopiaDiagnostico);
    }

    [Fact]
    public async Task PropiaConElMismoPrefijo_NoSeToca()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), ConLlaveDeDiagnostico());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, MismoPrefijo, Secreto, OrigenCredencial.Alta, ct);

        await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa), ct);

        var fila = await FilaAsync(api, empresa, ct);
        fila.Version.Should().Be(1);
        fila.Origen.Should().Be(OrigenCredencial.Alta);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(MismoPrefijo);
    }

    [Fact]
    public async Task LaMismaLlave_YElMismoSobreDosVeces_SonIdempotentes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), ConLlaveDeDiagnostico());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, LlaveVieja, null, OrigenCredencial.CopiaDiagnostico, ct);
        var sobre = Actualizada(empresa);

        (await EntornoAprovisionamiento.EntregarAsync(api, sobre, ct)).Should().BeTrue();
        (await EntornoAprovisionamiento.EntregarAsync(api, sobre, ct)).Should().BeFalse("la bandeja ya lo tiene");
        (await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa), ct)).Should().BeTrue();

        (await FilaAsync(api, empresa, ct)).Version.Should().Be(2, "la segunda entrega con la misma llave no reescribe");
    }

    [Fact]
    public async Task SinCredencialEnImpuestos_OSinLlaveEnDiagnostico_SinEfecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sinFila = Guid.NewGuid();
        var conCopia = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, conCopia, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, conCopia, LlaveVieja, null, OrigenCredencial.CopiaDiagnostico, ct);

        (await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(sinFila), ct)).Should().BeTrue();
        (await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(conCopia), ct)).Should().BeTrue();

        (await EntornoAprovisionamiento.LeerCredencialAsync(api, sinFila, ct)).Should().BeNull();
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, conCopia, ct))!.ApiKey.Should().Be(LlaveVieja);
    }

    [Fact]
    public async Task LaLlaveSePideAIdentidad_SinTenerBloqueadoElAprovisionamiento()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = ConLlaveDeDiagnostico();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, LlaveVieja, null, OrigenCredencial.CopiaDiagnostico, ct);
        bool? bloqueadaDuranteElRpc = null;
        identidad.AlLeerLlave = async e =>
        {
            // Otra transacción intenta tomar la fila del aprovisionamiento sin esperar mientras dura el rpc.
            await using var cn = new Npgsql.NpgsqlConnection(api.BaseDeDatos);
            await cn.OpenAsync(ct);
            await using var tx = await cn.BeginTransactionAsync(ct);
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1 FROM impuestos.aprovisionamientos WHERE tenant_id = @e FOR UPDATE NOWAIT", cn, tx);
            cmd.Parameters.AddWithValue("e", e);
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
                bloqueadaDuranteElRpc = false;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "55P03")
            {
                bloqueadaDuranteElRpc = true;
            }

            await tx.RollbackAsync(ct);
        };

        (await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa), ct)).Should().BeTrue();

        bloqueadaDuranteElRpc.Should().BeFalse("el FOR UPDATE se toma después del rpc, en el tramo corto de comparar y escribir");
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(EntornoAprovisionamiento.LlaveDiagnostico);
    }

    [Fact]
    public async Task IdentidadCaida_SeReintenta()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new IdentidadDePrueba { LlaveCaida = true };
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, LlaveVieja, null, OrigenCredencial.CopiaDiagnostico, ct);

        var act = () => EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa), ct);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<SinReintentoAutomaticoException>();
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(LlaveVieja);
    }

    [Fact]
    public async Task OtraEmpresaEnLosDatos_OOtroProducto_NoSeAplican()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = ConLlaveDeDiagnostico();
        await using var api = await EntornoAprovisionamiento.ConApiAsync(new ApiLiquidacionDePrueba(), identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, Domain.Aprovisionamiento.EstadoAprovisionamiento.Listo, ct);
        await EntornoAprovisionamiento.GuardarCredencialAsync(api, empresa, LlaveVieja, null, OrigenCredencial.CopiaDiagnostico, ct);

        var ajena = () => EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa, enLosDatos: Guid.NewGuid()), ct);
        await ajena.Should().ThrowAsync<SinReintentoAutomaticoException>();
        (await EntornoAprovisionamiento.EntregarAsync(api, Actualizada(empresa, producto: "tramites"), ct)).Should().BeTrue();

        identidad.LlamadasLlave.Should().Be(0);
        (await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct))!.ApiKey.Should().Be(LlaveVieja);
    }
}
