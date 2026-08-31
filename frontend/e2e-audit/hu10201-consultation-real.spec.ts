import { test, expect } from '@playwright/test';

/**
 * HU #10201 — E2E auditoría T27
 * Valida POST .../consultations/RUNT_VEHICLE (API real, no stub cliente)
 * y panel semáforo PreflightPanel.
 */
test.describe('HU10201 — consulta real multi-proveedor', () => {
  test('AC2+AC3: wizard → consulta API → semáforo', async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage.setItem('flit:authed', '1');
    });

    await test.step('Navegar a Trámites → Operación', async () => {
      await page.goto('/');
      await expect(page.getByRole('button', { name: 'Trámites' })).toBeVisible();
      await page.getByRole('button', { name: 'Trámites' }).click();
      await expect(page.getByText('Iniciar nuevo trámite')).toBeVisible();
    });

    await test.step('Iniciar wizard desde tipo published', async () => {
      const lista = page.getByRole('list', { name: /Tipos de trámite publicados/i });
      await expect(lista).toBeVisible();
      await lista.getByRole('button').first().click();
      await expect(page.locator('h1.text-xl.font-bold')).toBeVisible();
      await page.waitForTimeout(1500);
    });

    await test.step('Opcional: llenar primer input texto habilitado', async () => {
      const textInput = page.locator('input[type="text"]:not([disabled])').first();
      if (await textInput.isVisible()) {
        await textInput.fill('ABC123');
      }
    });

    await test.step('Consultar RUNT vía API real', async () => {
      const consultarBtn = page.getByRole('button', { name: /Consultar RUNT/i });
      if (!(await consultarBtn.isVisible())) {
        await page.getByRole('button', { name: /Anterior/i }).click();
        await page.waitForTimeout(1000);
      }

      const responsePromise = page.waitForResponse(
        (res) =>
          res.request().method() === 'POST' &&
          res.url().includes('/consultations/'),
        { timeout: 25000 },
      );

      await page.getByRole('button', { name: /Consultar RUNT/i }).click();
      const response = await responsePromise;
      expect(response.status()).toBeLessThan(500);
    });

    await test.step('Panel semáforo visible', async () => {
      await expect(page.getByText('Verificación de requisitos')).toBeVisible();
      await expect(
        page.getByText(/Sin novedades|Con advertencias|Con bloqueos/i),
      ).toBeVisible();
    });
  });
});
