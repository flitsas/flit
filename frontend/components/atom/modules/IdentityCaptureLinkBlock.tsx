'use client';

import { useState } from 'react';
import { Icono } from "@flit/ui/Icono";
import { QRCodeSVG } from 'qrcode.react';

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
export function IdentityCaptureLinkBlock({
  captureUrl,
  provider,
}: {
  captureUrl: string;
  /** Proveedor de la validación: solo `manual`/`mock` quitan el nombre «Kyverum» del CTA. */
  provider?: string | null;
}) {
  const ctaLabel = provider === 'manual' || provider === 'mock' ? 'Abrir enlace de captura' : 'Abrir captura Kyverum';
  const href = qrHref(captureUrl);
  return (
    <div
      className="rounded-xl border bg-card p-3"
      style={{ borderColor: 'var(--border)' }}
      data-testid="identity-capture-qr"
    >
      <p className="mb-2 text-[12px] font-semibold" style={{ color: 'var(--di-action)' }}>
        Enlace de captura
      </p>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div
          className="grid w-fit place-items-center rounded-xl border bg-white p-2"
          style={{ borderColor: 'var(--border)' }}
        >
          <QRCodeSVG value={href} size={112} aria-label="Código QR del enlace de captura" />
        </div>
        <div className="flex min-w-0 flex-col gap-2">
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex w-fit items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{
              background: 'var(--di-gradient-action)',
              outlineColor: 'var(--di-action)',
            }}
          >
            <Icono nombre="externo" className="h-3 w-3" />
            {ctaLabel}
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
        className="inline-flex w-fit items-center gap-1.5 rounded-full border px-4 py-2 text-xs font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{
          borderColor: 'var(--di-action)',
          color: 'var(--di-action)',
          outlineColor: 'var(--di-action)',
        }}
        aria-label="Copiar enlace de captura"
      >
        <Icono nombre="copiar" className="h-3 w-3" />
        {copied ? 'Copiado' : 'Copiar enlace'}
      </button>
      <span className="sr-only" role="status" aria-live="polite">
        {copied ? 'Enlace copiado al portapapeles.' : ''}
      </span>
    </>
  );
}
