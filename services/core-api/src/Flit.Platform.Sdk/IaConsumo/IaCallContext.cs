namespace Flit.Platform.Sdk.IaConsumo;

/// <summary>
/// Contexto de negocio de una llamada a la IA (contrato ia-consumo v1, regla 7). Lo abre el código que llama y lo lee el
/// cliente HTTP al registrar. Todos los campos son opcionales: en un ámbito anidado, lo que viene null se hereda del
/// ámbito de afuera. Usuario y tenant normalmente NO se ponen aquí: se toman de la petición en curso.
/// </summary>
public sealed record IaAmbito
{
    public string? Modulo { get; init; }
    public string? Proceso { get; init; }
    public string? TipoTarea { get; init; }
    public string? Origen { get; init; }
    public string? Placa { get; init; }
    public string? EntidadTipo { get; init; }
    public string? EntidadId { get; init; }
    public string? PromptCodigo { get; init; }
    public string? PromptVersion { get; init; }
    public string? ArchivoMime { get; init; }
    public long? ArchivoBytes { get; init; }
    public int? ArchivoPaginas { get; init; }
    public string? ArchivoSha256 { get; init; }
    /// <summary>1 = primera; 2.. = segunda pasada o failover a otro proveedor.</summary>
    public short? Pasada { get; init; }
    public string? TenantId { get; init; }
    public string? UsuarioId { get; init; }
    public string? UsuarioLogin { get; init; }

    /// <summary>Lo de <paramref name="hijo"/> que no es null gana sobre lo de este ámbito.</summary>
    internal IaAmbito Combinar(IaAmbito hijo) => new()
    {
        Modulo = hijo.Modulo ?? Modulo,
        Proceso = hijo.Proceso ?? Proceso,
        TipoTarea = hijo.TipoTarea ?? TipoTarea,
        Origen = hijo.Origen ?? Origen,
        Placa = hijo.Placa ?? Placa,
        EntidadTipo = hijo.EntidadTipo ?? EntidadTipo,
        EntidadId = hijo.EntidadId ?? EntidadId,
        PromptCodigo = hijo.PromptCodigo ?? PromptCodigo,
        PromptVersion = hijo.PromptVersion ?? PromptVersion,
        ArchivoMime = hijo.ArchivoMime ?? ArchivoMime,
        ArchivoBytes = hijo.ArchivoBytes ?? ArchivoBytes,
        ArchivoPaginas = hijo.ArchivoPaginas ?? ArchivoPaginas,
        ArchivoSha256 = hijo.ArchivoSha256 ?? ArchivoSha256,
        Pasada = hijo.Pasada ?? Pasada,
        TenantId = hijo.TenantId ?? TenantId,
        UsuarioId = hijo.UsuarioId ?? UsuarioId,
        UsuarioLogin = hijo.UsuarioLogin ?? UsuarioLogin,
    };
}

/// <summary>El ámbito vigente y su <c>correlacion_id</c> (el mismo para toda la operación de negocio).</summary>
public sealed record IaAmbitoActual(Guid CorrelacionId, IaAmbito Ambito);

/// <summary>
/// Ámbito ambiental (AsyncLocal) de las llamadas a la IA. <c>using (ctx.Iniciar(...))</c> es anidable: el ámbito de
/// adentro hereda lo que no redefine y conserva el <c>correlacion_id</c> del de afuera, así reintentos, segunda pasada y
/// failover quedan unidos. Las tareas que se lanzan dentro (Task.WhenAll) heredan el ámbito; lo que abren ellas no se
/// filtra hacia afuera.
/// </summary>
public interface IIaCallContext
{
    /// <summary>El ámbito vigente, o null si nadie abrió uno.</summary>
    IaAmbitoActual? Actual { get; }

    /// <summary>Abre un ámbito; al hacer Dispose se restaura el anterior.</summary>
    IDisposable Iniciar(IaAmbito ambito);
}

/// <summary>Implementación única: el estado vive en un AsyncLocal estático, así que todas las instancias lo comparten.</summary>
public sealed class IaCallContext : IIaCallContext
{
    private static readonly AsyncLocal<IaAmbitoActual?> Vigente = new();

    /// <summary>Instancia para quien no tiene contenedor (pruebas, clientes construidos a mano).</summary>
    public static IaCallContext Instancia { get; } = new();

    public IaAmbitoActual? Actual => Vigente.Value;

    public IDisposable Iniciar(IaAmbito ambito)
    {
        ArgumentNullException.ThrowIfNull(ambito);
        var anterior = Vigente.Value;
        Vigente.Value = anterior is null
            ? new IaAmbitoActual(Guid.CreateVersion7(), ambito)
            : anterior with { Ambito = anterior.Ambito.Combinar(ambito) };
        return new Restaurar(anterior);
    }

    private sealed class Restaurar(IaAmbitoActual? anterior) : IDisposable
    {
        private bool _hecho;

        public void Dispose()
        {
            if (_hecho)
                return;
            _hecho = true;
            Vigente.Value = anterior;
        }
    }
}

/// <summary>Atajos sobre <see cref="IIaCallContext"/>.</summary>
public static class IaCallContextExtensions
{
    public static IDisposable Iniciar(
        this IIaCallContext ctx,
        string? modulo,
        string? proceso,
        string? tipoTarea = null,
        string? origen = null,
        string? placa = null,
        string? entidadTipo = null,
        string? entidadId = null,
        string? promptCodigo = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.Iniciar(new IaAmbito
        {
            Modulo = modulo,
            Proceso = proceso,
            TipoTarea = tipoTarea,
            Origen = origen,
            Placa = placa,
            EntidadTipo = entidadTipo,
            EntidadId = entidadId,
            PromptCodigo = promptCodigo,
        });
    }
}
