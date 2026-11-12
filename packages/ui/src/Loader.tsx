/**
 * Cargador del Manual UI Kit 2026 (propuesta-2, `.cargador`): la pista azul fija y, encima, la línea turquesa y el
 * vehículo girando en sentidos contrarios. Es la misma escena del `CarLoader` de Trámites (mismas capas
 * `/assets/loading/*.svg`, hoy con el dibujo del kit, y mismo sistema de coordenadas), con las animaciones
 * `.flit-escena-*` de `@flit/ui/loader.css`. Con «menos movimiento» se queda quieta y el texto sigue informando.
 */
export function Cargador({ className = "" }: { className?: string }) {
  return (
    <span className={`relative inline-flex h-32 w-32 items-center justify-center ${className}`} aria-hidden="true">
      <svg viewBox="0 0 300 300" className="pointer-events-none absolute inset-0 h-full w-full">
        <image href="/assets/loading/circulo.svg" x={37.5} y={37.5} width={225} height={225} />
        <g className="flit-escena-orbita" style={{ transformBox: "view-box", transformOrigin: "50% 50%" }}>
          <image href="/assets/loading/linea.svg" x={115.6} y={264} width={68.8} height={8.88} />
        </g>
        <g className="flit-escena-vehiculo" style={{ transformBox: "view-box", transformOrigin: "50% 50%" }}>
          <image href="/assets/loading/carro.svg" x={126.8} y={238.3} width={46.41} height={23.42} />
        </g>
      </svg>
    </span>
  );
}

/** Espera con su mensaje (antes el aro con el ícono de carro). Mismas props. */
export function VehicleLoader({ label = "Procesando…" }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-6">
      <div className="flit-cargador" role="status" aria-label={label}>
        <Cargador />
      </div>
      <p className="text-sm font-semibold text-[color:var(--di-text-tertiary)]">{label}</p>
    </div>
  );
}
