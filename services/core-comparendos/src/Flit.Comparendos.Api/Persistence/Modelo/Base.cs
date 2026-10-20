namespace Flit.Comparendos.Api.Persistence.Modelo;

/// <summary>Columnas de auditoría estándar (especificación §0.3). <c>CreadoPor</c> null = el sistema.</summary>
public abstract class Auditada
{
    public DateTimeOffset CreadoEn { get; set; }
    public Guid? CreadoPor { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }
    public Guid? ActualizadoPor { get; set; }
}

/// <summary>Bytes de un archivo cuando no hay file-manager (local y pruebas). En los servidores no se usa.</summary>
public sealed class ArchivoLocal
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Contenido { get; set; } = [];
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>
/// Referencia a un archivo guardado (§3.13): el registro del file-manager (o de <see cref="ArchivoLocal"/>) con su
/// huella. Nunca se borra mientras algo lo referencie. <c>TenantId</c> null = archivo global (plantillas).
/// </summary>
public sealed class Archivo : Auditada
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public string Dominio { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>Auditoría de configuración (no de comparendos): quién cambió qué y cómo estaba antes (§3.30).</summary>
public sealed class AuditoriaConfiguracion
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Entidad { get; set; } = string.Empty;
    public string EntidadId { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;

    /// <summary>JSON <c>[{campo, anterior, nuevo}]</c>.</summary>
    public string Cambios { get; set; } = "[]";
    public Guid UsuarioId { get; set; }
    public string UsuarioNombre { get; set; } = string.Empty;
    public DateTimeOffset RegistradoEn { get; set; }
}

/// <summary>Día del calendario maestro (§3.31). Festivos sembrados por cálculo; el SuperAdmin puede corregirlos.</summary>
public sealed class CalendarioDia
{
    public DateOnly Fecha { get; set; }
    public bool EsHabil { get; set; }
    public bool EsFinDeSemana { get; set; }
    public bool EsFestivo { get; set; }
    public string? DescripcionFestivo { get; set; }
    public string Origen { get; set; } = "SEMILLA";
}

/// <summary>Código del Código Nacional de Tránsito con su descripción (§3.29).</summary>
public sealed class InfraccionCatalogo
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Vigente { get; set; } = true;
}
