"use client";

import { Suspense, useState } from "react";
import dynamic from "next/dynamic";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { InlineAlert } from "@flit/ui";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Cargador } from "@flit/ui/Loader";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { apiFetch } from "@/lib/api";
import { diaColombia, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { FRENTES, hrefBandeja, inicioDeMes, pesosCorto, type Frentes, type Monto, type RespuestaDinero } from "@/lib/reportes";
import { capacidades, tienePermiso } from "@/lib/sesion";
import type { Contadores } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/** El bloque de dinero (y recharts) se baja solo si la empresa tiene reportes; mientras, el mismo cargador de antes. */
const DineroInicio = dynamic(() => import("@/components/inicio/DineroInicio").then((m) => m.DineroInicio), { loading: () => <Cargador /> });

/** Frentes sin el módulo de reportes: solo cantidades, de los mismos contadores de la bandeja. */
function useFrentesSinDinero(listo: boolean, empresa: string | null): { frentes: Record<keyof Frentes, Monto | null> | null; error: string | null } {
  const q = { empresa: empresa ?? undefined };
  const base = useCarga(listo ? `inicio-contadores:${empresa}` : null, (signal) => apiFetch<Contadores>("/comparendos/contadores", { signal, query: q }));
  const sinGestion = useCarga(listo ? `inicio-sin-gestion:${empresa}` : null, (signal) =>
    apiFetch<Contadores>("/comparendos/contadores", { signal, query: { ...q, sinGestion: true } }),
  );
  const caducidad = useCarga(listo ? `inicio-caducidad:${empresa}` : null, (signal) =>
    apiFetch<Contadores>("/comparendos/contadores", { signal, query: { ...q, juridico: "CaducidadInmediata" } }),
  );
  const error = base.error ?? sinGestion.error ?? caducidad.error ?? null;
  if (!base.datos) return { frentes: null, error };
  const n = (cantidad: number | undefined): Monto | null => (cantidad === undefined ? null : { cantidad, valor: 0 });
  return {
    error,
    frentes: {
      proximosAVencer: n(base.datos.proximosAVencer),
      sinContraventor: n(base.datos.sinContraventor),
      rechazadosOt: n(base.datos.rechazadosOt),
      vencidos: n(base.datos.estadosLegales.Vencido),
      sinGestion: n(sinGestion.datos?.total),
      caducidadInmediata: n(caducidad.datos?.total),
    },
  };
}

function ParaHoy({
  frentes,
  conDinero,
  descuento,
  empresa,
}: {
  frentes: Record<keyof Frentes, Monto | null> | null;
  conDinero: boolean;
  descuento: { cantidad: number; ahorro: number } | null;
  empresa: string | null;
}) {
  const conCifra = FRENTES.filter((f) => (frentes?.[f.clave]?.cantidad ?? 0) > 0).length;
  const subtitulo = !frentes
    ? "Contando lo que necesita atención…"
    : conCifra === 0
      ? "Nada urgente hoy."
      : `Hay ${conCifra} ${conCifra === 1 ? "frente" : "frentes"} por atender. Cada tarjeta abre la bandeja con ese filtro.`;
  return (
    <PanelInicio titulo="Para hoy" subtitulo={subtitulo} icono="alerta" data-testid="para-hoy">
      {/* 150 px por casilla: los 7 frentes caben en una fila desde ~1280 px y no queda uno huérfano a 1512. */}
      <div className="grid grid-cols-[repeat(auto-fit,minmax(150px,1fr))] gap-4">
        {FRENTES.map((f) => {
          const m = frentes?.[f.clave] ?? null;
          const cantidad = m?.cantidad ?? null;
          const vacio = cantidad === 0;
          return (
            <TarjetaMetrica
              key={f.clave}
              data-testid={`frente-${f.clave}`}
              etiqueta={f.titulo}
              cifra={cantidad === null ? "—" : cantidad.toLocaleString("es-CO")}
              pendiente={!!f.pendiente && !!cantidad}
              colorCifra={vacio ? "var(--di-text-tertiary)" : undefined}
              title={conDinero && m ? `${cantidad?.toLocaleString("es-CO")} comparendos · ${formatoPesos(m.valor)}` : undefined}
              aria-label={`${f.titulo}: ${cantidad === null ? "sin dato" : cantidad.toLocaleString("es-CO")} comparendos${conDinero && m ? ` · ${formatoPesos(m.valor)}` : ""}. ${f.accion}`}
              pie={conDinero && m && !vacio ? `${pesosCorto(m.valor)} · ${f.accion}` : f.accion}
              href={vacio || cantidad === null ? undefined : hrefBandeja(f.query, { empresa })}
              enlace={Link}
            />
          );
        })}
        {conDinero && descuento && (
          <TarjetaMetrica
            data-testid="frente-descuento"
            etiqueta="En ventana de descuento"
            cifra={descuento.cantidad.toLocaleString("es-CO")}
            colorCifra={descuento.cantidad === 0 ? "var(--di-text-tertiary)" : "var(--flit-success-ink)"}
            title={`Ahorro posible estimado: ${formatoPesos(descuento.ahorro)}`}
            pie={descuento.cantidad > 0 ? `Pagar ya ahorraría ~${pesosCorto(descuento.ahorro)} (estimado)` : "Ninguno con descuento vigente"}
          />
        )}
      </div>
    </PanelInicio>
  );
}

function Contenido() {
  const { sesion } = useSesion();
  const params = useSearchParams();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  const [empresa, setEmpresa] = useState<string | null>(() => params.get("empresa"));
  const listo = !!sesion && (!esSuperAdmin || !!empresa);
  const conDinero = listo && capacidades(sesion).reportes && tienePermiso(sesion, P.reportesVer);
  const veBandeja = tienePermiso(sesion, P.bandejaVer);
  const empresaQuery = esSuperAdmin ? empresa : null;

  const hoy = diaColombia();
  const dinero = useCarga(conDinero ? `inicio-dinero:${empresaQuery}` : null, (signal) =>
    apiFetch<RespuestaDinero>("/reportes/dinero", { signal, query: { empresa: empresaQuery, desde: inicioDeMes(hoy), hasta: hoy } }),
  );
  const sinDinero = useFrentesSinDinero(listo && !conDinero && veBandeja, empresaQuery);
  const d = dinero.datos?.dinero;
  const frentes = conDinero ? (d?.frentes ?? null) : sinDinero.frentes;
  const error = conDinero ? dinero.error : sinDinero.error;

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Inicio"
        ilustracion="comparendos"
        descripcion={
          esSuperAdmin
            ? "Vista de administración FLIT: elige una empresa para ver su tablero."
            : `${sesion?.empresa?.nombre ? `${sesion.empresa.nombre} · ` : ""}Lo que hay que atender hoy${conDinero ? " y cómo va la plata" : ""}.`
        }
        acciones={esSuperAdmin ? <SelectorEmpresa valor={empresa} onCambio={setEmpresa} /> : undefined}
      />

      {!sesion && <Cargador />}
      {sesion && esSuperAdmin && !empresa && (
        <EstadoVacio titulo="Elige una empresa" mensaje="El tablero muestra los comparendos de una empresa a la vez." />
      )}
      {error && (
        <InlineAlert tone="error" title="No se pudo cargar el tablero">
          {error}{" "}
          <button type="button" className="font-semibold underline" onClick={conDinero ? dinero.recargar : () => window.location.reload()}>
            Reintentar
          </button>
        </InlineAlert>
      )}

      {listo && veBandeja && (
        <ParaHoy
          frentes={frentes}
          conDinero={conDinero}
          descuento={d ? { cantidad: d.descuento.d50.cantidad + d.descuento.d25.cantidad, ahorro: d.descuento.d50.ahorroEstimado + d.descuento.d25.ahorroEstimado } : null}
          empresa={empresaQuery}
        />
      )}
      {conDinero && dinero.estado === "loading" && <Cargador />}
      {conDinero && dinero.datos && <DineroInicio datos={dinero.datos} empresa={empresaQuery} />}
    </section>
  );
}

/**
 * Inicio de Comparendos (rediseño, S2): «Para hoy» con los frentes que llevan a la bandeja filtrada y, si la empresa
 * tiene el módulo de reportes, la lente de dinero (deuda cobrable contra caducada o prescrita, ahorro, pagado,
 * tendencia y organismos). Sin el módulo solo usa los contadores de la bandeja: nada de cifras en pesos ni 403.
 */
export default function Inicio() {
  return (
    <Suspense fallback={<Cargador />}>
      <Contenido />
    </Suspense>
  );
}
