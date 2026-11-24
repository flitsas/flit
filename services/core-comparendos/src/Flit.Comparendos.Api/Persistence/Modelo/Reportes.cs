namespace Flit.Comparendos.Api.Persistence.Modelo;

/// <summary>
/// Reportes 2.0 — consulta personalizada guardada (motor compartido Flit.Queries.Core). Las de fábrica (V1:
/// «caducan pronto», «seguimiento de radicados»…) no tienen empresa ni usuario y no se editan ni se borran.
/// </summary>
public sealed class ConsultaGuardada
{
    public Guid Id { get; set; }

    /// <summary>Null solo en las de fábrica.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Quien la guardó; null solo en las de fábrica.</summary>
    public Guid? UsuarioId { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool DeFabrica { get; set; }

    /// <summary><c>QueryDefinition</c> normalizada, en JSON.</summary>
    public string Definicion { get; set; } = "{}";

    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }
}

/// <summary>
/// Reportes 2.0 — envío periódico por correo de una consulta guardada, con el Excel adjunto. Lo ejecuta el trabajo
/// de reportes automáticos; el resultado se escribe en términos de negocio porque lo lee la empresa.
/// </summary>
public sealed class ConsultaProgramada
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConsultaId { get; set; }
    public Guid CreadoPor { get; set; }

    /// <summary>«semanal» o «mensual».</summary>
    public string Periodicidad { get; set; } = "semanal";

    /// <summary>1 = lunes … 7 = domingo; solo semanal.</summary>
    public int? DiaSemana { get; set; }

    /// <summary>1 … 28; solo mensual (hasta 28 para que exista en todos los meses).</summary>
    public int? DiaMes { get; set; }

    /// <summary>Hora de Colombia.</summary>
    public TimeOnly Hora { get; set; }

    public List<string> Destinatarios { get; set; } = [];
    public bool Activa { get; set; } = true;
    public DateTimeOffset? UltimaEjecucion { get; set; }
    public DateTimeOffset? ProximaEjecucion { get; set; }
    public string? UltimoResultado { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
