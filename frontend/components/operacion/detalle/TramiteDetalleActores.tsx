'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  FirmaParteEstado,
  ProcedureActor,
  RepresentanteLegal,
} from '@/lib/api/types/procedure-runtime';
import {
  CampoValor,
  DetalleBadgeSoft,
  ListaCampos,
  SeccionCargando,
  SeccionError,
  SeccionVacia,
  TarjetaDetalle,
  type SeccionDetalleProps,
} from './primitivos';
import { DETALLE_GREEN, DETALLE_GOLD, DETALLE_RED, DETALLE_GREY } from './detalle-visual';

/**
 * Sección «Actores» del modal de detalle (Frente C, `ActorCard` + `Paso2` de la propuesta).
 *
 * Fuente: `tramitesClient.getActors` (GET .../actors → `ProcedureActor[]`), NO
 * `tramitesClient.getInstance` (`ProcedureInstanceDetail.actors: Actor[]`). El `Actor` embebido
 * solo trae `actorType`/`documentType`/`documentNumber`/`fullName`/`email`; `ProcedureActor` además
 * trae `telefono`, `direccion`, `ciudad` y el `representanteLegal` embebido — lo que la propuesta
 * necesita pintar.
 *
 * La propuesta inventa teléfono/correo/dirección de ejemplo y dibuja una «firma digitalizada» como
 * un SVG a mano (función `Signature`) que no existe en el contrato: se omiten los dos. En su lugar,
 * el pie de cada tarjeta usa el estado REAL de acreditación por parte —
 * `item.firmaVendedorEstado` / `item.firmaCompradorEstado`, ya presentes en las props sin llamada
 * adicional— con el MISMO vocabulario que ya usa `TramitesTable` (Firmado / Sin firma / Rechazado /
 * Sin registrar). No se usa `tramitesClient.listFirmas`: su `SignatureEstado`
 * (pendiente_envio/enviada/firmada/rechazada) es OTRO eje — la firma electrónica de la
 * compraventa, no la acreditación de identidad de la parte (ver el docstring de `FirmaParteEstado`).
 *
 * Ningún campo de `Actor`/`ProcedureActor`/`InstanceSummary`/`Signature` expone un certificado o
 * evidencia descargable de la firma o de la validación biométrica: no se ofrece descarga.
 *
 * El representante legal viaja embebido en el actor `comprador` (persona jurídica). La propuesta
 * solo lo pinta en la rama de matrícula inicial (líneas 134-144); en traspaso no aparece, así que
 * aquí tampoco se pinta aunque el contrato lo traiga, para no introducir una composición que la
 * propuesta no define.
 */


const FIRMA_SOFT: Record<FirmaParteEstado, { label: string; color: string }> = {
  firmado: { label: 'Firmado', color: DETALLE_GREEN },
  pendiente: { label: 'Sin firma', color: DETALLE_GOLD },
  rechazado: { label: 'Rechazado', color: DETALLE_RED },
};

function FirmaEstadoSoft({ estado }: { estado: FirmaParteEstado | null | undefined }) {
  const meta = estado
    ? FIRMA_SOFT[estado]
    : { label: 'Sin registrar', color: DETALLE_GREY };
  return <DetalleBadgeSoft text={meta.label} color={meta.color} />;
}

/** `${tipoDocumento} ${numeroDocumento}` — mismo formato que ya usa `BiometricStep`. */
function documentoValor(tipo: string, numero: string): string | null {
  const partes = [tipo, numero].filter(Boolean);
  return partes.length ? partes.join(' ') : null;
}

const MECANISMO_FIRMA_LABEL: Record<string, string> = {
  baul: 'Firma del baúl',
  identidad: 'Validación de identidad',
};

/** Tarjeta de una parte (vendedor/comprador): datos del actor + estado de firma al pie. */
function ActorCard({
  titulo,
  actor,
  firmaEstado,
}: {
  titulo: string;
  actor: ProcedureActor;
  firmaEstado: FirmaParteEstado | null | undefined;
}) {
  return (
    <TarjetaDetalle
      titulo={titulo}
      tituloAzul
      accion={<FirmaEstadoSoft estado={firmaEstado} />}
    >
      <ListaCampos>
        <CampoValor campo="Nombre" valor={actor.nombreCompleto} />
        <CampoValor campo="Documento" valor={documentoValor(actor.tipoDocumento, actor.numeroDocumento)} />
        <CampoValor campo="Correo" valor={actor.email} />
        {actor.telefono ? <CampoValor campo="Teléfono" valor={actor.telefono} /> : null}
        {actor.direccion ? <CampoValor campo="Dirección" valor={actor.direccion} /> : null}
        {actor.ciudad ? <CampoValor campo="Ciudad" valor={actor.ciudad} /> : null}
      </ListaCampos>
    </TarjetaDetalle>
  );
}

