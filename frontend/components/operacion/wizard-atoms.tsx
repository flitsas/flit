/**
 * Átomos de presentación del wizard, en el lenguaje visual de la propuesta.
 *
 * Son las piezas que el diseño repite en cada paso: el par rótulo/valor de las grillas
 * consolidadas, la píldora de estado de las cabeceras, la cabecera de tarjeta y el control
 * segmentado. Viven aquí —y no dentro de un panel— porque los usan pasos distintos y, escritos a
 * mano en cada uno, ya habían divergido en tamaño de rótulo y en el radio de la píldora.
 */

import type { ReactNode } from 'react';

/**
 * Par rótulo/valor de las grillas de datos consolidados (RUNT, resumen del FUR).
 *
 * El valor se trunca en una línea a propósito: la grilla es un vistazo, no el detalle. Cuando el
 * dato puede llegar largo y necesita leerse completo —una razón social, el nombre de un organismo
 * de tránsito— el `title` lo deja disponible en el tooltip nativo sin romper la retícula.
 */
export function WizardPair({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      {/* opacity-70 es el piso del sistema sobre texto (ver comentario en `WizardCardHeader`);
          estaba en 0.55 (3.57:1) y se propagaba a todas las grillas consolidadas que usan este
          átomo. */}
      <p className="text-xs uppercase tracking-wide opacity-70">{label}</p>
      <p className="mt-0.5 truncate text-xs font-semibold" title={value}>
        {value}
      </p>
    </div>
  );
}

/*
 * `WizardPill` vivía aquí: una píldora de relleno pleno con texto blanco, portada del `Pill` de la
 * propuesta. Se retira, no se deja como opción.
 *
 * Su documentación afirmaba que solo se le pasaban tonos con contraste suficiente sobre blanco, y
 * era falso: el verde de marca da 2.05:1, el naranja 3.31:1 y el azul 3.61:1 — los tres por debajo
 * del 4.5:1 exigido. El sistema ya prohíbe el badge sólido con texto blanco y ya tiene el
 * componente correcto para esto, `atom/StatusBadge`, tintado y con tonos semánticos.
 *
 * Dejarla aquí sin usos habría sido dejar el patrón prohibido a mano del siguiente que necesite
 * una píldora.
 */

/**
 * Cabecera de una tarjeta de sección: título y, opcionalmente, una línea que explica qué se
 * resuelve dentro y una acción a la derecha.
 *
 * El título es un `h3` y no un `<p>` en negrita —como estaba escrito a mano en varios pasos—
 * porque los pasos ya llegan a media docena de secciones y sin encabezados reales la navegación
 * por landmarks del lector de pantalla se queda en un único bloque plano.
 */
export function WizardCardHeader({
  title,
  subtitle,
  action,
  id,
  level = 'h3',
  className = 'mb-3',
}: {
  title: string;
  subtitle?: string;
  action?: ReactNode;
  /** Para colgar un `aria-labelledby` desde la sección que envuelve la tarjeta. */
  id?: string;
  /**
   * Nivel semántico del título (consolidación HU auditoría de diseño). Lo que cuelga directamente
   * de la shell del paso arranca en `h3`; sus subniveles internos (bloque dentro de una tarjeta que
   * ya tiene su propio `h3`) usan `h4` para no romper la jerarquía de encabezados.
   */
  level?: 'h3' | 'h4';
  /**
   * Margen inferior del bloque título/acción. Por defecto `mb-3` (tarjeta estándar); las cabeceras
   * que viven solas dentro de una franja con su propio padding (p. ej. una barra con borde inferior)
   * pasan `''` para no duplicar el espaciado.
   */
  className?: string;
}) {
  const Tag = level;
  return (
    <div className={`flex items-start justify-between gap-3 ${className}`}>
      <div className="min-w-0">
        {/* Azul de marca: es la regla de títulos del sistema y es lo que hacen las dos pantallas de
            la propuesta. Al extraer el átomo se perdió el color y las tarjetas quedaron
            descoloridas frente a la cabecera del propio paso. */}
        <Tag id={id} className="text-sm font-bold leading-tight" style={{ color: '#557EFF' }}>
          {title}
        </Tag>
        {/* opacity-70 es el piso del sistema sobre texto; por debajo el contraste efectivo cae de AA. */}
        {subtitle ? <p className="mt-1 text-xs leading-snug opacity-70">{subtitle}</p> : null}
      </div>
      {action ? <div className="shrink-0">{action}</div> : null}
    </div>
  );
}

/**
 * Control segmentado: dos o tres opciones excluyentes en una sola pista.
 *
 * El diseño lo usa para las elecciones binarias que cambian el resto del formulario —persona
 * natural/jurídica, con/sin prenda, carga individual/masiva—, donde un `<select>` esconde la
 * consecuencia detrás de un desplegable y un par de radios ocupa el doble de alto.
 *
 * Se implementa con botones `aria-pressed` dentro de un `role="group"`, que es el patrón que el
 * paso de actores ya usaba: cambiarlo por radios nativos habría reescrito las consultas de los
 * tests sin ganar nada en accesibilidad —el grupo ya se anuncia con su rótulo y cada opción con su
 * estado de presionado.
 */
export function WizardSegmented<T extends string>({
  label,
  value,
  options,
  onChange,
  disabled,
  className = '',
}: {
  /** Rótulo visible sobre la pista; también nombra el grupo para el lector de pantalla. */
  label: string;
  value: T;
  options: ReadonlyArray<{ value: T; label: string; disabled?: boolean }>;
  onChange: (value: T) => void;
  /** Deshabilita el control completo (solo lectura del asistente). */
  disabled?: boolean;
  className?: string;
}) {
  return (
    <div className={className}>
      <span className="mb-1.5 block text-xs font-semibold">{label}</span>
      {/* La pista va en el azul de fondo de la app (`background.app`) y no en un gris frío: sobre la
          tarjeta blanca hunde el control lo justo, y el gris que trae la propuesta para esto es de
          la escala `slate-*` de Tailwind, que no es paleta FLIT. */}
      <div
        role="group"
        aria-label={label}
        className="inline-flex gap-0.5 rounded-xl border p-1"
        style={{ borderColor: '#DFE5ED', background: '#EEF5FF' }}
      >
        {options.map((o) => {
          const active = value === o.value;
          return (
            <button
              key={o.value}
              type="button"
              onClick={() => onChange(o.value)}
              disabled={disabled || o.disabled}
              aria-pressed={active}
              className={
                'rounded-[10px] px-4 py-1.5 text-xs font-semibold transition ' +
                'focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]/40 ' +
                'disabled:cursor-not-allowed disabled:opacity-50 ' +
                (active ? '' : 'opacity-70 hover:opacity-100')
              }
              style={
                active
                  ? {
                      background: '#FFFFFF',
                      color: '#557EFF',
                      // `shadow.card` del token file. La sombra de la propuesta se apoya en
                      // rgba(15,23,42,…) —slate-900— y no en el navy de marca.
                      boxShadow: '0 8px 24px rgba(22, 39, 68, 0.08)',
                    }
                  : undefined
              }
            >
              {o.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
