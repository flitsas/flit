using System;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Gates del wizard de MATRÍCULA INICIAL (5 pasos, 1 actor = comprador), puros.
/// Equivalente conceptual a <see cref="TraspasoGates"/> para la modalidad VIN-first.
/// Johan no expone gates dedicados de matrícula; se derivan de la matriz de pasos.
///
/// <para>HU #10935 — orden del wizard: los documentos van DESPUÉS del actor
/// (VIN, comprador, documentos, identidad, FUR).</para>
/// </summary>
public static class MatriculaGates
{
    public const int TotalPasos = 5;

    /// <summary>Paso N (1–5) completado → puede avanzar a N+1.</summary>
    public static GateResult PasoCompleto(int paso, MatriculaGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        bool forzar = ctx.ForzarContinuar;

        switch (paso)
        {
            case 1:
                if (!ctx.VehiculoConsultado)
                    return GateResult.Block("vin_pendiente", "Consulta el VIN del vehículo antes de continuar");
                // Bloqueo DURO: si la consulta del vehículo no se pudo verificar (proveedor caído/
                // timeout), el paso 1 NO se completa aunque el VIN esté en field_values. Así no se
                // avanza con un dato vital sin verificar; hay que reejecutar la consulta.
                if (ctx.Preflight?.ProviderError == true)
                    return GateResult.Block("preflight_provider_error", "No fue posible verificar la información del vehículo en el RUNT. Vuelve a ejecutar la consulta antes de continuar");
                // Bloqueo DURO: el RUNT respondió y el vehículo NO existe. Sin vehículo no hay trámite,
                // así que no se subsana con "aceptar riesgo" ni forzando; hay que corregir el VIN.
                if (ctx.Preflight?.VehiculoNoEncontrado == true)
                    return GateResult.Block("vehiculo_no_encontrado", "El vehículo no se encontró en el RUNT. Verifica el VIN antes de continuar");
                return GateResult.Allowed;

            case 2:
                // HU #10935 — Paso 2 = Comprador (antes iba en el paso 3): parte + RUNT consultado.
                if (!ParteCompleta(ctx.Comprador))
                    return GateResult.Block("comprador_incompleto", "Completa nombre, documento y email del comprador");
                if (!RuntConsultado(ctx.RuntComprador, ctx.Comprador?.Documento))
                    return GateResult.Block("runt_comprador", "Consulta RUNT del comprador antes de continuar");
                return GateResult.Allowed;

            case 3:
                // HU #10935 — Paso 3 = Documentos, DESPUÉS del actor (antes iba en el paso 2).
                // Bloqueo DURO: si una consulta no se pudo verificar (proveedor caído/timeout), la
                // información es vital y NO se puede continuar ni "aceptando el riesgo" ni forzando.
                if (ctx.Preflight?.ProviderError == true)
                    return GateResult.Block("preflight_provider_error", "No fue posible verificar la información en el RUNT. Vuelve a ejecutar la consulta antes de continuar");
                // Mismo bloqueo DURO del paso 1: sin vehículo verificado no se avanza a documentos.
                if (ctx.Preflight?.VehiculoNoEncontrado == true)
                    return GateResult.Block("vehiculo_no_encontrado", "El vehículo no se encontró en el RUNT. Verifica el VIN antes de continuar");
                if (!forzar && !ctx.RiesgoPreflightAceptado && ctx.Preflight?.Overall == "red")
                    return GateResult.Block("preflight_red", "Hay bloqueos críticos en los documentos. Subsana antes de continuar");
                if (!ctx.DocumentosObligatoriosCompletos)
                    return GateResult.Block("documentos_incompletos", "Sube los documentos obligatorios antes de continuar");
                return GateResult.Allowed;

            case 4:
                return ctx.IdentidadAprobada || forzar
                    ? GateResult.Allowed
                    : GateResult.Block("identidad_pendiente", "Valida la identidad del comprador antes de continuar");

            case 5:
                return GateResult.Allowed;

            default:
                return GateResult.Block("paso_invalido", "Paso inválido");
        }
    }

    /// <summary>Máximo paso alcanzable según datos (1–5).</summary>
    public static int MaxPasoAlcanzable(MatriculaGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        for (int p = 1; p <= TotalPasos; p++)
        {
            if (!PasoCompleto(p, ctx).Ok)
                return p;
        }
        return TotalPasos;
    }

    /// <summary>Puede avanzar desde el paso actual.</summary>
    public static GateResult PuedeAvanzar(int pasoActual, MatriculaGateContext ctx) =>
        PasoCompleto(pasoActual, ctx);

    /// <summary>Paso ya validado y cerrado → solo lectura.</summary>
    public static bool PasoSoloLectura(int paso, MatriculaGateContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (paso < 1 || paso > TotalPasos)
            return false;
        return paso < MaxPasoAlcanzable(ctx);
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
