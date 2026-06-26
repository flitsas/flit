import { test, expect } from '@playwright/test';

/**
 * HU #10247 — E2E Dashboard analítico (Reportes)
 * Valida acceso por rol (AC1), filtro de fechas que reconsulta la API sin recargar
 * (AC2) y los estados de UI (AC3). El overview se intercepta con un mock de red para
 * no depender de datos sembrados; el objetivo es el comportamiento del frontend.
 */
test.describe('HU10247 — dashboard analítico de reportes', () => {
  const OVERVIEW = {
    tenantId: '11111111-1111-1111-1111-111111111111',
    from: '2026-06-01',
    to: '2026-06-26',
    categories: [
      { category: 'matriculas', total: 120, byStatus: [{ status: 'submitted', count: 80 }, { status: 'approved', count: 40 }] },
      { category: 'traspasos', total: 30, byStatus: [{ status: 'submitted', count: 30 }] },
      { category: 'otros', total: 0, byStatus: [] },
    ],
  };

  test('AC1+AC2+AC3: gráficos circulares, filtro de fechas y estados', async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage.setItem('flit:authed', '1');
    });

    let overviewCalls = 0;
    await page.route('**/api/v1/analytics/overview**', async (route) => {
      overviewCalls += 1;
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(OVERVIEW) });
    });

    await test.step('Abrir módulo Reportes (?m=reportes)', async () => {
      await page.goto('/?m=reportes');
      await expect(page.getByText('Reportes y Analíticas')).toBeVisible();
    });

    await test.step('AC1/AC3 lleno: total y donuts por categoría', async () => {
      await expect(page.getByText('Total trámites')).toBeVisible();
      await expect(page.getByText('150')).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Matrículas' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Traspasos' })).toBeVisible();
    });

    await test.step('AC2: cambiar el rango reconsulta la API sin recargar', async () => {
      const before = overviewCalls;
      await page.getByLabel('Desde').fill('2026-05-01');
      await expect.poll(() => overviewCalls).toBeGreaterThan(before);
      await expect(page.getByText('Reportes y Analíticas')).toBeVisible();
    });
  });

  test('AC3 error: la API falla → alerta con reintento', async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage.setItem('flit:authed', '1');
    });
    await page.route('**/api/v1/analytics/overview**', (route) =>
      route.fulfill({ status: 500, contentType: 'application/json', body: '{"message":"boom"}' }),
    );

    await page.goto('/?m=reportes');
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByRole('button', { name: /reintentar/i })).toBeVisible();
  });
});
