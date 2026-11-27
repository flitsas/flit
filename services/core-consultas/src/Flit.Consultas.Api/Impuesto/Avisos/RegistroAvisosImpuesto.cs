namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>En qué quedó la consulta del aviso para esa empresa.</summary>
internal enum EstadoConsultaAviso
{
    /// <summary>No hay trabajo de esa empresa con ese id externo (otra empresa, vencida o inventada).</summary>
    NoEncontrada,

    /// <summary>El trabajo sigue esperando resultado (reservado o en cola).</summary>
    Abierta,

    /// <summary>El trabajo ya se cerró (por el polling o por otro aviso).</summary>
    Cerrada,
}

/// <summary>Persistencia del receptor: deduplicación por entrega y estado de la consulta. Sin cuerpo ni datos personales.</summary>
internal interface IRegistroAvisosImpuesto
{
    /// <summary><c>INSERT … ON CONFLICT DO NOTHING</c> por (empresa, entrega): true si es la primera vez que llega.</summary>
    Task<bool> RegistrarEntregaAsync(Guid tenantId, string deliveryId, string idExterno, int attempt, CancellationToken ct);

    Task<EstadoConsultaAviso> EstadoConsultaAsync(Guid tenantId, string idExterno, CancellationToken ct);

    /// <summary>Borra las entregas recibidas antes de <paramref name="antesDe"/>; devuelve cuántas.</summary>
    Task<int> PurgarAsync(DateTimeOffset antesDe, CancellationToken ct);
}
