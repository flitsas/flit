namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>Resumen de trámite de cliente visible para OT admin (HU #10217).</summary>
public sealed class OtClientProcedure
{
    public Guid Id { get; init; }

    public Guid ClientTenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public string ProcedureTypeName { get; init; } = string.Empty;

    public string ClientTenantName { get; init; } = string.Empty;

    public string ReferenceNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Feature #10587 / HU #10785 — sub-estado interno de la ruta de placa, ortogonal al <see cref="Status"/>
    /// (que permanece en 'entregado'): <c>null</c> (sin ruta de placa), <c>preasignado</c> (esperando placa)
    /// o <c>asignado</c> (placa registrada). Gobierna las acciones del OT (Asignar/Revocar placa).
    /// </summary>
    public string? PlateFlowStatus { get; init; }

    /// <summary>
    /// HU #10804 (Feature #10587) — estado del SOAT del vehículo (field_value <c>soat_estado</c>) en la
    /// ruta de placa: <c>null</c>/<c>unknown</c>/<c>vencido</c> = sin evidencia; <c>vigente</c> = registrado.
    /// Gobierna (junto con <see cref="PlateFlowStatus"/>) si el OT puede ver Aprobar/Rechazar: solo con la
    /// placa <c>asignado</c> Y el SOAT <c>vigente</c>. El gate DURO de aprobación ya vive en el backend.
    /// </summary>
    public string? SoatEstado { get; init; }

    /// <summary>
    /// HU #10805 (Feature #10587) — dígito de preferencia de placa (field_value
    /// <c>plate_preferred_last_digit</c>, un carácter 0-9) indicado por el gestor al radicar sin placa.
    /// Es SOLO una guía para el OT al asignar: puede elegir una placa que termine en este dígito o
    /// cualquier otra. <c>null</c> si el gestor no indicó preferencia.
    /// </summary>
    public string? PlatePreferredLastDigit { get; init; }

    /// <summary>Checks opcionales del gestor (visibles en dashboard OT solo en Terminado).</summary>
    public bool SoatPagado { get; init; }

    /// <summary>Checks opcionales del gestor (visibles en dashboard OT solo en Terminado).</summary>
    public bool ImpuestoDepartamentalPagado { get; init; }

    public Guid? TransitOfficeId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    /// <summary>HU #10536 — trámite marcado como prioritario: se ordena con primacía en la bandeja del OT.</summary>
    public bool Prioritario { get; init; }

    /// <summary>Detalle (GET by id): actores del trámite. Vacío en listados.</summary>
    public IReadOnlyList<OtClientProcedureActor> Actors { get; init; } = [];

    /// <summary>Detalle (GET by id): placa del vehículo.</summary>
    public string? Placa { get; init; }

    /// <summary>Detalle (GET by id): VIN.</summary>
    public string? Vin { get; init; }

    /// <summary>Detalle (GET by id): marca.</summary>
    public string? Marca { get; init; }

    /// <summary>Detalle (GET by id): línea.</summary>
    public string? Linea { get; init; }

    /// <summary>Detalle (GET by id): modelo.</summary>
    public string? Modelo { get; init; }

    /// <summary>Detalle (GET by id): color.</summary>
    public string? Color { get; init; }

    /// <summary>Detalle (GET by id): clase.</summary>
    public string? Clase { get; init; }

    /// <summary>Detalle (GET by id): servicio.</summary>
    public string? Servicio { get; init; }

    /// <summary>Detalle (GET by id): combustible.</summary>
    public string? Combustible { get; init; }
}

/// <summary>Actor visible para el OT en el detalle del trámite de cliente.</summary>
public sealed class OtClientProcedureActor
{
    public string ActorType { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? PersonType { get; init; }
}
