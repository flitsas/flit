// HU #12702 (Feature #12692, épica #12551) — términos canónicos en Ayuda (A25).
import { describe, expect, it } from 'vitest';
import { AYUDA_FAQ } from '@/components/atom/modules/Ayuda';
import { INTRO_ARTICLES } from '@/lib/manual/articles/intro';
import { GESTOR_ARTICLES } from '@/lib/manual/articles/gestor';
import { OT_ARTICLES } from '@/lib/manual/articles/ot';
import { MANUAL_NAV_SECTIONS } from '@/lib/manual/articles/meta';
import { COPY } from '../copy-catalog';

describe('homologación Ayuda intro (HU #12702)', () => {
  it('AC1 — intro y FAQ nombran Gestor y Organismo de tránsito con COPY.A06/A05', () => {
    expect(COPY.A06).toBe('Gestor');
    expect(COPY.A05).toBe('Organismo de tránsito');
    expect(COPY.A05).not.toBe('Organismo de Tránsito');

    const bienvenida = INTRO_ARTICLES.find((a) => a.slug === '0-introduccion/1-bienvenida');
    const perfiles = INTRO_ARTICLES.find((a) => a.slug === '0-introduccion/3-perfiles-y-roles');
    expect(bienvenida?.summary).toContain(COPY.A06);
    expect(bienvenida?.summary).toContain(COPY.A05);
    expect(perfiles?.title).toBe(`Perfiles: ${COPY.A06} vs ${COPY.A05}`);

    expect(MANUAL_NAV_SECTIONS.find((s) => s.id === 'gestor')?.label).toBe(COPY.A06);
    expect(MANUAL_NAV_SECTIONS.find((s) => s.id === 'ot')?.label).toBe(COPY.A05);

    const faqManual = AYUDA_FAQ.find((f) => f.q.includes('manual'));
    expect(faqManual?.a).toContain(COPY.A06);
    expect(faqManual?.a).toContain(COPY.A05);
    expect(faqManual?.a).not.toContain('Organismo de Tránsito');
  });

  it('AC2 — no reescribe el cuerpo de artículos solo-OT o solo-gestor (RN-07)', () => {
    const gestorDocs = GESTOR_ARTICLES.find((a) => a.slug.includes('documentos'));
    const cuerpoGestor = JSON.stringify(gestorDocs);
    expect(cuerpoGestor).toContain('Organismo de Tránsito');

    const otUsuarios = OT_ARTICLES.find((a) => a.slug.includes('usuarios'));
    const cuerpoOt = JSON.stringify(otUsuarios);
    expect(cuerpoOt).toContain('Organismo de Tránsito');
    expect(otUsuarios?.audience).toBe('Organismo de Tránsito');
  });

  it('AC3 — sufijos DR. FLIT (Gestor) y DR. FLIT (OT) se conservan', () => {
    const drGestor = GESTOR_ARTICLES.find((a) => a.slug === '1-gestor/6-ayuda-dr-flit');
    const drOt = OT_ARTICLES.find((a) => a.slug === '2-ot/8-ayuda-dr-flit');
    expect(drGestor?.title).toBe('Ayuda con DR. FLIT (Gestor)');
    expect(drOt?.title).toBe('Ayuda con DR. FLIT (OT)');
  });
});
