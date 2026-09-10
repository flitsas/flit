// La receta visual «lista de tarjetas»: cabecera-píldora gris y filas que flotan como tarjetas
// separadas, no renglones de un bloque rayado.
//
// La tabla de Trámites es la referencia del producto y NO es una `<table>`: es una rejilla de
// `<div>` con una `<ul>` de filas. Esta receta es esa misma imagen traducida a tabla de verdad,
// que es lo que ya usan `DataTable` y `CompanyListTable`. Se ofrece como clases sueltas, y no como
// un componente que envuelva la tabla, porque las pantallas que la adoptan ya tienen su marcado
// hecho —con sus celdas, sus enlaces, sus barras de progreso y sus estados vacíos propios—: lo que
// les faltaba era el aspecto, no la estructura. Cambiarles la estructura para uniformar el color
// sería arriesgar el comportamiento a cambio de nada.
//
// El redondeo de los extremos se resuelve con las variantes `first:`/`last:` en vez de comparar
// índices en cada celda: así vale igual para una cabecera escrita a mano y para una recorrida con
// `map`, y ninguna pantalla tiene que enterarse de cuántas columnas tiene.
//
// Los colores salen de los tokens de `globals.css` (`--table-*`), así que el tema oscuro ya está
// resuelto y no hay un segundo sitio donde cambiarlos.

/** Envoltorio con desplazamiento horizontal. Va por fuera de la `<table>`. */
export const CARDLIST_SCROLL = "overflow-x-auto";

/**
 * La `<table>`. `border-separate` + `border-spacing-y-2` es lo que abre el hueco entre filas que
 * las convierte en tarjetas sueltas; con `border-collapse` las tarjetas se pegan y vuelve el bloque.
 */
export const CARDLIST_TABLE = "w-full border-separate border-spacing-y-2 text-xs";

/** El `<tr>` de la cabecera. La alineación se deja a cada `<th>` para no estorbar a las numéricas. */
export const CARDLIST_HEAD_ROW = "text-left text-[11px] font-semibold uppercase tracking-wider";

/** Cada `<th>`. El fondo va aquí y no en el `<tr>`: con `border-separate` es lo que pinta parejo. */
export const CARDLIST_TH =
  "bg-[color:var(--table-header-bg)] px-4 py-3 font-semibold text-[color:var(--table-header-fg)] " +
  "first:rounded-l-xl last:rounded-r-xl";

/** El `<tr>` de una fila. `group` habilita el resaltado al pasar por encima si la fila se pulsa. */
export const CARDLIST_ROW = "group bg-card";

/** Cada `<td>`. El borde completo de la tarjeta sale de los cuatro lados repartidos entre celdas. */
export const CARDLIST_CELL =
  "border-y border-[color:var(--table-row-border)] px-4 py-3 " +
  "first:rounded-l-xl first:border-l last:rounded-r-xl last:border-r";

/** Añadir a cada `<td>` de una fila que se pueda pulsar. Requiere `CARDLIST_ROW` en su `<tr>`. */
export const CARDLIST_CELL_CLICKABLE =
  "transition-colors group-hover:bg-[color:var(--table-row-hover)]";
