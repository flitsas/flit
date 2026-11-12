using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>Celda ya leída. El estado manda: si no está completada, la lectura se ignora.</summary>
public sealed record LecturaCelda<T>(EstadoCelda Estado, T? Lectura);

/// <summary>
/// Entrada del lector. Las lecturas ya vienen armadas: este tipo no descifra ni llama proveedores.
/// El impuesto puede faltar; no es fuente obligatoria.
/// </summary>
public sealed record PedidoLectura(
    string TipoTramiteCodigo,
    DateOnly Hoy,
    LecturaCelda<DetalleRuntReporte>? RuntDetalle,
    LecturaCelda<VehiculoRunt>? RuntNormalizado,
    LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitVehiculo,
    LecturaCelda<IReadOnlyList<ResultadoRuntPersonaDetalle>>? RuntPropietario,
    LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitPropietario,
    LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitLocatario,
    LecturaCelda<IReadOnlyList<ResultadoRuntPersonaDetalle>>? RuntLocatario,
    LecturaCelda<ResultadoImpuestoDetalle>? Impuesto);

/// <summary>
/// Arma <see cref="HechosDictamen"/> desde lecturas ya proyectadas. No evalúa reglas y no persiste.
/// </summary>
public static class LectorHechosDictamen
{
    private static readonly string[] LicenciaPeor =
    [
        "SUSPENDIDA", "SUSPENDIDO", "CANCELADA", "CANCELADO", "VENCIDA", "VENCIDO",
    ];

    /// <summary>Traduce el pedido. Un pedido null no se lee.</summary>
    public static HechosDictamen Leer(PedidoLectura pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var (estadoRunt, detalle) = Simple(pedido.RuntDetalle);
        var (estadoSimitVehiculo, simitVehiculo) = Lista(pedido.SimitVehiculo, true);
        var (estadoRuntPropietario, personasPropietario) = Lista(pedido.RuntPropietario, false);
        var (estadoSimitPropietario, simitPropietario) = Lista(pedido.SimitPropietario, true);
        var (_, simitLocatario) = Lista(pedido.SimitLocatario, true);
        var (estadoLocatario, personasLocatario) = Lista(pedido.RuntLocatario, false);
        var impuesto = Impuesto(pedido.Impuesto);

        return new HechosDictamen(
            pedido.TipoTramiteCodigo,
            pedido.Hoy,
            estadoRunt,
            estadoSimitVehiculo,
            estadoRuntPropietario,
            estadoSimitPropietario,
            estadoRunt == EstadoFuenteDictamen.Disponible && detalle is not null ? Runt(detalle, pedido) : null,
            simitVehiculo is null ? null : ResumirSimit(simitVehiculo),
            simitPropietario is null ? null : ResumirSimit(simitPropietario),
            simitLocatario is null ? null : ResumirSimit(simitLocatario),
            personasPropietario is null ? null : ResumirLicencia(personasPropietario, pedido.Hoy),
            estadoLocatario == EstadoFuenteDictamen.Disponible && personasLocatario is not null
                ? ResumirLicencia(personasLocatario, pedido.Hoy)
                : null,
            impuesto.Monto,
            impuesto.TieneDeuda,
            impuesto.Estado);
    }

    private static HechosRuntVehiculo Runt(DetalleRuntReporte detalle, PedidoLectura pedido)
    {
        var normalizado = pedido.RuntNormalizado is { Estado: EstadoCelda.Completado, Lectura: { } vehiculo } ? vehiculo : null;
        return new HechosRuntVehiculo(
            detalle.General.EstadoVehiculo,
            Fecha(detalle.Vehiculo.FechaMatricula),
            normalizado?.Propietarios.Any(p => p.TipoPropiedad == TipoPropiedad.Leasing) == true,
            normalizado?.Locatarios.Any(l => !string.IsNullOrWhiteSpace(l.NumeroDocumento)) == true,
            detalle.Soat.Select(s => s.Estado).ToArray(),
            detalle.Rtm.Select(r => r.Estado).ToArray(),
            detalle.Limitaciones?.Count ?? 0,
            Si(detalle.General.TieneGravamenes),
            detalle.GarantiasMobiliarias?.Count ?? 0,
            Si(detalle.General.Prendas),
            detalle.Infracciones?.Count ?? 0,
            Clase(detalle.Vehiculo, normalizado),
            ClasificadorVehiculoDictamen.Servicio(Texto(detalle.Vehiculo.TipoServicio) ?? normalizado?.Servicio));
    }

    /// <summary>
    /// Clase del dictamen (ola 2026-11-11, V4/V5): la clase del detalle RUNT o, si viene vacía, la del RUNT normalizado; luego
    /// la clasificación; y si nada dice, el tipo de la placa (maquinaria o remolque).
    /// </summary>
    private static ClaseVehiculoDictamen Clase(DetalleRuntVehiculo vehiculo, VehiculoRunt? normalizado)
    {
        var placa = Texto(vehiculo.Placa) ?? normalizado?.Placa;
        var tipoPlaca = placa is null ? null : ValidadorIdentificador.Validar(placa).Tipo;
        return ClasificadorVehiculoDictamen.Clase(Texto(vehiculo.Clase) ?? normalizado?.Clase, vehiculo.Clasificacion, tipoPlaca);
    }

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor;

