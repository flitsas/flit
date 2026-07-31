using System;
using System.Collections.Generic;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Gates del wizard de TRASPASO (6 pasos), puros (sin IO). Paridad <c>traspaso-gates.ts</c> de Johan.
/// Fuente única de verdad de "paso completo / puede avanzar / inmutabilidad de pasos cerrados",
/// reutilizada luego por API (checks server-side) y UI (sidebar + Continuar).
///
/// <para>HU #10935 — orden del wizard: los documentos van DESPUÉS de los actores
/// (consulta → vendedor → comprador → documentos → comercial → fur).</para>
/// </summary>
public static class TraspasoGates
{
    public const int TotalPasos = 6;

    /// <summary>Claves de datos asociadas a cada paso (para detectar modificación de pasos cerrados).</summary>
    public static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> PasoDataKeys =
        new Dictionary<int, IReadOnlyList<string>>
        {
            // El paz y salvo de impuesto se confirma en el paso 1 (junto a la consulta/preflight).
            // HU #10935 — los actores pasan al frente (2 vendedor, 3 comprador) y Documentos (4) no
            // tiene claves de datos propias en field_values (su completitud sale del checklist).
            [1] = ["paz_salvo_impuesto", "impuesto_consulta"],
            [2] = ["vendedor", "runt_vendedor"],
            [3] = ["comprador", "runt_comprador", "simit_comprador"],
            [5] = ["comercial"],
        };

    /// <summary>Paso N (1–6) completado → puede avanzar a N+1. Paridad <c>pasoTraspasoCompleto</c>.</summary>
    public static GateResult PasoCompleto(int paso, TraspasoGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        bool forzar = ctx.ForzarContinuar;

        switch (paso)
        {
            case 1:
                // Paso 1 = consulta del vehículo por placa + (movido aquí) confirmación del paz y
                // salvo de impuesto cuando el preflight lo reporta unknown. Sin consulta el paso NO
                // se completa, así un trámite recién creado abre en el paso 1 (no salta al 2).
                if (!ctx.TramiteRadicado)
                    return GateResult.Block("sin_radicado", "Radica el trámite antes de continuar");
                if (!ctx.VehiculoConsultado)
                    return GateResult.Block("consulta_pendiente", "Consulta el vehículo por placa antes de continuar");
                // Bloqueo DURO: si la consulta del vehículo no se pudo verificar (proveedor caído/
                // timeout), el paso 1 NO se completa aunque la placa esté en field_values → no se
                // avanza con un dato vital sin verificar; hay que reejecutar la consulta.
                if (ctx.Preflight?.ProviderError == true)
                    return GateResult.Block("preflight_provider_error", "No fue posible verificar la información del vehículo en el RUNT. Vuelve a ejecutar la consulta antes de continuar");
                if (ImpuestoGateBloquea(ctx.Preflight, ctx.PazSalvoImpuestoVerificado, forzar))
                    return GateResult.Block("impuesto_pendiente", "Confirma paz y salvo de impuesto vehicular antes de continuar");
                return GateResult.Allowed;

            case 2:
                // HU #10935 — Paso 2 = Vendedor (antes iba en el paso 3): parte + RUNT consultado.
                if (!ParteCompleta(ctx.Vendedor))
                    return GateResult.Block("vendedor_incompleto", "Completa nombre, documento y email del vendedor");
                if (!RuntConsultado(ctx.RuntVendedor, ctx.Vendedor?.Documento))
                    return GateResult.Block("runt_vendedor", "Consulta RUNT del vendedor antes de continuar");
                return GateResult.Allowed;

            case 3:
                // HU #10935 — Paso 3 = Comprador (antes iba en el paso 4): parte + RUNT + SIMIT.
                if (!ParteCompleta(ctx.Comprador))
                    return GateResult.Block("comprador_incompleto", "Completa nombre, documento y email del comprador");
                if (!RuntConsultado(ctx.RuntComprador, ctx.Comprador?.Documento))
                    return GateResult.Block("runt_comprador", "Consulta RUNT del comprador antes de continuar");
                return SimitCompradorGate(ctx, forzar);

            case 4:
                // HU #10935 — Paso 4 = Documentos, DESPUÉS de los actores (antes iba en el paso 2).
                // Preflight crítico + checklist. El gestor puede asumir el riesgo de un preflight rojo
                // subsanable (sin tocar docs). Bloqueo DURO: una consulta no verificable (proveedor
                // caído/timeout) NO se subsana con "aceptar riesgo" ni forzando; hay que reintentar.
                if (ctx.Preflight?.ProviderError == true)
                    return GateResult.Block("preflight_provider_error", "No fue posible verificar la información en el RUNT/SIMIT/RNMC. Vuelve a ejecutar la consulta antes de continuar");
                if (PreflightBloquea(ctx.Preflight, forzar || ctx.RiesgoPreflightAceptado))
                    return GateResult.Block("preflight_red", "Hay bloqueos críticos (SOAT/RTM). Subsana antes de continuar");
                if (!ctx.DocumentosObligatoriosCompletos)
                    return GateResult.Block("documentos_incompletos", "Sube los documentos obligatorios antes de continuar");
                return GateResult.Allowed;

            case 5:
                return ValidarComercial(ctx);

            case 6:
                // Paso 6 = Generar FUR. Los documentos ya se exigen en el paso 4; aquí el gating
                // (biometría de ambas partes + firma + FUR) se evalúa de forma diferida en
                // WizardStateQuery.BuildTraspaso. PasoCompleto(6) no bloquea por documentos.
                return GateResult.Allowed;

            default:
                return GateResult.Block("paso_invalido", "Paso inválido");
        }
    }

