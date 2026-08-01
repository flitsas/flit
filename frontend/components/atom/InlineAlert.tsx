import type { ReactNode } from 'react';
import { AlertTriangle, CheckCircle2, Info, XCircle } from 'lucide-react';

// Aviso en línea reutilizable (dentro de modales, formularios o paneles). Unifica el patrón que el
// producto ya usaba suelto en varias pantallas —bloque redondeado, fondo tenue del color del tono y
// borde del mismo color al 30%— y le agrega icono y título opcional para que un mensaje largo no
// llegue como un párrafo rojo suelto.
//
// Accesibilidad: los tonos de error y aviso se anuncian como `alert` (assertive) porque interrumpen
// una acción del usuario; info y éxito usan `status` (polite) para no cortar al lector de pantalla.
// El icono es decorativo (aria-hidden): el texto ya comunica el mensaje completo.

export type InlineAlertTone = 'error' | 'warning' | 'info' | 'success';

const TONES: Record<
  InlineAlertTone,
  { color: string; background: string; border: string; Icon: typeof AlertTriangle }
> = {
  error: {
    color: '#C2410C',
    background: 'rgba(255,78,0,0.08)',
    border: 'rgba(255,78,0,0.30)',
    Icon: XCircle,
  },
  warning: {
    color: '#B45309',
    background: 'rgba(245,158,11,0.10)',
    border: 'rgba(245,158,11,0.35)',
    Icon: AlertTriangle,
  },
  info: {
    color: '#1D4ED8',
    background: 'rgba(85,126,255,0.08)',
    border: 'rgba(85,126,255,0.30)',
    Icon: Info,
  },
  success: {
    color: '#047857',
    background: 'rgba(0,219,213,0.10)',
    border: 'rgba(0,219,213,0.35)',
    Icon: CheckCircle2,
  },
};

export interface InlineAlertProps {
  /** Paleta e icono del aviso. Por defecto `warning`. */
  tone?: InlineAlertTone;
  /** Encabezado corto opcional; el mensaje va en `children`. */
  title?: string;
  children: ReactNode;
  /** Acción opcional a la derecha (p. ej. "Reintentar"). */
  action?: ReactNode;
  className?: string;
}

export function InlineAlert({
  tone = 'warning',
  title,
  children,
  action,
  className = '',
}: InlineAlertProps) {
  const { color, background, border, Icon } = TONES[tone];
  const interrumpe = tone === 'error' || tone === 'warning';

  return (
    <div
      role={interrumpe ? 'alert' : 'status'}
      aria-live={interrumpe ? 'assertive' : 'polite'}
      className={`flex items-start gap-3 rounded-xl p-3 ${className}`.trim()}
      style={{ background, border: `1px solid ${border}` }}
    >
      <Icon className="mt-0.5 h-4 w-4 shrink-0" style={{ color }} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        {title ? (
          <p className="text-xs font-semibold" style={{ color }}>
            {title}
          </p>
        ) : null}
        <div className={`text-xs leading-relaxed ${title ? 'mt-0.5' : ''}`} style={{ color }}>
          {children}
        </div>
      </div>
      {action ? <div className="shrink-0">{action}</div> : null}
    </div>
  );
}
