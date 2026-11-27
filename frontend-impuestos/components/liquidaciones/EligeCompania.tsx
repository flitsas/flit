import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import { TARJETA_CLS } from "@/components/comun/estilos";
import { ERRORES_EMPRESA } from "@/lib/errores";

const IconoEmpresas = iconoComo("empresas");

/**
 * Estado del Super Admin sin compañía elegida en la barra (contrato 1.1.0: sus lecturas y escrituras de liquidaciones
 * exigen ?empresa). Título = literal EMPRESA_REQUERIDA de x-flit-literales.errores; no se pide nada a la API. Mismo molde
 * que Configuración (EstadoVacio con el ícono de compañías dentro de una tarjeta).
 */
export function EligeCompania({ nivelTitulo = 2, enTarjeta = true }: { nivelTitulo?: 2 | 3; enTarjeta?: boolean }) {
  const vacio = (
    <EstadoVacio
      nivelTitulo={nivelTitulo}
      icono={<IconoEmpresas className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
      titulo={ERRORES_EMPRESA.EMPRESA_REQUERIDA}
    />
  );
  return enTarjeta ? <section className={TARJETA_CLS}>{vacio}</section> : vacio;
}
