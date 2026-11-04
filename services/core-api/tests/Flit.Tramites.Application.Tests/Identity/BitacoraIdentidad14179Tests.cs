using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// HU #14179 — al aprobar o rechazar la identidad de una parte, la bitácora gana una frase corta.
/// Anterior vacío. Sin imagen, puntaje ni cuerpo del proveedor. La misma frase no deja otra fila.
/// </summary>
public sealed class BitacoraIdentidad14179Tests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 15, 3, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid InstanceId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private const string CuerpoProveedor =
        """{"imagen":"base64:AAAA","puntaje":99,"score":99,"cuerpo":"JSON_PROVEEDOR"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly List<ProcedureInstanceEvent> _eventos = [];

    public BitacoraIdentidad14179Tests()
    {
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(_eventos.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private IdentityValidationResultApplier Applier() => new(_events, _repo);

    private static ProcedureInstanceBiometricValidation Validacion(string parte) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ProcedureInstanceId = InstanceId,
        PartyRole = parte,
        Status = BiometricEstados.EnProceso,
        Provider = BiometricProviders.Kyverum,
    };

    private static IdentityValidationTerminalResult Resultado(bool approved) =>
        new(approved, approved ? "approved" : "rejected", CuerpoProveedor, Score: 99);

    [Fact]
    public async Task AC1_IdentidadCompradorAprobada_AnteriorVacioYCreatedByNull()
    {
        await Applier().ApplyAsync(Validacion("comprador"), Resultado(true), Now, Ct);

        var evt = _eventos.Should().ContainSingle().Subject;
        evt.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        evt.CreatedBy.Should().BeNull();
        evt.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        evt.CreatedAt.Should().Be(Now);
        evt.TenantId.Should().Be(TenantId);
        evt.ProcedureInstanceId.Should().Be(InstanceId);

        using var doc = JsonDocument.Parse(evt.Payload);
        var payload = doc.RootElement;
        payload.GetProperty("etiqueta").GetString().Should().Be("Validación de identidad");
        payload.GetProperty("campo").GetString().Should().Be("identidad");
        payload.GetProperty("anterior").GetString().Should().Be(string.Empty);
        payload.GetProperty("nuevo").GetString().Should().Be("Identidad del comprador: aprobada");
    }

    [Fact]
    public async Task AC2_PayloadSinDatosDelProveedor_YRechazoConFrase()
    {
        await Applier().ApplyAsync(Validacion("comprador"), Resultado(true), Now, Ct);
        await Applier().ApplyAsync(Validacion("comprador"), Resultado(false), Now, Ct);

        _eventos.Should().HaveCount(2);
        AssertPayloadLimpio(_eventos[0], "Identidad del comprador: aprobada");
        AssertPayloadLimpio(_eventos[1], "Identidad del comprador: rechazada");
    }

    [Fact]
    public async Task MismaFrase_NoVuelveALlamarAddEventAsync()
    {
        var applier = Applier();
        var primera = Validacion("comprador");

        await applier.ApplyAsync(primera, Resultado(true), Now, Ct);
        await applier.ApplyAsync(primera, Resultado(true), Now, Ct);

        // Misma parte con otra grafía: la frase normalizada ya se encoló en este scope.
        await applier.ApplyAsync(Validacion(" COMPRADOR "), Resultado(true), Now, Ct);

        const string frase = "Identidad del comprador: aprobada";
        var yaRegistrada = new ProcedureInstanceModificacionEntry(
            Guid.NewGuid(),
            "Validación de identidad",
            "identidad",
            "",
            "  " + frase + "  ",
            Now,
            null,
            null,
            null);
        _repo.GetModificacionesPageAsync(
                InstanceId, TenantId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstanceModificacionEntry>)[yaRegistrada], 1));

        // Otro applier: la guarda es la frase ya persistida, Ordinal tras trim.
        await Applier().ApplyAsync(Validacion("comprador"), Resultado(true), Now, Ct);

        _eventos.Should().ContainSingle();
        await _repo.Received(1).AddEventAsync(
            Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    private static void AssertPayloadLimpio(ProcedureInstanceEvent evt, string frase)
    {
        using var doc = JsonDocument.Parse(evt.Payload);
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal(
            "etiqueta", "campo", "anterior", "nuevo");
        root.GetProperty("etiqueta").GetString().Should().Be("Validación de identidad");
        root.GetProperty("campo").GetString().Should().Be("identidad");
        root.GetProperty("anterior").GetString().Should().Be(string.Empty);
        root.GetProperty("nuevo").GetString().Should().Be(frase);

        evt.Payload.Should().NotContain("imagen");
        evt.Payload.Should().NotContain("puntaje");
        evt.Payload.Should().NotContain("score");
        evt.Payload.Should().NotContain("base64");
        evt.Payload.Should().NotContain("JSON_PROVEEDOR");
        evt.Payload.Should().NotContain(CuerpoProveedor);
        evt.CreatedBy.Should().BeNull();
    }
}
