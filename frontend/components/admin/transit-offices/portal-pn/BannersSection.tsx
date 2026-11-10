"use client";

import { PORTAL_BANNERS_MAX, type PortalMarca, type PortalMarcaBanner } from "@/lib/api/admin-ot-portal-marca";
import { avisoProporcionBanner, validatePortalImage } from "@/lib/brand/validate-portal-image";
import type { ListaImagenesApi, ClienteMarcaPortal } from "./cliente-marca";
import { GestorImagenes, type ConfigGestorImagenes } from "./ImagenesGestor";

export interface BannersSectionProps {
  items: PortalMarcaBanner[];
  api: ListaImagenesApi;
  imagen: ClienteMarcaPortal["imagen"];
  /** Solo lectura: se ven los banners pero no hay acciones. */
  readOnly: boolean;
  /** Otra operación de la pantalla en curso. */
  disabled: boolean;
  onChange: (marca: PortalMarca, mensaje: string) => void;
  onError: (err: unknown, fallback: string) => void;
  onBusyChange: (busy: boolean) => void;
}

const CONFIG: ConfigGestorImagenes = {
  idBase: "portal-pn-banners",
  titulo: "Banners",
  descripcion: `Es el banner principal que ve la persona arriba de tu portal; si agregas varios, rotan como carrusel. Hasta ${PORTAL_BANNERS_MAX} banners.`,
  max: PORTAL_BANNERS_MAX,
  botonAgregar: "Agregar banner",
  tituloModalAgregar: "Agregar banner",
  tituloModalEditar: "Editar banner",
  tituloModalQuitar: "Quitar banner",
  listaLabel: "Banners del portal",
  accionesLabel: (n, alt) => `Acciones para el banner ${n}: ${alt}`,
  textoLleno: `Llegaste al máximo de ${PORTAL_BANNERS_MAX} banners. Quita uno para agregar otro.`,
  vacio: "Sin banners: el portal usa el banner de FLIT.",
  guia: (
    <>
      <strong>Tamaño recomendado: 1600 × 366 px (proporción cercana a 4,4:1).</strong> PNG, JPEG o WebP, máximo 2 MB
      cada uno. Deja el texto y lo importante en el 80 % central: en pantallas angostas se recortan los lados.
    </>
  ),
  ayudaArchivo: "1600 × 366 px (4,4:1) · PNG, JPEG o WebP · máx. 2 MB",
  placeholderAlt: "Describe el banner para quien no puede verlo",
  miniatura: "aspect-[22/5] w-40",
  msgAgregado: "Banner agregado al borrador.",
  msgActualizado: "Banner actualizado en el borrador.",
  msgOrden: "Orden de los banners guardado en el borrador.",
  msgQuitado: "Banner quitado del borrador.",
  errAgregar: "No se pudo agregar el banner.",
  errGuardar: "No se pudo guardar el banner.",
  errOrden: "No se pudo cambiar el orden.",
  errQuitar: "No se pudo quitar el banner.",
  confirmarQuitar: (alt) => `Se quita «${alt}» del borrador. El portal dejará de mostrarlo cuando publiques.`,
  validarArchivo: (file) => validatePortalImage(file, "banner"),
  avisarArchivo: avisoProporcionBanner,
};

/** Administración de los banners del portal (el banner principal, que rota si hay varios). */
export function BannersSection({ items, api, ...rest }: BannersSectionProps) {
  return <GestorImagenes config={CONFIG} items={items} api={api} {...rest} />;
}
