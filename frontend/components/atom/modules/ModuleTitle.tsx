import type { ReactNode } from "react";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { ILUSTRACIONES, ilustracionDe, rutaIlustracion, type Ilustracion } from "@flit/ui/ilustraciones";

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

const POR_TITULO: ReadonlyArray<[RegExp, Ilustracion]> = [
  [/generaci[oó]n documental/i, "generacion-documental"],
  [/historial/i, "historial-placa"],
  [/revocatoria/i, "revocatorias"],
  [/trazabilidad/i, "trazabilidad"],
  [/log qx|quipux|\bict\b|integraci/i, "integraciones"],
  [/confirmaci[oó]n runt|runt/i, "fuentes-oficiales"],
  [/causal/i, "causales"],
  [/organismo|tr[aá]nsito/i, "organismos"],
  [/compa[nñ][ií]a|empresa|red de clientes/i, "empresas"],
  [/impronta/i, "improntas"],
  [/documental|documento/i, "documentos"],
  [/mandato/i, "mandatos"],
  [/\bfur\b/i, "fur"],
  [/notificaci|mensajes muertos|correo/i, "notificaciones"],
  [/banner/i, "banners"],
  [/rol|permiso|rbac/i, "roles"],
  [/auditor/i, "auditoria"],
  [/proceso/i, "procesos"],
  [/usuario|colaborador/i, "colaboradores"],
  [/identidad|validaci|biom/i, "identidad"],
  [/reporte|anal[ií]tica|consulta/i, "reportes"],
  [/placa/i, "consulta-placa"],
  [/configuraci|plataforma|par[aá]metro|tipos? de tr[aá]mite/i, "configuracion"],
  [/ayuda|manual/i, "ayuda"],
  [/perfil|cuenta|contrase/i, "perfil"],
  [/comparendo/i, "comparendos"],
  [/diagn[oó]stico/i, "diagnostico"],
  [/matr[ií]cula/i, "matricula"],
  [/traspaso/i, "traspaso"],
  [/tr[aá]mite/i, "tramites"],
];

function rutaPara(title: string, ilustracion?: string): string {
  if (ilustracion) {
    if ((ILUSTRACIONES as readonly string[]).includes(ilustracion)) return rutaIlustracion(ilustracion as Ilustracion);
    const porClave = ilustracionDe(ilustracion);
    if (porClave) return porClave;
  }
  const hallada = POR_TITULO.find(([re]) => re.test(title))?.[1] ?? "inicio";
  return rutaIlustracion(hallada);
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
  /** Ilustración del módulo (nombre o clave del dock). Si no se pasa, sale del título. */
  ilustracion?: string;
  /** Antetítulo en versalitas sobre el título (p. ej. «Gestión integral»). */
  antetitulo?: string;
}) {
  return (
    <CabeceraModulo
      titulo={title}
      descripcion={subtitle}
      antetitulo={antetitulo}
      ilustracion={rutaPara(title, ilustracion)}
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
