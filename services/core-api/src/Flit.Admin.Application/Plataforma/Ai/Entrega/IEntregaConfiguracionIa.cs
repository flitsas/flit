namespace Flit.Admin.Application.Plataforma.Ai.Entrega;

/// <summary>
/// Entrega de la configuración de IA de una herramienta a un servicio consumidor con la llave de cada proveedor sellada
/// para la clave pública del destinatario (ADR-0091, contrato <c>flit.platform.v1.ConfiguracionIaService</c>). Idempotente y
/// sin efectos: no cambia el proveedor activo ni registra transiciones. La llave en claro nunca sale de la implementación.
/// </summary>
public interface IEntregaConfiguracionIa
{
    /// <summary>¿Hay una clave pública válida (RSA ≥ 3072) registrada para <paramref name="audiencia"/> bajo <paramref name="kid"/>?</summary>
    bool ConoceKid(string audiencia, string kid);

    /// <summary>
    /// Arma la cola (enabled ∩ capaz ∩ con llave) y sella cada llave. Sin fila: <see cref="MotivoNoDisponibleIa.HerramientaSinConfigurar"/>;
    /// cola vacía: <see cref="MotivoNoDisponibleIa.SinProveedorConLlave"/>. El llamante ya validó cliente, herramienta, kid y nonce.
    /// </summary>
    Task<EntregaConfiguracionIa> EntregarAsync(SolicitudEntregaIa solicitud, CancellationToken cancellationToken);
}

/// <summary>Lo que pidió el consumidor, ya validado. <paramref name="Audiencia"/> sale del cliente, no del request.</summary>
public sealed record SolicitudEntregaIa(string Audiencia, string ToolCode, string Kid, byte[] Nonce);

public enum MotivoNoDisponibleIa
{
    Ninguno = 0,
    HerramientaSinConfigurar = 1,
    SinProveedorConLlave = 2,
}

/// <summary>Sobre híbrido RSA-OAEP-256 + A256GCM de una llave (sin la llave en claro).</summary>
public sealed record SobreLlaveIa(string Alg, string Kid, byte[] CekCifrada, byte[] Iv, byte[] TextoCifrado, byte[] Tag);

public sealed record ProveedorEntregadoIa(string Proveedor, int Prioridad, string Modelo, SobreLlaveIa Llave);

public sealed record EntregaConfiguracionIa(
    string ToolCode,
    bool Disponible,
    MotivoNoDisponibleIa Motivo,
    string ProveedorActivo,
    bool RetornoAutomatico,
    long RowVersion,
    IReadOnlyList<ProveedorEntregadoIa> Cola,
    int TtlSegundos,
    DateTimeOffset EmitidaEn);
