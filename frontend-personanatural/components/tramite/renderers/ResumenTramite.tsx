"use client";

import { skipToken, useQuery } from "@tanstack/react-query";
import { TriangleAlert } from "lucide-react";
import { useEffect, useId, useMemo, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { Card } from "@/components/tramite/piezas";
import type { Cotizacion } from "@/lib/api/cliente";
import { claveCotizacion } from "@/lib/tramite/cotizacion";
import { grupoDePendiente, type GrupoPaso } from "@/lib/tramite/pasos";
import { armarResumen, destinoDePendiente, pasoTieneSecciones, textoPendiente } from "@/lib/tramite/resumen";

const MOTIVO_CONFIRMAR = "Confirma que revisaste la información para continuar.";

const NOMBRE_PASO: Readonly<Record<GrupoPaso, string>> = {
  diagnostico: "Diagnóstico",
  actores: "Actores",
  requisitos: "Requisitos",
  resumen: "Resumen",
  envio: "Envío",
};

/**
 * Paso 4 «Resumen» (HU F5, reducida): lo que el ciudadano llenó, en solo lectura, con «Editar» por sección que vuelve al paso
 * donde se llenó. El documento, el correo y el celular de cada persona salen enmascarados. No hay cobro: el trámite se entrega
 * a FLIT 2 como borrador. Para pasar al envío hay que confirmar explícitamente; si algo cambia después (cambia la versión del
 * borrador), la confirmación se pide de nuevo.
 */
export function ResumenTramite({ seccion, onEstado }: PropsSeccion) {
  const { definicion, solicitud, irAPaso } = useWizard();
  const idConfirmacion = useId();
  const [confirmadaEn, setConfirmadaEn] = useState<number | null>(null);
  const confirmada = confirmadaEn === solicitud.version;

  // Solo lectura de la cotización que el ciudadano ya calculó en la barra lateral: aquí no se pide ninguna.
  const transformaciones = solicitud.datos.requisitos?.transformaciones ?? [];
  const cotizacion = useQuery<Cotizacion>({ queryKey: claveCotizacion(solicitud.organismoId, definicion.code, transformaciones), queryFn: skipToken, staleTime: Infinity });
  const valorInformativo = cotizacion.data?.totalCop ?? null;

  const secciones = useMemo(() => armarResumen(definicion, solicitud, valorInformativo), [definicion, solicitud, valorInformativo]);
  const pendientes = solicitud.pendientes.filter((p) => grupoDePendiente(p, definicion) !== "envio");

  useEffect(() => {
    onEstado({ completo: confirmada, bloqueo: confirmada ? undefined : MOTIVO_CONFIRMAR });
  }, [confirmada, onEstado]);

  return (
    <Card title="Resumen de tu trámite" id={seccion.clave.replace(/[^a-z0-9]/gi, "-")}>
      <p className="mb-4 text-sm text-muted-foreground">
        Revisa que la información esté correcta antes de enviar. Por tu seguridad, el documento, el correo y el celular se muestran de forma parcial.
      </p>

      {pendientes.length > 0 && (
        <div role="status" className="mb-4 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
          <p className="flex items-center gap-2 font-semibold">
            <TriangleAlert aria-hidden className="h-5 w-5 shrink-0" />
            Aún falta información
          </p>
          <ul className="mt-2 space-y-1">
            {pendientes.map((p, i) => {
              const destino = destinoDePendiente(p, definicion);
              return (
                <li key={`${p.codigo}-${p.referencia ?? ""}-${i}`} className="flex flex-wrap items-center gap-2">
                  <span>{textoPendiente(p, definicion)}</span>
                  {destino && (
                    <button type="button" className={boton("link", "h-auto p-0 text-alert", "none")} onClick={() => irAPaso(destino)}>
                      Ir a {NOMBRE_PASO[destino]}
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}

      <div className="space-y-4">
        {secciones.map((s) => {
          const idTitulo = `${idConfirmacion}-${s.id.replace(/[^a-z0-9]/gi, "-")}`;
          const puedeEditar = s.editar !== null && pasoTieneSecciones(s.editar, definicion);
          return (
            <section key={s.id} aria-labelledby={idTitulo} className="rounded-[9px] border border-border p-4">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h4 id={idTitulo} className="text-base font-bold text-primary">
                  {s.titulo}
                </h4>
                {puedeEditar && s.editar && (
                  <button type="button" className={boton("back", "h-9 px-4", "none")} onClick={() => irAPaso(s.editar as GrupoPaso)} aria-label={`Editar ${s.titulo}`}>
                    Editar
                  </button>
                )}
              </div>
              <div className="mt-3 grid gap-4 md:grid-cols-2">
                {s.bloques.map((b, i) => (
                  <div key={`${b.titulo ?? "bloque"}-${i}`} className={s.bloques.length === 1 ? "md:col-span-2" : undefined}>
                    {b.titulo && <p className="mb-1 text-sm font-semibold">{b.titulo}</p>}
                    <dl className="grid min-w-0 grid-cols-[minmax(0,2fr)_minmax(0,3fr)] gap-x-3 gap-y-1 text-sm">
                      {b.filas.map((f) => (
                        <div key={f.etiqueta} className="contents">
                          <dt className="font-light text-muted-foreground">{f.etiqueta}</dt>
                          <dd className="min-w-0 break-words font-medium">{f.valor}</dd>
                        </div>
                      ))}
                    </dl>
                  </div>
                ))}
              </div>
            </section>
          );
        })}
      </div>

      <label htmlFor={idConfirmacion} className="mt-6 flex cursor-pointer items-start gap-3 text-sm font-medium">
        <input id={idConfirmacion} type="checkbox" className="mt-0.5 h-5 w-5 shrink-0 accent-primary" checked={confirmada} onChange={(e) => setConfirmadaEn(e.target.checked ? solicitud.version : null)} />
        <span>Revisé la información y autorizo el envío</span>
      </label>
    </Card>
  );
}
