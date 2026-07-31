using System.Security.Cryptography;
using System.Text.Json;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Application.Clients;

// =============================================================================
// Administración (CRUD) de los clientes de integración ICT (ict.integration_clients).
// Lo usa el submódulo "Clientes ICT" del frontend (dentro de Usuarios y Roles), gateado por el permiso
// de plataforma ict.clients.manage. El SECRETO de un cliente lo GENERA el sistema y se muestra UNA sola
// vez (nunca se persiste en claro ni se puede recuperar): así se evita que el admin teclee credenciales
// débiles, acorde al modelo de credencial de máquina.
// =============================================================================

/// <summary>Alta de un cliente ICT: username + compañía (tenant) + scopes opcionales.</summary>
public sealed record CreateClientCommand(string Username, Guid TenantId, IReadOnlyList<string>? Scopes);

/// <summary>Edición de un cliente ICT: activar/desactivar, forzar rotación, cambiar scopes.</summary>
public sealed record UpdateClientCommand(Guid Id, bool? IsActive, bool? MustRotate, IReadOnlyList<string>? Scopes);

/// <summary>Vista de un cliente ICT para el listado/detalle (nunca incluye el hash ni el secreto).</summary>
public sealed record IntegrationClientView(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Username,
    string Scopes,
    bool IsActive,
    bool MustRotate,
    DateTime? LastLoginAt,
    DateTime? LockedUntil,
    DateTime CreatedAt);

/// <summary>Resultado de crear/regenerar: la vista del cliente + el secreto en claro (mostrar UNA vez).</summary>
public sealed record ClientSecretResult(IntegrationClientView Client, string GeneratedSecret);

