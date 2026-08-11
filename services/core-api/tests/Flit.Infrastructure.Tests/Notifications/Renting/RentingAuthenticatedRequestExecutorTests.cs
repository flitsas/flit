using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11360 AC4/AC5/AC6 — política de reintento del ejecutor autenticado del canal Renting.
/// <para>
/// Uso de ejemplo del componente bajo prueba:
/// <code>
/// var executor = new RentingAuthenticatedRequestExecutor(tokenCache, logger);
/// var outcome = await executor.ExecuteAsync((token, ct) => httpSend(token, ct), cancellationToken);
/// </code>
/// </para>
/// <para>
/// El "adaptador de transporte" (<c>send</c>) es SIEMPRE un doble en estas pruebas — nunca la API
/// real de Renting, tal como exige el alcance de esta HU (el multipart/envío real es la HU
/// #11361). <see cref="IRentingTokenCache"/> se sustituye con NSubstitute (patrón vigente en este
/// proyecto de pruebas).
/// </para>
/// </summary>
public sealed class RentingAuthenticatedRequestExecutorTests
{
    // ── AC4 — un solo reintento, y solo si el primero fue 401 ──────────────────────

    [Fact]
    public async Task ExecuteAsync_Con401YLuegoExito_InvalidaElTokenYReintentaUnaSolaVez()
    {
        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-viejo", "token-nuevo");

        var sendCalls = new List<string>();
        Task<HttpResponseMessage> Send(string token, CancellationToken ct)
        {
            sendCalls.Add(token);
            var status = sendCalls.Count == 1 ? System.Net.HttpStatusCode.Unauthorized : System.Net.HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }

        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Send, TestContext.Current.CancellationToken);

        sendCalls.Should().Equal("token-viejo", "token-nuevo");
        tokenCache.Received(1).Invalidate("token-viejo");
        outcome.IsFailure.Should().BeFalse();
        outcome.Response!.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExecuteAsync_ConDos401Seguidos_FallaPorAutenticacionYNoHaceUnTercerIntento()
    {
        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-1", "token-2", "token-3-no-deberia-pedirse");

        var sendCalls = new List<string>();
        Task<HttpResponseMessage> Send(string token, CancellationToken ct)
        {
            sendCalls.Add(token);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        }

        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Send, TestContext.Current.CancellationToken);

        // AC4 — EXACTAMENTE dos intentos, nunca tres: el segundo 401 falla de inmediato.
        sendCalls.Should().HaveCount(2);
        sendCalls.Should().Equal("token-1", "token-2");
        outcome.IsFailure.Should().BeTrue();
        outcome.Failure!.Outcome.Should().Be(EmailSendOutcome.AuthenticationFailed);
        outcome.Failure!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Con200EnElPrimerIntento_NoInvalidaNiReintenta()
    {
        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-vigente");

        var sendCalls = 0;
        Task<HttpResponseMessage> Send(string token, CancellationToken ct)
        {
            sendCalls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }

        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Send, TestContext.Current.CancellationToken);

        sendCalls.Should().Be(1);
        tokenCache.DidNotReceive().Invalidate(Arg.Any<string>());
        outcome.IsFailure.Should().BeFalse();
    }

    // ── AC5 — sin reintento ante tiempo agotado ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConEnvioQueAgotaElTiempo_FallaPorTiempoAgotadoSinReintentar()
    {
        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-vigente");

        var sendCalls = 0;
        Task<HttpResponseMessage> Send(string token, CancellationToken ct)
        {
            sendCalls++;
            // Simula el timeout PROPIO del envío (no la cancelación del llamador): una
            // OperationCanceledException con un token distinto al que recibió el ejecutor.
            throw new OperationCanceledException("Simula timeout de envío.", new CancellationTokenSource().Token);
        }

        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Send, TestContext.Current.CancellationToken);

        // AC5 — sin ningún reintento: exactamente un intento de envío.
        sendCalls.Should().Be(1);
        tokenCache.DidNotReceive().Invalidate(Arg.Any<string>());
        outcome.IsFailure.Should().BeTrue();
        outcome.Failure!.Outcome.Should().Be(EmailSendOutcome.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_ConCancelacionDelPropioLlamador_PropagaLaCancelacionSinMapearATimedOut()
    {
        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-vigente");

        using var callerCts = new CancellationTokenSource();

        Task<HttpResponseMessage> Send(string token, CancellationToken ct)
        {
            callerCts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }

        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        // Distinción deliberada del AC5: si la cancelación viene del cancellationToken del propio
        // llamador (no un timeout interno del envío), el ejecutor no debe tragársela como TimedOut.
        var act = async () => await executor.ExecuteAsync(Send, callerCts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── AC6 — el fallo de login no se silencia ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CuandoElLoginInicialFalla_FallaPorAutenticacionSinLlamarAlEnvio()
    {
        var tokenCache = Substitute.For<IRentingTokenCache>();
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new RentingAuthenticationException("Renting: login rechazado."));

        var sendCalls = 0;
        Task<HttpResponseMessage> Send(string token, CancellationToken ct)
        {
            sendCalls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }

        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Send, TestContext.Current.CancellationToken);

        // AC6 — el proveedor de envío NUNCA se llama si no hay token, y el resultado NUNCA es un
        // "éxito parcial" con token nulo: es un fallo tipado de autenticación.
        sendCalls.Should().Be(0);
        outcome.IsFailure.Should().BeTrue();
        outcome.Failure!.Success.Should().BeFalse();
        outcome.Failure!.Outcome.Should().Be(EmailSendOutcome.AuthenticationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_CuandoElLoginDeReintentoFalla_FallaPorAutenticacion()
    {
        var tokenCache = Substitute.For<IRentingTokenCache>();
        var callCount = 0;
        tokenCache.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            return callCount == 1
                ? Task.FromResult("token-viejo")
                : throw new RentingAuthenticationException("Renting: login de reintento rechazado.");
        });

        var sendCalls = 0;
        Task<HttpResponseMessage> Send(string token, CancellationToken ct)
        {
            sendCalls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        }

        var executor = new RentingAuthenticatedRequestExecutor(tokenCache, NullLogger<RentingAuthenticatedRequestExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Send, TestContext.Current.CancellationToken);

        sendCalls.Should().Be(1, "el 401 inicial invalida y pide login de nuevo; si ESE login falla, no hay segundo envío");
        outcome.IsFailure.Should().BeTrue();
        outcome.Failure!.Outcome.Should().Be(EmailSendOutcome.AuthenticationFailed);
    }
}
