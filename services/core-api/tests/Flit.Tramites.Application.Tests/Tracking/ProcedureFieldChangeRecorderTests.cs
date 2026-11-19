using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Tracking;

/// <summary>
/// HU #14171 — <see cref="ProcedureFieldChangeRecorder"/>: genera eventos de bitácora
/// append-only (<c>dato_modificado</c>) a partir de cambios de campo del trámite.
/// </summary>
public sealed class ProcedureFieldChangeRecorderTests
{
    private static readonly Guid TenantId            = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProcedureInstanceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ActorUserId         = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTimeOffset AtUtc     = new(2026, 10, 29, 14, 0, 0, TimeSpan.Zero);

    // -----------------------------------------------------------------------
    // AC1: tres campos visibles con valores distintos → 3 eventos con los
    //      metadatos correctos; mismo CreatedAt y mismo CreatedBy en todos.
    // -----------------------------------------------------------------------
    [Fact]
    public void AC1_TresCambiosVisibles_Genera3EventosConEtiquetasYMetadatosCorrectos()
    {
        // Arrange
        var changes = new List<ProcedureFieldChange>
        {
            new("plate",         "ABC123",  "XYZ999"),
            new("vin",           "OLD-VIN", "NEW-VIN"),
            new("vehicle_brand", "RENAULT", "CHEVROLET"),
        };

        // Act
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, changes, ActorUserId, AtUtc);

        // Assert — cantidad y tipo
        events.Should().HaveCount(3);
        events.Should().OnlyContain(e => e.Tipo == ProcedureFieldChangeRecorder.EventType);

        // Todos comparten el MISMO CreatedAt y el MISMO CreatedBy
        events.Should().OnlyContain(e => e.CreatedAt == AtUtc);
        events.Should().OnlyContain(e => e.CreatedBy == ActorUserId);

        // TenantId y ProcedureInstanceId copiados
        events.Should().OnlyContain(e => e.TenantId == TenantId);
        events.Should().OnlyContain(e => e.ProcedureInstanceId == ProcedureInstanceId);

        // Cada evento tiene un Id único (Guid.NewGuid())
        events.Select(e => e.Id).Should().OnlyHaveUniqueItems();

        // Payload del evento de plate
        var ePlate = events.Single(e => PayloadContiene(e.Payload, "campo", "plate"));
        var pPlate = ParsePayload(ePlate.Payload);
        pPlate.GetProperty("etiqueta").GetString().Should().Be("Placa");
        pPlate.GetProperty("anterior").GetString().Should().Be("ABC123");
        pPlate.GetProperty("nuevo").GetString().Should().Be("XYZ999");

        // Payload del evento de vin
        var eVin = events.Single(e => PayloadContiene(e.Payload, "campo", "vin"));
        var pVin = ParsePayload(eVin.Payload);
        pVin.GetProperty("etiqueta").GetString().Should().Be("VIN");
        pVin.GetProperty("anterior").GetString().Should().Be("OLD-VIN");
        pVin.GetProperty("nuevo").GetString().Should().Be("NEW-VIN");

