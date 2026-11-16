using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Derivacion;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Un vehículo de una operación de derivación (<c>diagnostico.derivaciones</c>, Punto 4, diseño §4): su estado, el motivo si quedó
/// OMITIDO o FALLIDO, los intentos y, si se creó, el trámite. Un solo ítem vivo (PENDIENTE, ENVIADO o CREADO) por vehículo
/// (<c>ux_derivaciones_vehiculo_vivo</c>, R-16): el doble clic no crea dos. Solo <see cref="CierreItemDerivacion"/> lo cierra.
/// <see cref="DisponibleDesde"/>, <see cref="Republicaciones"/> y el estado PENDIENTE son del masivo (incremento 3): un ítem PENDIENTE
/// espera crédito de su compañía (<see cref="DespachadorCredito"/>).
/// </summary>
public sealed class ItemDerivacion : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid OperacionId { get; init; }

    public Guid VehiculoId { get; init; }

    /// <summary>Orden de liberación dentro de la operación (el de la selección).</summary>
    public int Orden { get; init; }

    public EstadoItemDerivacion Estado { get; set; }

    /// <summary>Motivo de OMITIDO o FALLIDO (<see cref="MotivoDerivacion"/>); null en los demás.</summary>
    public string? MotivoCodigo { get; set; }

    /// <summary>Llamadas al rpc hechas por este ítem.</summary>
    public short Intentos { get; set; }

    /// <summary>Espera del reintento programado (masivo); null si no espera.</summary>
    public DateTimeOffset? DisponibleDesde { get; set; }

    /// <summary>Instante en que salió hacia Trámites.</summary>
    public DateTimeOffset? EnviadoEn { get; set; }

    public Guid? TramiteId { get; set; }

    public string? TramiteNumero { get; set; }

    public DateTimeOffset ActualizadoEn { get; set; }

    /// <summary>Veces que el reconciliador re-publicó el ítem colgado (0 o 1; a la segunda queda FALLIDO).</summary>
    public short Republicaciones { get; set; }

    /// <summary>
    /// Usuario que reabrió el ítem con «reintentar fallidas» (claim <c>sub</c>); null si nunca se reintentó. Cuando existe, es el autor
    /// del borrador en lugar del que lanzó la operación (revisión de seguridad 2026-11-11).
    /// </summary>
    public Guid? ReintentadoPor { get; set; }

    /// <summary>Autor del borrador de este ítem: quien lo reintentó o, si nadie, quien lanzó <paramref name="operacion"/>.</summary>
    public Guid Autor(OperacionDerivacion operacion)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        return ReintentadoPor ?? operacion.UsuarioId;
    }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>).</summary>
    public uint Xmin { get; set; }

    /// <summary>Ítem ENVIADO de la operación (el individual sale sin cola): en vuelo hacia Trámites desde <paramref name="ahora"/>.</summary>
    public static ItemDerivacion Enviado(OperacionDerivacion operacion, Guid vehiculo, int orden, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        return new ItemDerivacion
        {
            TenantId = operacion.TenantId,
            OperacionId = operacion.Id,
            VehiculoId = vehiculo,
            Orden = orden,
            Estado = EstadoItemDerivacion.Enviado,
            EnviadoEn = ahora,
            ActualizadoEn = ahora,
        };
    }

    /// <summary>Ítem PENDIENTE del masivo: espera el crédito de su compañía.</summary>
    public static ItemDerivacion Pendiente(OperacionDerivacion operacion, Guid vehiculo, int orden, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        return new ItemDerivacion
        {
            TenantId = operacion.TenantId,
            OperacionId = operacion.Id,
            VehiculoId = vehiculo,
            Orden = orden,
            Estado = EstadoItemDerivacion.Pendiente,
            ActualizadoEn = ahora,
        };
    }

    /// <summary>Ítem del masivo que no es elegible al encolar: nace OMITIDO con su motivo (no ocupa el índice de vivos).</summary>
    public static ItemDerivacion Omitido(OperacionDerivacion operacion, Guid vehiculo, int orden, string motivo, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        return new ItemDerivacion
        {
            TenantId = operacion.TenantId,
            OperacionId = operacion.Id,
            VehiculoId = vehiculo,
            Orden = orden,
            Estado = EstadoItemDerivacion.Omitido,
            MotivoCodigo = motivo,
            ActualizadoEn = ahora,
        };
    }

    /// <summary>
    /// Reabre un ítem FALLIDO a PENDIENTE (reintentar fallidas): mismo ítem, sin motivo, intentos ni espera, y con
    /// <paramref name="usuario"/> como <see cref="ReintentadoPor"/> (el autor de su borrador).
    /// </summary>
    public void Reabrir(Guid usuario, DateTimeOffset ahora)
    {
        if (Estado != EstadoItemDerivacion.Fallido)
            throw new InvalidOperationException($"Solo se reabre un ítem FALLIDO; {Id} está {Estado}.");
        if (usuario == Guid.Empty)
            throw new ArgumentException("El reintento exige el usuario que lo pide.", nameof(usuario));
        ReintentadoPor = usuario;
        Estado = EstadoItemDerivacion.Pendiente;
        MotivoCodigo = null;
        Intentos = 0;
        DisponibleDesde = null;
        EnviadoEn = null;
        Republicaciones = 0;
        ActualizadoEn = ahora;
    }
}
