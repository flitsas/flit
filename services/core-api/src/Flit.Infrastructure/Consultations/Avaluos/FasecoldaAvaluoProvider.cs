using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Avaluos;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Proveedor de avalúo Fasecolda (Key <c>fasecolda</c>, Feature #10707 / ADR-0029). Real: flujo
/// <c>analisis</c> por VIN (busquedaVin → token → consultabycodigo), filtra por atributos del
/// vehículo y selecciona el <c>valorModelo</c> del año, aplicando ×1000 (Fasecolda entrega miles de COP).
/// Mock (default): lee <c>avaluo_mock_values</c>. NUNCA lanza al handler.
/// </summary>
internal sealed class FasecoldaAvaluoProvider(
    IHttpClientFactory httpFactory,
    IOptions<FasecoldaOptions> options,
    IOptions<ConsultationProviderModeOptions> modeOptions,
    FasecoldaTokenCache tokenCache,
    AvaluoMockValueReader mockReader) : IAvaluoProvider
{
    private const string SourceKey = "fasecolda";
    private const long ThousandsToPesos = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FasecoldaOptions _options = options.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => SourceKey;

    public async Task<AvaluoResult> GetAvaluoAsync(AvaluoContext ctx, CancellationToken ct)
    {
        var vin = Field(ctx, "vin");
        if (string.IsNullOrWhiteSpace(vin))
            return AvaluoResult.NoData(SourceKey, "El vehículo no tiene VIN para consultar Fasecolda");

        if (ConsultationProviderModeOptions.IsMock(_modes.FasecoldaMode))
        {
            var mock = await mockReader.GetValueAsync(vin!, SourceKey, ct);
            return mock is null
                ? AvaluoResult.NoData(SourceKey, "Sin valor de referencia mock para el VIN")
                : AvaluoResult.Ok(SourceKey, mock.Value);
        }

        try
        {
            return await GetRealAsync(ctx, vin!, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return AvaluoResult.Error(SourceKey);
        }
    }

    private async Task<AvaluoResult> GetRealAsync(AvaluoContext ctx, string vin, CancellationToken ct)
    {
        // 1) Búsqueda por VIN → códigos (sin auth).
        var vinClient = httpFactory.CreateClient("fasecolda-vin");
        using var vinResp = await vinClient.GetAsync($"{_options.ByVinPath}/{Uri.EscapeDataString(vin)}", ct);
        if (vinResp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            return AvaluoResult.NoData(SourceKey, "VIN sin coincidencia en Fasecolda");
        if (!vinResp.IsSuccessStatusCode)
            return AvaluoResult.Error(SourceKey);

        var vinPayload = await vinResp.Content.ReadFromJsonAsync<FasecoldaVinResponse>(JsonOptions, ct);
        var codigos = vinPayload?.Codigos?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (codigos is null || codigos.Count == 0)
            return AvaluoResult.NoData(SourceKey, "VIN sin códigos en Fasecolda");

        // 2) Token OAuth2 (cacheado).
        var apiClient = httpFactory.CreateClient("fasecolda-api");
        var token = await tokenCache.GetTokenAsync(apiClient, _options, ct);
        if (string.IsNullOrWhiteSpace(token))
            return AvaluoResult.Error(SourceKey, "No fue posible autenticar contra Fasecolda");

        // 3) Consulta por códigos (Bearer).
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.ListCodePath}/{Uri.EscapeDataString(string.Join(",", codigos))}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var detResp = await apiClient.SendAsync(req, ct);
        if (!detResp.IsSuccessStatusCode)
            return AvaluoResult.Error(SourceKey);

        var items = await detResp.Content.ReadFromJsonAsync<List<FasecoldaCodeItem>>(JsonOptions, ct);
        if (items is null || items.Count == 0)
            return AvaluoResult.NoData(SourceKey, "Sin fichas técnicas para el VIN");

        // 4) Filtrar por atributos del vehículo y seleccionar el menor valor del año.
        var value = SelectValue(items, ctx);
        return value is null
            ? AvaluoResult.NoData(SourceKey, "No hay valor para el año/atributos del vehículo")
            : AvaluoResult.Ok(SourceKey, value.Value);
    }

    /// <summary>
    /// Réplica de la lógica <c>analisis</c>: filtra por cilindraje/combustible/pasajeros/puertas
    /// (comparación laxa: solo aplica el filtro si el atributo está presente) y, para el año del
    /// vehículo, elige el menor <c>valor</c> entre las fichas restantes; ×1000 → pesos reales.
    /// </summary>
    private static long? SelectValue(List<FasecoldaCodeItem> items, AvaluoContext ctx)
    {
        var year = Field(ctx, "vehicle_year")?.Trim();
        if (string.IsNullOrWhiteSpace(year))
            return null;

        var cilindraje = ParseInt(Field(ctx, "vehicle_engine_displacement"));
        var combustible = Field(ctx, "vehicle_fuel")?.Trim();
        var pasajeros = ParseInt(Field(ctx, "vehicle_passengers"));

        decimal? min = null;
        foreach (var item in items)
        {
            if (cilindraje is not null && item.Cilindraje is not null && item.Cilindraje != cilindraje)
                continue;
            if (!string.IsNullOrWhiteSpace(combustible) && !string.IsNullOrWhiteSpace(item.Combustible) &&
                !string.Equals(item.Combustible.Trim(), combustible, StringComparison.OrdinalIgnoreCase))
                continue;
            if (pasajeros is not null && item.CapacidadPasajeros is not null && item.CapacidadPasajeros != pasajeros)
                continue;

            var forYear = item.ValorModelo?.Where(v => string.Equals(v.Modelo?.Trim(), year, StringComparison.Ordinal));
            if (forYear is null)
                continue;

            foreach (var vm in forYear)
            {
                if (vm.Valor > 0 && (min is null || vm.Valor < min))
                    min = vm.Valor;
            }
        }

        return min is null ? null : (long)(min.Value * ThousandsToPesos);
    }

    private static string? Field(AvaluoContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var value) ? value : null;

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
