namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>Resumen de trámite de cliente visible para OT admin (HU #10217).</summary>
public sealed record OtClientProcedure
{
    public Guid Id { get; init; }

    public Guid ClientTenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public string ProcedureTypeName { get; init; } = string.Empty;

    public string ClientTenantName { get; init; } = string.Empty;

    public string ReferenceNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Familia del tipo de trámite (<c>MATRICULAS</c> | <c>TRASPASO</c> | <c>OTROS</c>). La necesita
    /// el modal de rechazo para ofrecer solo las causales del proceso correcto: «manifiesto de
    /// aduana» no aplica a un traspaso ni «escritura del vendedor» a una matrícula inicial.
    /// <para>Se llamaba <c>ModalidadEntrada</c> y su documentación prometía los dos literales del
    /// vocabulario que ADR-0050 eliminó; el dato que transporta es <c>procedure_types.family</c>
    /// desde que se retiró la columna.</para>
    /// </summary>
    public string Familia { get; init; } = string.Empty;

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

    /// <summary>Placa denormalizada (también en listado de bandeja).</summary>
    public string? Placa { get; init; }

    /// <summary>VIN denormalizado (también en listado de bandeja).</summary>
    public string? Vin { get; init; }

    /// <summary>Nombre del propietario/vendedor (actor vendedor); null en matrícula inicial.</summary>
    public string? VendedorNombre { get; init; }

    /// <summary>Nombre del comprador.</summary>
    public string? CompradorNombre { get; init; }

    /// <summary>Nombre visible del gestor que radicó (created_by_user → DisplayName).</summary>
    public string? GestorNombre { get; init; }

    /// <summary>Detalle (GET by id): marca.</summary>
    public string? Marca { get; init; }

    /// <summary>Detalle (GET by id): línea.</summary>
    public string? Linea { get; init; }

    /// <summary>Detalle (GET by id): modelo.</summary>
    public string? Modelo { get; init; }

    /// <summary>Detalle (GET by id): color EFECTIVO (el nuevo si hay transformación declarada).</summary>
    public string? Color { get; init; }

    /// <summary>Detalle (GET by id): clase.</summary>
    public string? Clase { get; init; }

    /// <summary>Detalle (GET by id): servicio.</summary>
    public string? Servicio { get; init; }

    /// <summary>Detalle (GET by id): combustible EFECTIVO. Ver <see cref="Color"/>.</summary>
    public string? Combustible { get; init; }

    /// <summary>Detalle (GET by id): carrocería EFECTIVA. Ver <see cref="Color"/>.</summary>
    public string? Carroceria { get; init; }

    /// <summary>Detalle (GET by id): cilindraje.</summary>
    public string? Cilindraje { get; init; }

    /// <summary>Detalle (GET by id): capacidad de pasajeros.</summary>
    public string? Capacidad { get; init; }

    /// <summary>Detalle (GET by id): número de ejes.</summary>
    public string? Ejes { get; init; }

    /// <summary>Detalle (GET by id): estado del vehículo según el RUNT.</summary>
    public string? EstadoVehiculo { get; init; }

    /// <summary>Detalle (GET by id): número de motor.</summary>
    public string? NumeroMotor { get; init; }

    /// <summary>Detalle (GET by id): número de chasis.</summary>
    public string? NumeroChasis { get; init; }

    /// <summary>Detalle (GET by id): número de serie.</summary>
    public string? NumeroSerie { get; init; }

    /// <summary>
    /// HU #11929 — snapshot del RUNT de los tres atributos transformables. El OT necesita las DOS
    /// caras: sin el valor del RUNT, un color nuevo declarado por el gestor se lee como si fuera el
    /// dato oficial del vehículo. <c>null</c> cuando el trámite es anterior a la captura del snapshot.
    /// </summary>
    public OtClientProcedureVehicleSnapshot? RuntSnapshot { get; init; }

    /// <summary>
    /// HU #11929 — banderas <c>cambio_*</c> con las que el gestor DECLARA la transformación. Son
    /// independientes del diff RUNT↔efectivo: un trámite puede declarar el cambio antes de capturar
    /// el valor nuevo, y un tipo de trámite de la familia OTROS transforma por definición.
    /// </summary>
    public OtClientProcedureTransformationFlags TransformacionesDeclaradas { get; init; } = new();

    /// <summary>Detalle (GET by id): datos comerciales del trámite; <c>null</c> si no se capturaron.</summary>
    public OtClientProcedureCommercial? Comercial { get; init; }

    /// <summary>Detalle (GET by id): decisión de prenda del trámite; <c>null</c> si no hay decisión.</summary>
    public OtClientProcedurePrenda? Prenda { get; init; }
}

/// <summary>
/// Valores con los que el vehículo figura en el RUNT para los atributos que un trámite puede
/// transformar. Se contrastan contra los efectivos del <see cref="OtClientProcedure"/>.
/// </summary>
public sealed class OtClientProcedureVehicleSnapshot
{
    public string? Color { get; init; }
    public string? Combustible { get; init; }
    public string? Carroceria { get; init; }
}

/// <summary>Banderas <c>cambio_color</c> / <c>cambio_combustible</c> / <c>cambio_carroceria</c>.</summary>
public sealed class OtClientProcedureTransformationFlags
{
    public bool Color { get; init; }
    public bool Combustible { get; init; }
    public bool Carroceria { get; init; }
}

/// <summary>Datos comerciales del trámite tal como los ve el OT (solo lectura).</summary>
public sealed class OtClientProcedureCommercial
{
    public decimal? ValorVenta { get; init; }
    public string? Causal { get; init; }
    public decimal? TasaImpuesto { get; init; }
    public decimal? Derechos { get; init; }
    public string? MetodoPago { get; init; }
}

/// <summary>Decisión de prenda del trámite tal como la ve el OT (solo lectura).</summary>
public sealed class OtClientProcedurePrenda
{
    public string Decision { get; init; } = string.Empty;
    public string Estado { get; init; } = string.Empty;
    public string? AcreedorNombre { get; init; }
    public string? AcreedorDocumento { get; init; }
    public string? LevantamientoEntidad { get; init; }
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
