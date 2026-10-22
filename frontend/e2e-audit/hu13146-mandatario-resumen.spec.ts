import { test, expect, type Page } from '@playwright/test';

/**
 * HU #13146 — Resumen: frase de quién firmará el contrato de mandato, y alerta cuando no hay mandatario.
 *
 * Como `hu12126-wizard-copy-tipo-tramite.spec.ts`, es un script de auditoría contra
 * `playwright.audit.config.ts` (no corre en CI). Requiere un backend con un trámite en borrador con
 * todos los pasos completos (E2E_TRAMITE_ID) y el organismo elegido. El firmante previsto se
 * simula interceptando `GET /api/v1/tramites/instances/{id}/mandate-signer`, de modo que la corrida
 * valide la pantalla para el caso con mandatario y sin mandatario sin depender del seed.
 */
const TRAMITE_ID = process.env.E2E_TRAMITE_ID ?? '';

async function abrirResumen(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('flit:authed', '1');
  });
  await page.goto(`/tramites/${TRAMITE_ID}`);
  await page.getByRole('button', { name: /^Paso \d+: Resumen/ }).click();
}

test.describe('HU13146 — Quién firmará el mandato en el Resumen', () => {
  test.skip(!TRAMITE_ID, 'Defina E2E_TRAMITE_ID con un trámite en borrador completo.');

  test('AC1/AC6: con mandatario válido dice quién firmará el mandato, sin controles', async ({ page }) => {
    await page.route('**/mandate-signer', (route) =>
      route.fulfill({
        json: { estado: 'valido', nombre: 'Ana Restrepo', formaFirma: 'baul', modo: 'block' },
      }),
    );
    await abrirResumen(page);
    const indicador = page.getByTestId('mandatario-firma-valido');
    await expect(indicador).toContainText(
      'Ana Restrepo firmará el contrato de mandato con la firma que ya tiene guardada.',
    );
    await expect(indicador.getByRole('button')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Finalizar y enviar trámite' })).toBeEnabled();
  });

  test('AC2/AC6: sin mandatario en block muestra la alerta y deshabilita Radicar', async ({
    page,
  }) => {
    await page.route('**/mandate-signer', (route) =>
      route.fulfill({
        json: { estado: 'sin_mandatario', motivo: 'sin_mandatario_configurado', modo: 'block' },
      }),
    );
    await abrirResumen(page);
    await expect(
      page.getByText('Sin mandatario configurado — no se puede radicar'),
    ).toBeVisible();
    await expect(page.getByRole('button', { name: 'Finalizar y enviar trámite' })).toBeDisabled();
  });

  test('AC3: en warn la alerta es una advertencia y se puede radicar', async ({ page }) => {
    await page.route('**/mandate-signer', (route) =>
      route.fulfill({
        json: { estado: 'sin_mandatario', motivo: 'sin_mandatario_configurado', modo: 'warn' },
      }),
    );
    await abrirResumen(page);
    await expect(page.getByTestId('mandatario-firma-aviso')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Finalizar y enviar trámite' })).toBeEnabled();
  });
});