/** Tarjeta del representante legal embebido en el actor `comprador` (persona jurídica). */
function RepresentanteLegalCard({
  representante,
  parte,
}: {
  representante: RepresentanteLegal;
  /** De qué parte es. Con dos representantes en pantalla, «Representante legal» a secas no basta. */
  parte?: string;
}) {
  const documento = documentoValor(representante.tipoDocumento ?? '', representante.numeroDocumento ?? '');
  return (
    <TarjetaDetalle titulo={parte ? `Representante legal · ${parte}` : 'Representante legal'}>
      <ListaCampos>
        <CampoValor campo="Nombre" valor={representante.nombreCompleto} />
        <CampoValor campo="Documento" valor={documento} />
        {representante.email ? <CampoValor campo="Correo" valor={representante.email} /> : null}
        {representante.telefono ? <CampoValor campo="Teléfono" valor={representante.telefono} /> : null}
        {representante.mecanismoFirma ? (
          <CampoValor campo="Firma con" valor={MECANISMO_FIRMA_LABEL[representante.mecanismoFirma]} />
        ) : null}
      </ListaCampos>
    </TarjetaDetalle>
  );
}

export function TramiteDetalleActores({ instanceId, tenantId, item }: SeccionDetalleProps) {
  const [actors, setActors] = useState<ProcedureActor[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await tramitesClient.getActors(instanceId, tenantId);
        if (!cancelled) setActors(res ?? []);
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'No se pudieron cargar los actores del trámite.');
          setActors([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [instanceId, tenantId, reloadKey]);

  if (loading) {
    return (
      <TarjetaDetalle titulo="Actores del trámite">
        <SeccionCargando etiqueta="Cargando actores del trámite" />
      </TarjetaDetalle>
    );
  }

  if (error) {
    return (
      <TarjetaDetalle titulo="Actores del trámite">
        <SeccionError mensaje={error} onReintentar={() => setReloadKey((k) => k + 1)} />
      </TarjetaDetalle>
    );
  }

  // Solo la familia TRASPASO tiene parte vendedora; en las demás interviene un único titular.
  const esTraspaso = item.modalidad === 'TRASPASO';
  const vendedor = esTraspaso ? actors.find((a) => a.rol === 'vendedor') : undefined;
  const comprador = actors.find((a) => a.rol === 'comprador');

  // El representante legal se pinta SIEMPRE que la parte lo traiga, sea cual sea la modalidad. La
  // propuesta solo lo dibuja en matrícula inicial, pero eso es un límite de su maqueta, no del
  // negocio: en un traspaso cualquiera de las dos partes puede ser una empresa, y entonces quien
  // firma es su representante. Ocultarlo por fidelidad a la maqueta escondería a un actor real
  // del trámite, que es peor que apartarse de ella.
  const representantes = [
    { parte: 'Propietario / vendedor', actor: vendedor },
    { parte: 'Comprador', actor: comprador },
  ].flatMap(({ parte, actor }) =>
    actor?.representanteLegal ? [{ parte, representante: actor.representanteLegal }] : [],
  );

  if (!vendedor && !comprador) {
    return (
      <TarjetaDetalle titulo="Actores del trámite">
        <SeccionVacia mensaje="Este trámite no tiene actores registrados." />
      </TarjetaDetalle>
    );
  }

  return (
    <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
      {vendedor ? (
        <ActorCard titulo="Propietario / vendedor" actor={vendedor} firmaEstado={item.firmaVendedorEstado} />
      ) : null}
      {comprador ? (
        <ActorCard titulo="Comprador" actor={comprador} firmaEstado={item.firmaCompradorEstado} />
      ) : null}
      {representantes.map((r) => (
        <RepresentanteLegalCard
          key={r.parte}
          representante={r.representante}
          parte={esTraspaso ? r.parte : undefined}
        />
      ))}
    </div>
  );
}