        // Payload del evento de vehicle_brand
        var eBrand = events.Single(e => PayloadContiene(e.Payload, "campo", "vehicle_brand"));
        var pBrand = ParsePayload(eBrand.Payload);
        pBrand.GetProperty("etiqueta").GetString().Should().Be("Marca");
        pBrand.GetProperty("anterior").GetString().Should().Be("RENAULT");
        pBrand.GetProperty("nuevo").GetString().Should().Be("CHEVROLET");
    }

    // -----------------------------------------------------------------------
    // AC2: guardar el MISMO valor (exacto o con espacios) → 0 eventos.
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData("ABC123",   "ABC123")]   // valor idéntico
    [InlineData(" ABC123 ", "ABC123")]   // espacios en anterior → normaliza a igual
    [InlineData("ABC123",   " ABC123")] // espacios en nuevo → normaliza a igual
    [InlineData("ABC123",   " ABC123 ")] // espacios en ambos extremos del nuevo
    public void AC2_MismoValorNormalizado_CeroEventos(string anterior, string nuevo)
    {
        // Arrange
        var changes = new List<ProcedureFieldChange>
        {
            new("plate", anterior, nuevo),
        };

        // Act
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, changes, ActorUserId, AtUtc);

        // Assert
        events.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // AC3: clave interna (current_step, *_runt, soat_estado) → 0 eventos.
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData("current_step")]
    [InlineData("vehicle_color_runt")]
    [InlineData("vehicle_fuel_runt")]
    [InlineData("vehicle_body_type_runt")]
    [InlineData("soat_estado")]
    [InlineData("soat_vencimiento")]
    [InlineData("transit_office_id")]
    [InlineData("es_leasing")]
    [InlineData("clave_desconocida_xyz")]
    public void AC3_ClaveInterna_CeroEventos(string fieldKey)
    {
        // Arrange
        var changes = new List<ProcedureFieldChange>
        {
            new(fieldKey, "anterior", "nuevo_valor"),
        };

        // Act
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, changes, ActorUserId, AtUtc);

        // Assert
        events.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Caso borde: anterior null y nuevo con valor → 1 evento con anterior "".
    // -----------------------------------------------------------------------
    [Fact]
    public void CasoBorde_AnteriorNullNuevoConValor_UnEventoConAnteriorVacio()
    {
        // Arrange
        var changes = new List<ProcedureFieldChange>
        {
            new("plate", null, "XYZ987"),
        };

        // Act
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, changes, ActorUserId, AtUtc);

        // Assert
        events.Should().HaveCount(1);
        var payload = ParsePayload(events[0].Payload);
        payload.GetProperty("anterior").GetString().Should().Be(string.Empty);
        payload.GetProperty("nuevo").GetString().Should().Be("XYZ987");
    }

    // -----------------------------------------------------------------------
    // Caso borde: nuevo null y anterior con valor → 1 evento con nuevo "".
    // -----------------------------------------------------------------------
    [Fact]
    public void CasoBorde_NuevoNullAnteriorConValor_UnEventoConNuevoVacio()
    {
        // Arrange
        var changes = new List<ProcedureFieldChange>
        {
            new("vin", "ANTERIOR-VIN", null),
        };

        // Act
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, changes, ActorUserId, AtUtc);

        // Assert
        events.Should().HaveCount(1);
        var payload = ParsePayload(events[0].Payload);
        payload.GetProperty("anterior").GetString().Should().Be("ANTERIOR-VIN");
        payload.GetProperty("nuevo").GetString().Should().Be(string.Empty);
    }

    // -----------------------------------------------------------------------
    // Caso borde: lista vacía → lista vacía (sin excepción).
    // -----------------------------------------------------------------------
    [Fact]
    public void CasoBorde_ListaVacia_DevuelveListaVacia()
    {
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, [], ActorUserId, AtUtc);

        events.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Caso borde: lista null → lista vacía (sin excepción).
    // -----------------------------------------------------------------------
    [Fact]
    public void CasoBorde_ListaNull_DevuelveListaVacia()
    {
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, null!, ActorUserId, AtUtc);

        events.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Sensibilidad a mayúsculas: ABC123 → abc123 → 1 evento (son distintos).
    // -----------------------------------------------------------------------
    [Fact]
    public void Mayusculas_ABC123_abc123_SonDistintos_UnEvento()
    {
        // Arrange
        var changes = new List<ProcedureFieldChange>
        {
            new("plate", "ABC123", "abc123"),
        };

        // Act
        var events = ProcedureFieldChangeRecorder.Record(
            TenantId, ProcedureInstanceId, changes, ActorUserId, AtUtc);

        // Assert
        events.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static JsonElement ParsePayload(string json)
        => JsonDocument.Parse(json).RootElement;

    private static bool PayloadContiene(string json, string propiedad, string valor)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propiedad, out var prop)
                   && prop.GetString() == valor;
        }
        catch
        {
            return false;
        }
    }
}
