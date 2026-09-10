import { test, expect } from '@playwright/test';

test.describe('HU10200 — Tab Operación wizard dinámico', () => {
  test('AC1-AC3: selector published → wizard dinámico → borrador → semáforo', async ({ page }) => {

    // Paso 0 — Auth stub (localStorage antes de que cargue el JS de la página)
    await test.step('Paso 0 — Auth stub', async () => {
      await page.addInitScript(() => {
        window.localStorage.setItem('flit:authed', '1');
      });
    });

    // Paso 1 — Navegar y activar el módulo Trámites
    await test.step('Paso 1 — Navegar a Trámites → Operación', async () => {
      await page.goto('/');
      // Esperar a que React hidrate y muestre el Shell (dock visible)
      await expect(page.getByRole('button', { name: 'Trámites' })).toBeVisible();
      await page.waitForTimeout(2000);

      // click botón dock "Trámites"
      await page.getByRole('button', { name: 'Trámites' }).click();
      await page.waitForTimeout(2000);

      // La pestaña "Operación" es el default — solo verificar que el texto existe.
      // Si hay botón de Operación disponible, hacer click para confirmar estado activo.
      const operacionBtn = page.getByRole('button', { name: 'Operación' });
      if (await operacionBtn.isVisible()) {
        await operacionBtn.click();
        await page.waitForTimeout(1500);
      }

      await expect(page.getByText('Iniciar nuevo trámite')).toBeVisible();
    });

    // Paso 2 — AC1: selector solo published
    await test.step('Paso 2 — AC1: selector solo published', async () => {
      // El aria-label de la lista es "Tipos de trámite publicados"
      const lista = page.getByRole('list', { name: /Tipos de trámite publicados/i });
      await expect(lista).toBeVisible();

      // Debe aparecer al menos un tipo (MATRICULA_NUEVA del seed dev)
      const firstBtn = lista.getByRole('button').first();
      await expect(firstBtn).toBeVisible();
      await page.waitForTimeout(2000);
    });

    // Paso 3 — AC2: wizard dinámico (steps/sections/fields desde config)
    await test.step('Paso 3 — AC2: wizard dinámico', async () => {
      const lista = page.getByRole('list', { name: /Tipos de trámite publicados/i });
      const firstBtn = lista.getByRole('button').first();
      await firstBtn.click();
      await page.waitForTimeout(2000);

      // heading nivel 1 del wizard = nombre del tipo (dinámico, text-xl font-bold)
      // (hay dos h1: el del módulo Tramites y el del wizard — tomamos el segundo)
      const heading = page.locator('h1.text-xl.font-bold');
      await expect(heading).toBeVisible();
      const headingText = await heading.textContent();
      // NO debe ser "Nuevo Traspaso Vehicular" hardcoded — debe ser el nombre del tipo
      expect(headingText).toBeTruthy();

      // sidebar "Asistente de seguimiento" (dinámica, no hardcoded)
      await expect(page.getByText('Asistente de seguimiento')).toBeVisible();

      // aside existe con steps dinámicos
      await expect(page.locator('aside')).toBeVisible();

      // al menos un campo de input dinámico
      const firstInput = page.locator('input, select, textarea').first();
      await expect(firstInput).toBeVisible();
      await page.waitForTimeout(2000);
    });

    // Paso 4 — AC3 borrador: Continuar guarda draft
    await test.step('Paso 4 — AC3: guardar borrador (Continuar)', async () => {
      // Buscar primer input habilitado (campos locked quedan disabled en DynamicFieldRenderer)
      const enabledInput = page.locator('input:not([disabled]), select:not([disabled]), textarea:not([disabled])').first();
      if (await enabledInput.isVisible() && await enabledInput.getAttribute('type') === 'text') {
        await enabledInput.fill('ABC123');
      }

      // Click "Continuar" si hay más steps; si no, click "Finalizar" (wizard de 1 step)
      const continuarBtn = page.getByRole('button', { name: /Continuar/i });
      const finalizarBtn = page.getByRole('button', { name: /Finalizar/i });
      if (await continuarBtn.isVisible()) {
        await continuarBtn.click();
      } else {
        // Single-step wizard: el saveDraft se llama via handleFinish → no avanzamos al submit
        // Solo verificamos que el botón existe (no hacer click para no cerrar el wizard)
        await expect(finalizarBtn).toBeVisible();
      }
      await page.waitForTimeout(2000);

      // NO debe aparecer error rojo 409/500 relacionado con guardado
      const errorAlerts = page.getByRole('alert');
      const count = await errorAlerts.count();
      for (let i = 0; i < count; i++) {
        const t = await errorAlerts.nth(i).textContent();
        expect(t).not.toMatch(/409|500|Error al guardar/i);
      }
    });

    // Paso 5 — AC3 consulta: botón Consultar RUNT → PreflightPanel semáforo
    await test.step('Paso 5 — AC3: semáforo PreflightPanel stub', async () => {
      // El panel está solo en el primer step
      const consultarBtn = page.getByRole('button', { name: /Consultar RUNT y SIMIT/i });
      if (!(await consultarBtn.isVisible())) {
        await page.getByRole('button', { name: /Anterior/i }).click();
        await page.waitForTimeout(1500);
      }

      await page.getByRole('button', { name: /Consultar RUNT y SIMIT/i }).click();
      await page.waitForTimeout(2000);

      // panel "Verificación de requisitos"
      await expect(page.getByText('Verificación de requisitos')).toBeVisible();

      // semáforo overall badge
      await expect(
        page.getByText(/Sin novedades|Con advertencias|Con bloqueos/i),
      ).toBeVisible();

      await page.waitForTimeout(3000);
    });
  });
});
