using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Application.Plataforma.Ai.Entrega;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Infrastructure.Ai.Entrega;

/// <summary>Lectura de la llave en claro de un proveedor, solo para sellarla (ADR-0091 §8: nunca fuera de Infrastructure).</summary>
internal interface ILectorLlaveProveedorIa
{
    string? LeerLlave(string providerCode);
}

/// <summary>
/// <see cref="IEntregaConfiguracionIa"/> sobre <c>admin.ai_tool_provider_settings</c> y las llaves del entorno de core-api
/// (ADR-0091 §4, DELTA P2=b: la <c>ANTHROPIC_API_KEY</c> general). La cola es la de siempre
/// (<see cref="AiProviderQueue"/>: enabled ∩ capaz ∩ con llave, activo primero); el modelo sale de
/// <c>Ai:Herramientas:&lt;tool&gt;:Modelos:&lt;proveedor&gt;</c> o del catálogo y debe cumplir la regex del cliente (si no, el
/// proveedor sale de la cola con un Warning); el TTL de <c>Ai:Entrega:TtlSegundos</c> acotado a
/// [1, 300] (60 si no hay proveedor). Sin caché ni escritura: cada llamada lee la fila y sella con CEK/IV nuevos.
/// </summary>
internal sealed partial class ServicioEntregaConfiguracionIa(
    IAiToolProviderSettingsRepository repository,
    IAiApiKeyEnvironmentProbe probe,
    ILectorLlaveProveedorIa lector,
    RegistroClavesDestinatarioIa claves,
    IConfiguration configuration,
    TimeProvider? tiempo = null,
    ILogger<ServicioEntregaConfiguracionIa>? logger = null) : IEntregaConfiguracionIa
{
    internal const int TtlMaximoSegundos = 300;
    internal const int TtlNoDisponibleSegundos = 60;

    /// <summary>Modelo por defecto de catálogo por (herramienta, proveedor). CF1: el redactor sigue con claude-haiku-4-5.</summary>
    private static readonly Dictionary<(string Tool, string Proveedor), string> ModelosPorDefecto = new()
    {
        [(AiToolCodes.DiagnosticoDictamen, AiProviderCodes.Anthropic)] = "claude-haiku-4-5",
    };

    private readonly TimeProvider _tiempo = tiempo ?? TimeProvider.System;
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public bool ConoceKid(string audiencia, string kid) => claves.Conoce(audiencia, kid);

    public async Task<EntregaConfiguracionIa> EntregarAsync(SolicitudEntregaIa solicitud, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var ahora = DateTimeOffset.FromUnixTimeSeconds(_tiempo.GetUtcNow().ToUnixTimeSeconds());
        var tool = solicitud.ToolCode;

        var fila = await repository.GetByToolCodeAsync(tool, cancellationToken).ConfigureAwait(false);
        if (fila is null)
            return NoDisponible(tool, MotivoNoDisponibleIa.HerramientaSinConfigurar, string.Empty, false, 0, ahora);

        var codigos = AiProviderQueue.Build(tool, fila.ActiveProvider, fila.Providers, probe.HasApiKey);
        var cola = new List<ProveedorEntregadoIa>(codigos.Count);
        foreach (var codigo in codigos)
        {
            var modelo = Modelo(tool, codigo);
            if (modelo is null)
                continue;
            var llave = lector.LeerLlave(codigo);
            if (string.IsNullOrWhiteSpace(llave))
                continue; // §1.3: sin llave no entra; nunca un sobre vacío ni la llave de otro proveedor.

            using var publica = claves.Crear(solicitud.Audiencia, solicitud.Kid)
                ?? throw new InvalidOperationException("Clave del destinatario no registrada.");
            var aad = AadLlaveIa.Componer(solicitud.Audiencia, tool, codigo, modelo, fila.RowVersion, solicitud.Kid, solicitud.Nonce, ahora.ToUnixTimeSeconds());
            var sobre = SelladorLlaveIa.Sellar(publica, solicitud.Kid, llave, aad);
            var prioridad = fila.Providers.FirstOrDefault(p => string.Equals(AiProviderCodes.Normalize(p.ProviderCode), codigo, StringComparison.Ordinal))?.PriorityRank ?? 0;
            cola.Add(new ProveedorEntregadoIa(codigo, prioridad, modelo, sobre));
        }

        if (cola.Count == 0)
            return NoDisponible(tool, MotivoNoDisponibleIa.SinProveedorConLlave, fila.ActiveProvider, fila.AutoReturnToPrimary, fila.RowVersion, ahora);

        return new EntregaConfiguracionIa(tool, true, MotivoNoDisponibleIa.Ninguno, fila.ActiveProvider, fila.AutoReturnToPrimary, fila.RowVersion, cola, Ttl(), ahora);
    }

    private static EntregaConfiguracionIa NoDisponible(string tool, MotivoNoDisponibleIa motivo, string activo, bool retorno, long rowVersion, DateTimeOffset ahora) =>
        new(tool, false, motivo, activo, retorno, rowVersion, [], TtlNoDisponibleSegundos, ahora);

    private string? Modelo(string tool, string proveedor)
    {
        var configurado = configuration[$"Ai:Herramientas:{tool}:Modelos:{proveedor}"];
        var modelo = string.IsNullOrWhiteSpace(configurado) ? ModelosPorDefecto.GetValueOrDefault((tool, proveedor)) : configurado;
        if (modelo is null)
            return null;
        // Review HU-6 CR-4: la misma regex que el cliente y sin recortar (un salto final no se arregla, se descarta). Inválido ⇒
        // el proveedor sale de la cola (nunca INTERNAL) y el Warning no lleva el valor.
        if (!AadLlaveIa.ModeloValido(modelo))
        {
            LogModeloDescartado(_logger, tool, proveedor);
            return null;
        }

        return modelo;
    }

    [LoggerMessage(EventId = 7302, EventName = "ia.modelo.descartado", Level = LogLevel.Warning,
        Message = "ia.modelo.descartado tool={Tool} proveedor={Proveedor} motivo=modelo_invalido")]
    private static partial void LogModeloDescartado(ILogger logger, string tool, string proveedor);

    private int Ttl()
    {
        var valor = configuration.GetValue<int?>("Ai:Entrega:TtlSegundos");
        return valor is > 0 and <= TtlMaximoSegundos ? valor.Value : TtlMaximoSegundos;
    }
}
