"use client";

import { PORTAL_CARRUSEL_MAX, type PortalMarca, type PortalMarcaCarruselItem } from "@/lib/api/admin-ot-portal-marca";
import { validatePortalCarouselImage } from "@/lib/brand/validate-portal-image";
import type { ClienteMarcaPortal } from "./cliente-marca";
import { GestorImagenes, type ConfigGestorImagenes } from "./ImagenesGestor";

type ApiCarrusel = NonNullable<ClienteMarcaPortal["carrusel"]>;

export interface CarruselSectionProps {
  items: PortalMarcaCarruselItem[];
  api: ApiCarrusel;
  imagen: ClienteMarcaPortal["imagen"];
  /** Solo lectura: se ven las imágenes pero no hay acciones. */
  readOnly: boolean;
  /** Otra operación de la pantalla en curso. */
  disabled: boolean;
  onChange: (marca: PortalMarca, mensaje: string) => void;
  /** Errores que debe mostrar la pantalla (conflicto de versión, etc.). */
  onError: (err: unknown, fallback: string) => void;
  onBusyChange: (busy: boolean) => void;
}

const CONFIG: ConfigGestorImagenes = {
  idBase: "portal-pn-carousel",
  titulo: "Carrusel de imágenes",
  descripcion: `Se muestra en la columna derecha de tu portal, en lugar de «Servicios Adicionales». Hasta ${PORTAL_CARRUSEL_MAX} imágenes.`,
  max: PORTAL_CARRUSEL_MAX,
  botonAgregar: "Agregar imagen",
  tituloModalAgregar: "Agregar imagen",
  tituloModalEditar: "Editar imagen",
  tituloModalQuitar: "Quitar imagen del carrusel",
  listaLabel: "Imágenes del carrusel",
  accionesLabel: (n, alt) => `Acciones para la imagen ${n}: ${alt}`,
  textoLleno: `Llegaste al máximo de ${PORTAL_CARRUSEL_MAX} imágenes. Quita una para agregar otra.`,
  vacio: "Sin imágenes: el portal muestra los servicios de siempre.",
  guia: (
    <>
      <strong>Tamaño recomendado: 900 × 600 px (proporción 3:2).</strong> JPG, PNG o WebP, máximo 512 KB. Deja el
      texto y lo importante en el 80 % central: en pantallas angostas se recortan los lados.
    </>
  ),
  ayudaArchivo: "900 × 600 px (3:2) · PNG, JPEG o WebP · máx. 512 KB",
  placeholderAlt: "Describe la imagen para quien no puede verla",
  miniatura: "aspect-[3/2] w-24",
  msgAgregado: "Imagen agregada al borrador.",
  msgActualizado: "Imagen actualizada en el borrador.",
  msgOrden: "Orden del carrusel guardado en el borrador.",
  msgQuitado: "Imagen quitada del carrusel.",
  errAgregar: "No se pudo agregar la imagen.",
  errGuardar: "No se pudo guardar la imagen.",
  errOrden: "No se pudo cambiar el orden.",
  errQuitar: "No se pudo quitar la imagen.",
  confirmarQuitar: (alt) => `Se quita «${alt}» del borrador. El portal lo dejará de mostrar cuando publiques.`,
  validarArchivo: validatePortalCarouselImage,
};

/** Administración del carrusel de imágenes del portal (se muestra en la columna lateral, en lugar de «Servicios Adicionales»). */
export function CarruselSection({ items, api, ...rest }: CarruselSectionProps) {
  return <GestorImagenes config={CONFIG} items={items} api={api} {...rest} />;
}
