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

/**
 * Paleta e icono por tono. Se exporta porque hay avisos que NO pueden usar `InlineAlert` como
 * contenedor —los que llevan dentro grids de datos, selectores o badges, a los que el componente
 * teñiría el cuerpo con el color del tono—, y aun así deben pintarse con estos valores en vez de con
 * hex sueltos. Una sola definición evita que esos avisos vuelvan a derivar hacia otra paleta.
 */
export const INLINE_ALERT_TONES: Record<
  InlineAlertTone,
  { color: string; background: string; border: string; Icon: typeof AlertTriangle }
> = {
  error: {
    color: 'var(--badge-danger-fg)',
    background: 'var(--badge-danger-bg)',
    border: 'var(--badge-danger-border)',
    Icon: XCircle,
  },
  warning: {
    // Ámbar FLIT (#F9AC00 familia) — tokens de globals.css / design guardian v2.1.
    color: 'var(--badge-warning-fg)',
    background: 'var(--badge-warning-bg)',
    border: 'var(--badge-warning-border)',
    Icon: AlertTriangle,
  },
  info: {
    color: 'var(--badge-info-fg)',
    background: 'var(--badge-info-bg)',
    border: 'var(--badge-info-border)',
    Icon: Info,
  },
  success: {
    // Verde tintado (PDF 20 ago) — no cian.
    color: 'var(--badge-success-fg)',
    background: 'var(--badge-success-bg)',
    border: 'var(--badge-success-border)',
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
  const { color, background, border, Icon } = INLINE_ALERT_TONES[tone];
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
