using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Saneamiento;

/// <summary>
/// Evento del circuito de saneamiento (<c>diagnostico.saneamiento_historial</c>, HU #14576, ADR-0087, clase (b)): solo inserción,
/// sin token. <see cref="Id"/> es el <c>referencia_id</c> de los avisos <c>SANEAMIENTO_*</c> (C18, SP-22).
/// </summary>
public sealed class EventoSaneamiento : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid SaneamientoId { get; init; }

    public TipoEventoSaneamiento Evento { get; init; }

    public EstadoSaneamiento? EstadoAnterior { get; init; }

    public EstadoSaneamiento? EstadoNuevo { get; init; }

    /// <summary>Renglón del evento; sin FK (ADR-0087).</summary>
    public Guid? RenglonId { get; init; }

    public FuenteSaneamiento? Fuente { get; init; }

    public Guid UsuarioId { get; init; }

    public bool EsFlit { get; init; }

    public string? Motivo { get; init; }

    public DateTimeOffset OcurridoEn { get; init; }
}
