import { TEXTO_POLITICAS } from "@/lib/landing/contenido";
import { boton } from "./estilos";

export function Pie({ onAbrirPoliticas }: { onAbrirPoliticas: () => void }) {
  return (
    <footer className="dashboard-footer">
      <p>
        <button type="button" onClick={onAbrirPoliticas} className={boton("link", "h-auto p-0 text-xs font-light text-foreground/70", "none")}>{TEXTO_POLITICAS}</button>
        {" · "}© 2026 FLIT · Todos los derechos reservados · Protegido por cifrado TLS · Auditoría continua
      </p>
    </footer>
  );
}
