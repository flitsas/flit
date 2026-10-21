import { test, expect } from '@playwright/test';

/**
 * HU #13729 (F00.1-H6) — loader de Trámites sin regresión tras mover `.flit-loader` / `.flit-escena-*`
 * de `frontend/app/globals.css` a `@flit/ui/loader.css`.
 *
 * Script de auditoría contra `playwright.audit.config.ts` (no corre en CI), como el resto de
 * `e2e-audit/`. Requiere Trámites corriendo en `http://localhost:3000`.
 *
 * Procedimiento (captura antes y después):
 *   1. En `develop` (antes del cambio): `pnpm exec playwright test -c playwright.audit.config.ts
 *      e2e-audit/loader-regresion.spec.ts --update-snapshots` — guarda la línea base.
 *   2. En la rama de la HU: el mismo comando sin `--update-snapshots` — debe coincidir.
 *
 * La página monta el mismo marcado de `VehicleLoader` (`@flit/ui/Loader`) sobre el CSS real de la app
 * (la hoja que Next sirve para `/login`), y `toHaveScreenshot` congela las animaciones CSS en su
 * estado inicial para que la captura sea determinista.
 */
test.describe('loader de Trámites sin regresión', () => {
  test('captura igual a la línea base', async ({ page }) => {
    await page.goto('/login');
    await page.evaluate(() => {
      const host = document.createElement('div');
      host.id = 'loader-regresion';
      host.style.cssText = 'position:fixed;inset:0;z-index:99999;display:grid;place-items:center;background:var(--color-background);';
      host.innerHTML = `
        <div style="display:flex;gap:48px;align-items:center">
          <div class="flit-loader" role="status" aria-label="Procesando…">
            <div class="flit-vehicle"><svg width="32" height="32" viewBox="0 0 24 24"><rect x="2" y="9" width="20" height="8" rx="2" fill="currentColor"/></svg></div>
          </div>
          <svg width="128" height="128" viewBox="0 0 300 300">
            <g class="flit-escena-orbita" style="transform-origin:150px 150px"><circle cx="150" cy="150" r="140" fill="none" style="stroke:var(--color-flit-brand)" stroke-width="4"/></g>
            <g class="flit-escena-vehiculo" style="transform-origin:150px 150px"><rect x="140" y="20" width="20" height="12" style="fill:var(--color-flit-tech)"/></g>
          </svg>
        </div>`;
      document.body.appendChild(host);
    });
    await expect(page.locator('#loader-regresion')).toHaveScreenshot('loader-tramites.png', {
      animations: 'disabled',
    });
  });
});
