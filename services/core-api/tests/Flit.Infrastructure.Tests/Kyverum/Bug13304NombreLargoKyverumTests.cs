using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Kyverum;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Kyverum;

/// <summary>
/// Bug #13304 — el actor admite nombre de hasta 320 caracteres. Verifica que, en NUESTRO código, el nombre
/// llega completo a Kyverum: el sujeto se arma desde el actor sin recorte (<see cref="IdentitySubjectResolver"/>),
/// el adaptador lo pasa tal cual (<see cref="KyverumIdentityValidationProvider"/>) y el cliente HTTP lo
/// serializa entero en <c>subjects[].nombre</c> del create-validation (<see cref="KyverumVerifyClient"/>).
/// <para>
/// ACLARACIÓN: el límite real del servidor de Kyverum no se puede probar offline. El usuario confirma que ya
/// funciona con nombres largos; aquí solo se prueba que nosotros no recortamos ni rechazamos el valor.
/// </para>
/// <para>Uso de ejemplo:</para>
/// <code>
/// var result = await client.StartVerificationAsync(new KyverumVerifyStartRequest(id, vid, "comprador",
///     nombre320, "CC", "123456", "a@b.co"), ct);   // body: subjects[0].nombre == nombre320
/// </code>
/// </summary>
public sealed class Bug13304NombreLargoKyverumTests
{
    private static readonly string Nombre320 = new string('N', 150) + " " + new string('A', 100) + " " + new string('R', 68);

    [Fact]
    public void El_nombre_de_prueba_tiene_320_caracteres() => Nombre320.Should().HaveLength(320);

    [Fact]
    public async Task Create_validation_lleva_el_nombre_de_320_completo_en_subjects_nombre()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler(HttpStatusCode.Created,
            """{"id":"kyv_1","status":"pending","webhookSecret":"whsec","captureLinks":[{"captureUrl":"https://verify.kyverum.test/s/1"}]}""");
        var client = new KyverumVerifyClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://verify.kyverum.test") },
            Options.Create(new KyverumOptions { BaseUrl = "https://verify.kyverum.test", ApiKey = "k" }),
            NullLogger<KyverumVerifyClient>.Instance);

        var act = async () => await client.StartVerificationAsync(
            new KyverumVerifyStartRequest(Guid.NewGuid(), Guid.NewGuid(), "comprador", Nombre320, "CC", "123456", "a@b.co"),
            ct);

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.VerificationId.Should().Be("kyv_1");
        using var body = JsonDocument.Parse(handler.LastBody!);
        var nombre = body.RootElement.GetProperty("subjects")[0].GetProperty("nombre").GetString();
        nombre.Should().Be(Nombre320);
        nombre.Should().HaveLength(320);
        // El nombre no se filtra al payload sanitizado que se persiste.
        result.RawPayloadSanitized.Should().NotContain(Nombre320);
    }

    [Fact]
    public void El_sujeto_de_identidad_conserva_el_nombre_de_320_del_actor()
    {
        var actor = new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123456",
            FullName = Nombre320,
            Email = "a@b.co",
            Phone = new string('3', 50),
        };

        IdentitySubjectResolver.For(actor).Nombre.Should().Be(Nombre320);
    }

    [Fact]
    public async Task El_adaptador_de_proveedor_pasa_el_nombre_de_320_sin_recorte()
    {
        var ct = TestContext.Current.CancellationToken;
        var kyverum = Substitute.For<IKyverumVerifyClient>();
        KyverumVerifyStartRequest? enviado = null;
        kyverum.StartVerificationAsync(Arg.Do<KyverumVerifyStartRequest>(r => enviado = r), ct)
            .Returns(new KyverumVerifyStartResult("kyv_1", "https://x", "whsec", "pending", "{}"));

        await new KyverumIdentityValidationProvider(kyverum).StartAsync(
            new IdentityProviderStartRequest(Guid.NewGuid(), Guid.NewGuid(), "comprador", Nombre320, "CC", "123456", "a@b.co"),
            ct);

        enviado!.Nombre.Should().Be(Nombre320);
    }

    private sealed class CapturingHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
