"use client";

import { ClipboardCheck, GraduationCap, IdCard, ShieldPlus, Wrench, type LucideIcon } from "lucide-react";
import { INTERVALO_CARRUSEL_MS, INTERVALO_OTROS_SERVICIOS_MS, OTROS_SERVICIOS, SERVICIOS_ADICIONALES, type IconoServicio } from "@/lib/landing/contenido";
import { boton } from "./estilos";
import { useCarrusel } from "./useCarrusel";

const ICONOS: Record<IconoServicio, LucideIcon> = {
  SOAT: ShieldPlus,
  Curso: GraduationCap,
  Taller: Wrench,
  Licencia: IdCard,
  Revision: ClipboardCheck,
  Impuestos: ClipboardCheck,
};

/** Carrusel lateral «Servicios Adicionales». Contenido estático: sus botones abren el aviso «Próximamente». */
export function ServiciosAdicionales({ onAbrir }: { onAbrir: (cta: string) => void }) {
  const { activa, ir } = useCarrusel(SERVICIOS_ADICIONALES.length, INTERVALO_CARRUSEL_MS);
  return (
    <section className="dashboard-card servicios-slide" aria-label="Servicios Adicionales" aria-roledescription="carrusel">
      <h2 className="dashboard-title">Servicios Adicionales</h2>
      <div className="relative min-h-0 flex-1 overflow-hidden">
        <div className="flex h-full transition-transform duration-500 motion-reduce:transition-none" style={{ transform: `translateX(-${activa * 100}%)` }}>
          {SERVICIOS_ADICIONALES.map(({ icono, titulo, texto, cta }, i) => {
            const Icono = ICONOS[icono];
            return (
              <div key={texto} aria-hidden={i !== activa} className="service-item flex h-full w-full shrink-0 flex-col items-center gap-2 px-1 text-center">
                <Icono aria-hidden className="h-10 w-10 shrink-0 text-primary" />
                <h3 className="text-sm font-bold">{titulo}</h3>
                <p className="text-sm leading-snug">{texto}</p>
                <button type="button" tabIndex={i === activa ? 0 : -1} className={boton("flit", "mt-auto h-9 shrink-0 px-4 text-xs", "none")} onClick={() => onAbrir(cta)}>{cta}</button>
              </div>
            );
          })}
        </div>
      </div>
      <div className="carousel-dots flex items-center justify-center gap-1">
        {SERVICIOS_ADICIONALES.map((s, i) => (
          <button key={s.texto} type="button" aria-label={`Ir al servicio ${i + 1}`} aria-current={i === activa} onClick={() => ir(i)} className={boton("ghost", "h-5 w-6 p-0", "none")}>
            <span className={`banner-dot h-2 rounded-full ${i === activa ? "w-4 bg-primary" : "w-2 bg-primary/25"}`} />
          </button>
        ))}
      </div>
    </section>
  );
}

export function OtrosServicios({ onAbrir }: { onAbrir: (cta: string) => void }) {
  const { activa } = useCarrusel(OTROS_SERVICIOS.length, INTERVALO_OTROS_SERVICIOS_MS);
  const { icono, titulo, texto, cta } = OTROS_SERVICIOS[activa];
  const Icono = ICONOS[icono];
  return (
    <section id="servicios" className="dashboard-card otros-servicios" aria-label="Otros Servicios Vehiculares">
      <h2 className="dashboard-title flit-gradient-title">Otros Servicios Vehiculares</h2>
      <div key={titulo} className={`otros-servicios-slide ${activa % 2 === 1 ? "otros-servicios-reversed" : ""}`}>
        <div className="otros-servicios-text">
          <h3 className="font-bold text-primary">{titulo}</h3>
          <p>{texto}</p>
          <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => onAbrir(cta)}>{cta}</button>
        </div>
        <div className="otros-servicios-visual" aria-hidden>
          <Icono className="h-16 w-16" />
        </div>
      </div>
    </section>
  );
}
