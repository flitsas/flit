'use client';

import { useEffect, useId, useState } from 'react';
import { ExternalLink } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';

const BLUE = '#557EFF';
const BORDER = '#DFE5ED';

/**
 * Enlace que se muestra mientras el backend no ha dicho cuál es el vigente (o si no responde). Es
 * el mismo valor por defecto de `ProcedureTermsOptions.DefaultUrl`: el documento público de FLIT 1.
 */
export const TERMINOS_URL_FALLBACK = 'https://www.flitsas.com/privacyPolicy/public';

/**
 * Qué documento enlazar. Sale del backend para que el enlace que ve el usuario y el `terms_url`
 * que queda en la evidencia sean el mismo; si no responde, el enlace por defecto sigue funcionando.
 */
export function useTerminosUrl(): string {
  const [termsUrl, setTermsUrl] = useState(TERMINOS_URL_FALLBACK);

  useEffect(() => {
    let active = true;
    void tramitesClient
      .getCurrentProcedureTerms()
      .then((info) => {
        if (active && info?.url) setTermsUrl(info.url);
      })
      .catch(() => {
        /* se conserva el fallback */
      });
    return () => {
      active = false;
    };
  }, []);

  return termsUrl;
}

interface Props {
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
}

/**
 * Bloque de aceptación de Términos y Condiciones (Epic #12543, §3.2): checkbox con check azul y el
 * texto en línea, con el enlace al documento en pestaña nueva. Vive en el modal «Nuevo trámite»:
 * el gestor elige el tipo, marca la casilla y solo entonces se habilita «Iniciar trámite».
 *
 * El enlace queda FUERA del `<label>` a propósito: dentro, un clic sobre él marcaba/desmarcaba el
 * checkbox (lo cazó el test en jsdom).
 */
export function TerminosCondicionesCheckbox({ checked, onChange, disabled }: Props) {
  const checkboxId = useId();
  const termsUrl = useTerminosUrl();

  return (
    <div
      className="flex items-start gap-3 rounded-2xl border p-4 transition"
      style={{
        borderColor: checked ? BLUE : BORDER,
        background: checked ? 'rgba(85,126,255,0.06)' : undefined,
      }}
    >
      <input
        id={checkboxId}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-4 w-4 shrink-0 cursor-pointer accent-[#557EFF] disabled:opacity-60"
      />
      <span className="text-[13px] leading-snug text-[#162744] dark:text-white">
        <label htmlFor={checkboxId} className="cursor-pointer">
          Acepto los Términos y Condiciones de uso de la plataforma.
        </label>{' '}
        <a
          href={termsUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1 font-semibold underline underline-offset-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ color: BLUE }}
        >
          Ver Términos y Condiciones
          <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="sr-only">(se abre en una pestaña nueva)</span>
        </a>
      </span>
    </div>
  );
}
