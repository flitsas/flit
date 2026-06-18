using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ValueObjects;

namespace Flit.Tramites.Domain.Services;

public interface IProcedureTypeValidator
{
    ValidationResult Validate(ProcedureType procedureType);
}
