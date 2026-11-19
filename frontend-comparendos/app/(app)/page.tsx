"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { InlineAlert } from "@flit/ui";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Cargador } from "@flit/ui/Loader";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { DineroInicio } from "@/components/inicio/DineroInicio";
import { OperacionInicio } from "@/components/inicio/OperacionInicio";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { SuperAdminInicio } from "@/components/inicio/SuperAdminInicio";
import { controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { diaColombia, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { FRENTES, hrefBandeja, inicioDeMes, pesosCorto, type Frentes, type Monto, type Operacion, type RespuestaDineroInicio, type RespuestaOperacion } from "@/lib/reportes";
import { capacidades, tienePermiso } from "@/lib/sesion";
import type { Contadores } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/** «412 comparendos pendientes en 287 vehículos · en 30 días entraron 45 y salieron 18». */
function fraseResumen(o: Operacion): string {
  const r = o.resumen;
  const n = (x: number) => x.toLocaleString("es-CO");
  const valor = r.valorPendiente === null ? "" : ` por ${pesosCorto(r.valorPendiente)}`;
  return `${n(r.pendientes)} comparendos pendientes${valor} en ${n(r.vehiculos)} ${r.vehiculos === 1 ? "vehículo" : "vehículos"}. En los últimos 30 días entraron ${n(r.nuevos30)} y ${n(r.resueltos30)} dejaron de estar pendientes (pagados o cerrados).`;
}

/** Frentes sin el módulo de reportes: solo cantidades, de los mismos contadores de la bandeja. */
function useFrentesSinDinero(listo: boolean, empresa: string | null): { frentes: Record<keyof Frentes, Monto | null> | null; error: string | null; recargar: () => void } {
  const q = { empresa: empresa ?? undefined };
  const base = useCarga(listo ? `inicio-contadores:${empresa}` : null, (signal) => apiFetch<Contadores>("/comparendos/contadores", { signal, query: q }));
  const sinGestion = useCarga(listo ? `inicio-sin-gestion:${empresa}` : null, (signal) =>
    apiFetch<Contadores>("/comparendos/contadores", { signal, query: { ...q, sinGestion: true } }),
  );
  const caducidad = useCarga(listo ? `inicio-caducidad:${empresa}` : null, (signal) =>
    apiFetch<Contadores>("/comparendos/contadores", { signal, query: { ...q, juridico: "CaducidadInmediata" } }),
  );
  const error = base.error ?? sinGestion.error ?? caducidad.error ?? null;
  // «Reintentar» vuelve a pedir solo lo que falló, sin recargar la página.
  const recargar = () => {
    if (base.error) base.recargar();
    if (sinGestion.error) sinGestion.recargar();
    if (caducidad.error) caducidad.recargar();
  };
  if (!base.datos) return { frentes: null, error, recargar };
  const n = (cantidad: number | undefined): Monto | null => (cantidad === undefined ? null : { cantidad, valor: 0 });
  return {
    error,
    recargar,
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
          const enlaza = cantidad !== null && cantidad > 0;
          return (
            <TarjetaMetrica
              key={f.clave}
              data-testid={`frente-${f.clave}`}
              etiqueta={f.titulo}
              cifra={cantidad === null ? "—" : cantidad.toLocaleString("es-CO")}
              pendiente={!!f.pendiente && !!cantidad}
              colorCifra={vacio ? "var(--di-text-tertiary)" : undefined}
              // El monto es el valor de esos comparendos (como en la bandeja, pagados incluidos), no la deuda.
              title={conDinero && m ? `${cantidad?.toLocaleString("es-CO")} comparendos por un valor de ${formatoPesos(m.valor)}` : undefined}
              // Solo el enlace lleva aria-label: en una tarjeta sin enlace (frente en 0) el contenido ya se lee solo.
              aria-label={
                enlaza
                  ? `${f.titulo}: ${cantidad.toLocaleString("es-CO")} comparendos${conDinero && m ? ` por un valor de ${formatoPesos(m.valor)}` : ""}. ${f.accion}`
                  : undefined
              }
              pie={conDinero && m && !vacio ? `Valor ${pesosCorto(m.valor)} · ${f.accion}` : f.accion}
              href={enlaza ? hrefBandeja(f.query, { empresa }) : undefined}
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
  const router = useRouter();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  const [empresa, setEmpresaEstado] = useState<string | null>(() => params.get("empresa"));
  // Como la bandeja: la empresa elegida queda en la URL, así «atrás» o recargar vuelven al mismo tablero.
  const setEmpresa = (id: string | null) => {
    setEmpresaEstado(id);
    router.replace(id ? `/?empresa=${encodeURIComponent(id)}` : "/", { scroll: false });
  };
  const listo = !!sesion && (!esSuperAdmin || !!empresa);
  const conDinero = listo && capacidades(sesion).reportes && tienePermiso(sesion, P.reportesVer);
  const veBandeja = tienePermiso(sesion, P.bandejaVer);
  const empresaQuery = esSuperAdmin ? empresa : null;

  const hoy = diaColombia();
  const dinero = useCarga(conDinero ? `inicio-dinero:${empresaQuery}` : null, (signal) =>
    // partes=inicio: solo los frentes, el descuento y la franja (sin organismos, cobro coactivo ni serie, que son de Reportes).
    apiFetch<RespuestaDineroInicio>("/reportes/dinero", { signal, query: { empresa: empresaQuery, desde: inicioDeMes(hoy), hasta: hoy, partes: "inicio" } }),
  );
  const sinDinero = useFrentesSinDinero(listo && !conDinero && veBandeja, empresaQuery);
  // Operación (R2): con BandejaVer, tenga o no el módulo de reportes; los pesos vienen en null sin él.
  const operacion = useCarga(listo && veBandeja ? `inicio-operacion:${empresaQuery}` : null, (signal) =>
    apiFetch<RespuestaOperacion>("/comparendos/operacion", { signal, query: { empresa: empresaQuery } }),
  );
  // Solo lo que corresponde a la clave actual: al pasar del SuperAdmin de la empresa A a la B (o volver a «Todas»), useCarga
  // conserva los datos de A mientras carga B, y no deben verse (ni sus pesos) bajo el nombre de B.
  const d = dinero.estado === "ready" ? dinero.datos?.dinero : undefined;
  const o = operacion.estado === "ready" ? operacion.datos?.operacion : undefined;
  const frentes = conDinero ? (d?.frentes ?? null) : sinDinero.frentes;
  // Solo con una empresa elegida: al volver a «Todas» un error de la empresa anterior no debe quedarse a la vista.
  const error = listo ? ((conDinero ? dinero.error : sinDinero.error) ?? operacion.error) : null;
  const reintentar = () => {
    if (conDinero && dinero.error) dinero.recargar();
    if (!conDinero && sinDinero.error) sinDinero.recargar();
    if (operacion.error) operacion.recargar();
  };

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Inicio"
        ilustracion="comparendos"
        descripcion={
          esSuperAdmin && !empresa
            ? "Vista de administración FLIT: la salud de todas las empresas. Elige una para ver su Inicio."
            : o
              ? fraseResumen(o)
              : `${sesion?.empresa?.nombre ? `${sesion.empresa.nombre} · ` : ""}Lo que hay que atender hoy.`
        }
        acciones={
          esSuperAdmin ? (
            <>
              <SelectorEmpresa valor={empresa} onCambio={setEmpresa} />
              {empresa && (
                <button type="button" className={controlCls(false)} onClick={() => setEmpresa(null)}>
                  Todas las empresas
                </button>
              )}
            </>
          ) : undefined
        }
      />

      {!sesion && <Cargador />}
      {sesion && esSuperAdmin && !empresa && <SuperAdminInicio />}
      {error && (
        <InlineAlert tone="error" title="No se pudo cargar el tablero">
          {error}{" "}
          <button type="button" className="font-semibold underline" onClick={reintentar}>
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
      {/* R4 (Samuel): el dinero va segundo, debajo de «Para hoy». Mientras carga ocupa su sitio (MAIA): si apareciera
          después, empujaría toda la operación hacia abajo. */}
      {/* Con error no se queda cargando para siempre: el aviso de arriba ya dice qué pasó y ofrece «Reintentar» (GIA). */}
      {conDinero && dinero.estado !== "error" && <DineroInicio datos={dinero.estado === "ready" ? dinero.datos : null} />}
      {listo && !veBandeja && !conDinero && (
        <EstadoVacio
          titulo="Tu Inicio está vacío por ahora"
          mensaje="Tu usuario no tiene acceso a la bandeja de comparendos ni a los reportes de la empresa. Pídele al administrador de tu empresa que te dé acceso."
        />
      )}
      {listo && veBandeja && operacion.estado === "loading" && <Cargador />}
      {/* listo: al volver a «Todas», useCarga conserva los datos de la empresa anterior y no deben verse. */}
      {listo && o && <OperacionInicio o={o} empresa={empresaQuery} />}
    </section>
  );
}

/**
 * Inicio de Comparendos (rediseño S2, R2 y R4). Gestora: «Para hoy» (frentes que llevan a la bandeja filtrada), con el
 * módulo de reportes la franja plegable de dinero (segunda, R4) y la operación (gestión, vehículos, evitables,
 * calendario, antigüedad, DP por organismo; GET /comparendos/operacion con BandejaVer). Sin el módulo no hay pesos ni 403. SuperAdmin: con «Todas»,
 * la salud de la plataforma y las empresas que necesitan algo; con una empresa, el Inicio de esa gestora.
 */
export default function Inicio() {
  return (
    <Suspense fallback={<Cargador />}>
      <Contenido />
    </Suspense>
  );
}
