namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Configuración del proveedor Fasecolda (Feature #10707). Dos hosts: búsqueda por VIN
/// (sin auth) y guía de valores (token OAuth2 + consulta por código). Credenciales por
/// User Secrets / env — NUNCA en el repo.
/// </summary>
public sealed class FasecoldaOptions
{
    public const string SectionName = "Fasecolda";

    public string ByVinBaseUrl { get; set; } = "https://fasecoldaback.quantil.co";
    public string ByVinPath { get; set; } = "/api/busquedaVin";
    public string ApiBaseUrl { get; set; } = "https://guiadevalores.fasecolda.com/apifasecolda";
    public string AuthPath { get; set; } = "/token";
    public string ListCodePath { get; set; } = "/api/listacodigosid/consultabycodigo";
    public string GrantType { get; set; } = "password";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 55;
}
