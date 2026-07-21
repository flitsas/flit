'use client';

import type {
  ConformationRuleProfile,
  EntryMode,
} from '@/lib/api/types/procedure-parametrization-f08';
import { VehicleQuerySection } from './VehicleQuerySection';
import { DocumentChecklistSection, type DocumentChecklistItem } from './DocumentChecklistSection';
import { ActorFormSection } from './ActorFormSection';
import { CommercialSection } from './CommercialSection';
import { BiometricSection } from './BiometricSection';
import { SignatureFurSection } from './SignatureFurSection';
import { PlateRequestSection, type PlateRequestStatus } from './PlateRequestSection';
import { PrendaDecisionSection, type PrendaDecision } from './PrendaDecisionSection';

/** Catálogo cerrado de section_types (CFD-09), en paridad con el CHECK del backend. */
export const SECTION_TYPES = [
  'vehicle_query',
  'document_checklist',
  'actor_form',
  'commercial',
  'biometric',
  'signature_fur',
  'plate_request',
  'prenda_decision',
  'generic_form',
] as const;

export type SectionType = (typeof SECTION_TYPES)[number];

/** Configuración de sección — unión DISCRIMINADA por sectionType (AC-07, sin `any`). */
export type SectionConfig =
  | { sectionType: 'vehicle_query'; entryMode: EntryMode; vin?: string; plate?: string }
  | { sectionType: 'document_checklist'; requirements: DocumentChecklistItem[]; uploadedCodes?: string[] }
  | { sectionType: 'actor_form'; conformationRules: ConformationRuleProfile[] }
  | { sectionType: 'commercial'; requiresCommercialValue: boolean; commercialValueSource?: string | null }
  | { sectionType: 'biometric'; actors: string[]; approvedActors?: string[] }
  | { sectionType: 'signature_fur'; furGenerated?: boolean }
  | { sectionType: 'plate_request'; status?: PlateRequestStatus; assignedPlate?: string }
  | { sectionType: 'prenda_decision'; decision?: PrendaDecision | null }
  | { sectionType: 'generic_form'; label?: string };

export function isRegisteredSectionType(value: string): value is SectionType {
  return (SECTION_TYPES as readonly string[]).includes(value);
}

/** Aviso visible cuando llega un section_type sin renderizador registrado (AC-02). */
export function UnknownSectionFallback({ sectionType }: { sectionType: string }) {
  return (
    <section
      role="alert"
      aria-label="Sección no soportada"
      className="rounded-xl p-4 border"
      style={{ borderColor: '#F9AC00' }}
    >
      <p className="text-xs font-semibold" style={{ color: '#a86f00' }}>
        Sección no soportada
      </p>
      <p className="text-[11px] opacity-70">
        El tipo de sección &quot;{sectionType}&quot; no tiene renderizador registrado.
      </p>
    </section>
  );
}

function GenericFormSection({ label }: { label?: string }) {
  return (
    <section aria-label="Datos" className="space-y-2">
      <h2 className="text-base font-bold mb-1">{label ?? 'Datos'}</h2>
      <p className="text-xs opacity-50">Formulario de datos configurable.</p>
    </section>
  );
}

/** Resuelve la configuración tipada al componente registrado (AC-01). */
export function SectionRenderer({ config }: { config: SectionConfig }) {
  switch (config.sectionType) {
    case 'vehicle_query':
      return <VehicleQuerySection entryMode={config.entryMode} vin={config.vin} plate={config.plate} />;
    case 'document_checklist':
      return (
        <DocumentChecklistSection requirements={config.requirements} uploadedCodes={config.uploadedCodes} />
      );
    case 'actor_form':
      return <ActorFormSection conformationRules={config.conformationRules} />;
    case 'commercial':
      return (
        <CommercialSection
          requiresCommercialValue={config.requiresCommercialValue}
          commercialValueSource={config.commercialValueSource}
        />
      );
    case 'biometric':
      return <BiometricSection actors={config.actors} approvedActors={config.approvedActors} />;
    case 'signature_fur':
      return <SignatureFurSection furGenerated={config.furGenerated} />;
    case 'plate_request':
      return <PlateRequestSection status={config.status} assignedPlate={config.assignedPlate} />;
    case 'prenda_decision':
      return <PrendaDecisionSection decision={config.decision} />;
    case 'generic_form':
      return <GenericFormSection label={config.label} />;
    default: {
      const exhaustive: never = config;
      return <UnknownSectionFallback sectionType={(exhaustive as { sectionType: string }).sectionType} />;
    }
  }
}

/**
 * FEATURE-08 / HU-FE-05 (CFD-09) — punto de entrada del registry para el wizard server-driven:
 * dado un <code>section_type</code> (string del backend) y su config, resuelve al componente
 * registrado o al <see cref="UnknownSectionFallback"/> si el tipo no está registrado.
 */
export function DynamicSection({
  sectionType,
  config,
}: {
  sectionType: string;
  config?: Omit<SectionConfig, 'sectionType'>;
}) {
  if (!isRegisteredSectionType(sectionType)) {
    return <UnknownSectionFallback sectionType={sectionType} />;
  }
  return <SectionRenderer config={{ sectionType, ...(config ?? {}) } as SectionConfig} />;
}
