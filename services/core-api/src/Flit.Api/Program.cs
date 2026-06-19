using Flit.Api.Authorization;
using Flit.Api.Endpoints.Public;
using Flit.Api.Endpoints.SuperAdmin;
using Flit.Api.Endpoints.Tramites;
using Flit.Infrastructure;
using Flit.Tramites.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var coreConnStr = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration.GetConnectionString("FlitDb");

if (!string.IsNullOrWhiteSpace(coreConnStr))
{
    builder.Services.AddPostgresInfrastructure(
        coreConnStr, builder.Configuration, builder.Environment);
}
else
{
    throw new System.InvalidOperationException(
        "ConnectionStrings:Core (PostgreSQL) es obligatoria.");
}

builder.Services.AddTramitesApplication();

builder.Services.AddAuthentication("Stub")
    .AddScheme<AuthenticationSchemeOptions, StubAuthenticationHandler>("Stub", null);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SuperAdminOnly", policy =>
        policy.Requirements.Add(new SuperAdminRequirement()));

builder.Services.AddSingleton<IAuthorizationHandler, SuperAdminStubAuthorizationHandler>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapSuperAdminEndpoints();
app.MapPublicProcedureEndpoints();
app.MapPublicProcedureTypeEndpoints();
app.MapPublicBiometricaEndpoints();
app.MapTramitesInstanceEndpoints();
app.MapTramitesActorEndpoints();
app.MapTramitesAttachmentEndpoints();
app.MapTramitesBiometricaEndpoints();
app.MapTramitesFirmaEndpoints();
app.MapTramitesFurEndpoints();
app.MapConsultationEndpoints();
app.MapTramitesCommercialEndpoints();
app.MapTramitesPreflightEndpoints();
app.MapTramitesWizardEndpoints();

app.Run();
