using System.Diagnostics.CodeAnalysis;
using System.Text;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>Un locatario del RUNT con su nombre, en claro (solo para el Excel del lote, D3). Tipo en código del producto (CC, NIT…).</summary>
public sealed record LocatarioRuntEnClaro(string? Tipo, string? Numero, string? Nombre)
{
    [SuppressMessage("Performance", "CA1822", Justification = "Firma que exige el compilador para los record: sin datos en ToString (SP-7).")]
    private bool PrintMembers(StringBuilder builder) => false;
}

/// <summary>Garantía a favor de un acreedor, en claro (D3: también el acreedor persona natural, sin la sustitución del PDF).</summary>
public sealed record GarantiaEnClaro(string? Acreedor, string? TipoDocumento, string? NumeroDocumento)
{
    [SuppressMessage("Performance", "CA1822", Justification = "Firma que exige el compilador para los record: sin datos en ToString (SP-7).")]
    private bool PrintMembers(StringBuilder builder) => false;
}

/// <summary>Garantía mobiliaria, en claro (D3).</summary>
public sealed record GarantiaMobiliariaEnClaro(string? Entidad, string? TipoDocumento, string? NumeroDocumento, string? Estado, string? FechaRegistro)
{
    [SuppressMessage("Performance", "CA1822", Justification = "Firma que exige el compilador para los record: sin datos en ToString (SP-7).")]
    private bool PrintMembers(StringBuilder builder) => false;
}

