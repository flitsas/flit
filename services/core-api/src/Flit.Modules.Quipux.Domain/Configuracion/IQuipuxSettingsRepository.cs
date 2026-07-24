namespace Flit.Modules.Quipux.Domain.Configuracion;

/// <summary>Acceso a la configuración Quipux. La implementación descifra los secretos al leer y los cifra al escribir.</summary>
public interface IQuipuxSettingsRepository
{
    /// <summary>
    /// Configuración vigente, o null si no hay fila. Los workers la releen en CADA ciclo: por eso
    /// cambiar la cadencia o las credenciales en BD surte efecto sin desplegar ni reiniciar.
    /// </summary>
    Task<QuipuxSettings?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea o actualiza la fila única, cifrando <c>Password</c> y <c>AwsSecretAccessKey</c>.</summary>
    Task<QuipuxSettings> SaveAsync(
        QuipuxSettings settings,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);
}
