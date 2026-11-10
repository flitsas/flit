"use client";

// HU #14458 / #14460 — Portal de caja del organismo. Sin menú Iniciar/Consultar/Cotizar: marca del organismo, la persona de
// caja con «Cerrar sesión» y dos secciones, «Cobrar» y «Cobros del día». La sesión OIDC vive en el servidor; aquí solo se
// pregunta quién es (`/caja/sesion`) y, si entró por la URL de otro organismo, se le lleva al suyo.
import { useQuery } from "@tanstack/react-query";
import { LogOut } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { boton } from "@/components/landing/estilos";
import { LogoPortal } from "@/components/marca/MarcaPunto";
import { cajaApi, CajaError } from "@/lib/caja/cliente";
import { mensajeDeCaja } from "@/lib/caja/mensajes";
import { cerrarSesion, irAlLogin } from "@/lib/caja/navegacion";
import { CobrarTramite } from "./CobrarTramite";
import { CobrosDelDia } from "./CobrosDelDia";
import { MensajeCaja } from "./MensajeCaja";

const SECCIONES = [
  { id: "cobrar", etiqueta: "Cobrar" },
  { id: "cobros", etiqueta: "Cobros del día" },
] as const;
type SeccionId = (typeof SECCIONES)[number]["id"];

export const rutaCajaDe = (codigo: string): string => `/o/${encodeURIComponent(codigo)}/caja`;

export function PortalCaja({ codigo }: { codigo: string }) {
  const router = useRouter();
  const [seccion, setSeccion] = useState<SeccionId>("cobrar");
  const pestanas = useRef<Record<string, HTMLButtonElement | null>>({});

  const sesion = useQuery({
    queryKey: ["pn", "caja", "sesion"],
    queryFn: ({ signal }) => cajaApi.sesion(signal),
    retry: (intentos, error) => !(error instanceof CajaError) && intentos < 2,
  });

  const organismoDeLaCaja = sesion.data?.organismo.codigo;
  const enOtroOrganismo = organismoDeLaCaja !== undefined && organismoDeLaCaja !== codigo;
  useEffect(() => {
    if (enOtroOrganismo) router.replace(rutaCajaDe(organismoDeLaCaja));
  }, [enOtroOrganismo, organismoDeLaCaja, router]);

  useEffect(() => {
    if (sesion.error instanceof CajaError && sesion.error.sinSesion) irAlLogin();
  }, [sesion.error]);

  function teclado(e: KeyboardEvent<HTMLButtonElement>) {
    const i = SECCIONES.findIndex((s) => s.id === seccion);
    const destino = e.key === "ArrowRight" ? (i + 1) % SECCIONES.length : e.key === "ArrowLeft" ? (i + SECCIONES.length - 1) % SECCIONES.length : null;
    if (destino === null) return;
    e.preventDefault();
    setSeccion(SECCIONES[destino].id);
    pestanas.current[SECCIONES[destino].id]?.focus();
  }

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border bg-card">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3 px-4 py-3">
          <LogoPortal alt="FLIT" />
          <div className="flex items-center gap-3 text-sm">
            {sesion.data && (
              <p>
                <span className="sr-only">Sesión de </span>
                <strong data-testid="usuario-caja">{sesion.data.usuario.nombre}</strong>
                <span className="text-muted-foreground"> · {sesion.data.organismo.nombre}</span>
              </p>
            )}
            <button type="button" onClick={() => void cerrarSesion()} className={boton("back", "min-h-10 px-4", "none")}>
              <LogOut aria-hidden />
              Cerrar sesión
            </button>
          </div>
        </div>
      </header>

      <main className="mx-auto grid max-w-5xl gap-6 px-4 py-6">
        <h1 className="text-2xl font-semibold">Caja del organismo</h1>

        {(sesion.isPending || enOtroOrganismo) && <MobilityLoader mensaje="Abriendo la caja…" />}

        {sesion.isError && !(sesion.error instanceof CajaError && sesion.error.sinSesion) && (
          <div className="grid gap-3">
            <MensajeCaja tono="error">{mensajeDeCaja(sesion.error).texto}</MensajeCaja>
            <div>
              <button type="button" className={boton("flit", "min-h-11 px-6", "none")} onClick={() => void sesion.refetch()}>
                Reintentar
              </button>
            </div>
          </div>
        )}

        {sesion.data && !enOtroOrganismo && (
          <>
            <div role="tablist" aria-label="Secciones de la caja" className="flex gap-2 border-b border-border">
              {SECCIONES.map((s) => (
                <button
                  key={s.id}
                  ref={(el) => {
                    pestanas.current[s.id] = el;
                  }}
                  type="button"
                  role="tab"
                  id={`pestana-caja-${s.id}`}
                  aria-selected={seccion === s.id}
                  aria-controls={`panel-caja-${s.id}`}
                  tabIndex={seccion === s.id ? 0 : -1}
                  onClick={() => setSeccion(s.id)}
                  onKeyDown={teclado}
                  className={`min-h-11 border-b-2 px-4 text-sm font-semibold ${seccion === s.id ? "border-primary text-primary" : "border-transparent"}`}
                >
                  {s.etiqueta}
                </button>
              ))}
            </div>
            {/* Los dos paneles quedan montados: cambiar de sección no pierde el trámite que se está cobrando. */}
            {SECCIONES.map((s) => (
              <div key={s.id} role="tabpanel" id={`panel-caja-${s.id}`} aria-labelledby={`pestana-caja-${s.id}`} hidden={seccion !== s.id} tabIndex={0}>
                {s.id === "cobrar" ? <CobrarTramite /> : <CobrosDelDia />}
              </div>
            ))}
          </>
        )}
      </main>
    </div>
  );
}
