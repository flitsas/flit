using FluentValidation;

namespace Flit.Ict.Application.Register;

/// <summary>
/// Validación de ENTRADA de una fila del lote (contrato Excel "2 - Transacction"). Es el equivalente
/// fiel del DTO class-validator de v1 (BackApiExternalTransact <c>ValidationTypeData</c> +
/// <c>ValidateActors</c>): decide, campo por campo y por <c>transaction_type</c>, si el body es válido.
/// Una fila que falla se rechaza en <see cref="RegisterIctBatchHandler"/> con Status=2 (no se persiste).
/// NO es la validación de negocio (esa la hace <c>sp_processor_validation_business</c> DESPUÉS, marcando
/// CON NOVEDADES). Tipos: 1=Matrícula Inicial, 2=Matrícula Leasing, 3=Traspaso Bilateral, 4=Traspaso
/// Unilateral, 5-16=otros. Presencia + longitud van en reglas separadas para que el mensaje (en español,
/// con el nombre del campo del contrato) también salga cuando el valor llega nulo.
/// </summary>
public sealed class RegisterRowValidator : AbstractValidator<RegisterRowInput>
{
    /// <summary>Formato de fecha del contrato: dd-mm-yyyy (mismo patrón que v1).</summary>
    private const string DatePattern =
        @"^(0[1-9]|[12][0-9]|3[01])-(0[1-9]|1[0-2])-(19\d{2}|2\d{3}|[3-9]\d{3})$";

    private const string Nit = "NIT";

