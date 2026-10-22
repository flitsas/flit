import { useId, type ReactNode } from "react";

// HU #13653 (Épica #12753) — editores de permisos (roles del Panel SA, roles propios de la compañía y la ficha
// «Permisos por módulo»): una sola lista con desplazamiento, separada por títulos, en este orden:
// «Datos del rol» (si aplica) → «Módulos» → «Tipos de servicio» (si hay). Sin pestañas dentro del modal.

export interface RoleEditorSectionsProps {
  /** Campos del rol (código, nombre, descripción…). Sin ellos no hay sección «Datos del rol». */
  data?: ReactNode;
  /** Permisos de los módulos que no son tipos de servicio. */
  modules: ReactNode;
  /** Permisos por tipo de servicio (Ver / Crear). `null` o ausente: no hay sección «Tipos de servicio». */
  services?: ReactNode | null;
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="flex flex-col gap-3">
      <h3
        id={id}
        className="border-b border-[#DFE5ED] pb-1.5 text-xs font-bold uppercase tracking-wide text-[#59677D] dark:border-white/15 dark:text-white/70"
      >
        {title}
      </h3>
      {children}
    </section>
  );
}

/** Con una sola sección (sin datos y sin tipos de servicio) solo se muestra la lista, sin título redundante. */
export function RoleEditorSections({ data, modules, services }: RoleEditorSectionsProps) {
  if (!data && !services) return <>{modules}</>;
  return (
    <div className="flex flex-col gap-5">
      {data ? <Section title="Datos del rol">{data}</Section> : null}
      <Section title="Módulos">{modules}</Section>
      {services ? <Section title="Tipos de servicio">{services}</Section> : null}
    </div>
  );
}
