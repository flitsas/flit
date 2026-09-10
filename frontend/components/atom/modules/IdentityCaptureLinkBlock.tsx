'use client';

import { useState } from 'react';
import { Copy, ExternalLink } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { FLIT } from '@/lib/flit-design-tokens';

/** Hay URL de captura Kyverum para pintar QR (identidad standalone o validación de trámite). */
export function hasKyverumCaptureQr(captureUrl: string | null | undefined): boolean {
  return typeof captureUrl === 'string' && captureUrl.trim().length > 0;
}

function qrHref(captureUrl: string): string {
  if (/^https?:\/\//i.test(captureUrl)) return captureUrl;
  if (typeof window === 'undefined') return captureUrl;
  try {
    return new URL(captureUrl, window.location.origin).href;
  } catch {
    return captureUrl;
  }
}

/**
 * Bloque de enlace de captura (QR + CTA degradado + copiar) — patrón del detalle
 * de validación de identidad / prevalidación. Se muestra siempre que exista URL,
 * tanto en validación por identidad como por trámite.
 */
export function IdentityCaptureLinkBlock({ captureUrl }: { captureUrl: string }) {
  const href = qrHref(captureUrl);
  return (
    <div
      className="rounded-xl border bg-white p-3"
      style={{ borderColor: FLIT.border.soft }}
      data-testid="identity-capture-qr"
    >
      <p className="mb-2 text-[12px] font-semibold" style={{ color: FLIT.brand.blue }}>
        Enlace de captura
      </p>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div
          className="grid w-fit place-items-center rounded-xl border bg-white p-2"
          style={{ borderColor: FLIT.border.soft }}
        >
          <QRCodeSVG value={href} size={112} aria-label="Código QR del enlace de captura" />
        </div>
        <div className="flex min-w-0 flex-col gap-2">
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex w-fit items-center gap-1.5 rounded-full px-4 py-2 text-[11px] font-semibold text-white shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{
              background: FLIT.gradientPrimary,
              outlineColor: FLIT.brand.blue,
            }}
          >
            <ExternalLink className="h-3 w-3" aria-hidden />
            Abrir captura Kyverum
          </a>
          <CopyLinkButton captureUrl={href} />
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
        className="inline-flex w-fit items-center gap-1.5 rounded-full border px-4 py-2 text-[11px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
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
