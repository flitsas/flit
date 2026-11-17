import type { ComponentType, CSSProperties, ReactNode } from 'react';

/**
 * Ícono de aviso del Manual UI Kit 2026: el disco del paquete (`/assets/kit/aviso-*.svg`, `estado-pendiente.svg`). Mismo
 * contrato que un ícono de línea (`className`, `style`) para que quien desestructura `Icon` de los tonos no cambie.
 */
type IconoAviso = ComponentType<{ className?: string; style?: CSSProperties; 'aria-hidden'?: boolean | 'true' | 'false' }>;
const aviso = (archivo: string, nombre: string): IconoAviso => {
  function AvisoKit({ className, style }: { className?: string; style?: CSSProperties }) {
    // eslint-disable-next-line @next/next/no-img-element -- disco estático del kit
    return <img src={`/assets/kit/${archivo}.svg`} alt="" aria-hidden="true" className={`object-contain ${className ?? ''}`} style={style} />;
  }
  AvisoKit.displayName = nombre;
  return AvisoKit;
};
// El nombre de cada uno dice qué ícono es (el triángulo de alerta, el disco de error…), como el de línea que reemplaza.
const AVISO_ERROR = aviso('aviso-error', 'XCircle·kit');
const AVISO_ALERTA = aviso('aviso-alerta', 'AlertTriangle·kit');
const AVISO_INFO = aviso('aviso-info', 'Info·kit');
const AVISO_OK = aviso('aviso-ok', 'CheckCircle·kit');

// Aviso en línea reutilizable (dentro de modales, formularios o paneles). Unifica el patrón que el
// producto ya usaba suelto en varias pantallas —bloque redondeado, fondo tenue del color del tono y
// borde del mismo color al 30%— y le agrega icono y título opcional para que un mensaje largo no
// llegue como un párrafo rojo suelto.
//
// Accesibilidad: los tonos de error y aviso se anuncian como `alert` (assertive) porque interrumpen
// una acción del usuario; info y éxito usan `status` (polite) para no cortar al lector de pantalla.
// El icono es decorativo (aria-hidden): el texto ya comunica el mensaje completo.

export type InlineAlertTone = 'error' | 'warning' | 'info' | 'success' | 'pending';

/**
 * Paleta e icono por tono. Se exporta porque hay avisos que NO pueden usar `InlineAlert` como
 * contenedor —los que llevan dentro grids de datos, selectores o badges, a los que el componente
 * teñiría el cuerpo con el color del tono—, y aun así deben pintarse con estos valores en vez de con
 * hex sueltos. Una sola definición evita que esos avisos vuelvan a derivar hacia otra paleta.
 */
export const INLINE_ALERT_TONES: Record<
  InlineAlertTone,
  { color: string; background: string; border: string; Icon: IconoAviso }
> = {
  error: {
    color: 'var(--badge-danger-fg)',
    background: 'var(--badge-danger-bg)',
    border: 'var(--badge-danger-border)',
    Icon: AVISO_ERROR,
  },
  warning: {
    // Ámbar FLIT (#F9AC00 familia) — tokens de globals.css / design guardian v2.1.
    color: 'var(--badge-warning-fg)',
    background: 'var(--badge-warning-bg)',
    border: 'var(--badge-warning-border)',
    Icon: AVISO_ALERTA,
  },
  info: {
    color: 'var(--badge-info-fg)',
    background: 'var(--badge-info-bg)',
    border: 'var(--badge-info-border)',
    Icon: AVISO_INFO,
  },
  success: {
    // Verde tintado (PDF 20 ago) — no cian.
    color: 'var(--badge-success-fg)',
    background: 'var(--badge-success-bg)',
    border: 'var(--badge-success-border)',
    Icon: AVISO_OK,
  },
  pending: {
    // Naranja corporativo FLIT — `--color-flit-alert` (#FF4E00). Texto `--badge-pending-fg`
    // (#BF3B00) ≈ 5.2:1 sobre `--badge-pending-bg`; cumple WCAG 2.1 AA en texto pequeño.
    color: 'var(--badge-pending-fg)',
    background: 'var(--badge-pending-bg)',
    border: 'var(--badge-pending-border)',
    Icon: AVISO_ALERTA,
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
  /** id del contenedor, para enlazarlo desde `aria-describedby` de un campo. */
  id?: string;
  /** Compacta el padding/gap para usarlo debajo de un campo de formulario denso. */
  compact?: boolean;
}

export function InlineAlert({
  tone = 'warning',
  title,
  children,
  action,
  className = '',
  id,
  compact = false,
}: InlineAlertProps) {
  const { color, background, border, Icon } = INLINE_ALERT_TONES[tone];
  const interrumpe = tone === 'error' || tone === 'warning' || tone === 'pending';

  return (
    <div
      id={id}
      role={interrumpe ? 'alert' : 'status'}
      aria-live={interrumpe ? 'assertive' : 'polite'}
      className={`flex items-start ${compact ? 'gap-1.5 rounded-[9px] p-1.5' : 'gap-3 rounded-[9px] px-4 py-3'} ${className}`.trim()}
      style={{ background, border: `1px solid ${border}` }}
    >
      <Icon className={compact ? 'mt-0.5 h-3.5 w-3.5 shrink-0' : 'h-[22px] w-[22px] shrink-0'} />
      <div className="min-w-0 flex-1">
        {title ? (
          <p className="text-sm font-semibold" style={{ color }}>
            {title}
          </p>
        ) : null}
        <div className={`text-sm leading-relaxed ${title ? 'mt-0.5' : ''}`} style={{ color }}>
          {children}
        </div>
      </div>
      {action ? <div className="shrink-0">{action}</div> : null}
    </div>
  );
}