    public RegisterRowValidator()
    {
        // ===== Campos de integración / gestor (obligatorios en todo tipo) =====
        RuleFor(r => r.TransactionType).InclusiveBetween(1, 16)
            .WithMessage("transaction_type debe estar entre 1 y 16");
        RuleFor(r => r.TransactionOperation).GreaterThan(0)
            .WithMessage("transaction_operation es obligatorio");

        RuleFor(r => r.CompanyManagerDocument).NotEmpty()
            .WithMessage("company_manager_document es obligatorio");
        // El contrato dice String(12), pero v2 acepta el NIT FORMATEADO (puntos/espacios/dígito de
        // verificación, p.ej. "901.698.038-3" = 13); el handler lo normaliza y lo compara contra el NIT
        // del token. Por eso el tope se relaja a 20 en vez de 12 (no romper esa tolerancia de formato).
        RuleFor(r => r.CompanyManagerDocument).MaximumLength(20)
            .When(r => !string.IsNullOrEmpty(r.CompanyManagerDocument))
            .WithMessage("company_manager_document debe tener máximo 20 caracteres");

        RuleFor(r => r.ManagerUser).NotEmpty().WithMessage("manager_user es obligatorio");
        RuleFor(r => r.ManagerUser).MaximumLength(50)
            .When(r => !string.IsNullOrEmpty(r.ManagerUser))
            .WithMessage("manager_user debe tener máximo 50 caracteres");

        RuleFor(r => r.ManagerMail).NotEmpty().WithMessage("manager_mail es obligatorio");
        RuleFor(r => r.ManagerMail).MaximumLength(100)
            .When(r => !string.IsNullOrEmpty(r.ManagerMail))
            .WithMessage("manager_mail debe tener máximo 100 caracteres");

        RuleFor(r => r.DeliveryAddress).NotEmpty().WithMessage("delivery_address es obligatorio");
        RuleFor(r => r.DeliveryAddress).MaximumLength(150)
            .When(r => !string.IsNullOrEmpty(r.DeliveryAddress))
            .WithMessage("delivery_address debe tener máximo 150 caracteres");

        RuleFor(r => r.ManagerIdTransaction).MaximumLength(20)
            .When(r => !string.IsNullOrEmpty(r.ManagerIdTransaction))
            .WithMessage("manager_id_transaction debe tener máximo 20 caracteres");
        RuleFor(r => r.TransactionFlit).MaximumLength(20)
            .When(r => !string.IsNullOrEmpty(r.TransactionFlit))
            .WithMessage("transaction_flit debe tener máximo 20 caracteres");
        RuleFor(r => r.UrlWebHook).MaximumLength(100)
            .When(r => r.UrlWebHook is not null)
            .WithMessage("url_web_hook debe tener máximo 100 caracteres");
        RuleFor(r => r.ObservationWhenPaused).MaximumLength(250)
            .When(r => r.ObservationWhenPaused is not null)
            .WithMessage("observation_when_paused debe tener máximo 250 caracteres");

        // ===== Vehículo =====
        // traffic_secretary_code: obligatorio en Matrícula (1, 2), 6-10 caracteres.
        RuleFor(r => r.TrafficSecretaryCode).NotEmpty()
            .When(r => r.TransactionType is 1 or 2)
            .WithMessage("traffic_secretary_code es obligatorio en matrícula");
        RuleFor(r => r.TrafficSecretaryCode).Length(6, 10)
            .When(r => !string.IsNullOrEmpty(r.TrafficSecretaryCode))
            .WithMessage("traffic_secretary_code debe tener entre 6 y 10 caracteres");

        // vin: obligatorio en Matrícula (1, 2); si viene, 11-18.
        RuleFor(r => r.Vin).NotEmpty()
            .When(r => r.TransactionType is 1 or 2)
            .WithMessage("vin es obligatorio en matrícula");
        RuleFor(r => r.Vin).Length(11, 18)
            .When(r => !string.IsNullOrEmpty(r.Vin))
            .WithMessage("vin debe tener entre 11 y 18 caracteres");

        // plate + plate_assignment_type (constraint combinada de v1 IsPlateValidConstraint).
        RuleFor(r => r.Plate).Must((row, _) => IsPlateValid(row))
            .WithMessage("plate no es válido para el plate_assignment_type indicado "
                + "(tipo 2: vacío o un dígito 0-9; tipo 3/4 o sin tipo: 6 a 15 caracteres)");
        RuleFor(r => r.PlateAssignmentType).Must(v => v is null or 2 or 3 or 4)
            .WithMessage("plate_assignment_type debe ser 2, 3 o 4");
        RuleFor(r => r.PlateAssignmentType).NotNull()
            .When(r => r.TransactionType is 1 or 2 && (r.Plate?.Length ?? 0) < 6)
            .WithMessage("plate_assignment_type es obligatorio en matrícula sin placa completa");

        // ===== Compraventa (solo Traspaso Bilateral = 3) =====
        RuleFor(r => r.SellingDate).NotEmpty()
            .When(r => r.TransactionType == 3)
            .WithMessage("selling_date es obligatorio en traspaso bilateral");
        RuleFor(r => r.SellingDate).Matches(DatePattern)
            .When(r => !string.IsNullOrEmpty(r.SellingDate))
            .WithMessage("selling_date debe tener el formato dd-mm-yyyy");

        RuleFor(r => r.SellingPrice).NotNull()
            .When(r => r.TransactionType == 3)
            .WithMessage("selling_price es obligatorio en traspaso bilateral");
        RuleFor(r => r.SellingPrice!.Value)
            .InclusiveBetween(0.01m, 99999999999999999m)
            .Must(v => decimal.Round(v, 2) == v)
            .When(r => r.TransactionType == 3 && r.SellingPrice is not null)
            .WithMessage("selling_price debe ser mayor que cero, con máximo 2 decimales y 17 dígitos");

        // ===== Otros trámites =====
        RuleFor(r => r.ArmorLevelNumberId).NotNull().Must(v => v is 1 or 2 or 3 or 4)
            .When(r => r.TransactionType == 5)
            .WithMessage("armor_level_number_id es obligatorio (1 a 4) en trámite de blindaje");
        RuleFor(r => r.NewVehicleFuelType).NotNull().InclusiveBetween((short)1, (short)12)
            .When(r => r.TransactionType == 9)
            .WithMessage("new_vehicle_fuel_type es obligatorio (1 a 12) en cambio de combustible");

        // ===== Compañía relacionada / limitaciones (opcionales; longitud si vienen) =====
        RuleFor(r => r.RelatedCompanyDocument).MaximumLength(12)
            .When(r => !string.IsNullOrEmpty(r.RelatedCompanyDocument));
        RuleFor(r => r.RelatedCompanyName).MaximumLength(100)
            .When(r => !string.IsNullOrEmpty(r.RelatedCompanyName));
        RuleFor(r => r.LimitationsCreditor).MaximumLength(50)
            .When(r => !string.IsNullOrEmpty(r.LimitationsCreditor));
        RuleFor(r => r.LimitationsCreditorDocumentType).MaximumLength(5)
            .When(r => !string.IsNullOrEmpty(r.LimitationsCreditorDocumentType));
        RuleFor(r => r.LimitationsCreditorDocumentNumber).MaximumLength(12)
            .When(r => !string.IsNullOrEmpty(r.LimitationsCreditorDocumentNumber));
        RuleFor(r => r.LimitationsInscriptionDate).MaximumLength(10)
            .When(r => !string.IsNullOrEmpty(r.LimitationsInscriptionDate));

        // Transformaciones declaradas (more_transaction_transaction_type).
        RuleForEach(r => r.MoreTransactionTransactionType)
            .SetValidator(new RegisterTransformationInputValidator())
            .When(r => r.MoreTransactionTransactionType is not null);

        // ===== Actores: presencia por tipo (columnas OBLIGATORIO del contrato) =====
        // seller: S en todos los tipos. buyer: S en traspasos (3, 4). lessee: S en matrícula leasing (2)
        // y cambio de locatario (8).
        RuleFor(r => r.Seller).NotEmpty()
            .WithMessage("seller es obligatorio");
        RuleFor(r => r.Buyer).NotEmpty()
            .When(r => r.TransactionType is 3 or 4)
            .WithMessage("buyer es obligatorio en traspaso");
        RuleFor(r => r.Lessee).NotEmpty()
            .When(r => r.TransactionType is 2 or 8)
            .WithMessage("lessee es obligatorio en matrícula leasing / cambio de locatario");

        // ===== Actores: forma de cada uno =====
        // seller/buyer exigen representante legal si el actor es NIT; lessee NO (paridad v1 ValidateActors).
        RuleForEach(r => r.Seller).SetValidator(new RegisterActorInputValidator(requireLegalRepForNit: true))
            .When(r => r.Seller is not null);
        RuleForEach(r => r.Buyer).SetValidator(new RegisterActorInputValidator(requireLegalRepForNit: true))
            .When(r => r.Buyer is not null);
        RuleForEach(r => r.Lessee).SetValidator(new RegisterActorInputValidator(requireLegalRepForNit: false))
            .When(r => r.Lessee is not null);
    }

