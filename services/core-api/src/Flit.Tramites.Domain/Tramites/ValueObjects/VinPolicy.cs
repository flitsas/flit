namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Código de conflicto VIN de matrícula inicial (paridad <c>TramiteVinConflictCode</c> de Johan).</summary>
public enum VinConflictCode
{
    /// <summary>Ya existe un trámite activo (no rechazado) para el VIN.</summary>
    TramiteDuplicado,

    /// <summary>El VIN ya tiene una matrícula inicial completada (bloqueo de por vida).</summary>
    TramiteMatriculaCompletada,
}

/// <summary>
/// Trámite existente mínimo para evaluar la invariante VIN (sin acoplar a la entidad de persistencia).
/// <para><see cref="Secretaria"/> (nombre del organismo de tránsito) y <see cref="FechaRegistro"/> son
/// metadatos informativos del registro previo — no intervienen en la decisión de conflicto; solo
/// alimentan el mensaje del check <c>vin_matricula</c> que se ofrece al gestor (HU #10538, R3).</para>
/// </summary>
public sealed record VinTramiteExistente(
    Guid Id,
    string Estado,
    int Paso,
    string? Placa,
    string Vin,
    string? Secretaria = null,
    DateTimeOffset? FechaRegistro = null,
    bool SubsanacionActiva = false);

/// <summary>Conflicto VIN detectado, con mensaje accionable (paridad <c>TramiteVinConflict</c> de Johan).</summary>
public sealed record VinConflict(
    VinConflictCode Code,
    string Message,
    VinTramiteExistente ExistingTramite);
