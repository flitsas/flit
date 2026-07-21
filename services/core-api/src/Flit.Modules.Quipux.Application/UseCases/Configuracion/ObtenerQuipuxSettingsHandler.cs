using Flit.Modules.Quipux.Domain.Configuracion;

namespace Flit.Modules.Quipux.Application.UseCases.Configuracion;

/// <summary>
/// Lee la configuración Quipux vigente para la consola de administración, redactada
/// (<see cref="QuipuxSettingsView"/> nunca lleva secretos). Devuelve <c>null</c> si aún no hay fila:
/// la integración está inerte y el front debe mostrar el formulario vacío.
/// </summary>
public sealed class ObtenerQuipuxSettingsHandler
{
    private readonly IQuipuxSettingsRepository _repository;

    public ObtenerQuipuxSettingsHandler(IQuipuxSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<QuipuxSettingsView?> HandleAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetAsync(cancellationToken).ConfigureAwait(false);
        return settings is null ? null : QuipuxSettingsMapper.ToView(settings);
    }
}
