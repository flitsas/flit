import type { SoporteVista } from "@/lib/configuracion";
import { TEXTO_POLITICA_DATOS, TEXTO_POLITICAS, TEXTO_TERMINOS } from "@/lib/legal";
import { boton } from "./estilos";

const CLASE_ENLACE = boton("link", "h-auto p-0 text-xs font-light text-foreground/70", "none");

interface Props {
  onAbrirPoliticas: () => void;
  /** URL http(s) válida por código de enlace con documento publicado (ver `resolverContenido`). */
  enlaces?: Readonly<Record<string, string | null>>;
  soporte?: SoporteVista | null;
}

export function Pie({ onAbrirPoliticas, enlaces = {}, soporte = null }: Props) {
  const documentos = [
    { titulo: TEXTO_POLITICA_DATOS, url: enlaces.POLITICA_DATOS ?? null },
    { titulo: TEXTO_TERMINOS, url: enlaces.TERMINOS ?? null },
  ].filter((d): d is { titulo: string; url: string } => d.url !== null);

  return (
    <footer className="dashboard-footer">
      <p>
        {documentos.length === 0 ? (
          <button type="button" onClick={onAbrirPoliticas} className={CLASE_ENLACE}>{TEXTO_POLITICAS}</button>
        ) : (
          documentos.map((d, i) => (
            <span key={d.titulo}>
              {i > 0 && " · "}
              <a href={d.url} target="_blank" rel="noopener noreferrer" aria-label={`${d.titulo} (se abre en una pestaña nueva)`} className={CLASE_ENLACE}>{d.titulo}</a>
            </span>
          ))
        )}
        {" · "}© 2026 FLIT · Todos los derechos reservados · Protegido por cifrado TLS · Auditoría continua
      </p>
      {soporte && (
        <p aria-label="Soporte">
          Soporte:{" "}
          {soporte.correo && <a href={`mailto:${soporte.correo}`} className={CLASE_ENLACE}>{soporte.correo}</a>}
          {soporte.correo && soporte.telefono && " · "}
          {soporte.telefono && <a href={`tel:${soporte.telefono.replace(/[^\d+]/g, "")}`} className={CLASE_ENLACE}>{soporte.telefono}</a>}
          {(soporte.correo || soporte.telefono) && soporte.horario && " · "}
          {soporte.horario}
        </p>
      )}
    </footer>
  );
}