    /// <summary>Máximo paso alcanzable según datos (1–6). Paridad <c>maxPasoTraspasoAlcanzable</c>.</summary>
    public static int MaxPasoAlcanzable(TraspasoGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.TramiteRadicado)
            return 1;

        for (int p = 1; p <= TotalPasos; p++)
        {
            if (!PasoCompleto(p, ctx).Ok)
                return p;
        }
        return TotalPasos;
    }

    /// <summary>Puede avanzar desde el paso actual (botón Continuar). Paridad <c>puedeAvanzarDesdePasoTraspaso</c>.</summary>
    public static GateResult PuedeAvanzar(int pasoActual, TraspasoGateContext ctx) =>
        PasoCompleto(pasoActual, ctx);

    /// <summary>
    /// Puede navegar al paso destino (sidebar). Retroceder siempre permitido si hay trámite.
    /// Paridad <c>puedeIrAPasoTraspaso</c>.
    /// </summary>
    public static GateResult PuedeIrAPaso(int pasoActual, int targetPaso, TraspasoGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (targetPaso < 1 || targetPaso > TotalPasos)
            return GateResult.Block("paso_invalido", "Paso inválido");
        if (targetPaso == 1)
            return GateResult.Allowed;
        if (!ctx.TramiteRadicado)
            return GateResult.Block("sin_tramite", "Radica el trámite primero");
        if (targetPaso <= pasoActual)
            return GateResult.Allowed;

        for (int p = pasoActual; p < targetPaso; p++)
        {
            var r = PasoCompleto(p, ctx);
            if (!r.Ok)
                return r;
        }
        return GateResult.Allowed;
    }

    /// <summary>Paso ya validado y cerrado → solo lectura. Paridad <c>pasoTraspasoSoloLectura</c>.</summary>
    public static bool PasoSoloLectura(int paso, TraspasoGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!ctx.TramiteRadicado || paso < 1 || paso > TotalPasos)
            return false;
        return paso < MaxPasoAlcanzable(ctx);
    }

    /// <summary>
    /// Rechaza una modificación que toque datos de pasos ya cerrados
    /// (&lt; <paramref name="maxPasoEditable"/>). Paridad <c>detectarModificacionPasosCerradosTraspaso</c>.
    /// </summary>
    /// <param name="maxPasoEditable">Máximo paso aún editable (típicamente <see cref="MaxPasoAlcanzable"/>).</param>
    /// <param name="clavesModificadas">Claves de datos que el PATCH pretende modificar (ver <see cref="PasoDataKeys"/>).</param>
    /// <param name="pasoPatch">Paso desde el que se emite el PATCH (6 = anexos, permite paz/salvo).</param>
    public static GateResult DetectarModificacionPasosCerrados(
        int maxPasoEditable,
        IReadOnlyCollection<string> clavesModificadas,
        int? pasoPatch = null)
    {
        ArgumentNullException.ThrowIfNull(clavesModificadas);
        var keys = new HashSet<string>(clavesModificadas);
        bool paso6Docs = pasoPatch == 6;

        for (int p = 2; p < maxPasoEditable && p <= TotalPasos; p++)
        {
            if (!PasoDataKeys.TryGetValue(p, out var pasoKeys))
                continue;

            foreach (var k in pasoKeys)
            {
                if (!keys.Contains(k))
                    continue;
                if (paso6Docs && p == 2 && k == "paz_salvo_impuesto")
                    continue;
                return GateResult.Block(
                    "paso_cerrado",
                    $"El paso {p} está cerrado. No puedes modificar datos ya validados.");
            }
        }

        return GateResult.Allowed;
    }

    /// <summary>Gate de generación del FUR: exige biometría de ambas partes salvo forzar. Paridad <c>gateFurTraspaso</c>.</summary>
    public static GateResult GateFur(BiometriaSnapshot? biometria, bool forzarContinuar)
    {
        if (forzarContinuar)
            return GateResult.Allowed;
        if (biometria is { Vendedor: true, Comprador: true })
            return GateResult.Allowed;
        return GateResult.Block(
            "biometria_pendiente",
            "Valida la biométrica de vendedor y comprador antes de generar el FUR");
    }

    /// <summary>Valida el paso comercial (valor de venta &gt; 0). Paridad <c>validateTraspasoComercial</c>.</summary>
    public static GateResult ValidarComercial(TraspasoGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.ValorVenta > 0
            ? GateResult.Allowed
            : GateResult.Block("comercial_valor", "Ingresa un valor de venta mayor a cero antes de continuar");
    }

    private static GateResult SimitCompradorGate(TraspasoGateContext ctx, bool forzar)
    {
        if (forzar)
            return GateResult.Allowed;

        var doc = TramiteDocumento.Normalizar(ctx.Comprador?.Documento);
        if (string.IsNullOrEmpty(doc))
            return GateResult.Block("comprador_doc", "Documento del comprador requerido");

        var simit = ctx.SimitComprador;
        if (simit is not { Consultado: true })
            return GateResult.Block("simit_pendiente", "Consulta SIMIT del comprador obligatoria antes de continuar");
        if (TramiteDocumento.Normalizar(simit.Documento) != doc)
            return GateResult.Block("simit_doc", "La consulta SIMIT no corresponde al documento del comprador");
        // FEATURE 05 — los comparendos solo bloquean si la compañía los marcó bloqueantes para el OT
        // destino (default true = comportamiento previo). Si son informativos, se advierten en el
        // preflight (warn) pero no vetan el avance.
        if (simit.TotalComparendos > 0 && ctx.ComparendosBloquean)
            return GateResult.Block("simit_multas", "El comprador tiene comparendos SIMIT pendientes");

        return GateResult.Allowed;
    }

    private static bool PreflightBloquea(PreflightSnapshot? preflight, bool forzar)
    {
        if (forzar)
            return false;
        return preflight?.Overall == "red";
    }

    private static bool ImpuestoGateBloquea(PreflightSnapshot? preflight, bool pazSalvoVerificado, bool forzar)
    {
        if (forzar)
            return false;
        if (preflight is not { ImpuestoVehicularUnknown: true })
            return false;
        return !pazSalvoVerificado;
    }

    private static bool ParteCompleta(ParteDatos? parte) =>
        parte is not null &&
        !string.IsNullOrWhiteSpace(parte.Nombre) &&
        !string.IsNullOrWhiteSpace(parte.Documento) &&
        TramiteDocumento.EmailValido(parte.Email);

    private static bool RuntConsultado(RuntSnapshot? runt, string? documentoParte)
    {
        if (runt is not { Consultado: true })
            return false;
        var doc = TramiteDocumento.Normalizar(documentoParte);
        if (string.IsNullOrEmpty(doc))
            return false;
        return TramiteDocumento.Normalizar(runt.Documento) == doc;
    }
}
