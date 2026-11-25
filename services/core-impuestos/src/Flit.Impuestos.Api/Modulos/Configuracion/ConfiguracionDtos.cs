using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Configuracion;

/// <summary>Respuesta de <c>GET/PUT /configuracion</c> y del PUT contractual (esquema <c>Configuracion</c> de impuestos.v1.yaml).</summary>
public sealed record ConfiguracionDto(ConfiguracionContractualDto Contractual, ConfiguracionOperativaDto Operativa, IReadOnlyList<DepartamentoDto> DepartamentosDisponibles);

/// <summary>Nivel contractual: lo fija el Super Admin. <see cref="DepartamentosHabilitados"/> null = todos los activos de /fuentes.</summary>
public sealed record ConfiguracionContractualDto(int MaxRegistrosLote, IReadOnlyList<string>? DepartamentosHabilitados);

/// <summary>Nivel operativo: lo fija el Administrador Impuestos. <see cref="MaxRegistrosLote"/> null = el contractual.</summary>
public sealed record ConfiguracionOperativaDto(int? MaxRegistrosLote, IReadOnlyList<string> CorreosAviso);

/// <summary>Un departamento del proveedor (esquema <c>Departamento</c>): código DANE de 2 cifras, nombre y si está activo.</summary>
public sealed record DepartamentoDto(string Codigo, string Nombre, bool Activa);

/// <summary>Cuerpo de <c>PUT /configuracion</c> (esquema <c>ConfiguracionOperativaEntrada</c>).</summary>
public sealed record ConfiguracionOperativaEntrada(int? MaxRegistrosLote, IReadOnlyList<string>? CorreosAviso);

/// <summary>Cuerpo del PUT contractual (esquema <c>ConfiguracionContractualEntrada</c>).</summary>
public sealed record ConfiguracionContractualEntrada(int? MaxRegistrosLote, IReadOnlyList<string>? DepartamentosHabilitados);

/// <summary>
/// Errores de regla de configuración y catálogos con el literal exacto de <c>x-flit-literales.errores</c> del contrato
/// (impuestos.v1.yaml 1.1.0): el front muestra el <c>error</c> tal cual. Los errores de <c>?empresa</c> (<c>EMPRESA_REQUERIDA</c>,
/// <c>EMPRESA_SOLO_SUPER_ADMIN</c>) son de <see cref="Comun.EmpresaExplicita"/>; <c>EMPRESA_NO_PERMITIDA</c> quedó retirado.
/// </summary>
internal static class ErroresConfiguracion
{
    public const string TopeInvalido = "TOPE_INVALIDO";
    public const string TopeSuperaContractual = "TOPE_SUPERA_CONTRACTUAL";
    public const string CorreoInvalido = "CORREO_INVALIDO";
    public const string DepartamentoNoDisponible = "DEPARTAMENTO_NO_DISPONIBLE";
    public const string AprovisionamientoNoListo = "APROVISIONAMIENTO_NO_LISTO";
    public const string ProveedorNoDisponible = "PROVEEDOR_NO_DISPONIBLE";
    public const string EmpresaNoEncontrada = "EMPRESA_NO_ENCONTRADA";

    public static ReglaException TopeInvalidoEn(string campo) =>
        new(TopeInvalido, "El máximo por lote debe ser un número entre 1 y 5000.") { Campo = campo };

    public static ReglaException SuperaContractual(int tope) =>
        new(TopeSuperaContractual, $"El máximo por lote no puede superar el tope contractual de {tope}.") { Campo = "maxRegistrosLote", Tope = tope };

    public static ReglaException CorreoInvalidoEn(string campo) => new(CorreoInvalido, "Un correo no es válido.") { Campo = campo };

    public static ReglaException DepartamentoNoDisponibleEn(string campo) =>
        new(DepartamentoNoDisponible, "Un departamento no está en la lista del proveedor.") { Campo = campo };

    public static ReglaException NoListo() =>
        ReglaException.Conflicto(AprovisionamientoNoListo, "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.");

    public static ReglaException SinProveedor() =>
        new(ProveedorNoDisponible, "El proveedor no responde. Intenta más tarde.", StatusCodes.Status503ServiceUnavailable);

    public static ReglaException NoEncontrada() =>
        new(EmpresaNoEncontrada, "La compañía no existe o no tiene Impuestos.", StatusCodes.Status404NotFound);
}
