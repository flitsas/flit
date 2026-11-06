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
  /** Solo si la marca lleva banner. */
  bannerQuitado?: string;
  sinMarca: string;
  publicarTitulo: string;
  publicarDescripcion: string;
  publicarNota: string;
  retirarDescripcion: string;
  logoSinImagen: string;
  logoAlt: string;
  logoInput: string;
}

export interface TextosVistaPrevia {
  ariaLabel: string;
  logoAlt: string;
  nombrePorDefecto: string;
  /** Solo si la marca lleva banner. */
  sinBanner?: string;
  pie: string;
}

export const TEXTOS_DISENO_OT: TextosDiseno = {
  titulo: "Diseño del portal",
  descripcion:
    "Elige el nombre, los colores, el logo y el banner que verá la persona en el enlace del punto. Guarda un borrador, revísalo en la vista previa y publícalo cuando esté listo.",
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
  bannerQuitado: "Banner quitado del borrador. Al publicar, el portal no muestra banner.",
  sinMarca: "Sin marca publicada: el portal usa la marca FLIT.",
  publicarTitulo: "Publicar la marca del portal",
  publicarDescripcion:
    "Al publicar, el enlace del punto de este organismo muestra el nombre, los colores, el logo y el banner de inmediato, desde la siguiente carga.",
  publicarNota: "Sin logo o banner propios, el portal usa los de FLIT.",
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
  sinBanner: "Sin banner propio: el portal usa el de FLIT.",
  pie: "Sin logo o banner propios, el portal usa los de FLIT.",
};
