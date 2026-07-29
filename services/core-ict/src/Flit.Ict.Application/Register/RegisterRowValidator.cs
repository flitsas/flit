using FluentValidation;

namespace Flit.Ict.Application.Register;

/// <summary>Validación estructural de una fila del lote antes de persistir el pre-trámite.</summary>
public sealed class RegisterRowValidator : AbstractValidator<RegisterRowInput>
{
    public RegisterRowValidator()
    {
        RuleFor(r => r.TransactionType).InclusiveBetween(1, 16)
            .WithMessage("transaction_type debe estar entre 1 y 16");
        RuleFor(r => r.TransactionOperation).GreaterThan(0)
            .WithMessage("transaction_operation es obligatorio");
        RuleFor(r => r.ManagerMail).NotEmpty()
            .WithMessage("manager_mail es obligatorio");
        RuleFor(r => r.Seller).NotEmpty()
            .WithMessage("seller es obligatorio");
        RuleFor(r => r.Vin).NotEmpty()
            .When(r => r.TransactionType is 1 or 2)
            .WithMessage("vin es obligatorio para matrículas (tipos 1 y 2)");
        RuleFor(r => r.Plate).NotEmpty()
            .When(r => r.TransactionType is not (1 or 2))
            .WithMessage("plate es obligatorio para este tipo de trámite");
    }
}
