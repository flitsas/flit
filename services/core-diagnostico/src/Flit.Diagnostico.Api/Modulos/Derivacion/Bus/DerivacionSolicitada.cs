namespace Flit.Diagnostico.Api.Modulos.Derivacion.Bus;

/// <summary>
/// Evento <c>diagnostico.derivacion.solicitada</c> v1 (Punto 4, incremento 3, ADR-0084, diseño §5.3 punto 2 y §8): un ítem del masivo
/// recibió crédito de su compañía y espera que el consumidor de <see cref="Cola"/> llame a Trámites. SOLO ids (operación, ítem y
/// vehículo): ni placa, ni VIN, ni documento; la empresa viaja en el sobre (<c>tenantId</c>). Va a la PROPIA cola de Diagnóstico, por
/// eso es un evento (<c>IPlatformOutbox.Enqueue</c>) en <c>flit.diagnostico</c> y no un trabajo. Contrato:
/// <c>contracts/asyncapi/diagnostico-events.v1.yaml</c> y el fixture <c>contracts/fixtures/bus/diagnostico.derivacion.solicitada.v1.json</c>.
/// </summary>
/// <example>
/// <code>
/// outbox.Enqueue(DerivacionSolicitada.Tipo, DerivacionSolicitada.VersionContrato, empresa, new DerivacionSolicitada(op, item, vehiculo));
/// </code>
/// </example>
public sealed record DerivacionSolicitada(Guid OperacionId, Guid ItemId, Guid VehiculoId)
{
    public const string Tipo = "diagnostico.derivacion.solicitada";

    public const int VersionContrato = 1;

    /// <summary>Cola propia del consumidor (<c>&lt;consumidor&gt;.&lt;propósito&gt;</c>), atada a <c>flit.diagnostico</c>.</summary>
    public const string Cola = "diagnostico.derivaciones";

    /// <summary>Único productor aceptado: el propio Diagnóstico (<c>user_id</c> validado por el broker).</summary>
    public const string Productor = "diagnostico";
}
