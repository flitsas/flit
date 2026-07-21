import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import {
  SECTION_TYPES,
  DynamicSection,
  SectionRenderer,
  UnknownSectionFallback,
  isRegisteredSectionType,
  type SectionConfig,
} from '../SectionRendererRegistry';

// FEATURE-08 / HU-FE-05 (CFD-09) — SectionRendererRegistry.

describe('SectionRendererRegistry', () => {
  it('registra los 9 section_types del catálogo (AC-04)', () => {
    expect(SECTION_TYPES).toHaveLength(9);
    expect(SECTION_TYPES).toEqual([
      'vehicle_query',
      'document_checklist',
      'actor_form',
      'commercial',
      'biometric',
      'signature_fur',
      'plate_request',
      'prenda_decision',
      'generic_form',
    ]);
    expect(isRegisteredSectionType('vehicle_query')).toBe(true);
    expect(isRegisteredSectionType('desconocido')).toBe(false);
  });

  it('resuelve un section_type conocido al componente correcto (AC-01)', () => {
    render(<DynamicSection sectionType="vehicle_query" config={{ entryMode: 'VIN' }} />);
    expect(screen.getByLabelText('VIN del vehículo')).toBeInTheDocument();
  });

  it('renderiza UnknownSectionFallback para un tipo no registrado (AC-02)', () => {
    render(<DynamicSection sectionType="tipo_futuro" config={{}} />);
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/no soportada/i);
    expect(alert).toHaveTextContent('tipo_futuro');
  });

  it('PrendaDecisionSection se renderiza para prenda_decision (AC-05)', () => {
    const config: SectionConfig = { sectionType: 'prenda_decision', decision: null };
    render(<SectionRenderer config={config} />);
    expect(screen.getByLabelText('Decisión de prenda')).toBeInTheDocument();
    expect(screen.getByLabelText('Con prenda')).toBeInTheDocument();
    expect(screen.getByLabelText('Sin prenda')).toBeInTheDocument();
  });

  it('UnknownSectionFallback es accesible (role=alert) (AC-08)', () => {
    render(<UnknownSectionFallback sectionType="x" />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});
