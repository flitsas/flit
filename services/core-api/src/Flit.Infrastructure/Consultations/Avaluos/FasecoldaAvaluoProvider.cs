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
        // 1) Búsqueda por VIN → códigos (sin auth). URL absoluta (ver FasecoldaUrl).
        var vinClient = httpFactory.CreateClient("fasecolda-vin");
        var vinUrl = FasecoldaUrl.Absolute(_options.ByVinBaseUrl, $"{_options.ByVinPath}/{Uri.EscapeDataString(vin)}");
        using var vinResp = await vinClient.GetAsync(vinUrl, ct);
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

        // 3) Consulta por códigos (Bearer). URL absoluta; los códigos van separados por coma en el path
        // (la API acepta comas literales y %2C — validado). Son numéricos, no requieren escaping.
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            FasecoldaUrl.Absolute(_options.ApiBaseUrl, $"{_options.ListCodePath}/{string.Join(",", codigos)}"));
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
    /// Réplica de la lógica <c>analisis</c> de la referencia: compara las fichas del VIN contra los
    /// atributos del vehículo (cilindraje / combustible / pasajeros; laxo: solo filtra si ambos lados
    /// tienen el dato) y, para el año del vehículo, elige el MENOR <c>valor</c>; ×1000 → pesos reales.
    ///
    /// Robustez (RUNT-fed): si el filtro por atributos no deja ninguna ficha con el año del vehículo
    /// (p. ej. el cilindraje del RUNT no coincide exacto con el del catálogo Fasecolda), se cae a un
    /// fallback que selecciona el menor valor del año entre TODAS las fichas del VIN. Así nunca queda
    /// en <c>null</c> cuando el catálogo sí tiene el año — todas las fichas provienen del mismo VIN.
    /// </summary>
    private static long? SelectValue(List<FasecoldaCodeItem> items, AvaluoContext ctx)
    {
        var year = Field(ctx, "vehicle_year")?.Trim();
        if (string.IsNullOrWhiteSpace(year))
            return null;

        var cilindraje = ParseInt(Field(ctx, "vehicle_engine_displacement"));
        var combustible = Field(ctx, "vehicle_fuel")?.Trim();
        var pasajeros = ParseInt(Field(ctx, "vehicle_passengers"));

        bool MatchesAttributes(FasecoldaCodeItem item)
        {
            if (cilindraje is not null && item.Cilindraje is not null && item.Cilindraje != cilindraje)
                return false;
            if (!string.IsNullOrWhiteSpace(combustible) && !string.IsNullOrWhiteSpace(item.Combustible) &&
                !string.Equals(item.Combustible.Trim(), combustible, StringComparison.OrdinalIgnoreCase))
                return false;
            if (pasajeros is not null && item.CapacidadPasajeros is not null && item.CapacidadPasajeros != pasajeros)
                return false;
            return true;
        }

        // 1) Preferido: fichas que matchean los atributos del vehículo.
        var min = MinValueForYear(items.Where(MatchesAttributes), year);

        // 2) Fallback: si el match estricto no dio valor, seleccionar por año entre todas las fichas del VIN.
        min ??= MinValueForYear(items, year);

        return min is null ? null : (long)(min.Value * ThousandsToPesos);
    }

    /// <summary>Menor <c>valor</c> (&gt; 0) entre los <c>valorModelo</c> del año dado; null si ninguno.</summary>
    private static decimal? MinValueForYear(IEnumerable<FasecoldaCodeItem> items, string year)
    {
        decimal? min = null;
        foreach (var item in items)
        {
            var forYear = item.ValorModelo?.Where(v => string.Equals(v.Modelo?.Trim(), year, StringComparison.Ordinal));
            if (forYear is null)
                continue;

            foreach (var vm in forYear)
            {
                if (vm.Valor > 0 && (min is null || vm.Valor < min))
                    min = vm.Valor;
            }
        }

        return min;
    }

    private static string? Field(AvaluoContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var value) ? value : null;

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
