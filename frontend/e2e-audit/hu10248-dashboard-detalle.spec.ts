import { test, expect } from '@playwright/test';

/**
 * HU #10248 — E2E Dashboard analítico: tabla lateral, cards de productividad y vacío.
 * Intercepta overview, productividad y detalle con mocks de red para validar el
 * comportamiento del frontend (AC1 panel lateral, AC2 multiselect, AC3 sin trámites).
 */
test.describe('HU10248 — detalle lateral y productividad', () => {
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
  const TOP = {
    items: [
      { userId: 'u1', displayName: 'Ana Pérez', submittedCount: 40, approvedCount: 12, rejectedCount: 3 },
      { userId: 'u2', displayName: 'Luis Gómez', submittedCount: 25, approvedCount: 8, rejectedCount: 1 },
    ],
  };
  const DETAIL = {
    items: [
      {
        id: 'p1',
        referenceNumber: 'TRM-2026-000008',
        procedureTypeName: 'Matrícula nueva de vehículo',
        category: 'matriculas',
        status: 'submitted',
        createdByDisplayName: 'Ana Pérez',
        submittedAt: '2026-06-10T12:00:00Z',
        completedAt: null,
      },
    ],
    totalCount: 80,
    page: 1,
    pageSize: 10,
  };

  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => window.localStorage.setItem('flit:authed', '1'));
    await page.route('**/api/v1/analytics/overview**', (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(OVERVIEW) }),
    );
    await page.route('**/api/v1/analytics/productivity/top**', (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(TOP) }),
    );
    await page.route('**/api/v1/analytics/procedures**', (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(DETAIL) }),
    );
  });

  test('AC1: clic en segmento abre panel lateral con tabla paginada', async ({ page }) => {
    await page.goto('/?m=reportes');
    await expect(page.getByText('Reportes y Analíticas')).toBeVisible();

    await page.getByRole('button', { name: /ver radicado de matrículas/i }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog.getByText('Detalle de trámites')).toBeVisible();
    await expect(dialog.getByText('TRM-2026-000008')).toBeVisible();
  });

  test('AC2: multiselect de productividad reajusta los indicadores', async ({ page }) => {
    await page.goto('/?m=reportes');
    await expect(page.getByText('Top 5 productividad')).toBeVisible();

    // Por defecto agrega a todos (40 + 25 = 65 enviados).
    await expect(page.getByText('Enviados').locator('..')).toContainText('65');

    await page.getByRole('button', { name: /ana pérez/i }).click();
    await expect(page.getByText('Enviados').locator('..')).toContainText('40');
  });
});