    /// <summary>
    /// Porta <c>IsPlateValidConstraint</c> de v1: si <c>plate_assignment_type=2</c> la placa debe ir vacía
    /// o ser un único dígito 0-9 (número de preferencia); con tipo 3/4 o sin tipo, 6 a 15 caracteres.
    /// </summary>
    private static bool IsPlateValid(RegisterRowInput row)
    {
        var plate = row.Plate;
        if (row.PlateAssignmentType == 2)
        {
            return string.IsNullOrEmpty(plate) || (plate.Length == 1 && plate[0] is >= '0' and <= '9');
        }

        if (row.PlateAssignmentType is null or 3 or 4)
        {
            return plate is not null && plate.Length is >= 6 and <= 15;
        }

        return false;
    }

    internal static bool IsNit(string? documentType) =>
        string.Equals(documentType?.Trim(), Nit, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Valida un actor del payload (vendedor/comprador/locatario). Paridad con <c>ActorRequestDto</c> de v1.</summary>
public sealed class RegisterActorInputValidator : AbstractValidator<RegisterActorInput>
{
    private const string DatePattern =
        @"^(0[1-9]|[12][0-9]|3[01])-(0[1-9]|1[0-2])-(19\d{2}|2\d{3}|[3-9]\d{3})$";

    public RegisterActorInputValidator(bool requireLegalRepForNit)
    {
        RuleFor(a => a.DocumentType).NotEmpty().WithMessage("document_type del actor es obligatorio");
        RuleFor(a => a.DocumentType).MaximumLength(5)
            .When(a => !string.IsNullOrEmpty(a.DocumentType))
            .WithMessage("document_type debe tener máximo 5 caracteres");

        RuleFor(a => a.DocumentNumber).NotEmpty().WithMessage("document_number del actor es obligatorio");
        RuleFor(a => a.DocumentNumber).MaximumLength(12)
            .When(a => !string.IsNullOrEmpty(a.DocumentNumber))
            .WithMessage("document_number debe tener máximo 12 caracteres");

        RuleFor(a => a.Name).NotEmpty().WithMessage("name del actor es obligatorio");
        RuleFor(a => a.Name).MaximumLength(100)
            .When(a => !string.IsNullOrEmpty(a.Name))
            .WithMessage("name debe tener máximo 100 caracteres");

        // first_last_name: obligatorio si el actor NO es NIT (persona natural).
        RuleFor(a => a.FirstLastName).NotEmpty()
            .When(a => !RegisterRowValidator.IsNit(a.DocumentType))
            .WithMessage("first_last_name es obligatorio cuando el documento no es NIT");
        RuleFor(a => a.FirstLastName).MaximumLength(100)
            .When(a => !string.IsNullOrEmpty(a.FirstLastName))
            .WithMessage("first_last_name debe tener máximo 100 caracteres");

        RuleFor(a => a.SecondLastName).MaximumLength(100)
            .When(a => !string.IsNullOrEmpty(a.SecondLastName));
        RuleFor(a => a.Phone).MaximumLength(50)
            .When(a => !string.IsNullOrEmpty(a.Phone))
            .WithMessage("phone debe tener máximo 50 caracteres");
        RuleFor(a => a.ExpeditionDate).Matches(DatePattern)
            .When(a => !string.IsNullOrEmpty(a.ExpeditionDate))
            .WithMessage("expedition_date debe tener el formato dd-mm-yyyy");

        // Representante legal: obligatorio si el actor es NIT (y aplica el requisito, i.e. no locatario).
        When(a => requireLegalRepForNit && RegisterRowValidator.IsNit(a.DocumentType), () =>
        {
            RuleFor(a => a.LegalRepresentative).NotNull()
                .WithMessage("legal_representative es obligatorio cuando el documento es NIT");
            RuleFor(a => a.LegalRepresentative!)
                .SetValidator(new RegisterLegalRepresentativeInputValidator())
                .When(a => a.LegalRepresentative is not null);
        });

        // Mandante: si viene el objeto, debe estar completo (paridad v1 validatePrincipalMandante).
        RuleFor(a => a.PrincipalMandante!)
            .SetValidator(new RegisterPrincipalMandanteInputValidator())
            .When(a => a.PrincipalMandante is not null);
    }
}

/// <summary>Representante legal: campos exigidos cuando el actor es NIT.</summary>
public sealed class RegisterLegalRepresentativeInputValidator : AbstractValidator<RegisterLegalRepresentativeInput>
{
    public RegisterLegalRepresentativeInputValidator()
    {
        RuleFor(r => r.DocumentType).NotEmpty().WithMessage("legal_representative_document_type es obligatorio");
        RuleFor(r => r.DocumentType).MaximumLength(5)
            .When(r => !string.IsNullOrEmpty(r.DocumentType))
            .WithMessage("legal_representative_document_type debe tener máximo 5 caracteres");

        RuleFor(r => r.DocumentNumber).NotEmpty().WithMessage("legal_representative_document_number es obligatorio");
        RuleFor(r => r.DocumentNumber).MaximumLength(12)
            .When(r => !string.IsNullOrEmpty(r.DocumentNumber))
            .WithMessage("legal_representative_document_number debe tener máximo 12 caracteres");

        RuleFor(r => r.Name).NotEmpty().WithMessage("legal_representative_name es obligatorio");
        RuleFor(r => r.Name).MaximumLength(100)
            .When(r => !string.IsNullOrEmpty(r.Name))
            .WithMessage("legal_representative_name debe tener máximo 100 caracteres");

        RuleFor(r => r.FirstLastName).NotEmpty().WithMessage("legal_representative_first_last_name es obligatorio");
        RuleFor(r => r.FirstLastName).MaximumLength(100)
            .When(r => !string.IsNullOrEmpty(r.FirstLastName))
            .WithMessage("legal_representative_first_last_name debe tener máximo 100 caracteres");

        RuleFor(r => r.SecondLastName).MaximumLength(100)
            .When(r => !string.IsNullOrEmpty(r.SecondLastName));
    }
}

/// <summary>Mandante/apoderado: completo cuando el actor lo incluye.</summary>
public sealed class RegisterPrincipalMandanteInputValidator : AbstractValidator<RegisterPrincipalMandanteInput>
{
    public RegisterPrincipalMandanteInputValidator()
    {
        RuleFor(m => m.DocumentType).NotEmpty().WithMessage("principal_mandante_document_type es obligatorio");
        RuleFor(m => m.DocumentNumber).NotEmpty().WithMessage("principal_mandante_document_number es obligatorio");
        RuleFor(m => m.Name).NotEmpty().WithMessage("principal_mandante_name es obligatorio");
        RuleFor(m => m.FirstLastName).NotEmpty().WithMessage("principal_mandante_first_last_name es obligatorio");
        RuleFor(m => m.Email).NotEmpty().WithMessage("principal_mandante_email es obligatorio");
        RuleFor(m => m.Email).EmailAddress()
            .When(m => !string.IsNullOrEmpty(m.Email))
            .WithMessage("principal_mandante_email con formato inválido");
    }
}

/// <summary>Transformación declarada (more_transaction_transaction_type).</summary>
public sealed class RegisterTransformationInputValidator : AbstractValidator<RegisterTransformationInput>
{
    public RegisterTransformationInputValidator()
    {
        RuleFor(t => t.TransactionType).GreaterThan(0)
            .WithMessage("more_transaction_transaction_type.transactionType es obligatorio");
        RuleFor(t => t.Description).MaximumLength(50)
            .When(t => !string.IsNullOrEmpty(t.Description));
    }
}