/// <summary>
/// Proyección del RUNT del vehículo para el Excel del lote (HU #14752, contrato-excel §3.2): el mismo <see cref="DetalleRuntReporte"/> del
/// PDF (sin cambio de proyección) más lo que solo el Excel lleva en claro (D3): los locatarios con su nombre, las garantías a favor y las
/// mobiliarias con acreedor y documento completos, y <c>informacion_blindaje.nivel_blindaje_numero</c>. Nada más de la respuesta (SP-11).
/// Se arma en memoria al generar y no se guarda (SP-1); <see cref="object.ToString"/> no imprime los datos (SP-7).
/// </summary>
/// <example>
/// <code>
/// var runt = LectorDetalleRunt.LeerRespuesta(protector, cifrada) is { } r ? DatosRuntExcel.Desde(r) : null;
/// </code>
/// </example>
public sealed record DatosRuntExcel(
    DetalleRuntReporte Detalle,
    IReadOnlyList<LocatarioRuntEnClaro> Locatarios,
    IReadOnlyList<GarantiaEnClaro> GarantiasAFavor,
    IReadOnlyList<GarantiaMobiliariaEnClaro> GarantiasMobiliarias,
    string? NivelBlindajeNumero)
{
    /// <summary>La proyección de la respuesta; null si no es ENCONTRADO con vehículo (lo mismo que <see cref="LectorDetalleRunt.Desde"/>).</summary>
    internal static DatosRuntExcel? Desde(ConsultarVehiculoIntempoResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (LectorDetalleRunt.Desde(respuesta) is not { } detalle)
            return null;
        var v = respuesta.Vehiculo;

        // Primero lessee[] y luego propietarios_actuales[].locatarios[] (contrato-excel §2.1, columna 5).
        var locatarios = v.Lessee
            .Select(l => new LocatarioRuntEnClaro(Tipo(l.DocumentType), Numero(l.DocumentNumber),
                T(l.Name) ?? T(string.Join(' ', new[] { l.FirstName, l.SecondName, l.FirstLastName, l.SecondLastName }.Select(T).OfType<string>()))))
            .Concat(v.PropietariosActuales.SelectMany(p => p.Locatarios)
                .Select(l => new LocatarioRuntEnClaro(Tipo(l.Tdoc), Numero(l.Ndoc), T(l.NombreRazonSocial))))
            .Where(l => l.Tipo is not null || l.Numero is not null || l.Nombre is not null)
            .ToList();
        var aFavor = v.GarantiasFavorDe
            .Select(g => new GarantiaEnClaro(T(g.Acreedor), Tipo(g.TipoDocumentoAcreedor), T(g.NumeroDocumentoAcreedor)))
            .Where(g => g.Acreedor is not null || g.TipoDocumento is not null || g.NumeroDocumento is not null)
            .ToList();
        var mobiliarias = v.GarantiasMobiliarias
            .Select(g => new GarantiaMobiliariaEnClaro(T(g.Entidad), Tipo(g.TipoDocumentoEntidad), T(g.NumeroDocumentoEntidad), T(g.Estado), T(g.FechaRegistro)))
            .Where(g => g != new GarantiaMobiliariaEnClaro(null, null, null, null, null))
            .ToList();
        return new DatosRuntExcel(detalle, locatarios, aFavor, mobiliarias, T(v.InformacionBlindaje?.NivelBlindajeNumero));
    }

    /// <summary>El número sin espacios (la forma con la que se compara con el locatario del vehículo).</summary>
    internal static string? Numero(string? numero) =>
        T(numero) is { } texto ? new string([.. texto.Where(c => !char.IsWhiteSpace(c))]) : null;

    private static string? Tipo(string? texto) => T(texto) is { } tipo ? MapperIntempo.TipoDocumentoDeTexto(tipo) : null;

    private static string? T(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    [SuppressMessage("Performance", "CA1822", Justification = "Firma que exige el compilador para los record: sin datos en ToString (SP-7).")]
    private bool PrintMembers(StringBuilder builder) => false;
}

/// <summary>
/// Lo que solo el Excel del lote lleva de cada vehículo (HU #14752, D3; ADR-0076 enmienda 2026-11-27), descifrado en memoria por
/// <see cref="LectorContenidoReporte"/> al generar: tipo y número del propietario del RUNT, tipo, número y nombre del locatario (solo el
/// informado por el RUNT), la proyección <see cref="DatosRuntExcel"/> y si la celda RUNT está Completado sin respuesta legible
/// (<see cref="RuntSinDetalle"/>: aviso D4). No entra a <see cref="ContenidoReporteLote.ParaHuella"/> (SP-2) y
/// <see cref="object.ToString"/> no imprime los datos (SP-7). Un número que no se pudo descifrar es null (celda vacía).
/// </summary>
/// <example>
/// <code>
/// var excel = new DatosExcelVehiculo("CC", "1012345678", "CC", "1098765432",
///     DatosExcelVehiculo.NombreDelLocatario(runt, "CC", "1098765432"), runt, RuntSinDetalle: runt is null);
/// </code>
/// </example>
public sealed record DatosExcelVehiculo(
    string? PropietarioTipo,
    string? PropietarioNumero,
    string? LocatarioTipo,
    string? LocatarioNumero,
    string? LocatarioNombre,
    DatosRuntExcel? Runt,
    bool RuntSinDetalle)
{
    /// <summary>
    /// El nombre del locatario del RUNT con el mismo tipo (código del producto) y el mismo número (sin espacios) que
    /// <paramref name="tipo"/> y <paramref name="numero"/>: la primera entrada con nombre, en el orden de <see cref="DatosRuntExcel.Locatarios"/>
    /// (lessee[] y luego propietarios_actuales[].locatarios[]). Null si falta algo o no casa ninguno.
    /// </summary>
    internal static string? NombreDelLocatario(DatosRuntExcel? runt, string? tipo, string? numero)
    {
        if (runt is null || string.IsNullOrWhiteSpace(tipo) || DatosRuntExcel.Numero(numero) is not { } buscado)
            return null;
        var codigo = MapperIntempo.TipoDocumentoDeTexto(tipo.Trim());
        return runt.Locatarios.FirstOrDefault(l =>
            l.Nombre is not null && string.Equals(l.Tipo, codigo, StringComparison.Ordinal) && string.Equals(l.Numero, buscado, StringComparison.Ordinal))?.Nombre;
    }

    [SuppressMessage("Performance", "CA1822", Justification = "Firma que exige el compilador para los record: sin datos en ToString (SP-7).")]
    private bool PrintMembers(StringBuilder builder) => false;
}
