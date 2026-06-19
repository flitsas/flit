// Política de complejidad en cliente (espejo del backend PasswordPolicy, HU #10171):
// mínimo 8 caracteres con al menos una mayúscula, una minúscula y un dígito.
export const PASSWORD_MIN_LENGTH = 8;

export const PASSWORD_POLICY_HINT =
  "Mínimo 8 caracteres, con al menos una mayúscula, una minúscula y un número.";

export function isPasswordCompliant(password: string, minLength = PASSWORD_MIN_LENGTH): boolean {
  if (!password || password.length < minLength) {
    return false;
  }
  return /[A-Z]/.test(password) && /[a-z]/.test(password) && /\d/.test(password);
}
