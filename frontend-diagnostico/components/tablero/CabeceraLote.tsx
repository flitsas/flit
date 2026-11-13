"use client";

import Link from "next/link";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { Icono } from "@flit/ui/Icono";
import { Modal } from "@flit/ui/Modal";
import { Migas } from "@/components/comun/Migas";
import { AYUDA_CLS, BOTON_PRIMARIO_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";
import { idCorto } from "@/lib/diagnosticos/listado";
import type { Grilla } from "@/lib/tablero/api";
import { fechaDelDato } from "./CeldaSemaforo";

/**
 * HU #14562 AC23/AC31: «ID · modo · trámite · Creado dd/mm/aaaa hh:mm · Por nombre» de `grilla.lote` (B2). Sin `lote` (API
 * previo) no hay línea; el trámite (null en Simple) y el creador (null si Identidad no responde) se omiten si faltan.
 */
export function metadatosLote(grilla: Grilla): string | null {
  const { lote } = grilla;
  if (!lote) return null;
  const partes = [`ID ${idCorto(grilla.id)}`, lote.modo === "CON_ANALISIS" ? "Con análisis IA" : "Simple"];
  if (lote.tipo_tramite?.nombre) partes.push(lote.tipo_tramite.nombre);
  partes.push(`Creado ${fechaDelDato(lote.creado_en)}`);
  if (lote.usuario_creador?.nombre) partes.push(`Por ${lote.usuario_creador.nombre}`);
  return partes.join(" · ");
}

export interface CabeceraLoteProps {
  grilla: Grilla | null;
  empresa?: string;
  /** Listado con los filtros que traía (HU #14087). */
  volver: string;
  puedeCerrar: boolean;
  cerrando: boolean;
  confirmando: boolean;
  onPedirCierre: () => void;
  onCancelarCierre: () => void;
  onConfirmarCierre: () => void;
}

/**
 * Cabecera del tablero (HU #14562 AC23/AC24): migas, nombre del lote, metadatos, «Volver a diagnósticos» y «Cerrar lote» con
 * su modal de confirmación.
 */
export function CabeceraLote({
  grilla,
  empresa,
  volver,
  puedeCerrar,
  cerrando,
  confirmando,
  onPedirCierre,
  onCancelarCierre,
  onConfirmarCierre,
}: CabeceraLoteProps) {
  const titulo = grilla?.nombre ?? "Tablero del diagnóstico";
  const metadatos = grilla ? metadatosLote(grilla) : null;
  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="tablero-titulo"
        tituloId="tablero-titulo"
        titulo={titulo}
        descripcion={
          metadatos || empresa ? (
            <span className="flex flex-col gap-0.5">
              {empresa && <span>{empresa}</span>}
              {metadatos && <span className={AYUDA_CLS}>{metadatos}</span>}
            </span>
          ) : undefined
        }
        encima={<Migas migas={[{ texto: "Diagnósticos", href: volver }, { texto: titulo }]} />}
        icono="diagnostico"
        acciones={
          <>
            <Link href={volver} className={BOTON_SECUNDARIO_CLS}>
              <Icono nombre="izquierda" className="h-4 w-4" />
              Volver a diagnósticos
            </Link>
            {puedeCerrar && (
              <button type="button" onClick={onPedirCierre} disabled={cerrando} aria-busy={cerrando} className={BOTON_SECUNDARIO_CLS}>
                <Icono nombre="portapapeles" className="h-4 w-4" />
                Cerrar lote
              </button>
            )}
          </>
        }
      />
      <Modal
        open={confirmando}
        onClose={onCancelarCierre}
        title="¿Cerrar el lote?"
        size="sm"
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" onClick={onCancelarCierre} className={BOTON_SECUNDARIO_CLS}>
              Cancelar
            </button>
            <button type="button" onClick={onConfirmarCierre} className={BOTON_PRIMARIO_CLS}>
              Cerrar lote
            </button>
          </div>
        }
      >
        <p className="text-sm text-muted-foreground">
          El lote pasa a Incompleto: las consultas con error quedan sin resultado y no se vuelven a intentar. Esta acción no se
          puede deshacer.
        </p>
      </Modal>
    </>
  );
}
