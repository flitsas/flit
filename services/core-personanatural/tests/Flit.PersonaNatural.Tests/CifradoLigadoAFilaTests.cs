using System.Security.Cryptography;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14037, revisión de seguridad M10 — el AAD de AES-GCM lleva columna, solicitud y <c>parte:ordinal</c>: un valor copiado a
/// otra fila (o a otra parte de la misma solicitud) no se descifra. Datos ficticios.
/// </summary>
public sealed class CifradoLigadoAFilaTests
{
    private const string Llave = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string Correo = "ficticia.prueba@example.test";

    private static CifradoPii Cifrado() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Pii:Cifrado:Llaves:1"] = Llave }).Build());

    [Fact]
    public void ElMismoContextoCompleto_AbreElValor()
    {
        var c = Cifrado();
        var solicitud = Guid.NewGuid();

        var cifrado = c.Cifrar(Correo, new ContextoCifrado("persona.correo", solicitud, "comprador", 1))!;

        c.Descifrar(cifrado, new ContextoCifrado("persona.correo", solicitud, "comprador", 1)).Should().Be(Correo);
    }

    [Theory]
    [InlineData("otra-solicitud", "comprador", 1, "persona.correo")]
    [InlineData("misma", "vendedor", 1, "persona.correo")]
    [InlineData("misma", "comprador", 2, "persona.correo")]
    [InlineData("misma", "comprador", 1, "persona.celular")]
    public void UnValorEnOtraFilaParteOColumna_NoSeDescifra(string solicitud, string parte, int ordinal, string columna)
    {
        var c = Cifrado();
        var original = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var cifrado = c.Cifrar(Correo, new ContextoCifrado("persona.correo", original, "comprador", 1))!;
        var destino = solicitud == "misma" ? original : Guid.Parse("22222222-2222-2222-2222-222222222222");

        c.Invoking(x => x.Descifrar(cifrado, new ContextoCifrado(columna, destino, parte, ordinal))).Should().Throw<CryptographicException>();
    }

    [Fact]
    public void ElContextoSoloDeColumna_NoAbreUnValorLigadoAFila_NiAlReves()
    {
        var c = Cifrado();
        var solicitud = Guid.NewGuid();
        var ligado = c.Cifrar(Correo, new ContextoCifrado("persona.correo", solicitud, "comprador", 1))!;
        var suelto = c.Cifrar(Correo, "persona.correo")!;

        c.Invoking(x => x.Descifrar(ligado, "persona.correo")).Should().Throw<CryptographicException>();
        c.Invoking(x => x.Descifrar(suelto, new ContextoCifrado("persona.correo", solicitud, "comprador", 1))).Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task CopiarElCiphertextAOtraFila_HaceQueLaLecturaFalle()
    {
        await using var host = await PortalHost.IniciarAsync("cifra");
        var a = await SembrarSolicitudAsync(host, "PN-CIFRA-A");
        var b = await SembrarSolicitudAsync(host, "PN-CIFRA-B");
        var datos = new DatosPersona("comprador", 1, "CC", "1020456789", "Maria Ficticia", Correo, "3001234567", "Calle 1 # 2-3");
        await host.EnScopeAsync(async sp => await sp.GetRequiredService<PersonaService>().GuardarAsync(a, datos, TestContext.Current.CancellationToken));
        await host.EnScopeAsync(async sp => await sp.GetRequiredService<PersonaService>().GuardarAsync(b, datos with { Correo = "otra.ficticia@example.test" }, TestContext.Current.CancellationToken));
        (await host.EnScopeAsync(sp => sp.GetRequiredService<PersonaService>().LeerAsync(b, "prueba", TestContext.Current.CancellationToken))).Single().Correo
            .Should().Be("otra.ficticia@example.test", "antes de la copia, cada fila abre lo suyo");

        await using (var db = host.NuevaDb())
        {
            var correoDeA = await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == a).Select(p => p.CorreoCifrado).SingleAsync(TestContext.Current.CancellationToken);
            await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == b)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.CorreoCifrado, correoDeA), TestContext.Current.CancellationToken);
        }

        var leer = () => host.EnScopeAsync(sp => sp.GetRequiredService<PersonaService>().LeerAsync(b, "prueba", TestContext.Current.CancellationToken));
        await leer.Should().ThrowAsync<CryptographicException>("el correo cifrado para la solicitud A no abre en la solicitud B");
    }

    private static async Task<Guid> SembrarSolicitudAsync(PortalHost host, string radicado)
    {
        await using var db = host.NuevaDb();
        var solicitud = new SolicitudTramite
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            CodigoPublico = radicado,
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            OrganismoId = Guid.NewGuid(),
            OrganismoNombre = "Organismo de prueba",
            Placa = "ABC123",
            Estado = EstadoSolicitud.Borrador,
            CorrelationId = Guid.NewGuid(),
        };
        db.Add(solicitud);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return solicitud.Id;
    }
}
