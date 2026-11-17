namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Cabecera de <c>diagnostico.definiciones_fuentes_obligatorias</c> (ADR-0078). Catálogo global de FLIT: no implementa
/// <c>IEntidadDeEmpresa</c> y no lleva <c>tenant_id</c>. El identificador lo pone quien escribe (UUID v5 en la semilla,
/// UUID v7 si el PUT crea la cabecera). <see cref="Xmin"/> es el token de concurrencia. Cero líneas de esta cabecera es
/// la lista vacía explícita.
/// </summary>
public sealed class DefinicionFuentesObligatorias
{
    /// <summary>UUID de la aplicación (<c>ValueGeneratedNever</c>). Sin <c>DEFAULT uuidv7()</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>Familia del trámite. Nula cuando la definición es de un tipo.</summary>
    public string? Familia { get; init; }

    /// <summary>Código del tipo de trámite. Nulo cuando la definición es de una familia.</summary>
    public string? TipoTramiteCodigo { get; init; }

    /// <summary>Instante del último guardado (<c>timestamptz</c>). En la semilla es el instante fijo del ADR.</summary>
    public DateTimeOffset ActualizadoEn { get; set; }

    /// <summary>UUID del claim <c>sub</c>. Nulo en la semilla. Sin nombre, correo ni documento.</summary>
    public Guid? ActualizadoPor { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>).</summary>
    public uint Xmin { get; set; }
}
