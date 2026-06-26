import { test, expect } from '@playwright/test';

/**
 * HU #10249 — E2E Dashboard analítico: exportaciones Excel y PDF.
 * Intercepta overview/top/procedures para pintar el dashboard y los endpoints de
 * export para validar AC1 (Excel + progreso), AC2 (PDF) y AC3 (error accesible).
 */
test.describe('HU10249 — exportaciones del dashboard', () => {
  const OVERVIEW = {
    tenantId: '11111111-1111-1111-1111-111111111111',
    from: '2026-06-01',
    to: '2026-06-26',
    categories: [
      { category: 'matriculas', total: 120, byStatus: [{ status: 'submitted', count: 80 }] },
      { category: 'traspasos', total: 30, byStatus: [{ status: 'submitted', count: 30 }] },
      { category: 'otros', total: 0, byStatus: [] },
    ],
  };

  async function stubDashboard(page: import('@playwright/test').Page) {
    await page.addInitScript(() => window.localStorage.setItem('flit:authed', '1'));
    await page.route('**/api/v1/analytics/overview**', (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(OVERVIEW) }),
    );
    await page.route('**/api/v1/analytics/productivity/top**', (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ items: [] }) }),
    );
    await page.route('**/api/v1/analytics/procedures**', (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 10 }) }),
    );
  }

  test('AC1: Exportar Excel descarga un .xlsx con los filtros activos', async ({ page }) => {
    await stubDashboard(page);
    await page.route('**/api/v1/analytics/export/excel**', (r) =>
      r.fulfill({
        status: 200,
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        headers: { 'Content-Disposition': 'attachment; filename="tramites.xlsx"' },
        body: 'PK',
      }),
    );

    await page.goto('/?m=reportes');
    await expect(page.getByText('Reportes y Analíticas')).toBeVisible();

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: /exportar excel/i }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toContain('.xlsx');
  });

  test('AC2: Resumen Ejecutivo PDF descarga el PDF de la API', async ({ page }) => {
    await stubDashboard(page);
    await page.route('**/api/v1/analytics/export/executive-pdf**', (r) =>
      r.fulfill({
        status: 200,
        contentType: 'application/pdf',
        headers: { 'Content-Disposition': 'attachment; filename="resumen.pdf"' },
        body: '%PDF-1.4',
      }),
    );

    await page.goto('/?m=reportes');
    await expect(page.getByText('Reportes y Analíticas')).toBeVisible();

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: /resumen ejecutivo pdf/i }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toContain('.pdf');
  });

  test('AC3: error 500 muestra alerta sin bloquear el dashboard', async ({ page }) => {
    await stubDashboard(page);
    await page.route('**/api/v1/analytics/export/excel**', (r) =>
      r.fulfill({ status: 500, contentType: 'application/json', body: '{"message":"boom"}' }),
    );

    await page.goto('/?m=reportes');
    await expect(page.getByText('Reportes y Analíticas')).toBeVisible();

    await page.getByRole('button', { name: /exportar excel/i }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    // El dashboard sigue visible (no se bloquea).
    await expect(page.getByText('Reportes y Analíticas')).toBeVisible();
  });
});
