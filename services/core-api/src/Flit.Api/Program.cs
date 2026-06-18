using Flit.Admin.Application;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Persistencia (EF Core + PostgreSQL).
var coreConnStr = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration.GetConnectionString("FlitDb");

if (!string.IsNullOrWhiteSpace(coreConnStr))
{
    builder.Services.AddPostgresInfrastructure(
        coreConnStr, builder.Configuration, builder.Environment);
}
else
{
    throw new InvalidOperationException(
        "ConnectionStrings:Core (PostgreSQL) es obligatoria.");
}

// Seguridad: JWT + policy SuperAdmin (HU #10189, RF01).
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);

// Módulo Admin (HU #10189, RF02).
builder.Services.AddAdminApplication();
builder.Services.AddAdminInfrastructure();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapAdminCompaniesEndpoints();

app.Run();

/// <summary>Punto de entrada expuesto para pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
