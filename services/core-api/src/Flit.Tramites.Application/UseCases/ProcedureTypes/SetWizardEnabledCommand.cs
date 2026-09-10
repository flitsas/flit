using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

/// <summary>
/// ADR-0050 — enciende o apaga la barrera de operación de un tipo (<c>wizard_enabled</c>): si el
/// gestor puede elegirlo al crear un trámite.
///
/// <para>Es una palanca OPERATIVA, deliberadamente separada de la publicación y de
/// <see cref="UpdateProcedureTypeHandler"/>. Publicar congela la DEFINICIÓN del tipo —por eso el
/// update rechaza los publicados—, y los 21 del catálogo están publicados: por ahí la barrera nunca
/// se habría podido mover. Habilitar no cambia la definición; dice que ese recorrido ya se puede
/// recorrer.</para>
///
/// <para>Encender EXIGE que el tipo esté listo (publicado, activo, con pasos y sin errores de
/// validación). Es el mecanismo que impide prometer flujos inexistentes: un tipo a medio parametrizar
/// se puede publicar en el catálogo, pero no ofrecerse en el asistente. Apagar NO exige nada: una
/// palanca de seguridad que solo se puede accionar cuando todo está bien no sirve de palanca.</para>
/// </summary>
public sealed class SetWizardEnabledHandler(
    IProcedureTypeRepository repository,
    IProcedureTypeValidator validator)
{
    /// <summary>Motivo por el que un tipo no puede habilitarse todavía.</summary>
    public const string NotReady = "not_ready";

    public async Task<(ProcedureTypeSummary? Result, string? Error, object? Detail)> HandleAsync(
        Guid id,
        bool enabled,
        CancellationToken ct = default)
    {
        var entity = await repository.GetByIdWithDetailsAsync(id, ct);
        if (entity is null)
            return (null, "not_found", null);

        if (entity.WizardEnabled == enabled)
        {
            // Idempotente: pedir lo que ya es no es un error. Quien automatice el alta de tipos no
            // debería tener que consultar antes de habilitar.
            return (CreateProcedureTypeHandler.ToSummary(entity), null, null);
        }

        if (enabled)
        {
            var impedimentos = Impedimentos(entity, validator);
            if (impedimentos.Count > 0)
                return (null, NotReady, new { motivos = impedimentos });
        }

        entity.WizardEnabled = enabled;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return (CreateProcedureTypeHandler.ToSummary(entity), null, null);
    }

    /// <summary>
    /// Lo que falta para poder operar el tipo, en lenguaje del configurador. Se devuelven TODOS los
    /// impedimentos y no el primero: quien está dando de alta un tipo quiere la lista de lo que le
    /// falta, no descubrirla de uno en uno.
    /// </summary>
    private static List<string> Impedimentos(
        Domain.Entities.ProcedureType entity,
        IProcedureTypeValidator validator)
    {
        var motivos = new List<string>();

        if (entity.PublicationStatus != PublicationStatus.Published)
            motivos.Add("El tipo no está publicado.");

        if (!entity.IsActive)
            motivos.Add("El tipo está inactivo.");

        // Sin pasos no hay recorrido: el asistente devolvería un estado vacío y bloqueado
        // (`tipo_sin_parametrizar`), que es peor que no ofrecer el trámite.
        if (entity.Steps.Count == 0)
            motivos.Add("El tipo no tiene pasos parametrizados.");
        else if (entity.Steps.All(s => s.Sections.Count == 0))
            motivos.Add("Ningún paso del tipo declara secciones.");

        var validation = validator.Validate(entity);
        if (!validation.IsValid)
            motivos.AddRange(validation.Errors.Select(e => e.Message));

        return motivos;
    }
}
