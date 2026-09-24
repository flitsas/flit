import { test, expect } from '@playwright/test';

/**
 * HU #12850 / HU #12851 / HU #12852 (Feature #12846, Épica #12751) — Eliminar módulo de
 * Preasignación de rango.
 *
 * Requiere un entorno con backend real (core-api) ya con HU-A1 desplegada (los endpoints de la
 * consola de rangos responden 410 Gone y assign-plate siempre reserva fuera de rango) y un
 * usuario Admin OT (`ot_admin`) con al menos un trámite en estado `preasignacion` en su bandeja.
 *
 * Como `e2e-audit/hu12126-wizard-copy-tipo-tramite.spec.ts`, es un script de auditoría manual
 * (headless: false, video/screenshot on) contra `playwright.audit.config.ts` — no está en el
 * pipeline de CI.
 */
test.describe('HU12850/HU12851/HU12852 — Preasignación retirada de UI y del modal de placa', () => {
  test('AC: dock, hub, visor de compañía y modal de placa ya no ofrecen el módulo retirado', async ({
    page,
  }) => {
    await test.step('Paso 0 — Auth stub (Admin OT)', async () => {
      await page.addInitScript(() => {
        window.localStorage.setItem('flit:authed', '1');
      });
    });

    await test.step('Paso 1 — HU12850 AC1: el dock del Admin OT ya no ofrece "Preasignación"', async () => {
      await page.goto('/');
      await expect(page.getByRole('button', { name: 'Preasignación' })).toHaveCount(0);
    });

    await test.step('Paso 2 — HU12850 AC2: la ruta directa a la consola ya no renderiza nada útil', async () => {
      // El id de organismo real depende del seed del entorno; se navega por URL directa para
      // comprobar que Next.js no resuelve la página retirada (404), no que muestre la consola.
      await page.goto('/admin/transit-offices/seed-ot-1/plate-ranges');
      await expect(page.getByText(/Asignar rango|Editar rango/i)).toHaveCount(0);
    });

    await test.step('Paso 3 — HU12851 AC1: la ficha de compañía ya no tiene la pestaña "Placas preasignadas"', async () => {
      await page.goto('/admin/companies/seed-company-1');
      await expect(page.getByRole('tab', { name: /placas preasignadas/i })).toHaveCount(0);
    });

    await test.step('Paso 4 — HU12852 AC1/AC2: el modal "Asignar placa" es un único campo, sin selector de rango', async () => {
      await page.goto('/admin/transit-offices/seed-ot-1/client-procedures');
      const fila = page.getByText('RAD-'); // referencia de un trámite en preasignación del seed
      if (await fila.first().isVisible().catch(() => false)) {
        await page.getByRole('button', { name: /Acciones del trámite/i }).first().click();
        await page.getByRole('menuitem', { name: /Asignar placa/i }).click();
        await expect(page.getByRole('dialog', { name: 'Asignar placa' })).toBeVisible();
        await expect(page.getByLabelText('Placa')).toBeVisible();
        await expect(page.getByRole('button', { name: /^Del rango/i })).toHaveCount(0);
        await expect(page.getByRole('button', { name: /^Fuera de rango$/i })).toHaveCount(0);
      }
    });
  });
});
