using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Bug #13194 (P4) — deja un trámite de prueba FIRMADO: por cada parte, un actor persona natural con
/// documento y una validación de identidad propia APROBADA y VIGENTE del MISMO documento. Desde el
/// gate de firma (toda llegada a preparado/preasignacion/entregado) y el resolutor fail-closed (sin actor
/// con documento no hay aprobación), los tests que ejercitan OTROS gates necesitan partir de aquí.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>FirmaFixture.Firmar(instance, "comprador", "vendedor");</c>
/// Datos ficticios (sin PII real): documentos 9000000001.. y correos @example.test.
/// </remarks>
internal static class FirmaFixture
{
    public static ProcedureInstance Firmar(ProcedureInstance instance, params string[] partes)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var ordinal = 0;
        foreach (var parte in partes.Length > 0 ? partes : [BiometricRules.ParteComprador])
        {
            ordinal++;
            var documento = $"900000000{ordinal}";
            var actor = instance.Actors.FirstOrDefault(a =>
                string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
            if (actor is null)
            {
                actor = new ProcedureInstanceActor
                {
                    Id = Guid.NewGuid(),
                    TenantId = instance.TenantId,
                    ProcedureInstanceId = instance.Id,
                    ActorType = parte,
                    PersonType = "natural",
                    DocumentType = "CC",
                    DocumentNumber = documento,
                    FullName = $"Persona {parte}",
                    Email = $"{parte}@example.test",
                };
                instance.Actors.Add(actor);
            }

            instance.BiometricValidations.Add(Aprobada(instance, parte, actor.DocumentType, actor.DocumentNumber));
        }

        return instance;
    }

    /// <summary>Validación propia aprobada hoy (vigente 30 días) para el documento indicado.</summary>
    public static ProcedureInstanceBiometricValidation Aprobada(
        ProcedureInstance instance, string parte, string? tipoDoc, string? documento) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            Name = $"Persona {parte}",
            DocumentType = tipoDoc ?? "CC",
            DocumentNumber = documento ?? string.Empty,
            Email = $"{parte}@example.test",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ValidatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
