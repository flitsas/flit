"use client";

import { Suspense, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { Miga } from "@flit/ui";
import { ContenidoDetalle } from "@/components/detalle/ContenidoDetalle";
import { escapeVuelve, hayCapaAbierta, nombreOrigen, volverSeguro } from "@/components/detalle/ruta";
import type { SeccionDetalle } from "@/components/detalle/Timeline";
import type { DetalleComparendo } from "@/lib/tipos";

const SECCIONES: SeccionDetalle[] = ["dp", "pago", "contraventor"];

/** Detalle del comparendo como página propia (rediseño S1). «Volver» y Escape regresan a la pantalla de origen. */
export default function PaginaDetalle() {
  return (
    <Suspense fallback={null}>
      <Contenido />
    </Suspense>
  );
}

function Contenido() {
  const { id } = useParams<{ id: string }>();
  const params = useSearchParams();
  const router = useRouter();
  const volver = volverSeguro(params.get("volver"));
  const seccion = SECCIONES.find((s) => s === params.get("seccion")) ?? null;
  const [numero, setNumero] = useState<string | null>(null);
  const alCargar = useCallback((d: DetalleComparendo) => setNumero(d.numero), []);

  useEffect(() => {
    // Escape cierra solo la capa de arriba. Si había una (modal, menú, dock), la página se queda: se mide en la CAPTURA,
    // porque al burbujear la capa ya pudo desmontarse (React 19 aplica el cierre entre un listener y otro).
    let habiaCapa = false;
    const alCapturar = (e: KeyboardEvent) => {
      if (e.key === "Escape") habiaCapa = hayCapaAbierta(document);
    };
    const alPulsar = (e: KeyboardEvent) => {
      if (escapeVuelve(e, habiaCapa)) router.push(volver);
      habiaCapa = false;
    };
    window.addEventListener("keydown", alCapturar, true);
    window.addEventListener("keydown", alPulsar);
    return () => {
      window.removeEventListener("keydown", alCapturar, true);
      window.removeEventListener("keydown", alPulsar);
    };
  }, [router, volver]);

  return (
    <section className="flex flex-col gap-3">
      <Miga
        items={[{ etiqueta: nombreOrigen(volver), href: volver }, { etiqueta: numero ?? "Comparendo" }]}
        enlace={({ href, className, children }) => (
          <Link href={href} className={className}>
            {children}
          </Link>
        )}
      />
      {/* La clave reinicia secciones y pestañas al pasar de un comparendo a otro. */}
      <ContenidoDetalle key={`${id}:${seccion ?? ""}`} id={id} seccionInicial={seccion} onCargado={alCargar} />
    </section>
  );
}
