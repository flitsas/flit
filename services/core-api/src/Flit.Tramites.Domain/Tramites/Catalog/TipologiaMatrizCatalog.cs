using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>
/// Matriz paso × tipología + journeys (paridad <c>TIPOLOGIA_JOURNEYS</c> de Johan).
/// Fuente única de QUIÉN participa (partes) y CÓMO cambia cada paso por modalidad.
/// MVP: <c>matricula_inicial</c> (5 pasos, solo comprador) y <c>traspaso_standard</c>
/// (6 pasos, vendedor + comprador). Extensible: agregar un <see cref="TipologiaJourney"/>.
/// </summary>
public static class TipologiaMatrizCatalog
{
    private static readonly ParteRequerida Comprador = new(
        ParteRol.Comprador,
        "Comprador",
        Obligatorio: true,
        Ayuda: "Adquirente de la propiedad (parte que recibe el vehículo).");

    private static readonly ParteRequerida Vendedor = new(
        ParteRol.Vendedor,
        "Vendedor",
        Obligatorio: true,
        Ayuda: "Titular saliente que transfiere la propiedad (compraventa directa).");

    /// <summary>Adquirente por defecto cuando no hay tipología (preserva el flujo "comprador").</summary>
    private static readonly AdquirenteConfig DefaultAdquirente = new(
        ParteRol.Comprador,
        "Comprador",
        "Adquirente de la propiedad.");

    private static readonly IReadOnlyList<TipologiaJourney> Journeys =
    [
        new TipologiaJourney(
            Codigo: TramiteTipologiaCatalog.CodigoMatriculaInicial,
            Nombre: "Matrícula inicial",
            Modalidad: TramiteModalidadEntrada.MatriculaInicial,
            Adquirente: new AdquirenteConfig(ParteRol.Comprador, "Comprador", "Adquirente / titular del vehículo."),
            VendedorRequerido: false,
            Partes: [Comprador],
            Pasos:
            [
                // HU #10935 — los documentos van DESPUÉS del comprador.
                new PasoTipologia(1, "Consulta VIN", Aplica: true, Nota: "VIN-first: el vehículo aún no existe en RUNT (primera matrícula)."),
                new PasoTipologia(2, "Comprador", Aplica: true, Nota: "Adquirente único; sin vendedor."),
                new PasoTipologia(3, "Documentos", Aplica: true, Nota: "Factura + manifiesto/aduana + impronta (obligatorios)."),
                new PasoTipologia(4, "Identidad", Aplica: true, Nota: "Validación de identidad del comprador."),
                new PasoTipologia(5, "Resumen del trámite", Aplica: true, Nota: "Revisión y cierre: FUR/impronta se generan automáticamente; Preparar/Radicar envían a tránsito."),
            ]),
        new TipologiaJourney(
            Codigo: TramiteTipologiaCatalog.CodigoTraspasoStandard,
            Nombre: "Traspaso estándar",
            Modalidad: TramiteModalidadEntrada.Traspaso,
            Adquirente: new AdquirenteConfig(ParteRol.Comprador, "Comprador", "Adquirente de la propiedad."),
            VendedorRequerido: true,
            Partes: [Comprador, Vendedor],
            Pasos:
            [
                // HU #10935 — los documentos van DESPUÉS del vendedor y comprador.
                // Paridad de pasos con matrícula (2026-08) — los datos comerciales se absorben en
                // Documentos (4) y el hueco lo ocupa Identidad (5): misma secuencia que matrícula.
                new PasoTipologia(1, "Consulta del vehículo", Aplica: true, Nota: "Pre-vuelo SOAT/SIMIT/RUNT del vehículo por placa y de ambas partes."),
                new PasoTipologia(2, "Vendedor", Aplica: true, Nota: "Parte saliente; consulta RUNT del vendedor."),
                new PasoTipologia(3, "Comprador", Aplica: true, Nota: "Parte entrante; RUNT + SIMIT del comprador."),
                new PasoTipologia(4, "Documentos", Aplica: true, Nota: "Contrato de compraventa + impronta + SOAT del vehículo + datos comerciales (valor de venta > 0)."),
                new PasoTipologia(5, "Identidad", Aplica: true, Nota: "Validación de identidad (biométrica) de vendedor y comprador."),
                new PasoTipologia(6, "Resumen del trámite", Aplica: true, Nota: "Revisión y cierre: FUR/impronta se generan automáticamente; Preparar/Radicar envían a tránsito."),
            ]),
    ];

    private static readonly Dictionary<string, TipologiaJourney> ByCode =
        Journeys.ToDictionary(j => j.Codigo);

    /// <summary>Todos los journeys configurados.</summary>
    public static IReadOnlyList<TipologiaJourney> All => Journeys;

    /// <summary>Journey de la tipología, o <c>null</c> si el código no existe.</summary>
    public static TipologiaJourney? Get(string? codigo) =>
        !string.IsNullOrEmpty(codigo) && ByCode.TryGetValue(codigo, out var j) ? j : null;

    /// <summary>
    /// ¿La tipología exige una parte VENDEDORA? Solo <c>traspaso_standard</c>.
    /// Sin tipología (o desconocida) → <c>false</c> (paridad <c>vendedorRequerido</c>).
    /// </summary>
    public static bool VendedorRequerido(string? codigo) =>
        Get(codigo)?.VendedorRequerido ?? false;

    /// <summary>Config del adquirente (etiqueta del paso). Cae al genérico "Comprador" (paridad <c>getAdquirente</c>).</summary>
    public static AdquirenteConfig GetAdquirente(string? codigo) =>
        Get(codigo)?.Adquirente ?? DefaultAdquirente;

    /// <summary>Partes requeridas del journey (vacío si no hay tipología).</summary>
    public static IReadOnlyList<ParteRequerida> GetPartesRequeridas(string? codigo) =>
        Get(codigo)?.Partes ?? [];

    /// <summary>Contexto de un paso del wizard para una tipología, o <c>null</c>.</summary>
    public static PasoTipologia? GetPasoTipologia(string? codigo, int paso) =>
        Get(codigo)?.Pasos.FirstOrDefault(p => p.Paso == paso);

    /// <summary>
    /// Detecta desincronización entre el catálogo de checklist y la matriz de journeys
    /// (paridad <c>matrizDriftIssues</c>). Vacío = sano. Sin side-effects.
    /// </summary>
    public static IReadOnlyList<string> DriftIssues()
    {
        var issues = new List<string>();

        foreach (var j in Journeys)
        {
            if (!TramiteTipologiaCatalog.IsValid(j.Codigo))
                issues.Add($"journey '{j.Codigo}' no existe en el catálogo de tipologías");

            if (j.VendedorRequerido &&
                !j.Partes.Any(p => p.Rol == ParteRol.Vendedor && p.Obligatorio))
                issues.Add($"journey '{j.Codigo}' marca VendedorRequerido pero no lista vendedor obligatorio");

            if (!j.Partes.Any(p => p.Rol == j.Adquirente.Rol))
                issues.Add($"journey '{j.Codigo}' no incluye su adquirente '{j.Adquirente.Rol}' en partes");

            int esperados = j.Modalidad == TramiteModalidadEntrada.Traspaso ? 6 : 5;
            if (j.Pasos.Count != esperados)
                issues.Add($"journey '{j.Codigo}' debe tener {esperados} pasos (tiene {j.Pasos.Count})");
        }

        foreach (var t in TramiteTipologiaCatalog.All)
        {
            if (!ByCode.ContainsKey(t.Codigo))
                issues.Add($"tipología '{t.Codigo}' del catálogo no tiene journey en la matriz");
        }

        return issues;
    }
}
