using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>Lo que el llamante mandó sobre el modelo, la forma de firma y la vigencia del mandatario.</summary>
internal sealed record MandateSignerProfileInput(
    string? SignerModel,
    string? SignatureMethod,
    string? ValidityKind,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string? FullName,
    string? DocumentType,
    string? DocumentNumber,
    string? Email,
    Guid? SignatureVaultId);

/// <summary>Perfil ya validado y normalizado, listo para persistir.</summary>
internal sealed record MandateSignerProfile(
    string Model,
    string? SignatureMethod,
    string ValidityKind,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string FullName,
    string DocumentType,
    string? DocumentNumber)
{
    public bool IsNatural => Model == MandateSignerModels.Natural;
}

/// <summary>
/// HU #13129 (ADR-0061) — reglas por modelo del mandatario. Persona natural: forma de firma obligatoria
/// (baúl o biometría) y vigencia fija o por rango con fechas coherentes. Persona jurídica y Formato en
/// blanco: sin forma de firma, sin fechas, sin correo de validación (422: solo aplican a Persona natural).
/// Los mensajes nunca incluyen el documento (PII, Ley 1581).
/// </summary>
public static class MandateSignerModelRules
{
    public const string SoloNaturalSuffix = "solo aplica a Persona natural.";

    public const string MetodoRequeridoMessage =
        "Elija la forma de firma del mandatario: baúl de firmas o validación de identidad.";

    public const string FechaInicioRequeridaMessage =
        "La fecha de inicio es obligatoria para la vigencia por rango.";

    public const string FechaFinRequeridaMessage =
        "La fecha de fin es obligatoria para la vigencia por rango.";

    public const string RangoInvertidoMessage =
        "La fecha de fin no puede ser anterior a la fecha de inicio.";

    public const string FechasSoloConRangoMessage =
        "Las fechas solo aplican a la vigencia por rango.";

    /// <summary>
    /// Valida y normaliza. En la edición, <paramref name="existing"/> aporta los valores vigentes para los
    /// campos que el llamante no manda (solo dentro de Persona natural: cambiar de modelo no arrastra la
    /// forma de firma ni el rango del modelo anterior).
    /// </summary>
    internal static (MandateSignerProfile Profile, List<MandateSignerValidationError> Errors) Evaluate(
        MandateSignerProfileInput input,
        MandateSignerItem? existing = null)
    {
        var errors = new List<MandateSignerValidationError>();

        var rawModel = Clean(input.SignerModel);
        var model = rawModel ?? existing?.SignerModel ?? MandateSignerModels.Natural;
        if (!MandateSignerModels.IsValid(model))
        {
            errors.Add(new MandateSignerValidationError(
                "signerModel",
                "El modelo del mandatario debe ser natural, juridica o formato_blanco.",
                null));
            model = MandateSignerModels.Natural;
        }

        var natural = model == MandateSignerModels.Natural;
        var existingNatural = natural && existing is { SignerModel: MandateSignerModels.Natural };

        var method = Clean(input.SignatureMethod);
        if (method is null && existingNatural)
        {
            method = existing!.SignatureMethod;
        }

        var kind = Clean(input.ValidityKind);
        var kindFromExisting = false;
        if (kind is null && existingNatural)
        {
            kind = existing!.ValidityKind;
            kindFromExisting = true;
        }

        kind ??= MandateValidityKinds.Fixed;
        if (!MandateValidityKinds.IsValid(kind))
        {
            errors.Add(new MandateSignerValidationError(
                "validityKind", "El tipo de vigencia debe ser fixed o range.", null));
            kind = MandateValidityKinds.Fixed;
        }

        var from = input.ValidFrom;
        var to = input.ValidTo;
        if (kindFromExisting && kind == MandateValidityKinds.Range)
        {
            from ??= existing!.ValidFrom;
            to ??= existing!.ValidTo;
        }

        if (method is not null && !MandateSignatureMethods.IsValid(method))
        {
            errors.Add(new MandateSignerValidationError(
                "signatureMethod", "La forma de firma debe ser baul o biometria.", null));
            method = null;
        }

        if (natural)
        {
            ValidateNatural(errors, method, kind, from, to);
        }
        else
        {
            ValidateNoNatural(errors, input, kind, from, to);
            method = null;
            kind = MandateValidityKinds.Fixed;
            from = null;
            to = null;
        }

        var (fullName, documentType, documentNumber) = model switch
        {
            MandateSignerModels.FormatoBlanco => (
                MandateSignerModels.FormatoBlancoFullName,
                string.IsNullOrWhiteSpace(input.DocumentType) ? "CC" : input.DocumentType.Trim(),
                (string?)null),
            MandateSignerModels.Juridica => (
                input.FullName?.Trim() ?? string.Empty,
                "NIT",
                input.DocumentNumber?.Trim()),
            _ => (
                input.FullName?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(input.DocumentType) ? "CC" : input.DocumentType.Trim(),
                input.DocumentNumber?.Trim()),
        };

        return (
            new MandateSignerProfile(
                model, method, kind, from, to, fullName, documentType, documentNumber),
            errors);
    }

    private static void ValidateNatural(
        List<MandateSignerValidationError> errors,
        string? method,
        string kind,
        DateOnly? from,
        DateOnly? to)
    {
        // Un método inválido ya se reportó arriba; aquí solo falta cuando no hay ninguno.
        if (method is null && !errors.Any(e => e.Field == "signatureMethod"))
        {
            errors.Add(new MandateSignerValidationError("signatureMethod", MetodoRequeridoMessage, null));
        }

        if (kind == MandateValidityKinds.Range)
        {
            if (from is null)
            {
                errors.Add(new MandateSignerValidationError("validFrom", FechaInicioRequeridaMessage, null));
            }

            if (to is null)
            {
                errors.Add(new MandateSignerValidationError("validTo", FechaFinRequeridaMessage, null));
            }

            if (from is not null && to is not null && to < from)
            {
                errors.Add(new MandateSignerValidationError("validTo", RangoInvertidoMessage, null));
            }
        }
        else if (from is not null || to is not null)
        {
            errors.Add(new MandateSignerValidationError(
                from is not null ? "validFrom" : "validTo", FechasSoloConRangoMessage, null));
        }
    }

    private static void ValidateNoNatural(
        List<MandateSignerValidationError> errors,
        MandateSignerProfileInput input,
        string kind,
        DateOnly? from,
        DateOnly? to)
    {
        if (Clean(input.SignatureMethod) is not null)
        {
            errors.Add(new MandateSignerValidationError(
                "signatureMethod", $"La forma de firma {SoloNaturalSuffix}", null));
        }

        if (input.SignatureVaultId is { } vault && vault != Guid.Empty)
        {
            errors.Add(new MandateSignerValidationError(
                "signatureVaultId", $"La firma del baúl {SoloNaturalSuffix}", null));
        }

        if (kind == MandateValidityKinds.Range)
        {
            errors.Add(new MandateSignerValidationError(
                "validityKind", $"La vigencia por rango {SoloNaturalSuffix}", null));
        }

        if (from is not null)
        {
            errors.Add(new MandateSignerValidationError("validFrom", $"La fecha de inicio {SoloNaturalSuffix}", null));
        }

        if (to is not null)
        {
            errors.Add(new MandateSignerValidationError("validTo", $"La fecha de fin {SoloNaturalSuffix}", null));
        }

        if (!string.IsNullOrWhiteSpace(input.Email))
        {
            errors.Add(new MandateSignerValidationError(
                "email", $"El correo de validación {SoloNaturalSuffix}", null));
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
