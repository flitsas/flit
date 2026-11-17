import type { ReactNode } from "react";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { ilustracionDe } from "@flit/ui/ilustraciones";
import { MAPA_ICONO } from "@flit/ui/iconos-mapas";

/**
 * Cabecera de módulo de Trámites: `CabeceraModulo` de @flit/ui (HU #14476) con la ilustración elegida por esta tabla.
 * Antes el marcado vivía aquí; el aspecto sigue siendo el modelo `page-header` de la propuesta «Diseño Inteligente»
 * (rama `propuesta`, propuesta.css): tarjeta con la ilustración del módulo a la izquierda, antetítulo opcional, título con su línea
 * de acento, bajada, y los dos círculos decorativos (turquesa lleno y aro) a la derecha. Las acciones van DENTRO de
 * la cabecera, a la derecha, como en el prototipo (antes, regla R7 de HU #10493, iban fuera de la caja).
 *
 * La ilustración sale del título con la misma tabla del prototipo, así que ninguna pantalla tiene que pasarla; quien
 * quiera otra la fija con `ilustracion` (nombre de ilustración o clave del dock).
 */

/** Título → clave del ícono del Manual UI Kit 2026 (MAPA_ICONO): un ícono distinto por módulo, como la propuesta. */
const POR_TITULO: ReadonlyArray<[RegExp, string]> = [
  [/generaci[oó]n documental/i, "generacion-documental"],
  [/historial/i, "historial-placa"],
  [/revocatoria/i, "revocatorias"],
  [/reportes? ict/i, "reportes-ict"],
  [/trazabilidad/i, "trazabilidad"],
  [/log qx/i, "log-qx"],
  [/log ict|\bict\b/i, "log-ict"],
  [/quipux/i, "quipux"],
  [/integraci/i, "integraciones"],
  [/confirmaci[oó]n runt|runt|fuentes? oficial/i, "fuentes-oficiales"],
  [/causal/i, "causales"],
  [/organismo|tr[aá]nsito/i, "organismos"],
  [/compa[nñ][ií]a|empresa|red de clientes/i, "empresas"],
  [/impronta/i, "improntas"],
  [/tipos? de tr[aá]mite/i, "tipos-tramite"],
  [/documental|documento/i, "documentos"],
  [/mandato/i, "mandatos"],
  [/\bfur\b/i, "fur"],
  [/notificaci|mensajes muertos|correo/i, "notificaciones"],
  [/banner/i, "banners"],
  [/rol|permiso|rbac/i, "roles"],
  [/seguridad/i, "seguridad"],
  [/auditor/i, "auditoria"],
  [/proceso|tarea|job/i, "procesos"],
  [/usuario|colaborador/i, "colaboradores"],
  [/biom/i, "biometria"],
  [/identidad|validaci/i, "identidad"],
  [/reportes? detallad/i, "reportes-detallados"],
  [/reporte|anal[ií]tica|consulta/i, "reportes"],
  [/placa/i, "consulta-placa"],
  [/configuraci|plataforma|par[aá]metro/i, "configuracion"],
  [/ayuda|manual/i, "ayuda"],
  [/contrase/i, "contrasena"],
  [/perfil|cuenta/i, "perfil"],
  [/comparendo/i, "comparendos"],
  [/diagn[oó]stico/i, "diagnostico"],
  [/matr[ií]cula/i, "matricula"],
  [/traspaso/i, "traspaso"],
  [/tr[aá]mite/i, "tramites"],
];

/** Clave del ícono: la que pide la pantalla (nombre o clave del dock) o, si no, la que sale del título. */
function iconoPara(title: string, ilustracion?: string): string {
  if (ilustracion) {
    if (ilustracion in MAPA_ICONO) return ilustracion;
    const ruta = ilustracionDe(ilustracion);
    const nombre = ruta?.split("/").pop()?.replace(/\.(svg|webp)$/, "");
    if (nombre && nombre in MAPA_ICONO) return nombre;
  }
  return POR_TITULO.find(([re]) => re.test(title))?.[1] ?? "inicio";
}

export function ModuleTitle({
  title,
  subtitle,
  right,
  action,
  ilustracion,
  antetitulo,
}: {
  title: string;
  subtitle?: string;
  /** Contenido a la derecha de la cabecera (indicadores de estado). */
  right?: ReactNode;
  /** Acción primaria; va a la derecha, dentro de la cabecera. */
  action?: ReactNode;
  /** Ícono del módulo (clave de MAPA_ICONO, nombre de ilustración o clave del dock). Si no se pasa, sale del título. */
  ilustracion?: string;
  /** Antetítulo en versalitas sobre el título (p. ej. «Gestión integral»). */
  antetitulo?: string;
}) {
  return (
    <CabeceraModulo
      titulo={title}
      descripcion={subtitle}
      antetitulo={antetitulo}
      icono={iconoPara(title, ilustracion)}
      acciones={
        right || action ? (
          <>
            {right}
            {action}
          </>
        ) : undefined
      }
    />
  );
}
