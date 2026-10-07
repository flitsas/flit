// Selector del cliente de revisión manual (Épica #13202): HTTP real por defecto; simulado solo con «true».
import { afterEach, describe, expect, it, vi } from 'vitest';
import { getManualReviewClient, manualReviewHttpClient } from '../manual-review-client';

afterEach(() => vi.unstubAllEnvs());

describe('getManualReviewClient', () => {
  it('sin variable devuelve el cliente HTTP real', () => {
    vi.stubEnv('NEXT_PUBLIC_MANUAL_REVIEW_MOCK', '');
    expect(getManualReviewClient()).toBe(manualReviewHttpClient);
  });

  it.each(['false', 'TRUE', '1'])('con «%s» sigue usando el cliente real', (valor) => {
    vi.stubEnv('NEXT_PUBLIC_MANUAL_REVIEW_MOCK', valor);
    expect(getManualReviewClient()).toBe(manualReviewHttpClient);
  });

  it('con «true» devuelve el simulado (no el HTTP)', () => {
    vi.stubEnv('NEXT_PUBLIC_MANUAL_REVIEW_MOCK', 'true');
    const cliente = getManualReviewClient();
    expect(cliente).not.toBe(manualReviewHttpClient);
    expect(getManualReviewClient()).toBe(cliente);
  });
});
