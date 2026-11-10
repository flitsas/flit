"use client";

// HU #14460 — Cobros del día del organismo: tabla paginada con el total del día; los anulados se marcan y no suman.
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { MobilityLoader } from "@/components/MobilityLoader";
import { cajaApi, CajaError } from "@/lib/caja/cliente";
import { mensajeDeCaja } from "@/lib/caja/mensajes";
import { irAlLogin } from "@/lib/caja/navegacion";
import { ETIQUETA_MEDIO } from "@/lib/caja/tipos";
import { formatearPesos } from "@/lib/formato";
import { CLAVE_COBROS_HOY } from "./CobrarTramite";
import { MensajeCaja } from "./MensajeCaja";
import { PaginacionCaja, TAMANO_POR_DEFECTO } from "./PaginacionCaja";

const ENCABEZADO = "px-4 py-2.5 text-left text-[10px] font-semibold uppercase tracking-wider";

export function CobrosDelDia() {
  const [pagina, setPagina] = useState(1);
  const [tamano, setTamano] = useState(TAMANO_POR_DEFECTO);

  const consulta = useQuery({
    queryKey: [...CLAVE_COBROS_HOY, pagina, tamano],
    queryFn: ({ signal }) => cajaApi.cobrosHoy(pagina, tamano, signal),
    // Al cambiar de página la tabla sigue en pantalla hasta que llega la siguiente.
    placeholderData: keepPreviousData,
    retry: (intentos, error) => !(error instanceof CajaError) && intentos < 2,
  });

  useEffect(() => {
    if (consulta.error instanceof CajaError && consulta.error.sinSesion) irAlLogin();
  }, [consulta.error]);

  if (consulta.isPending) {
    return (
      <div className="grid place-items-center py-10">
        <MobilityLoader mensaje="Cargando los cobros del día…" />
      </div>
    );
  }

  if (consulta.isError) {
    return (
      <div className="grid gap-3">
        <MensajeCaja tono="error">{mensajeDeCaja(consulta.error).texto}</MensajeCaja>
        <div>
          <button type="button" className={boton("flit", "min-h-11 px-6", "none")} onClick={() => void consulta.refetch()}>
            Reintentar
          </button>
        </div>
      </div>
    );
  }

  const datos = consulta.data;
  return (
    <section aria-labelledby="titulo-cobros-dia" className="grid gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h2 id="titulo-cobros-dia" className="text-xl font-semibold">
          Cobros del día
        </h2>
        <p className="text-sm">
          Total del día: <strong data-testid="total-dia">{formatearPesos(datos.totalDiaCop)}</strong>
        </p>
      </div>

      {datos.total === 0 ? (
        <p className="rounded-[9px] border border-border bg-card p-6 text-sm">Hoy no hay cobros registrados.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[40rem] border-separate border-spacing-y-1.5 text-sm">
            <caption className="sr-only">Cobros registrados hoy en este organismo</caption>
            <thead>
              <tr style={{ background: "#DFE5ED", color: "var(--flit-navy)" }}>
                <th scope="col" className={ENCABEZADO}>ID</th>
                <th scope="col" className={`${ENCABEZADO} text-right`}>Valor</th>
                <th scope="col" className={ENCABEZADO}>Medio</th>
                <th scope="col" className={ENCABEZADO}>Hora</th>
                <th scope="col" className={ENCABEZADO}>Usuario</th>
                <th scope="col" className={ENCABEZADO}>Estado</th>
              </tr>
            </thead>
            <tbody>
              {datos.items.map((c) => {
                const anulado = c.estado === "anulado";
                return (
                  <tr key={`${c.codigo}-${c.hora}-${c.estado}`} className="bg-card shadow-[0_1px_0_var(--border)]">
                    <td className={`px-4 py-2.5 font-semibold ${anulado ? "line-through" : ""}`}>{c.codigo}</td>
                    <td className={`px-4 py-2.5 text-right tabular-nums ${anulado ? "line-through" : ""}`}>{formatearPesos(c.valorCop)}</td>
                    <td className="px-4 py-2.5">{c.medioDetalle ? `${ETIQUETA_MEDIO[c.medio] ?? c.medio} (${c.medioDetalle})` : (ETIQUETA_MEDIO[c.medio] ?? c.medio)}</td>
                    <td className="px-4 py-2.5 tabular-nums">{c.hora}</td>
                    <td className="px-4 py-2.5">{c.usuario}</td>
                    <td className="px-4 py-2.5">
                      {anulado ? (
                        <span className="inline-block rounded-full bg-warning px-3 py-0.5 text-xs font-semibold text-secondary">Anulado</span>
                      ) : (
                        <span className="inline-block rounded-full bg-success px-3 py-0.5 text-xs font-semibold text-secondary">Pagado</span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <PaginacionCaja
        pagina={pagina}
        tamano={tamano}
        total={datos.total}
        onPagina={setPagina}
        onTamano={(t) => {
          setTamano(t);
          setPagina(1);
        }}
      />
    </section>
  );
}
