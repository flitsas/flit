namespace Flit.Tramites.Domain.Entities;

/// <summary>Estados de una marca de firma a posteriori (HU #11196).</summary>
public static class DeferredSignatureEstados
{
    /// <summary>Esperando a que el representante complete su validación de identidad.</summary>
    public const string Pendiente = "pendiente";

    /// <summary>La validación llegó y el trámite se firmó dentro del lote.</summary>
    public const string Aplicada = "aplicada";

    /// <summary>La validación llegó pero el trámite ya no era elegible (salió de borrador/subsanación).</summary>
    public const string Descartada = "descartada";
}

/// <summary>
/// Marca de "este trámite se firmará más adelante" (HU #11196, tabla
/// <c>tramites.deferred_signature_marks</c>). Se crea cuando el representante legal de una empresa tiene
/// la identidad y la firma del baúl <b>ambas vencidas</b>: en vez de dejar al gestor bloqueado, el
/// trámite queda a la espera y se firma solo cuando esa persona valide su identidad.
///
/// <para>La llave del lote es (<see cref="TenantId"/> + documento del representante). Se usa el
/// DOCUMENTO y no un id del directorio de Admin porque en el caso principal de la HU #11195 el
/// representante no está registrado allí. <see cref="CompanyDocumentNumber"/> se guarda para la traza,
/// no para filtrar el lote.</para>
/// </summary>
public sealed class DeferredSignatureMark
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Parte cuya firma se difiere: <c>comprador</c> | <c>vendedor</c>.</summary>
    public string PartyRole { get; set; } = string.Empty;

    /// <summary>NIT de la empresa representada. PII (Ley 1581): no loguear.</summary>
    public string CompanyDocumentNumber { get; set; } = string.Empty;

    public string RepresentativeDocumentType { get; set; } = string.Empty;

    /// <summary>Documento del representante. PII (Ley 1581): no loguear.</summary>
    public string RepresentativeDocumentNumber { get; set; } = string.Empty;

    public string Estado { get; set; } = DeferredSignatureEstados.Pendiente;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Cuándo se aplicó la firma diferida (AC6). <c>null</c> mientras siga pendiente.</summary>
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>Validación de identidad con la que se firmó (AC6).</summary>
    public Guid? AppliedValidationId { get; set; }

    /// <summary>Por qué se descartó, cuando la validación llegó tarde para este trámite.</summary>
    public string? DiscardedReason { get; set; }

    /// <summary>La marca sigue esperando la validación.</summary>
    public bool EstaPendiente => Estado == DeferredSignatureEstados.Pendiente;

    /// <summary>Cierra la marca como aplicada, dejando la traza de con qué validación se firmó.</summary>
    public void Aplicar(Guid validationId, DateTimeOffset now)
    {
        Estado = DeferredSignatureEstados.Aplicada;
        AppliedValidationId = validationId;
        AppliedAt = now;
    }

    /// <summary>
    /// Cierra la marca sin firmar. El estado del trámite se revalida AL APLICAR, no al marcar: entre una
    /// cosa y la otra pueden pasar días y el trámite pudo radicarse.
    /// </summary>
    public void Descartar(string motivo, DateTimeOffset now)
    {
        Estado = DeferredSignatureEstados.Descartada;
        DiscardedReason = motivo;
        AppliedAt = now;
    }
}
