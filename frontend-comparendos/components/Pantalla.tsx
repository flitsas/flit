"use client";

import type { ReactNode } from "react";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import type { Ilustracion } from "@flit/ui/ilustraciones";
import { tienePermiso } from "@/lib/sesion";

/** Ilustración de la cabecera según el título, como `ModuleTitle` de Trámites; sin coincidencia, la de Comparendos. */
const POR_TITULO: ReadonlyArray<[RegExp, Ilustracion]> = [
  [/dashboard|reporte|productividad/i, "reportes"],
  [/calendario|horario/i, "calendario"],
  [/organismo/i, "organismos"],
  [/municip|fuentes/i, "fuentes-oficiales"],
  [/alerta|integraci|ingesta|migraci/i, "integraciones"],
  [/ocr|prompt|paquete/i, "documentos"],
  [/plantilla|mensaje|correo/i, "notificaciones"],
  [/representante/i, "firmas"],
  [/empresa|parametrizador/i, "empresas"],
  [/tipolog|petici/i, "causales"],
  [/monitore|placa/i, "consulta-placa"],
  [/pago/i, "fur"],
];

function ilustracionPara(titulo: string): Ilustracion {
  return POR_TITULO.find(([re]) => re.test(titulo))?.[1] ?? "comparendos";
}

/**
 * Encabezado común de una pantalla: título, descripción y acciones a la derecha. Con el aspecto de la cabecera de
 * módulo de la propuesta (HU #14486). La ilustración sale del título salvo que la pantalla pida otra.
 */
export function Encabezado({
  titulo,
  descripcion,
  acciones,
  ilustracion,
}: {
  titulo: string;
  descripcion?: ReactNode;
  acciones?: ReactNode;
  ilustracion?: Ilustracion;
}) {
  return (
    <CabeceraModulo
      titulo={titulo}
      descripcion={descripcion}
      icono={ilustracion ?? ilustracionPara(titulo)}
      acciones={acciones && <div className="flex flex-wrap items-center gap-2">{acciones}</div>}
    />
  );
}

/**
 * Muestra el contenido solo si la sesión lo permite: `superAdmin` exige ser administrador de FLIT; `permiso`, tener ese
 * permiso del producto. Mientras carga la sesión no muestra nada.
 */
export function Requiere({ superAdmin, permiso, children }: { superAdmin?: boolean; permiso?: string; children: ReactNode }) {
  const { sesion, cargando, error } = useSesion();
  if (cargando) return <p className="text-sm opacity-60">Cargando…</p>;
  if (error) return <InlineAlert tone="error">{error}</InlineAlert>;
  if (superAdmin && !sesion?.usuario.esSuperAdmin)
    return <InlineAlert tone="warning" title="No tienes acceso">Esta configuración es solo del administrador de FLIT.</InlineAlert>;
  if (permiso && !tienePermiso(sesion, permiso))
    return <InlineAlert tone="warning" title="No tienes acceso">Tu rol no tiene permiso para esta pantalla. Pídelo a tu administrador.</InlineAlert>;
  return <>{children}</>;
}
