"use client";

// HU #14402 (Feature #14400, Épica #14297) — Pestaña «Diseño web» de Plataforma > Persona Natural: logo y colores de la web
// de persona, con el mismo proceso de borrador, vista previa, publicar y retirar que el portal de cada organismo. La
// sección es la misma (`PortalPnDesignSection`) con la API de la web y sus textos inyectados. No lleva banner: los de la
// web se administran en Contenido, y aquí un acceso lleva a ellos.
import { ArrowRight } from "lucide-react";
import {
  deleteMarcaWebLogo,
  fetchMarcaWebImage,
  getMarcaWeb,
  publishMarcaWeb,
  retireMarcaWeb,
  saveMarcaWebDraft,
  uploadMarcaWebLogo,
} from "@/lib/api/admin-pn-marca-web";
import type { ClienteMarcaPortal } from "@/components/admin/transit-offices/portal-pn/cliente-marca";
import { PortalPnDesignSection } from "@/components/admin/transit-offices/portal-pn/PortalPnDesignSection";
import type {
  TextosDiseno,
  TextosVistaPrevia,
} from "@/components/admin/transit-offices/portal-pn/textos-diseno";

const CLIENTE_WEB: ClienteMarcaPortal = {
  obtener: (signal) => getMarcaWeb(signal),
  guardarBorrador: (body) => saveMarcaWebDraft(body),
  publicar: (rowVersion) => publishMarcaWeb(rowVersion),
  retirar: (rowVersion) => retireMarcaWeb(rowVersion),
  subirLogo: (file) => uploadMarcaWebLogo(file),
  quitarLogo: () => deleteMarcaWebLogo(),
  imagen: (path, signal) => fetchMarcaWebImage(path, signal),
};

const TEXTOS_WEB: TextosDiseno = {
  titulo: "Diseño de la web de persona",
  descripcion:
    "Elige los colores y el logo que verá la persona al entrar por la web. Guarda un borrador, revísalo en la vista previa y publícalo cuando esté listo. Esto no cambia el portal de ningún organismo.",
  nombreEtiqueta: "Nombre en la web (opcional)",
  nombrePlaceholder: "Nombre que verá la persona junto al logo",
  nombreObligatorio: false,
  principalAyuda: "Títulos, botones y pasos de la web.",
  secundarioAyuda: "Tarjeta de acciones de la web.",
  errorCarga: "No se pudo cargar el diseño de la web.",
  contrasteMensaje:
    "Con estos colores la web no se leería bien (contraste menor al mínimo). Usa «Ajustar automáticamente» o elige otros colores.",
  guardado: "Borrador guardado. Todavía no se ve en la web.",
  publicado: "Diseño publicado. La web lo muestra desde la siguiente carga.",
  retirado: "Diseño retirado. La web vuelve a la marca FLIT; tu borrador se conserva.",
  logoQuitado: "Logo quitado del borrador. Al publicar, la web usa el logo FLIT.",
  sinMarca: "Sin diseño publicado: la web usa la marca FLIT.",
  publicarTitulo: "Publicar el diseño de la web",
  publicarDescripcion:
    "Al publicar, la web de persona muestra los colores y el logo de inmediato, desde la siguiente carga. Los portales de los organismos no cambian.",
  publicarNota: "Sin logo propio, la web usa el de FLIT.",
  retirarDescripcion:
    "La web vuelve a la marca FLIT desde la siguiente carga. Tu borrador se conserva para volver a publicarlo.",
  logoSinImagen: "Opcional: sin logo, la web usa el de FLIT.",
  logoAlt: "Logo de la web",
  logoInput: "Cargar logo de la web",
};

const TEXTOS_VISTA_PREVIA_WEB: TextosVistaPrevia = {
  ariaLabel: "Vista previa de la web de persona",
  logoAlt: "Logo de la web",
  nombrePorDefecto: "Nombre de la web",
  pie: "Sin logo propio, la web usa el de FLIT.",
};

export function DisenoWebTab({ onIrABanners }: { onIrABanners: () => void }) {
  return (
    <PortalPnDesignSection
      cliente={CLIENTE_WEB}
      textos={TEXTOS_WEB}
      textosVistaPrevia={TEXTOS_VISTA_PREVIA_WEB}
      accesoBanner={
        <div
          className="flex flex-col gap-2 rounded-xl border px-3 py-3"
          style={{ borderColor: "#DFE5ED" }}
        >
          <p className="text-xs font-semibold" style={{ color: "var(--color-flit-primary)" }}>
            Banners de la web
          </p>
          <p className="text-xs opacity-70">
            Los banners de la web no se configuran aquí: se administran en Contenido.
          </p>
          <button
            type="button"
            onClick={onIrABanners}
            className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] inline-flex w-fit items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-semibold"
            style={{ borderColor: "#162744", color: "#162744" }}
          >
            Ir a los banners de Contenido <ArrowRight className="h-3.5 w-3.5" aria-hidden />
          </button>
        </div>
      }
    />
  );
}