/// <summary>Genera secretos de cliente fuertes (credencial de máquina), url-safe.</summary>
internal static class ClientSecretGenerator
{
    public static string Generate()
    {
        // 24 bytes de entropía criptográfica -> base64url (~32 chars), sin caracteres problemáticos en URLs/JSON.
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

/// <summary>Utilidades de mapeo/serialización compartidas por los handlers de administración de clientes.</summary>
internal static class IntegrationClientMapper
{
    /// <summary>Scopes por defecto (paridad con el seed dev): escritura de transacciones + lectura de estado.</summary>
    private static readonly string[] DefaultScopes = ["ict.transactions.write", "ict.status.read"];

    public static string SerializeScopes(IReadOnlyList<string>? scopes)
    {
        var effective = scopes is { Count: > 0 }
            ? scopes.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToArray()
            : DefaultScopes;
        return JsonSerializer.Serialize(effective);
    }

    public static IntegrationClientView ToView(IntegrationClient client, string tenantName) => new(
        client.Id,
        client.TenantId,
        tenantName,
        client.Username,
        client.Scopes,
        client.IsActive,
        client.MustRotate,
        client.LastLoginAt,
        client.LockedUntil,
        client.CreatedAt);
}

/// <summary>Crea un cliente ICT para una compañía. El secreto lo genera el sistema (se muestra una vez).</summary>
public sealed class CreateIntegrationClientHandler(
    IIntegrationClientRepository repository,
    ITenantDirectory tenants,
    IIctPasswordHasher hasher)
{
    public async Task<(ClientSecretResult? Result, string? Error)> HandleAsync(
        CreateClientCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Se guarda en MINÚSCULAS: la unicidad y el login son case-insensitive por normalización en la app
        // (no por citext). "GestorA" y "gestora" son el mismo cliente.
        var username = (command.Username ?? string.Empty).Trim().ToLowerInvariant();
        if (username.Length is < 3 or > 50)
        {
            return (null, "invalid_username");
        }

        var tenant = await tenants.GetAsync(command.TenantId, ct);
        if (tenant is null)
        {
            return (null, "tenant_not_found");
        }

        if (!tenant.IsActive)
        {
            return (null, "tenant_inactive");
        }

        // username es UNIQUE global (case-insensitive por minúsculas): se rechaza el duplicado antes del INSERT.
        var existing = await repository.FindByUsernameAsync(username, ct);
        if (existing is not null)
        {
            return (null, "username_taken");
        }

        var secret = ClientSecretGenerator.Generate();
        var client = new IntegrationClient
        {
            Id = Guid.CreateVersion7(),
            TenantId = command.TenantId,
            Username = username,
            PasswordHash = hasher.Hash(secret),
            Scopes = IntegrationClientMapper.SerializeScopes(command.Scopes),
            IsActive = true,
            MustRotate = false,
            PasswordChangedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        await repository.AddAsync(client, ct);

        return (new ClientSecretResult(IntegrationClientMapper.ToView(client, tenant.LegalName), secret), null);
    }
}

/// <summary>Lista los clientes ICT (opcionalmente de una compañía), resolviendo el nombre de cada tenant.</summary>
public sealed class ListIntegrationClientsHandler(
    IIntegrationClientRepository repository,
    ITenantDirectory tenants)
{
    public async Task<IReadOnlyList<IntegrationClientView>> HandleAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        var clients = await repository.ListAsync(tenantId, ct);
        var nameCache = new Dictionary<Guid, string>();
        var views = new List<IntegrationClientView>(clients.Count);
        foreach (var client in clients)
        {
            if (!nameCache.TryGetValue(client.TenantId, out var name))
            {
                var tenant = await tenants.GetAsync(client.TenantId, ct);
                name = tenant?.LegalName ?? client.TenantId.ToString();
                nameCache[client.TenantId] = name;
            }

            views.Add(IntegrationClientMapper.ToView(client, name));
        }

        return views;
    }
}

/// <summary>Edita un cliente ICT (activar/desactivar, forzar rotación, scopes). No toca el secreto.</summary>
public sealed class UpdateIntegrationClientHandler(
    IIntegrationClientRepository repository,
    ITenantDirectory tenants)
{
    public async Task<(IntegrationClientView? Result, string? Error)> HandleAsync(
        UpdateClientCommand command, Guid? restrictToTenant = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var client = await repository.FindByIdAsync(command.Id, ct);
        // restrictToTenant: un admin no-super solo administra clientes de su propia compañía. Un cliente
        // de otro tenant se trata como inexistente (no filtra su existencia).
        if (client is null || (restrictToTenant is { } rt && client.TenantId != rt))
        {
            return (null, "not_found");
        }

        if (command.IsActive is { } isActive)
        {
            client.IsActive = isActive;
        }

        if (command.MustRotate is { } mustRotate)
        {
            client.MustRotate = mustRotate;
        }

        if (command.Scopes is not null)
        {
            client.Scopes = IntegrationClientMapper.SerializeScopes(command.Scopes);
        }

        client.UpdatedAt = DateTime.UtcNow;
        await repository.SaveAsync(client, ct);

        var tenant = await tenants.GetAsync(client.TenantId, ct);
        return (IntegrationClientMapper.ToView(client, tenant?.LegalName ?? client.TenantId.ToString()), null);
    }
}

/// <summary>Regenera el secreto de un cliente ICT (se muestra una vez) y limpia el lock de intentos.</summary>
public sealed class ResetIntegrationClientSecretHandler(
    IIntegrationClientRepository repository,
    ITenantDirectory tenants,
    IIctPasswordHasher hasher)
{
    public async Task<(ClientSecretResult? Result, string? Error)> HandleAsync(
        Guid id, Guid? restrictToTenant = null, CancellationToken ct = default)
    {
        var client = await repository.FindByIdAsync(id, ct);
        // restrictToTenant: un admin no-super solo administra clientes de su propia compañía.
        if (client is null || (restrictToTenant is { } rt && client.TenantId != rt))
        {
            return (null, "not_found");
        }

        var secret = ClientSecretGenerator.Generate();
        client.PreviousPasswordHash = client.PasswordHash;
        client.PasswordHash = hasher.Hash(secret);
        client.PasswordChangedAt = DateTime.UtcNow;
        client.MustRotate = false;
        client.FailedLoginAttempts = 0;
        client.LockedUntil = null;
        client.UpdatedAt = DateTime.UtcNow;
        await repository.SaveAsync(client, ct);

        var tenant = await tenants.GetAsync(client.TenantId, ct);
        return (new ClientSecretResult(
            IntegrationClientMapper.ToView(client, tenant?.LegalName ?? client.TenantId.ToString()), secret), null);
    }
}
