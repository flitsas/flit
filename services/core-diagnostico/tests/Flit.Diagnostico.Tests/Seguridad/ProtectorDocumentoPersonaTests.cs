using System.Net;
using System.Text;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Seguridad;

/// <summary>
/// HU #13751 (F02-H5), escenario 6 y Data Protection — <see cref="IProtectorDocumentoPersona"/> sobre ASP.NET Data
/// Protection (purpose <c>Flit.Diagnostico.DocumentoPersona.v1</c>) con el anillo de llaves en
/// <c>diagnostico.data_protection_keys</c> cifrado con un certificado que entra por configuración (llave fuera de la
/// base). Ida y vuelta, llave guardada cifrada, arranque fail-closed sin certificado o con uno ilegible y, tras una
/// confirmación, el número del documento no aparece en ningún log ni en la outbox de eventos.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ProtectorDocumentoPersonaTests
/// </code>
/// </example>
public sealed class ProtectorDocumentoPersonaTests
{
    // Ida y vuelta: el cifrado no contiene el texto plano, cambia en cada llamada y vuelve al número original.
    [Fact]
    public async Task Ida_y_vuelta_del_cifrado()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        const string Documento = "900111222";

        var uno = protector.Proteger(Documento);
        var dos = protector.Proteger(Documento);

        uno.Should().NotBeEmpty();
        Encoding.UTF8.GetString(uno).Should().NotContain(Documento);
        uno.Should().NotEqual(dos, "Data Protection no es determinista");
        protector.Desproteger(uno).Should().Be(Documento);
        protector.Desproteger(dos).Should().Be(Documento);
        var otroProposito = api.App.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("otro.v1");
        var leerConOtro = () => otroProposito.Unprotect(uno);
        leerConOtro.Should().Throw<System.Security.Cryptography.CryptographicException>("el purpose es propio del documento de persona");
    }

    // La llave del anillo que queda en data_protection_keys está cifrada con el certificado (no hay masterKey en claro).
    [Fact]
    public async Task La_llave_guardada_en_data_protection_keys_esta_cifrada_con_el_certificado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Proteger("1020304050");

        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand("SELECT xml FROM diagnostico.data_protection_keys", conexion);
        var llaves = new List<string>();
        await using (var lector = await consulta.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
                llaves.Add(lector.GetString(0));
        }

        llaves.Should().NotBeEmpty("el anillo vive en la base del servicio");
        llaves.Should().OnlyContain(x => x.Contains("EncryptedData", StringComparison.Ordinal) && x.Contains("EncryptedXmlDecryptor", StringComparison.Ordinal),
            "la llave va cifrada con el certificado");
        llaves.Should().OnlyContain(x => !x.Contains("<masterKey", StringComparison.Ordinal), "la llave maestra no queda en claro");
    }

    // Fail-closed: sin certificado configurado el servicio no arranca y nombra la variable.
    [Theory]
    [InlineData(CertificadoDePrueba.ClaveRuta, "Diagnostico__DataProtection__CertificadoRuta")]
    [InlineData(CertificadoDePrueba.ClaveClave, "Diagnostico__DataProtection__CertificadoClave")]
    public void Sin_certificado_el_servicio_no_arranca(string clave, string variable)
    {
        var settings = ApiDePrueba.Configuracion(ApiDePrueba.BaseInalcanzable);
        settings.Remove(clave);

        var arrancar = () => Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
        });

        arrancar.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(variable);
    }

    // Fail-closed: un certificado que no existe o con la clave equivocada tampoco deja arrancar, sin revelar la clave.
    [Fact]
    public void Con_un_certificado_ilegible_el_servicio_no_arranca()
    {
        var otro = CertificadoDePrueba.Otro();
        (string Ruta, string Clave)[] casos =
        [
            (Path.Combine(Path.GetTempPath(), $"no-existe-{Guid.NewGuid():N}.pfx"), "clave-cualquiera"),
            (otro.Ruta, "clave-equivocada"),
        ];

        foreach (var (ruta, clave) in casos)
        {
            var settings = ApiDePrueba.Configuracion(ApiDePrueba.BaseInalcanzable);
            settings[CertificadoDePrueba.ClaveRuta] = ruta;
            settings[CertificadoDePrueba.ClaveClave] = clave;

            var arrancar = () => Program.Build([], b =>
            {
                b.WebHost.UseTestServer();
                b.Configuration.AddInMemoryCollection(settings);
            });

            var mensaje = arrancar.Should().Throw<InvalidOperationException>().Which.Message;
            mensaje.Should().Contain("Diagnostico__DataProtection__CertificadoRuta").And.NotContain(clave);
        }
    }

    // Escenario 6 (F02-C13) — tras confirmar con documento declarado, el número no aparece en logs ni en eventos.
    [Fact]
    public async Task El_numero_del_documento_no_aparece_en_logs_ni_eventos_tras_la_peticion()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await Lotes.CrearAsync(api, DosEmpresas.En(api).A, new ConfiguracionEmpresa(), ct);
        const string Documento = "79797979";

        var respuesta = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila("ABC123", "CC", Documento), Lotes.Fila("MAL", "CC", "80808080")], ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        logs.Mensajes.Should().NotBeEmpty("la petición dejó logs");
        logs.Mensajes.Should().Contain(m => m.Contains(id.ToString(), StringComparison.Ordinal), "la confirmación deja su log de conteos");
        logs.Mensajes.Should().NotContain(m => m.Contains(Documento, StringComparison.Ordinal) || m.Contains("80808080", StringComparison.Ordinal));
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand("SELECT count(*) FROM diagnostico.outbox o WHERE o::text LIKE '%' || @d || '%'", conexion);
        consulta.Parameters.AddWithValue("d", Documento);
        ((long)(await consulta.ExecuteScalarAsync(ct))!).Should().Be(0, "ningún evento lleva el documento");
    }
}
