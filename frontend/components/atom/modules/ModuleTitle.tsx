import type { ReactNode } from "react";
import { ILUSTRACIONES, ilustracionDe, rutaIlustracion, type Ilustracion } from "@flit/ui/ilustraciones";

/**
 * Cabecera de módulo con el modelo `page-header` de la propuesta «Diseño Inteligente» (rama `propuesta`,
 * propuesta.css): tarjeta con la ilustración del módulo a la izquierda, antetítulo opcional, título con su línea
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
    <header
      className="relative flex shrink-0 flex-wrap items-center gap-6 overflow-clip rounded-[var(--di-radius-header)] border border-[var(--di-border)] py-8 pl-[132px] pr-8 shadow-[var(--shadow-card)] max-md:gap-3.5 max-md:rounded-[20px] max-md:py-5 max-md:pl-[84px] max-md:pr-4"
      style={{ background: "linear-gradient(120deg, var(--di-surface-elevated) 35%, var(--di-sky) 100%)" }}
    >
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -right-[60px] -top-[110px] h-[230px] w-[230px] rounded-full opacity-90 max-md:-right-12 max-md:-top-[60px] max-md:h-[120px] max-md:w-[120px]"
        style={{ background: "var(--di-highlight-decor)" }}
      />
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -right-[150px] -top-[60px] h-[420px] w-[420px] rounded-full border-[1.5px] opacity-50 max-md:hidden"
        style={{ borderColor: "var(--di-sky-line)" }}
      />
      <img
        src={rutaPara(title, ilustracion)}
        alt=""
        aria-hidden="true"
        className="absolute left-6 top-1/2 h-[92px] w-[92px] -translate-y-1/2 object-contain max-md:left-3.5 max-md:top-5 max-md:h-[58px] max-md:w-[58px] max-md:translate-y-0"
      />
      <div className="relative z-[1] min-w-0 flex-[1_1_320px]">
        {antetitulo && (
          <p className="mb-1 text-xs font-bold uppercase tracking-[0.06em]" style={{ color: "var(--di-highlight)" }}>
            {antetitulo}
          </p>
        )}
        <h1 className="text-[2rem] font-bold leading-tight max-md:text-2xl" style={{ color: "var(--di-heading)" }}>
          {title}
        </h1>
        <span
          aria-hidden="true"
          className="mt-2.5 block h-[5px] w-16 rounded-full"
          style={{ background: "linear-gradient(90deg, var(--di-sky-line), var(--di-sky))" }}
        />
        {subtitle && (
          <p className="mt-2.5 max-w-[60ch] text-sm max-md:text-[13px]" style={{ color: "var(--di-text-secondary)" }}>
            {subtitle}
          </p>
        )}
      </div>
      {(right || action) && (
        <div className="relative z-[1] flex flex-wrap items-center gap-3">
          {right}
          {action}
        </div>
      )}
    </header>
  );
}
