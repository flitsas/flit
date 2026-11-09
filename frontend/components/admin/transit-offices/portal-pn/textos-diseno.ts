/** Textos de la sección de diseño y de su vista previa: el organismo y la web de persona cuentan lo mismo con otras palabras. */
export interface TextosDiseno {
  titulo: string;
  descripcion: string;
  /** Etiqueta del campo nombre, o `null` si la marca no lleva nombre. */
  nombreEtiqueta: string | null;
  nombrePlaceholder: string;
  /** Si es `true`, publicar exige nombre. */
  nombreObligatorio: boolean;
  principalAyuda: string;
  secundarioAyuda: string;
  errorCarga: string;
  contrasteMensaje: string;
  guardado: string;
  publicado: string;
  retirado: string;
  logoQuitado: string;
  sinMarca: string;
  publicarTitulo: string;
  publicarDescripcion: string;
  /** Texto fijo, o según haya logo y banners propios en el borrador. */
  publicarNota: string | ((estado: EstadoImagenesMarca) => string);
  retirarDescripcion: string;
  logoSinImagen: string;
  logoAlt: string;
  logoInput: string;
}

export interface TextosVistaPrevia {
  ariaLabel: string;
  logoAlt: string;
  nombrePorDefecto: string;
  /** Solo si la marca lleva banners. */
  sinBanner?: string;
  /** Texto fijo, o según haya logo y banners propios en el borrador. */
  pie: string | ((estado: EstadoImagenesMarca) => string);
}

/** Qué imágenes propias tiene el borrador. */
export interface EstadoImagenesMarca {
  tieneLogo: boolean;
  tieneBanners: boolean;
}

/** Dice solo lo que falta: si ya hay logo y banners propios no afirma que el portal use los de FLIT. */
export function notaImagenesOt({ tieneLogo, tieneBanners }: EstadoImagenesMarca): string {
  if (tieneLogo && tieneBanners) return "El portal mostrará tu logo y tus banners.";
  if (!tieneLogo && !tieneBanners) return "Sin logo ni banners propios, el portal usa los de FLIT.";
  if (!tieneLogo) return "Sin logo propio, el portal usa el de FLIT.";
  return "Sin banners propios, el portal usa el banner de FLIT.";
}

/** Resuelve un texto que puede ser fijo o depender del estado de las imágenes. */
export function textoSegunImagenes(
  texto: string | ((estado: EstadoImagenesMarca) => string),
  estado: EstadoImagenesMarca,
): string {
  return typeof texto === "function" ? texto(estado) : texto;
}

export const TEXTOS_DISENO_OT: TextosDiseno = {
  titulo: "Diseño del portal",
  descripcion:
    "Elige el nombre, los colores, el logo y los banners que verá la persona en el enlace del punto. Guarda un borrador, revísalo en la vista previa y publícalo cuando esté listo.",
  nombreEtiqueta: "Nombre en el portal",
  nombrePlaceholder: "Nombre que verá la persona",
  nombreObligatorio: true,
  principalAyuda: "Títulos, botones y pasos del portal.",
  secundarioAyuda: "Tarjeta de acciones del punto.",
  errorCarga: "No se pudo cargar la marca del portal.",
  contrasteMensaje:
    "Con estos colores el portal no se leería bien (contraste menor al mínimo). Usa «Ajustar automáticamente» o elige otros colores.",
  guardado: "Borrador guardado. Todavía no se ve en el portal.",
  publicado: "Marca publicada. El portal la muestra desde la siguiente carga.",
  retirado: "Marca retirada. El portal vuelve a la marca FLIT; tu borrador se conserva.",
  logoQuitado: "Logo quitado del borrador. Al publicar, el portal usa el logo FLIT.",
  sinMarca: "Sin marca publicada: el portal usa la marca FLIT.",
  publicarTitulo: "Publicar la marca del portal",
  publicarDescripcion:
    "Al publicar, el enlace del punto de este organismo muestra el nombre, los colores, el logo y los banners de inmediato, desde la siguiente carga.",
  publicarNota: notaImagenesOt,
  retirarDescripcion:
    "El portal del punto vuelve a la marca FLIT desde la siguiente carga. Tu borrador se conserva para volver a publicarlo.",
  logoSinImagen: "Opcional: sin logo, el portal usa el de FLIT.",
  logoAlt: "Logo del portal",
  logoInput: "Cargar logo del portal",
};

export const TEXTOS_VISTA_PREVIA_OT: TextosVistaPrevia = {
  ariaLabel: "Vista previa del portal del punto",
  logoAlt: "Logo del organismo",
  nombrePorDefecto: "Nombre del organismo",
  sinBanner: "Sin banners propios: el portal usa el banner de FLIT.",
  pie: notaImagenesOt,
};
