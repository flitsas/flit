using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Impuestos.Tests.Credenciales;

/// <summary>
/// HU #14694 (revisión previa al PR) — <see cref="AlmacenCredenciales.Escribir"/> y el secreto de webhook anterior: sin
/// webhook_secret nuevo, o con la ventana del anterior ya vencida, las dos columnas <c>webhook_secret_anterior_*</c> quedan en null
/// (lo exige el CHECK de R2: las dos null o las dos con valor). Con la ventana vigente y el mismo secreto, el anterior se conserva.
/// Sin base: <see cref="AlmacenCredenciales.Escribir"/> solo prepara la fila.
/// </summary>
/// <example>
/// <code>
/// almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(apiKey, null), OrigenCredencial.CargaManual, actor);
/// // actual.WebhookSecretAnteriorCifrado == null &amp;&amp; actual.WebhookSecretAnteriorHasta == null
/// </code>
/// </example>
public sealed class SecretoAnteriorAlEscribirTests
{
    private const string ApiKey = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string Secreto = "whsec_secreto-de-prueba-no-real";
    private const string Anterior = "whsec_secreto-anterior-de-prueba";

    [Fact]
    public void Sin_webhook_secret_nuevo_las_dos_columnas_del_anterior_quedan_en_null()
    {
        var (almacen, db, empresa, reloj, proteccion) = Preparar();
        var actual = Fila(proteccion, empresa, hasta: reloj.Ahora.AddHours(10));

        almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(ApiKey, null), OrigenCredencial.CargaManual, null);

        actual.WebhookSecretCifrado.Should().BeNull();
        actual.WebhookSecretAnteriorCifrado.Should().BeNull();
        actual.WebhookSecretAnteriorHasta.Should().BeNull();
    }

    [Fact]
    public void Con_la_ventana_del_anterior_vencida_las_dos_columnas_quedan_en_null()
    {
        var (almacen, db, empresa, reloj, proteccion) = Preparar();
        var actual = Fila(proteccion, empresa, hasta: reloj.Ahora);

        almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(ApiKey, Secreto), OrigenCredencial.Alta, null);

        actual.WebhookSecretAnteriorCifrado.Should().BeNull("hasta <= ahora: la ventana ya cerró");
        actual.WebhookSecretAnteriorHasta.Should().BeNull();
    }

    [Fact]
    public void Con_la_ventana_vigente_y_el_mismo_secreto_el_anterior_se_conserva()
    {
        var (almacen, db, empresa, reloj, proteccion) = Preparar();
        var hasta = reloj.Ahora.AddHours(3);
        var actual = Fila(proteccion, empresa, hasta);
        var cifradoAnterior = actual.WebhookSecretAnteriorCifrado;

        almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(ApiKey, Secreto), OrigenCredencial.Alta, null);

        actual.WebhookSecretAnteriorCifrado.Should().BeSameAs(cifradoAnterior);
        actual.WebhookSecretAnteriorHasta.Should().Be(hasta);
    }

    [Fact]
    public void Con_un_secreto_distinto_el_guardado_pasa_a_anterior_con_su_ventana()
    {
        var (almacen, db, empresa, reloj, proteccion) = Preparar();
        var actual = Fila(proteccion, empresa, hasta: null);
        var cifradoVigente = actual.WebhookSecretCifrado;

        almacen.Escribir(db, actual, empresa, new CredencialesEnClaro(ApiKey, "whsec_secreto-nuevo-de-prueba"), OrigenCredencial.Alta, null);

        actual.WebhookSecretAnteriorCifrado.Should().BeSameAs(cifradoVigente);
        actual.WebhookSecretAnteriorHasta.Should().Be(reloj.Ahora + AlmacenCredenciales.VentanaSecretoAnterior);
    }

    private static (AlmacenCredenciales Almacen, ImpuestosDb Db, Guid Empresa, RelojDePrueba Reloj, IProteccionImpuestos Proteccion) Preparar()
    {
        var opciones = new DbContextOptionsBuilder<ImpuestosDb>().UseNpgsql("Host=sin-base.invalid;Database=nada").UseSnakeCaseNamingConvention().Options;
        var proteccion = new ProteccionImpuestos(new EphemeralDataProtectionProvider());
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var empresa = Guid.NewGuid();
        return (new AlmacenCredenciales(opciones, proteccion, reloj), new ImpuestosDb(opciones, new EmpresaFija(empresa)), empresa, reloj, proteccion);
    }

    /// <summary>Fila guardada con <see cref="Secreto"/> vigente y, si <paramref name="hasta"/> no es null, <see cref="Anterior"/> en su ventana.</summary>
    private static CredencialEmpresa Fila(IProteccionImpuestos proteccion, Guid empresa, DateTimeOffset? hasta) => new()
    {
        TenantId = empresa,
        ApiKeyCifrada = proteccion.ProtegerCredencial(empresa, CodigosCredencial.ApiKey, ApiKey),
        WebhookSecretCifrado = proteccion.ProtegerCredencial(empresa, CodigosCredencial.WebhookSecret, Secreto),
        WebhookSecretAnteriorCifrado = hasta is null ? null : proteccion.ProtegerCredencial(empresa, CodigosCredencial.WebhookSecret, Anterior),
        WebhookSecretAnteriorHasta = hasta,
        Origen = OrigenCredencial.Alta,
    };
}
