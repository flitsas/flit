namespace Flit.Analytics.Application.Dtos;

/// <summary>Inicio del Gestor (rediseño de los 3 dashboards) — una fila de la «Cola de gestión»: el trámite y cuántos días lleva sin movimiento.</summary>
/// <param name="Familia">Categoría del contrato del front: <c>matriculas</c>, <c>traspasos</c> u <c>otros</c>.</param>
/// <param name="DiasSinMovimiento">Días completos desde el último cambio de estado o actualización del trámite.</param>
public sealed record GestorDashboardColaItemDto(
    Guid Id,
    string? Referencia,
    string? Placa,
    string? Vin,
    string Estado,
    string Familia,
    int DiasSinMovimiento);

/// <summary>La cola: los <c>limit</c> trámites más quietos y el total que cumple el filtro (para «Ver todos»).</summary>
public sealed record GestorDashboardColaDto(IReadOnlyList<GestorDashboardColaItemDto> Items, int Total);
