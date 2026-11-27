using System.Security.Cryptography;
using System.Text;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Credenciales;

/// <summary>
/// HU #14694 (M06, plan §4.2, DA-2=b) — <see cref="AlmacenCredenciales"/>: la api_key y el webhook_secret se guardan cifrados con
/// un propósito por compañía y por código; lo cifrado de una compañía no se descifra con el propósito de otra ni con el de otro
/// código; la fila de una compañía no se ve desde el contexto de otra; reescribir sube la versión y conserva el id de empresa del
/// API. Con base: Postgres de <c>ConnectionStrings__Core</c> (5433).
/// </summary>
/// <example>
/// <code>
/// await using var db = almacen.ContextoDe(empresa);
/// almacen.Escribir(db, null, empresa, new CredencialesEnClaro(apiKey, secreto), OrigenCredencial.CargaManual, actor);
/// await db.SaveChangesAsync(ct);
/// </code>
/// </example>
public sealed class AlmacenCredencialesTests
{
    private const string ApiKey = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde"; // gitleaks:allow
    private const string Secreto = "whsec_secreto-de-prueba-no-real";

    [Fact]
    public void El_cifrado_es_por_compania_y_por_codigo()
    {
        var proteccion = new ProteccionImpuestos(new EphemeralDataProtectionProvider());
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var cifrado = proteccion.ProtegerCredencial(a, CodigosCredencial.ApiKey, ApiKey);

        proteccion.DesprotegerCredencial(a, CodigosCredencial.ApiKey, cifrado).Should().Be(ApiKey);
        FluentActions.Invoking(() => proteccion.DesprotegerCredencial(b, CodigosCredencial.ApiKey, cifrado))
            .Should().Throw<CryptographicException>("el propósito de otra compañía no descifra");
        FluentActions.Invoking(() => proteccion.DesprotegerCredencial(a, CodigosCredencial.WebhookSecret, cifrado))
            .Should().Throw<CryptographicException>("el propósito de otro código no descifra");
    }

    [Fact]
    public async Task Guarda_cifrado_con_prefijo_y_origen_y_lo_lee_en_claro()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await SembrarAprovisionamientoAsync(api, empresa, ct);
        using var scope = api.App.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();

        await using (var db = almacen.ContextoDe(empresa))
        {
            almacen.Escribir(db, null, empresa, new CredencialesEnClaro(ApiKey, Secreto, "empresa-api-1"), OrigenCredencial.CargaManual, actor);
            await db.SaveChangesAsync(ct);
        }

        await using var lectura = api.Contexto(empresa);
        var fila = await lectura.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct);
        fila.ApiKeyCifrada.Should().NotBeEmpty();
        Contiene(fila.ApiKeyCifrada, ApiKey).Should().BeFalse("la api_key no se guarda en claro");
        Contiene(fila.WebhookSecretCifrado!, Secreto).Should().BeFalse("el webhook_secret no se guarda en claro");
        fila.ApiKeyPrefijo.Should().Be("AbCdEfGh");
        fila.Origen.Should().Be(OrigenCredencial.CargaManual);
        fila.EmpresaApiId.Should().Be("empresa-api-1");
        fila.ActualizadoPor.Should().Be(actor);
        fila.Version.Should().Be(1);

        var enClaro = await almacen.LeerAsync(empresa, ct);
        enClaro.Should().Be(new CredencialesEnClaro(ApiKey, Secreto, "empresa-api-1"));
    }

    [Fact]
    public async Task Otra_compania_no_ve_la_fila_ni_la_descifra()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SembrarAprovisionamientoAsync(api, a, ct);
        using var scope = api.App.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();
        var proteccion = scope.ServiceProvider.GetRequiredService<IProteccionImpuestos>();
        await using (var db = almacen.ContextoDe(a))
        {
            almacen.Escribir(db, null, a, new CredencialesEnClaro(ApiKey, null), OrigenCredencial.Alta, null);
            await db.SaveChangesAsync(ct);
        }

        (await almacen.LeerAsync(b, ct)).Should().BeNull();
        await using (var deB = almacen.ContextoDe(b))
            (await almacen.FilaAsync(deB, a, ct)).Should().BeNull("el contexto de B filtra por B");

        await using var deA = api.Contexto(a);
        var cifrado = (await deA.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct)).ApiKeyCifrada;
        FluentActions.Invoking(() => proteccion.DesprotegerCredencial(b, CodigosCredencial.ApiKey, cifrado)).Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task Reescribir_sube_la_version_conserva_el_id_del_API_y_sin_secreto_lo_quita()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await SembrarAprovisionamientoAsync(api, empresa, ct);
        using var scope = api.App.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();
        await using (var db = almacen.ContextoDe(empresa))
        {
            almacen.Escribir(db, null, empresa, new CredencialesEnClaro(ApiKey, Secreto, "empresa-api-1"), OrigenCredencial.Alta, null);
            await db.SaveChangesAsync(ct);
        }

        const string Nueva = "ZyXwVuTsRqPoNmLkJiHgFeDcBa9876543210_-edcba";
        await using (var db = almacen.ContextoDe(empresa))
        {
            var actual = await almacen.FilaAsync(db, empresa, ct);
            almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(Nueva, null), OrigenCredencial.CargaManual, Guid.NewGuid());
            await db.SaveChangesAsync(ct);
        }

        await using var lectura = api.Contexto(empresa);
        var fila = await lectura.Set<CredencialEmpresa>().AsNoTracking().SingleAsync(ct);
        fila.Version.Should().Be(2);
        fila.WebhookSecretCifrado.Should().BeNull();
        fila.EmpresaApiId.Should().Be("empresa-api-1");
        fila.ApiKeyPrefijo.Should().Be("ZyXwVuTs");
        fila.Origen.Should().Be(OrigenCredencial.CargaManual);
        (await almacen.LeerAsync(empresa, ct)).Should().Be(new CredencialesEnClaro(Nueva, null, "empresa-api-1"));
    }

    [Theory]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde", "AbCdEfGh")]
    [InlineData("0123456789abcdef", "01234567")]
    [InlineData("0123456789abcde", null)]
    [InlineData("corta", null)]
    public void El_prefijo_son_8_caracteres_y_no_existe_en_llaves_cortas(string apiKey, string? prefijo)
    {
        AlmacenCredenciales.Prefijo(apiKey).Should().Be(prefijo);
    }

    [Fact]
    public async Task El_contexto_exige_una_compania()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        using var scope = api.App.Services.CreateScope();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenCredenciales>();

        FluentActions.Invoking(() => almacen.ContextoDe(Guid.Empty)).Should().Throw<ArgumentException>();
    }

    internal static async Task SembrarAprovisionamientoAsync(
        ApiDePrueba api, Guid empresa, CancellationToken ct, EstadoAprovisionamiento estado = EstadoAprovisionamiento.RequiereCredenciales)
    {
        await using var db = api.Contexto(empresa);
        db.Add(new AprovisionamientoEmpresa { TenantId = empresa, Estado = estado });
        await db.SaveChangesAsync(ct);
    }

    private static bool Contiene(byte[] datos, string texto)
    {
        var buscado = Encoding.UTF8.GetBytes(texto);
        return datos.AsSpan().IndexOf(buscado) >= 0;
    }
}
