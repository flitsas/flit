namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Política de complejidad de contraseñas (HU #10171, RNF02): longitud mínima y al menos
/// una mayúscula, una minúscula y un dígito.
/// </summary>
public static class PasswordPolicy
{
    public const int DefaultMinLength = 8;

    public static bool IsCompliant(string password, int minLength = DefaultMinLength)
    {
        if (string.IsNullOrEmpty(password) || password.Length < minLength)
            return false;

        bool hasUpper = false, hasLower = false, hasDigit = false;
        foreach (var c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
        }

        return hasUpper && hasLower && hasDigit;
    }
}
