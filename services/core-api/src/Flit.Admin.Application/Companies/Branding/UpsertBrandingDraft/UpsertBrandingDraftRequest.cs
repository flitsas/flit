namespace Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;

public sealed record UpsertBrandingDraftColorsRequest(string Primary, string Secondary, string OnPrimary);

/// <summary>Cuerpo de <c>PUT .../branding</c> (HU #12412 AC1/AC4). Crea o reemplaza el borrador completo.</summary>
public sealed record UpsertBrandingDraftRequest(
    string? PlatformName,
    UpsertBrandingDraftColorsRequest? Colors,
    Guid? LogoId,
    long? RowVersion);
