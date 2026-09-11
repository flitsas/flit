using System.Security.Claims;
using Flit.Admin.Application.Companies.Deeds;
using Flit.Admin.Application.Companies.Deeds.CreateDeed;
using Flit.Admin.Application.Companies.Deeds.DeleteDeed;
using Flit.Admin.Application.Companies.Deeds.GetDeed;
using Flit.Admin.Application.Companies.Deeds.ListDeeds;
using Flit.Admin.Application.Companies.Deeds.UpdateDeed;
using Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.DeleteLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.GetLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.ListLegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.UpdateLegalRepresentative;
using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Activate;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Confirm;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Create;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Deactivate;
using Flit.Admin.Application.Companies.PersonalizedDocuments.GetView;
using Flit.Admin.Application.Companies.PersonalizedDocuments.List;
using Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.GetSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.RevokeSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.UpdateSignatureVault;
using Flit.Admin.Application.CompanyDocumentParams;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>Rutas HU #12353 de submódulos de consola sobre un cliente hijo.</summary>
internal static class AdminCompanyChildrenSubmoduleEndpoints
{
    public static RouteGroupBuilder MapAdminCompanyMandateSignersChildRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("", ListMandateSignersAsync);
        group.MapGet("/transit-offices", ListChildTransitOfficesAsync);
        group.MapGet("/represented-companies", ListChildRepresentedCompaniesAsync);
        group.MapPost("", CreateMandateSignerAsync);
        group.MapPut("/{mandateSignerId:guid}", UpdateMandateSignerAsync);
        group.MapPost("/{mandateSignerId:guid}/inactivate", InactivateChildMandateSignerAsync);
        group.MapPost("/{mandateSignerId:guid}/reactivate", ReactivateChildMandateSignerAsync);
        return group;
    }

    public static RouteGroupBuilder MapAdminCompanyLegalRepresentativesChildRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("", ListLegalRepsAsync);
        group.MapGet("/procedure-types", ListChildProcedureTypesAsync);
        group.MapGet("/{id:guid}", GetLegalRepAsync);
        group.MapPost("", CreateLegalRepAsync);
        group.MapPut("/{id:guid}", UpdateLegalRepAsync);
        group.MapDelete("/{id:guid}", DeleteLegalRepAsync);
        return group;
    }

    public static RouteGroupBuilder MapAdminCompanySignatureVaultChildRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("", ListVaultAsync);
        group.MapGet("/{id:guid}", GetVaultAsync);
        group.MapPost("", CreateVaultAsync);
        group.MapPut("/{id:guid}", UpdateVaultAsync);
        group.MapPost("/{id:guid}/revoke", RevokeVaultAsync);
        return group;
    }

    public static RouteGroupBuilder MapAdminCompanyPersonalizedDocumentsChildRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("", ListPersonalizedDocsAsync);
        group.MapPost("", CreatePersonalizedDocAsync);
        group.MapPost("/{id:guid}/confirm", ConfirmPersonalizedDocAsync);
        group.MapPost("/{id:guid}/activate", ActivatePersonalizedDocAsync);
        group.MapDelete("/{documentType}", DeactivatePersonalizedDocAsync);
        group.MapGet("/{id:guid}/view", ViewPersonalizedDocAsync);
        return group;
    }

    public static RouteGroupBuilder MapAdminCompanyDeedsChildRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("", ListChildDeedsAsync);
        group.MapGet("/{id:guid}", GetChildDeedAsync);
        group.MapPost("", CreateChildDeedAsync);
        group.MapPut("/{id:guid}", UpdateChildDeedAsync);
        group.MapDelete("/{id:guid}", DeleteChildDeedAsync);
        return group;
    }

    public static RouteGroupBuilder MapAdminCompanyDocumentParamsChildRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("", ListChildDocumentParamsAsync);
        group.MapPut("", UpsertChildDocumentParamsAsync);
        return group;
    }

    private static async Task<IResult?> GuardAsync(
        ClaimsPrincipal user,
        Guid headTenantId,
        Guid childTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken ct) =>
        await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, ct)
            .ConfigureAwait(false);

    private static async Task<IResult> ListMandateSignersAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListCompanyMandateSignersHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(childTenantId, ct).ConfigureAwait(false);
        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> CreateMandateSignerAsync(
        Guid headTenantId,
        Guid childTenantId,
        CompanyMandateSignerRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] CreateCompanyMandateSignerHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(childTenantId, request, AdminCompanyChildrenConfigEndpoints.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{headTenantId}/children/{childTenantId}/mandate-signers/{result.MandateSignerId}",
                new { id = result.MandateSignerId })
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UpdateMandateSignerAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid mandateSignerId,
        CompanyMandateSignerRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] UpdateCompanyMandateSignerHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(childTenantId, mandateSignerId, request, AdminCompanyChildrenConfigEndpoints.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateMandateSignerOutcome.Updated => Results.Ok(new { integrityHash = result.IntegrityHash }),
            UpdateMandateSignerOutcome.NotFound => Results.NotFound(),
            _ => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> ListLegalRepsAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListLegalRepresentativesHandler handler,
        CancellationToken ct,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new ListLegalRepresentativesQuery { TenantId = childTenantId, Page = page, PageSize = pageSize }, ct)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetLegalRepAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] GetLegalRepresentativeByIdHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new GetLegalRepresentativeByIdQuery { TenantId = childTenantId, Id = id }, ct)
            .ConfigureAwait(false);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> CreateLegalRepAsync(
        Guid headTenantId,
        Guid childTenantId,
        CreateLegalRepresentativeRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] CreateLegalRepresentativeHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateLegalRepresentativeCommand
        {
            TenantId = childTenantId,
            CompanyNit = request.CompanyNit,
            CompanyName = request.CompanyName,
            CompanyEmail = request.CompanyEmail,
            CompanyAddress = request.CompanyAddress,
            CompanyCity = request.CompanyCity,
            CompanyPhone = request.CompanyPhone,
            DocumentType = request.DocumentType,
            DocumentNumber = request.DocumentNumber,
            FirstLastName = request.FirstLastName,
            SecondLastName = request.SecondLastName,
            Name = request.Name,
            Email = request.Email,
            Address = request.Address,
            City = request.City,
            Phone = request.Phone,
            ProcedureTypeIds = request.ProcedureTypeIds ?? [],
            Companies = request.Companies,
            SignatureVaultId = request.SignatureVaultId,
            ActorBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
        };

        var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{headTenantId}/children/{childTenantId}/legal-representatives/{result.Id}",
                new { id = result.Id, signals = result.Signals })
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UpdateLegalRepAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        UpdateLegalRepresentativeRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] UpdateLegalRepresentativeHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateLegalRepresentativeCommand
        {
            TenantId = childTenantId,
            Id = id,
            CompanyNit = request.CompanyNit,
            CompanyName = request.CompanyName,
            CompanyEmail = request.CompanyEmail,
            CompanyAddress = request.CompanyAddress,
            CompanyCity = request.CompanyCity,
            CompanyPhone = request.CompanyPhone,
            DocumentType = request.DocumentType,
            DocumentNumber = request.DocumentNumber,
            FirstLastName = request.FirstLastName,
            SecondLastName = request.SecondLastName,
            Name = request.Name,
            Email = request.Email,
            Address = request.Address,
            City = request.City,
            Phone = request.Phone,
            ProcedureTypeIds = request.ProcedureTypeIds ?? [],
            Companies = request.Companies,
            SignatureVaultId = request.SignatureVaultId,
            ActorBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
        };

        var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);

        if (!result.IsValid)
        {
            return Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(new { id = result.Id, signals = result.Signals });
    }

    private static async Task<IResult> DeleteLegalRepAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] DeleteLegalRepresentativeHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var outcome = await handler
            .HandleAsync(new DeleteLegalRepresentativeCommand { TenantId = childTenantId, Id = id }, ct)
            .ConfigureAwait(false);

        return outcome == DeleteLegalRepresentativeOutcome.Deactivated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> ListVaultAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListSignatureVaultHandler handler,
        CancellationToken ct,
        [FromQuery] string? documentType = null,
        [FromQuery] string? documentNumber = null,
        [FromQuery] bool? soloVigentes = null)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(
                new ListSignatureVaultQuery
                {
                    TenantId = childTenantId,
                    DocumentType = documentType,
                    DocumentNumber = documentNumber,
                    SoloVigentes = soloVigentes,
                },
                ct)
            .ConfigureAwait(false);

        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> GetVaultAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] GetSignatureVaultByIdHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new GetSignatureVaultByIdQuery { TenantId = childTenantId, Id = id }, ct)
            .ConfigureAwait(false);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> CreateVaultAsync(
        Guid headTenantId,
        Guid childTenantId,
        CreateSignatureVaultRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] CreateSignatureVaultHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(
            new CreateSignatureVaultCommand
            {
                TenantId = childTenantId,
                DocumentType = request.DocumentType,
                DocumentNumber = request.DocumentNumber,
                NitEmpresa = request.NitEmpresa,
                FullName = request.FullName,
                VigenciaDesde = request.VigenciaDesde,
                VigenciaHasta = request.VigenciaHasta,
                ArtefactoFirmaBase64 = request.ArtefactoFirmaBase64,
                CodigoHash = request.CodigoHash,
                MandateSignerId = request.MandateSignerId,
                CreatedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
            },
            ct).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{headTenantId}/children/{childTenantId}/signature-vault/{result.SignatureVaultId}",
                new { id = result.SignatureVaultId })
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UpdateVaultAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        AdminSignatureVaultEndpoints.UpdateSignatureVaultRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] UpdateSignatureVaultHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(
            new UpdateSignatureVaultCommand
            {
                TenantId = childTenantId,
                Id = id,
                FullName = request.FullName,
                CodigoHash = request.CodigoHash,
                VigenciaDesde = request.VigenciaDesde,
                VigenciaHasta = request.VigenciaHasta,
                ChangedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
            },
            ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateSignatureVaultOutcome.Updated => Results.NoContent(),
            UpdateSignatureVaultOutcome.NotFound => Results.NotFound(),
            UpdateSignatureVaultOutcome.Revoked => Results.Conflict(),
            _ => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> RevokeVaultAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] RevokeSignatureVaultHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var outcome = await handler
            .HandleAsync(new RevokeSignatureVaultCommand { TenantId = childTenantId, Id = id }, ct)
            .ConfigureAwait(false);

        return outcome == RevokeSignatureVaultOutcome.Revoked ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ListPersonalizedDocsAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListPersonalizedDocumentsHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var documents = await handler.HandleAsync(childTenantId, ct).ConfigureAwait(false);
        return Results.Ok(new { documents });
    }

    private static async Task<IResult> CreatePersonalizedDocAsync(
        Guid headTenantId,
        Guid childTenantId,
        CreatePersonalizedDocumentRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] CreatePersonalizedDocumentVersionHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(
            new CreatePersonalizedDocumentVersionCommand
            {
                TenantId = childTenantId,
                DocumentType = request.DocumentType,
                Filename = request.Filename,
                Sha256 = request.Sha256,
                SizeBytes = request.SizeBytes,
                CreatedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
            },
            ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            CreatePersonalizedDocumentVersionOutcome.Created => Results.Created(
                $"/api/v1/admin/companies/{headTenantId}/children/{childTenantId}/personalized-documents/{result.Id}",
                new
                {
                    id = result.Id,
                    version = result.Version,
                    upload = result.Upload is null
                        ? null
                        : new
                        {
                            storagePath = result.Upload.StoragePath,
                            url = result.Upload.Url,
                            fields = result.Upload.Fields,
                        },
                }),
            _ => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> ListChildTransitOfficesAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListCompanyTransitOfficesHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(childTenantId, ct).ConfigureAwait(false);
        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> ListChildRepresentedCompaniesAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ILegalRepresentativeReader reader,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var empresas = await reader.ListRepresentedCompaniesAsync(childTenantId, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            items = empresas.Select(e => new { id = e.Id, documentNumber = e.DocumentNumber, name = e.Name }),
        });
    }

    private static async Task<IResult> InactivateChildMandateSignerAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid mandateSignerId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListCompanyMandateSignersHandler listHandler,
        [FromServices] InactivateMandateSignerHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var transitOfficeId = await ResolverOrganismoPrimarioAsync(listHandler, childTenantId, mandateSignerId, ct)
            .ConfigureAwait(false);
        if (transitOfficeId is null)
        {
            return Results.NotFound();
        }

        var outcome = await handler
            .HandleAsync(
                new InactivateMandateSignerCommand
                {
                    TransitOfficeId = transitOfficeId.Value,
                    MandateSignerId = mandateSignerId,
                    ChangedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return outcome == InactivateMandateSignerOutcome.Inactivated ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ReactivateChildMandateSignerAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid mandateSignerId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListCompanyMandateSignersHandler listHandler,
        [FromServices] ReactivateMandateSignerHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var transitOfficeId = await ResolverOrganismoPrimarioAsync(listHandler, childTenantId, mandateSignerId, ct)
            .ConfigureAwait(false);
        if (transitOfficeId is null)
        {
            return Results.NotFound();
        }

        var outcome = await handler
            .HandleAsync(
                new ReactivateMandateSignerCommand
                {
                    TransitOfficeId = transitOfficeId.Value,
                    MandateSignerId = mandateSignerId,
                    ChangedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return outcome == ReactivateMandateSignerOutcome.Reactivated ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<Guid?> ResolverOrganismoPrimarioAsync(
        ListCompanyMandateSignersHandler listHandler,
        Guid tenantId,
        Guid mandateSignerId,
        CancellationToken cancellationToken)
    {
        var signers = await listHandler.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return signers.FirstOrDefault(s => s.Id == mandateSignerId)?.TransitOfficeId;
    }

    private static async Task<IResult> ListChildProcedureTypesAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] IProcedureTypeCatalog catalog,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var items = await catalog.ListActivePublishedAsync(ct).ConfigureAwait(false);
        return Results.Ok(items.Select(p => new { id = p.Id, code = p.Code, name = p.Name }));
    }

    private static async Task<IResult> ConfirmPersonalizedDocAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ConfirmPersonalizedDocumentVersionHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(
                new ConfirmPersonalizedDocumentVersionCommand
                {
                    TenantId = childTenantId,
                    Id = id,
                    ConfirmedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            ConfirmPersonalizedDocumentVersionOutcome.Activated => Results.Ok(new
            {
                id,
                version = result.Version,
                status = "activo",
                sha256 = result.Sha256,
                pageCount = result.PageCount,
            }),
            ConfirmPersonalizedDocumentVersionOutcome.NotFound => Results.NotFound(),
            _ => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> ActivatePersonalizedDocAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ActivatePersonalizedDocumentVersionHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(
                new ActivatePersonalizedDocumentVersionCommand
                {
                    TenantId = childTenantId,
                    Id = id,
                    ActivatedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            ActivatePersonalizedDocumentVersionOutcome.Activated => Results.Ok(new
            {
                id,
                version = result.Version,
                status = "activo",
            }),
            ActivatePersonalizedDocumentVersionOutcome.NotFound => Results.NotFound(),
            _ => Results.Json(
                new { error = "version_no_activable" },
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<IResult> DeactivatePersonalizedDocAsync(
        Guid headTenantId,
        Guid childTenantId,
        string documentType,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] DeactivatePersonalizedDocumentHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(
                new DeactivatePersonalizedDocumentCommand
                {
                    TenantId = childTenantId,
                    DocumentType = documentType,
                    DeactivatedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return result.Outcome == DeactivatePersonalizedDocumentOutcome.Deactivated
            ? Results.NoContent()
            : Results.Json(
                new { error = "canal_no_habilitado" },
                statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ViewPersonalizedDocAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] GetPersonalizedDocumentViewHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new GetPersonalizedDocumentViewCommand { TenantId = childTenantId, Id = id }, ct)
            .ConfigureAwait(false);

        return result.Outcome == GetPersonalizedDocumentViewOutcome.Found
            ? Results.Ok(new { url = result.Url, expiresAt = result.ExpiresAt })
            : Results.NotFound();
    }

    private static async Task<IResult> ListChildDeedsAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListDeedsHandler handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new ListDeedsQuery { TenantId = childTenantId, Page = page, PageSize = pageSize }, ct)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            data = result.Data,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
        });
    }

    private static async Task<IResult> GetChildDeedAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] GetDeedByIdHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new GetDeedByIdQuery { TenantId = childTenantId, Id = id }, ct)
            .ConfigureAwait(false);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> CreateChildDeedAsync(
        Guid headTenantId,
        Guid childTenantId,
        CreateDeedRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] CreateDeedHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        ArgumentNullException.ThrowIfNull(request);

        var result = await handler
            .HandleAsync(
                new CreateDeedCommand
                {
                    TenantId = childTenantId,
                    Description = request.Description,
                    VigenciaDesde = request.VigenciaDesde,
                    VigenciaHasta = request.VigenciaHasta,
                    Sha256 = request.Sha256,
                    RepresentedCompanyIds = request.RepresentedCompanyIds,
                    RepresentativeId = request.RepresentativeId,
                    CreatedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{headTenantId}/children/{childTenantId}/deeds/{result.DeedId}",
                new
                {
                    id = result.DeedId,
                    upload = result.Upload is null
                        ? null
                        : new
                        {
                            storagePath = result.Upload.StoragePath,
                            url = result.Upload.Url,
                            fields = result.Upload.Fields,
                        },
                })
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UpdateChildDeedAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        UpdateDeedRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] UpdateDeedHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        ArgumentNullException.ThrowIfNull(request);

        var result = await handler
            .HandleAsync(
                new UpdateDeedCommand
                {
                    TenantId = childTenantId,
                    Id = id,
                    Description = request.Description,
                    VigenciaDesde = request.VigenciaDesde,
                    VigenciaHasta = request.VigenciaHasta,
                    Sha256 = request.Sha256,
                    RepresentedCompanyIds = request.RepresentedCompanyIds,
                    UpdatedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateDeedOutcome.Updated => Results.Ok(new
            {
                id,
                upload = result.Upload is null
                    ? null
                    : new
                    {
                        storagePath = result.Upload.StoragePath,
                        url = result.Upload.Url,
                        fields = result.Upload.Fields,
                    },
            }),
            UpdateDeedOutcome.NotFound => Results.NotFound(),
            _ => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> DeleteChildDeedAsync(
        Guid headTenantId,
        Guid childTenantId,
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] DeleteDeedHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var outcome = await handler
            .HandleAsync(
                new DeleteDeedCommand
                {
                    TenantId = childTenantId,
                    Id = id,
                    ChangedBy = AdminCompanyChildrenConfigEndpoints.ResolveUserId(user),
                },
                ct)
            .ConfigureAwait(false);

        return outcome == DeleteDeedOutcome.Deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ListChildDocumentParamsAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListCompanyDocumentParamsHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(childTenantId, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpsertChildDocumentParamsAsync(
        Guid headTenantId,
        Guid childTenantId,
        UpsertCompanyDocumentParamRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] UpsertCompanyDocumentParamHandler handler,
        CancellationToken ct)
    {
        var forbid = await GuardAsync(user, headTenantId, childTenantId, hierarchy, ct).ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var (result, error) = await handler
            .HandleAsync(childTenantId, request, AdminCompanyChildrenConfigEndpoints.ResolveUserId(user), ct)
            .ConfigureAwait(false);

        return error switch
        {
            "invalid_code" => Results.Json(
                new { error = "El código de documento es obligatorio." },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            "invalid_state" => Results.Json(
                new { error = "Estado inválido (use OCULTO, OBLIGATORIO u OPCIONAL)." },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(result),
        };
    }
}
