'use client';

import { useState } from 'react';
import { Copy, ExternalLink } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { FLIT } from '@/lib/flit-design-tokens';

/**
 * Bloque de enlace de captura (QR + CTA degradado + copiar) — patrón del detalle
 * de validación de identidad / prevalidación.
 */
export function IdentityCaptureLinkBlock({ captureUrl }: { captureUrl: string }) {
  return (
    <div className="space-y-2 rounded-xl border p-3">
      <p className="text-[11px] font-semibold" style={{ color: FLIT.brand.blue }}>
        Enlace de captura
      </p>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="rounded-xl border bg-white p-2">
          <QRCodeSVG value={captureUrl} size={112} aria-label="Código QR del enlace de captura" />
        </div>
        <div className="min-w-0 flex-1 space-y-1.5">
          <a
            href={captureUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="flex w-fit items-center gap-1.5 rounded-full px-4 py-2 text-[11px] font-semibold text-white shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{
              background: FLIT.gradientPrimary,
              outlineColor: FLIT.brand.blue,
            }}
          >
            <ExternalLink className="h-3 w-3" aria-hidden />
            Abrir captura Kyverum
          </a>
          <CopyLinkButton captureUrl={captureUrl} />
        </div>
      </div>
    </div>
  );
}

function CopyLinkButton({ captureUrl }: { captureUrl: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard?.writeText(captureUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard no disponible */
    }
  };
  return (
    <>
      <button
        type="button"
        onClick={() => void copy()}
        className="flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-[11px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{
          borderColor: FLIT.brand.blue,
          color: FLIT.brand.blue,
          outlineColor: FLIT.brand.blue,
        }}
        aria-label="Copiar enlace de captura"
      >
        <Copy className="h-3 w-3" aria-hidden />
        {copied ? 'Copiado' : 'Copiar enlace'}
      </button>
      <span className="sr-only" role="status" aria-live="polite">
        {copied ? 'Enlace copiado al portapapeles.' : ''}
      </span>
    </>
  );
}
