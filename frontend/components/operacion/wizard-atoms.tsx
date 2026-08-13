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
      <p className="text-xs uppercase tracking-wide opacity-55">{label}</p>
      <p className="mt-0.5 truncate text-xs font-semibold" title={value}>
        {value}
      </p>
    </div>
  );
}

/**
 * Píldora de estado sobre fondo de color pleno.
 *
 * `color` es el tono de fondo; el texto va en blanco. Solo se le pasan tonos FLIT con contraste
 * suficiente sobre blanco (azul de marca, verde, naranja y gris de inactivo), nunca el cian, que
 * en pleno no alcanza 4.5:1 con texto blanco.
 */
export function WizardPill({ text, color }: { text: string; color: string }) {
  return (
    <span
      className="whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold text-white"
      style={{ background: color }}
    >
      {text}
    </span>
  );
}

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
}: {
  title: string;
  subtitle?: string;
  action?: ReactNode;
  /** Para colgar un `aria-labelledby` desde la sección que envuelve la tarjeta. */
  id?: string;
}) {
  return (
    <div className="mb-3 flex items-start justify-between gap-3">
      <div className="min-w-0">
        <h3 id={id} className="text-sm font-bold leading-tight">
          {title}
        </h3>
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
