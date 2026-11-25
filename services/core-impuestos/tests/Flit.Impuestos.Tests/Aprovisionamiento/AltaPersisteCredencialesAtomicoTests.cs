using System.Net;
using Flit.Impuestos.Api.Modulos.Aprovisionamiento;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using E = Flit.Impuestos.Domain.Aprovisionamiento.EstadoAprovisionamiento;

namespace Flit.Impuestos.Tests.Aprovisionamiento;

/// <summary>
/// HU #14694 (M07; F1a-CF5, DA-2=b) — el 201 del alta trae la api_key y el webhook_secret UNA sola vez: la credencial cifrada
/// (origen <c>ALTA</c>, con el id de la empresa en el API), el paso a <c>LISTO</c>, la versión y el historial se guardan en la
/// MISMA transacción. Si algo de eso falla no queda nada a medias (ni credencial sin estado ni estado sin credencial) y la marca
/// del envío, que se registró ANTES del POST, hace que la siguiente evaluación quede en <c>INCIERTO</c> en lugar de repetir el
/// alta.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~AltaPersisteCredencialesAtomicoTests
/// </code>
/// </example>
public sealed class AltaPersisteCredencialesAtomicoTests
{
    private const string EmpresaApiId = "3f6c2a7e-6a0b-4c8e-9a51-2f3b5d7c9e10";

    [Fact]
    public async Task El_201_guarda_credencial_cifrada_estado_version_e_historial_juntos()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        await using var db = api.Contexto(empresa);
        var aprov = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct);
        var credencial = await db.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct);
        aprov.Estado.Should().Be(E.Listo);
        aprov.Version.Should().Be(2);
        credencial.Origen.Should().Be(OrigenCredencial.Alta);
        credencial.EmpresaApiId.Should().Be(EmpresaApiId);
        credencial.ApiKeyPrefijo.Should().Be("AbCdEfGh");
        credencial.ActualizadoEn.Should().Be(aprov.ActualizadoEn, "se escribieron en el mismo guardado");
        (await db.Set<HistorialAprovisionamiento>().AsNoTracking().SingleAsync(ct)).Hacia.Should().Be(E.Listo);
        var enClaro = await EntornoAprovisionamiento.LeerCredencialAsync(api, empresa, ct);
        enClaro!.ApiKey.Should().Be(ClienteApiLiquidacionTests.ApiKey);
        enClaro.WebhookSecret.Should().Be(ClienteApiLiquidacionTests.WebhookSecret);
    }

    [Fact]
    public async Task El_envio_se_registra_antes_del_POST()
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        ApiDePrueba? api = null;
        string? marcaDuranteElPost = null;
        var proveedor = new ApiLiquidacionDePrueba();
        proveedor.Responder = r =>
        {
            if (r.RequestUri!.AbsolutePath != EntornoAprovisionamiento.RutaAlta)
                return ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200);
            marcaDuranteElPost = EntornoAprovisionamiento.FilaAsync(api!, empresa, CancellationToken.None).GetAwaiter().GetResult()!.MotivoDetalle;
            return ApiLiquidacionDePrueba.Json(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        };
        await using var entorno = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        api = entorno;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        marcaDuranteElPost.Should().Be(Aprovisionador.MarcaAltaEnviada, "ya guardada (otra conexión la ve) cuando sale el POST");
        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.MotivoDetalle.Should().BeNull();
    }

    [Fact]
    public async Task Si_falla_el_guardado_no_queda_credencial_ni_estado_a_medias_y_la_siguiente_evaluacion_es_INCIERTO()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoAprovisionamiento.ProveedorConAlta(HttpStatusCode.Created, ClienteApiLiquidacionTests.Alta201);
        await using var api = await EntornoAprovisionamiento.ConApiAsync(proveedor, new IdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoAprovisionamiento.SembrarAsync(api, empresa, E.Pendiente, ct);
        await RomperHistorialHaciaListoAsync(api, ct);

        var act = () => EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        await act.Should().ThrowAsync<DbUpdateException>();
        await using (var db = api.Contexto(empresa))
        {
            (await db.Set<CredencialEmpresa>().AsNoTracking().AnyAsync(ct)).Should().BeFalse("la credencial no se guarda sin su estado");
            var aprov = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleAsync(ct);
            aprov.Estado.Should().Be(E.Pendiente, "el estado no se guarda sin su credencial");
            aprov.Version.Should().Be(1);
            aprov.MotivoDetalle.Should().Be(Aprovisionador.MarcaAltaEnviada);
            aprov.ProximoIntentoEn = DateTimeOffset.UtcNow.AddSeconds(-1);
            db.Update(aprov);
            await db.SaveChangesAsync(ct);
        }

        await EntornoAprovisionamiento.EvaluarAsync(api, empresa, ct);

        (await EntornoAprovisionamiento.FilaAsync(api, empresa, ct))!.Estado.Should().Be(E.Incierto);
        EntornoAprovisionamiento.Altas(proveedor).Should().Be(1, "el alta no se repite: pudo haberse hecho");
    }

    /// <summary>Un trigger de prueba que rechaza el historial hacia LISTO: el guardado del 201 falla en su último paso.</summary>
    private static async Task RomperHistorialHaciaListoAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            """
            CREATE FUNCTION impuestos.prueba_rompe_historial() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.hacia = 'LISTO' THEN RAISE EXCEPTION 'prueba: historial rechazado'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER prueba_rompe_historial BEFORE INSERT ON impuestos.historial_aprovisionamiento
              FOR EACH ROW EXECUTE FUNCTION impuestos.prueba_rompe_historial();
            """, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }
}
