using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// Caso C 2026-11-09 — contrato HTTP de <c>POST /api/v1/admin/plate-ranges/procedures/{id}/assign-plate</c>
/// cuando el repositorio rechaza la placa por formato de carro: 422 con el literal exacto, y nunca el
/// genérico «La placa es obligatoria.» (al que caía cualquier causa no prevista).
/// <para>Uso de ejemplo: <c>POST .../assign-plate { plate: "ABC12D" }</c> sobre un carro ⇒ 422
/// «La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.».</para>
/// </summary>
public sealed class AssignPlateFormatoCarroEndpointTests : IClassFixture<AssignPlateFormatoCarroEndpointTests.Factory>
{
    private const string MensajeCarro = "La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.";
    private const string MensajeMoto = "La placa de esta moto debe tener 3 letras y 2 números, con o sin una letra al final, por ejemplo ABC12D o ABC12.";
    private const string MensajeRemolque = "La placa de este remolque debe empezar por R o S seguida de 5 o 6 números, por ejemplo R12345 o S54321.";
    private const string MensajeMaquinaria = "La placa de esta maquinaria debe tener 2 letras y 6 números, por ejemplo MC029554.";
    private const string MensajeUnion =
        "La placa no cumple el formato esperado. Ej: ABC123 (carro), ABC12D (moto), R12345 (remolque), S12345 (semirremolque) o MC029554 (maquinaria).";
    private static readonly Guid OtTenant = Guid.Parse("0e000000-0000-4000-8000-000000001109");

    private readonly Factory _factory;

    public AssignPlateFormatoCarroEndpointTests(Factory factory) => _factory = factory;

    // AC2 / HU8 — el endpoint devuelve el Detail del repo (literal del grupo) con 422.
    [Theory]
    [InlineData(MensajeCarro)]
    [InlineData(MensajeMoto)]
    [InlineData(MensajeRemolque)]
    [InlineData(MensajeMaquinaria)]
    public async Task AC2_FormatoDeCarroInvalido_Responde422ConLiteralExacto(string detailDelRepo)
    {
        var instanceId = Guid.NewGuid();
        _factory.Repo
            .AssignPlateAsync(OtTenant, instanceId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(PlateAssignmentOutcome.Fail(PlateAssignmentFailure.InvalidPlateFormatForClass, detailDelRepo));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(OtTenant));
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/plate-ranges/procedures/{instanceId}/assign-plate",
            new { Plate = "ABC12D" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Detail.Should().Be(detailDelRepo);
    }

    // Tanda 2 — la corrección de placa (update-plate) nombra la misma causa con el mismo literal y 422.
    [Theory]
    [InlineData(MensajeCarro)]
    [InlineData(MensajeMoto)]
    [InlineData(MensajeRemolque)]
    [InlineData(MensajeMaquinaria)]
    public async Task T1_UpdatePlate_FormatoDeCarroInvalido_Responde422ConLiteralExacto(string detailDelRepo)
    {
        var instanceId = Guid.NewGuid();
        _factory.Repo
            .UpdatePlateAsync(OtTenant, instanceId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PlateAssignmentOutcome.Fail(PlateAssignmentFailure.InvalidPlateFormatForClass, detailDelRepo));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(OtTenant));
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/plate-ranges/procedures/{instanceId}/update-plate",
            new { Plate = "ABC12D" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Detail.Should().Be(detailDelRepo);
    }

    // HU8 — sin Detail del repo el endpoint no sabe el grupo: responde 422 con el mensaje de la unión de
    // formatos (válido para cualquier clase), nunca «La placa es obligatoria.» ni el de carro.
    [Theory]
    [InlineData("assign-plate")]
    [InlineData("update-plate")]
    public async Task HU8_SinDetailDelRepo_Responde422ConMensajeDeLaUnion(string accion)
    {
        var instanceId = Guid.NewGuid();
        var fallo = PlateAssignmentOutcome.Fail(PlateAssignmentFailure.InvalidPlateFormatForClass);
        _factory.Repo
            .AssignPlateAsync(OtTenant, instanceId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(fallo);
        _factory.Repo
            .UpdatePlateAsync(OtTenant, instanceId, Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(fallo);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(OtTenant));
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/plate-ranges/procedures/{instanceId}/{accion}",
            new { Plate = "ABC12D" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Detail.Should().Be(MensajeUnion);
    }

    /// <summary>Host real con <see cref="IOtClientProcedureRepository"/> sustituido.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public IOtClientProcedureRepository Repo { get; } = Substitute.For<IOtClientProcedureRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOtClientProcedureRepository>();
                services.AddScoped(_ => Repo);
            });
        }
    }
}
