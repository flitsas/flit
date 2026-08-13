/**
 * Átomos de presentación del wizard, en el lenguaje visual de la propuesta.
 *
 * Son las dos piezas que el diseño repite en cada paso para mostrar datos ya resueltos: el par
 * rótulo/valor de las grillas consolidadas y la píldora de estado de las cabeceras. Viven aquí —y
 * no dentro de un panel— porque los usan pasos distintos y, escritos a mano en cada uno, ya
 * habían divergido en tamaño de rótulo y en el radio de la píldora.
 */

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
