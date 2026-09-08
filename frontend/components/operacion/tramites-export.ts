/**
 * Nombre del archivo exportado del listado de trámites (HU #12104).
 *
 * Lleva fecha y hora porque el destino real es la carpeta de Descargas: media docena de
 * `tramites.xlsx` seguidos no se distinguen entre sí, y el criterio del negocio es poder decir de
 * cuándo es cada informe sin abrirlo.
 *
 * Se separa del componente para poder afirmar sobre él en una prueba sin montar la tabla entera.
 */

/** `2026-09-07_10-30` en reloj LOCAL: el usuario reconoce la hora a la que pulsó, no la UTC. */
export function selloDeArchivo(ahora: Date): string {
  const dosDigitos = (n: number) => String(n).padStart(2, '0');
  const fecha = [
    ahora.getFullYear(),
    dosDigitos(ahora.getMonth() + 1),
    dosDigitos(ahora.getDate()),
  ].join('-');
  const hora = [dosDigitos(ahora.getHours()), dosDigitos(ahora.getMinutes())].join('-');
  return `${fecha}_${hora}`;
}

/**
 * `tramites_2026-09-07_10-30.xlsx`, o `..._parte_1_de_3.xlsx` cuando el resultado se repartió.
 *
 * El sufijo solo aparece con MÁS de un archivo: un `parte_1_de_1` en una exportación única sugiere
 * que falta algo por descargar.
 */
export function nombreArchivoTramites(
  sello: string,
  parte?: { numero: number; total: number },
): string {
  const sufijo = parte && parte.total > 1 ? `_parte_${parte.numero}_de_${parte.total}` : '';
  return `tramites_${sello}${sufijo}.xlsx`;
}
