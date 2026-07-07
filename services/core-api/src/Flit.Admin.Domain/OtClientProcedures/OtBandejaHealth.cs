namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>
/// Diagnóstico de la bandeja del OT (HU #10540 / R09 — cierre de circuito). Cuenta los trámites
/// en estado <c>entregado</c> dirigidos al organismo de tránsito del OT, discriminando los que
/// tienen grant vigente con la empresa cliente (visibles en la bandeja) de los que NO lo tienen
/// (entregados "huérfanos": invisibles hasta que se habilite el grant OT↔empresa). Permite al
/// operador detectar por qué un trámite real entregado no aparece, sin depender de datos sembrados.
/// </summary>
public sealed record OtBandejaHealth(
    Guid TransitOfficeId,
    int DeliveredTotal,
    int DeliveredWithGrant,
    int DeliveredWithoutGrant);
