'use client';

import { StatusBadge } from '@/components/atom/StatusBadge';
import { manualStatusMeta } from '@/lib/identidad/manual-review-meta';

/** Chip de estado: tono FLIT + icono + texto (nunca solo color). */
export function ManualStatusBadge({ status }: { status: string }) {
  const meta = manualStatusMeta(status);
  const Icon = meta.icon;
  return (
    <StatusBadge
      tone={meta.tone}
      ariaLabel={`Estado: ${meta.label}`}
      label={
        <span className="inline-flex items-center gap-1.5">
          <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden />
          {meta.label}
        </span>
      }
    />
  );
}
