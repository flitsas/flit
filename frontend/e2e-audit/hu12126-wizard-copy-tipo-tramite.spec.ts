import { test, expect } from '@playwright/test';

/**
 * HU #12126 — WIZARD Otros Trámites: mostrar copy configurado del tipo de trámite.
 *
 * Requiere un entorno con backend real (core-api) y catálogo seed que incluya, para esta
 * corrida, al menos:
 *  - Un tipo con `description` configurado (vía configurador HU #12125, pestaña Identidad) para
 *    verificar AC1.
 *  - Un tipo SIN `description` configurado para verificar AC2 (franja ausente, sin texto de
 *    relleno).
 *
 * Como `e2e-audit/hu10200-operacion.spec.ts`, es un script de auditoría manual (headless: false,
 * video/screenshot on) contra `playwright.audit.config.ts` — no está en el pipeline de CI.
 */
test.describe('HU12126 — Copy configurado del tipo de trámite en el selector', () => {
  test('AC1/AC2: la franja informativa refleja el description del catálogo, no texto fijo', async ({
    page,
  }) => {
    await test.step('Paso 0 — Auth stub', async () => {
      await page.addInitScript(() => {
        window.localStorage.setItem('flit:authed', '1');
      });
    });

    await test.step('Paso 1 — Navegar a Trámites → Operación → Nuevo trámite', async () => {
      await page.goto('/');
      await expect(page.getByRole('button', { name: 'Trámites' })).toBeVisible();
      await page.waitForTimeout(2000);
      await page.getByRole('button', { name: 'Trámites' }).click();
      await page.waitForTimeout(1500);

      const operacionBtn = page.getByRole('button', { name: 'Operación' });
      if (await operacionBtn.isVisible()) {
        await operacionBtn.click();
        await page.waitForTimeout(1000);
      }

      await page.getByText('Iniciar nuevo trámite').click();
      await page.waitForTimeout(1500);
    });

    await test.step('Paso 2 — AC1: elegir una tarjeta con description configurado muestra el copy', async () => {
      // La franja siempre está reservada (min-h-[56px]); solo cambia si tiene texto.
      const franja = page.getByRole('status');
      await expect(franja).toBeVisible();

      await page.getByRole('button', { name: /Matrícula Inicial/ }).click();
      await page.getByRole('option', { name: 'Matrícula Tradicional' }).click();
      await page.waitForTimeout(500);

      // El copy debe venir del catálogo (Description), no del texto hardcodeado retirado en esta
      // HU. No se afirma un texto literal porque depende de lo cargado en el configurador — solo
      // que la franja NO esté vacía cuando el tipo tiene description.
      const texto = await franja.textContent();
      expect(texto?.trim().length ?? 0).toBeGreaterThan(0);
    });

    await test.step('Paso 3 — AC2: un tipo sin description no muestra franja (sin relleno genérico)', async () => {
      // Requiere que el seed tenga un subtipo de OTROS sin `description` configurado para negar
      // el caso — se deja como paso manual de verificación visual/asserción cuando el seed lo
      // provea; documentado aquí para no perder el AC en la auditoría.
      const franja = page.getByRole('status');
      await expect(franja).toBeVisible();
    });
  });
});
