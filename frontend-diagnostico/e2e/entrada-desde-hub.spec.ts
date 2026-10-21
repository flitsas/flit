import { expect, test } from "@playwright/test";

/**
 * HU #13732 (F00.1-H7) — entrada a Diagnóstico desde el hub.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029 y esta
 * app en 4024) y el cliente OIDC `diagnostico` registrado en el hub (P4 + HU #13734). Excluido de `tsc` y de Vitest;
 * se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   E2E_USUARIO_SUSPENDIDO=... E2E_CLAVE_SUSPENDIDO=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/entrada-desde-hub.spec.ts
 *
 * Las credenciales son de usuarios de prueba del ambiente DEV y entran solo por variables de entorno.
 * El formulario que se llena es el del HUB: Diagnóstico no tiene uno propio.
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";

async function iniciarSesionEnElHub(page: import("@playwright/test").Page, usuario: string, clave: string) {
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel(/contraseña/i).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
}

test.describe("entrada desde el hub", () => {
  test("sin sesión va al hub y vuelve", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");

    await page.goto(`${APP}/`);

    // Escenario 1: el navegador termina en el authorize del hub, con client_id (aud) diagnostico y PKCE.
    await page.waitForURL((url) => !url.href.startsWith(APP));
    const authorize = page.url();
    expect(authorize).toContain("client_id=diagnostico");
    expect(authorize).toContain("code_challenge_method=S256");

    // Escenario 2: tras el login del hub vuelve a Diagnóstico con la sesión iniciada.
    await iniciarSesionEnElHub(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);
    await page.waitForURL(`${APP}/`);
    await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();

    // Escenario 3: en el subdominio no hay campos de usuario ni contraseña.
    await expect(page.locator('input[type="password"], input[autocomplete="username"]')).toHaveCount(0);
  });

  test("suscripción suspendida muestra 403", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO_SUSPENDIDO || !process.env.E2E_CLAVE_SUSPENDIDO, "Faltan E2E_USUARIO_SUSPENDIDO / E2E_CLAVE_SUSPENDIDO");

    await page.goto(`${APP}/`);
    await page.waitForURL((url) => !url.href.startsWith(APP));
    await iniciarSesionEnElHub(page, process.env.E2E_USUARIO_SUSPENDIDO!, process.env.E2E_CLAVE_SUSPENDIDO!);

    // Escenario 4: la compañía tiene Diagnóstico suspendido → la 403, nunca la pantalla base, y sin volver al login.
    await page.waitForURL(`${APP}/403?code=PRODUCTO_NO_HABILITADO`);
    await expect(page.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeVisible();
    await expect(page.getByText(/Diagnóstico no está habilitado/)).toBeVisible();
    await expect(page.getByRole("heading", { name: "Diagnóstico vehicular" })).toHaveCount(0);
  });
});
