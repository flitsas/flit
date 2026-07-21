'use client';

export interface DocumentChecklistItem {
  documentTypeCode: string;
  isRequired: boolean;
  isDummy: boolean;
}

interface DocumentChecklistSectionProps {
  requirements: DocumentChecklistItem[];
  uploadedCodes?: string[];
}

/**
 * FEATURE-08 / HU-FE-03 (CFD-06) — sección de documentos del wizard dinámico. Muestra cada requisito
 * con su estado (obligatorio / opcional / buzón) y marca los ya cargados. Los documentos
 * <code>is_dummy</code> llevan un indicador visual diferenciado (no bloquean el paso).
 */
export function DocumentChecklistSection({
  requirements,
  uploadedCodes = [],
}: DocumentChecklistSectionProps) {
  const uploaded = new Set(uploadedCodes);

  return (
    <section aria-label="Documentos del trámite" className="space-y-3">
      <h2 className="text-base font-bold mb-1">Documentos</h2>
      {requirements.length === 0 && (
        <p className="text-xs opacity-50">Este tipo no exige documentos.</p>
      )}
      <ul className="space-y-2">
        {requirements.map((req) => {
          const done = uploaded.has(req.documentTypeCode);
          const badge = req.isDummy ? 'Buzón' : req.isRequired ? 'Obligatorio' : 'Opcional';
          const badgeColor = req.isDummy ? '#F9AC00' : req.isRequired ? '#557EFF' : '#8A94A6';
          return (
            <li
              key={req.documentTypeCode}
              data-testid={`checklist-${req.documentTypeCode}`}
              data-dummy={req.isDummy ? 'true' : 'false'}
              className="flex items-center gap-3 rounded-xl p-3 border"
            >
              <span className="flex-1 text-xs font-semibold">{req.documentTypeCode}</span>
              <span
                className="text-[10px] font-bold px-2 py-0.5 rounded-full"
                style={{ background: `${badgeColor}22`, color: badgeColor }}
              >
                {badge}
              </span>
              <span
                className="text-[11px]"
                aria-label={done ? 'Documento cargado' : 'Documento pendiente'}
                style={{ color: done ? '#8CC63F' : '#DFE5ED' }}
              >
                {done ? '✓' : '○'}
              </span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
