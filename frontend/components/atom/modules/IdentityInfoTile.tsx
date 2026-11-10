'use client';

import type { LucideIcon } from 'lucide-react';

/**
 * Tile de lectura rápida (icono + label + valor) usado en el detalle de
 * validación de identidad / prevalidación. Patrón del panel lateral FLIT.
 */
export function IdentityInfoTile({
  icon: Icon,
  label,
  value,
  mono,
  className = '',
}: {
  icon: LucideIcon;
  label: string;
  value: string;
  mono?: boolean;
  className?: string;
}) {
  return (
    <div className={`flex items-start gap-2.5 rounded-xl border px-3 py-2.5 ${className}`}>
      <span
        className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg"
        style={{ background: 'var(--border)', color: 'var(--foreground)' }}
        aria-hidden
      >
        <Icon className="h-3.5 w-3.5" />
      </span>
      <div className="min-w-0">
        <p className="text-[10px] font-semibold uppercase tracking-wide opacity-55">{label}</p>
        <p
          className={`mt-0.5 truncate text-[12px] font-medium dark:text-white ${mono ? 'font-mono' : ''}`}
          style={{ color: 'var(--foreground)' }}
        >
          {value}
        </p>
      </div>
    </div>
  );
}
