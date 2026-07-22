using Flit.Admin.Domain.Companies.ProcedureGrants;

namespace Flit.Admin.Application.Companies.ProcedureGrants;

/// <summary>Cuerpo del POST de alta de grant: <c>{ procedureTypeId }</c>.</summary>
public sealed record AddCompanyProcedureGrantRequest(Guid? ProcedureTypeId);

/// <summary>Comando para habilitar un tipo de trámite a una compañía.</summary>
public sealed class AddCompanyProcedureGrantCommand
{
    public required Guid TenantId { get; init; }

    public required Guid ProcedureTypeId { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que habilita. Opcional.</summary>
    public Guid? CreatedBy { get; init; }
}

/// <summary>Error de validación de un grant de tipo de trámite (campo + mensaje + valor).</summary>
public sealed record CompanyProcedureGrantValidationError(string Field, string Message, string? Value);

/// <summary>
/// Resultado del alta de un grant. <see cref="IsValid"/> falso ⇒ 422 (id inválido). Con
/// <see cref="IsValid"/> verdadero, <see cref="Added"/> distingue alta nueva de idempotencia.
/// </summary>
public sealed class AddCompanyProcedureGrantResult
{
    private AddCompanyProcedureGrantResult(
        bool isValid, bool added, IReadOnlyList<CompanyProcedureGrantValidationError> errors)
    {
        IsValid = isValid;
        Added = added;
        Errors = errors;
    }

    public bool IsValid { get; }

    public bool Added { get; }

    public IReadOnlyList<CompanyProcedureGrantValidationError> Errors { get; }

    public static AddCompanyProcedureGrantResult Success(bool added) => new(true, added, []);

    public static AddCompanyProcedureGrantResult Invalid(IReadOnlyList<CompanyProcedureGrantValidationError> errors) =>
        new(false, false, errors);
}

/// <summary>
/// Caso de uso del alta de un grant de tipo de trámite por compañía (FEATURE-08). Valida que el id no
/// sea vacío (el catálogo de tipos publicados lo garantiza en la UI) y delega el alta + audit al repo,
/// que aplica el contexto RLS del tenant destino. Idempotente.
/// </summary>
public sealed class AddCompanyProcedureGrantHandler
{
    private readonly ICompanyProcedureGrantRepository _repository;

    public AddCompanyProcedureGrantHandler(ICompanyProcedureGrantRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<AddCompanyProcedureGrantResult> HandleAsync(
        AddCompanyProcedureGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ProcedureTypeId == Guid.Empty)
        {
            return AddCompanyProcedureGrantResult.Invalid(
            [
                new CompanyProcedureGrantValidationError(
                    "procedureTypeId", "El tipo de trámite es obligatorio.", null),
            ]);
        }

        var added = await _repository
            .AddGrantAsync(command.TenantId, command.ProcedureTypeId, command.CreatedBy, cancellationToken)
            .ConfigureAwait(false);

        return AddCompanyProcedureGrantResult.Success(added);
    }
}
