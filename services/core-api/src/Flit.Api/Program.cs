using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Flit.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
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

var app = builder.Build();

app.Run();