    private static HechosSimit ResumirSimit(IReadOnlyList<ResultadoSimitDetalle> personas)
    {
        var cantidad = 0;
        bool? paz = null;
        var suspendida = false;
        var cancelada = false;
        var acuerdos = new List<AcuerdoPago>();
        foreach (var persona in personas)
        {
            if (persona is null)
                continue;
            cantidad += persona.CantidadComparendos;
            if (persona.PazYSalvo == false)
                paz = false;
            else if (persona.PazYSalvo == true && paz is null)
                paz = true;
            suspendida |= persona.Suspendida == true;
            cancelada |= persona.Cancelada == true;
            foreach (var acuerdo in persona.Acuerdos ?? [])
                acuerdos.Add(new AcuerdoPago(Saldo(acuerdo.Pendiente)));
        }

        return new HechosSimit(cantidad, paz, suspendida, cancelada, acuerdos);
    }

    private static LicenciaConductor ResumirLicencia(IReadOnlyList<ResultadoRuntPersonaDetalle> personas, DateOnly hoy)
    {
        if (personas.Count == 0)
            return new LicenciaConductor(null, false);

        string? peor = null;
        var indice = int.MaxValue;
        string? primero = null;
        foreach (var persona in personas)
        {
            if (persona is null)
                continue;
            foreach (var licencia in persona.Licencias ?? [])
            {
                var normalizado = Normalizar(licencia.Estado);
                if (primero is null && normalizado.Length > 0)
                    primero = licencia.Estado;
                var puesto = Array.IndexOf(LicenciaPeor, normalizado);
                if (puesto >= 0 && puesto < indice)
                {
                    indice = puesto;
                    peor = licencia.Estado;
                }
            }
        }

        var vigentes = personas.Where(p => p is not null).All(p => (p.Certificados ?? []).Any(c => CertificadoVigente(c, hoy)));
        return new LicenciaConductor(peor ?? primero, personas.Any(p => p is not null) && vigentes);
    }

    private static bool CertificadoVigente(CertificadoAptitudDetalle certificado, DateOnly hoy)
    {
        var estado = Normalizar(certificado.Estado);
        if (estado is "VENCIDO" or "VENCIDA" or "CANCELADO" or "CANCELADA")
            return false;
        if (estado == "VIGENTE")
            return true;
        if (estado.Length > 0)
            return false;
        return Fecha(certificado.Vence) is { } vence && vence >= hoy;
    }

    /// <summary>
    /// Fuera del alcance (sin celda) no tiene estado. N/A no es pendiente. ERROR sí. Un monto positivo sin bandera
    /// sigue siendo deuda, para las celdas anteriores a <c>tiene_deuda</c>.
    /// </summary>
    private static (bool TieneDeuda, long? Monto, EstadoFuenteDictamen? Estado) Impuesto(LecturaCelda<ResultadoImpuestoDetalle>? celda)
    {
        if (celda is null)
            return (false, null, null);
        if (celda.Estado == EstadoCelda.Error)
            return (false, null, EstadoFuenteDictamen.Error);
        if (celda.Estado == EstadoCelda.Na)
            return (false, null, EstadoFuenteDictamen.NoAplica);
        if (celda.Estado != EstadoCelda.Completado || celda.Lectura is not { } lectura)
            return (false, null, EstadoFuenteDictamen.SinResultado);
        var monto = lectura.MontoTotal is > 0 ? lectura.MontoTotal : null;
        return (lectura.TieneDeuda || monto is not null, monto, EstadoFuenteDictamen.Disponible);
    }

    private static (EstadoFuenteDictamen Estado, T? Valor) Simple<T>(LecturaCelda<T>? celda)
        where T : class
    {
        if (celda is null)
            return (EstadoFuenteDictamen.SinResultado, null);
        if (celda.Estado == EstadoCelda.Error)
            return (EstadoFuenteDictamen.Error, null);
        if (celda.Estado == EstadoCelda.Na)
            return (EstadoFuenteDictamen.NoAplica, null);
        if (celda.Estado != EstadoCelda.Completado || celda.Lectura is null)
            return (EstadoFuenteDictamen.SinResultado, null);
        return (EstadoFuenteDictamen.Disponible, celda.Lectura);
    }

    private static (EstadoFuenteDictamen Estado, IReadOnlyList<T>? Valor) Lista<T>(
        LecturaCelda<IReadOnlyList<T>>? celda, bool vaciaEsSinResultado)
    {
        var (estado, valor) = Simple(celda);
        if (vaciaEsSinResultado && estado == EstadoFuenteDictamen.Disponible && valor is { Count: 0 })
            return (EstadoFuenteDictamen.SinResultado, null);
        return (estado, valor);
    }

    private static bool Si(string? valor) => string.Equals(valor?.Trim(), "SI", StringComparison.OrdinalIgnoreCase);

    private static DateOnly? Fecha(string? texto) =>
        DateOnly.TryParseExact(texto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;

    private static long Saldo(string? pendiente)
    {
        if (string.IsNullOrWhiteSpace(pendiente))
            return 1;
        var texto = pendiente.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (long.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var entero))
            return entero;
        if (decimal.TryParse(texto.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var conDecimal)
            && conDecimal is >= long.MinValue and <= long.MaxValue)
            return (long)decimal.Truncate(conDecimal);
        return 1;
    }

    private static string Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim().Replace('_', ' ').ToUpperInvariant();
}
