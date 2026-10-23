using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.Ocr;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Ajustes del chat leídos de <c>Anthropic:DrFlit*</c> (HU #12919). Sin API key el chat se considera
/// apagado: así un ambiente sin key no gasta el tope diario de los usuarios en respuestas degradadas.
/// </summary>
internal sealed class DrFlitChatSettings(IOptions<AnthropicOptions> options) : IDrFlitChatSettings
{
    private readonly AnthropicOptions _options = options.Value;

    public int DailyMessageLimit => _options.DrFlitDailyMessageLimit;

    public bool Enabled => _options.DrFlitEnabled && !string.IsNullOrWhiteSpace(_options.ApiKey);
}
