using System;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Gates del wizard de MATRÍCULA INICIAL (5 pasos, 1 actor = comprador), puros.
/// Equivalente conceptual a <see cref="TraspasoGates"/> para la modalidad VIN-first.
/// Johan no expone gates dedicados de matrícula; se derivan de la matriz de pasos
/// (5 pasos: VIN, documentos, comprador, identidad, FUR).
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
                return ctx.VehiculoConsultado
                    ? GateResult.Allowed
                    : GateResult.Block("vin_pendiente", "Consulta el VIN del vehículo antes de continuar");

            case 2:
                if (!forzar && !ctx.RiesgoPreflightAceptado && ctx.Preflight?.Overall == "red")
                    return GateResult.Block("preflight_red", "Hay bloqueos críticos en los documentos. Subsana antes de continuar");
                if (!ctx.DocumentosObligatoriosCompletos)
                    return GateResult.Block("documentos_incompletos", "Sube los documentos obligatorios antes de continuar");
                return GateResult.Allowed;

            case 3:
                if (!ParteCompleta(ctx.Comprador))
                    return GateResult.Block("comprador_incompleto", "Completa nombre, documento y email del comprador");
                if (!RuntConsultado(ctx.RuntComprador, ctx.Comprador?.Documento))
                    return GateResult.Block("runt_comprador", "Consulta RUNT del comprador antes de continuar");
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
