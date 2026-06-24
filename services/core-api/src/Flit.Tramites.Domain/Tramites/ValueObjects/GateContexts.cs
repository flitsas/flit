namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Datos mínimos de una parte para evaluar completitud en los gates.</summary>
public sealed record ParteDatos(string? Nombre, string? Documento, string? Email);

/// <summary>Estado de preflight (SOAT/RTM/impuesto) consolidado.</summary>
/// <param name="Overall">Semáforo global: "green" | "amber" | "red".</param>
/// <param name="ImpuestoVehicularUnknown">El check de impuesto vehicular quedó en "unknown".</param>
public sealed record PreflightSnapshot(string? Overall, bool ImpuestoVehicularUnknown);

/// <summary>Snapshot de una consulta RUNT contra un documento concreto.</summary>
/// <param name="Consultado">Si la consulta se realizó.</param>
/// <param name="Documento">Documento contra el que se consultó (debe coincidir con la parte).</param>
public sealed record RuntSnapshot(bool Consultado, string? Documento);

/// <summary>Snapshot de la consulta SIMIT del comprador.</summary>
/// <param name="Consultado">Si la consulta se realizó.</param>
/// <param name="Documento">Documento consultado.</param>
/// <param name="TotalComparendos">Cantidad de comparendos pendientes.</param>
public sealed record SimitSnapshot(bool Consultado, string? Documento, int TotalComparendos);

/// <summary>Aprobación biométrica por parte.</summary>
public sealed record BiometriaSnapshot(bool Vendedor, bool Comprador);

/// <summary>
/// Contexto explícito para los gates del wizard de TRASPASO (6 pasos).
/// Reemplaza el "JSONB con claves _" del antipatrón de Johan por tipos explícitos.
/// </summary>
public sealed record TraspasoGateContext
{
    public bool TramiteRadicado { get; init; }

    /// <summary>El vehículo ya fue consultado por placa (identificador persistido). Paridad con
    /// <see cref="MatriculaGateContext.VehiculoConsultado"/>: sin consulta, el paso 1 no se completa.</summary>
    public bool VehiculoConsultado { get; init; }

    public PreflightSnapshot? Preflight { get; init; }
    public bool PazSalvoImpuestoVerificado { get; init; }

    public ParteDatos? Vendedor { get; init; }
    public RuntSnapshot? RuntVendedor { get; init; }

    public ParteDatos? Comprador { get; init; }
    public RuntSnapshot? RuntComprador { get; init; }
    public SimitSnapshot? SimitComprador { get; init; }

    public decimal ValorVenta { get; init; }

    public BiometriaSnapshot? Biometria { get; init; }

    /// <summary>Todos los documentos obligatorios del checklist están cargados (gating estricto, sin override).</summary>
    public bool DocumentosObligatoriosCompletos { get; init; }

    /// <summary>Override del gestor que omite bloqueos no críticos (paridad <c>forzarContinuar</c>).</summary>
    public bool ForzarContinuar { get; init; }
}

/// <summary>
/// Contexto explícito para los gates del wizard de MATRÍCULA INICIAL (5 pasos, 1 actor).
/// </summary>
public sealed record MatriculaGateContext
{
    public bool VehiculoConsultado { get; init; }
    public PreflightSnapshot? Preflight { get; init; }

    public ParteDatos? Comprador { get; init; }
    public RuntSnapshot? RuntComprador { get; init; }

    public bool IdentidadAprobada { get; init; }

    /// <summary>Todos los documentos obligatorios del checklist están cargados (gating estricto, sin override).</summary>
    public bool DocumentosObligatoriosCompletos { get; init; }

    public bool ForzarContinuar { get; init; }
}
