import { expect, test as setup } from "@playwright/test";
import { SESION } from "./playwright.config";

// Entra una vez con el formulario de ingreso (hub o app) y guarda la sesión para todos los perfiles.
// Usuarios seed locales (demo/admin/otadmin): nunca usuarios reales.
setup("ingresar y guardar sesión", async ({ page }) => {
  await page.goto(process.env.RESP_LOGIN_URL!);
  const usuario = page
    .locator('input[autocomplete="username"], input[type="email"], input[name="email"], input[name="username"]')
    .first();
  await usuario.fill(process.env.RESP_USUARIO!);
  await page.locator('input[type="password"]').first().fill(process.env.RESP_CLAVE!);
  await page.locator('input[type="password"]').first().press("Enter");
  await expect(page).not.toHaveURL(/\/(login|ingreso)(\?|$)/, { timeout: 45_000 });
  await page.context().storageState({ path: SESION });
});
