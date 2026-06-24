namespace Flit.Admin.Domain.OtProfile;

/// <summary>Modos de operación del perfil OT (HU #10215).</summary>
public static class OtOperationModes
{
    public const string Dashboard = "dashboard";
    public const string Quipux = "quipux";

    public static bool IsValid(string? value) =>
        value is Dashboard or Quipux;
}
