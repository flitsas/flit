// HU #11666 — copy de cara al gestor de los motivos tipificados de no envío (HU #11665).
// Contrato: los 6 códigos del backend tienen texto propio, la naturaleza (bloqueo/información)
// coincide con el flag `informativo` y solo los corregibles ofrecen acción.
import { describe, expect, it } from 'vitest';
import {
  ENVIO_VALIDACION_MOTIVO_CODIGOS,
  motivoDeParte,
  presentarMotivoNoEnvio,
} from '../envio-validacion-motivos';

// Uso de ejemplo: presentarMotivoNoEnvio('rl_sin_correo', 'Comprador')
//   → { naturaleza: 'bloqueo', titulo: 'Falta el correo del representante legal', accion: 'actores' }

describe('presentarMotivoNoEnvio — códigos del backend (HU #11665)', () => {
  it('traduce cada código a un texto sin jerga técnica', () => {
    for (const codigo of ENVIO_VALIDACION_MOTIVO_CODIGOS) {
      const copy = presentarMotivoNoEnvio(codigo, 'Comprador');
      expect(copy.titulo.length).toBeGreaterThan(0);
      expect(copy.detalle.length).toBeGreaterThan(0);
      // El gestor no debe leer nunca el código crudo.
      expect(`${copy.titulo} ${copy.detalle}`).not.toContain(codigo);
    }
  });

  it('los motivos corregibles por el gestor llevan al paso de actores', () => {
    for (const codigo of ['rl_sin_documento', 'rl_sin_correo', 'sujeto_no_es_representante']) {
      const copy = presentarMotivoNoEnvio(codigo, 'Vendedor');
      expect(copy.naturaleza).toBe('bloqueo');
      expect(copy.accion).toBe('actores');
      expect(copy.accionLabel).toBeTruthy();
    }
  });

  it('proveedor_no_envia es bloqueo pero NO ofrece corrección: no depende del gestor', () => {
    const copy = presentarMotivoNoEnvio('proveedor_no_envia', 'Comprador');
    expect(copy.naturaleza).toBe('bloqueo');
    expect(copy.accion).toBeNull();
    expect(copy.detalle).toMatch(/ambiente/i);
  });

  it('los informativos son información, no error, y no sugieren corrección', () => {
    for (const codigo of ['cubierto_por_baul', 'representante_utilizable']) {
      const copy = presentarMotivoNoEnvio(codigo, 'Comprador');
      expect(copy.naturaleza).toBe('informacion');
      expect(copy.accion).toBeNull();
    }
  });

  it('no atribuye a la empresa una causa que el código no afirma', () => {
    // `sujeto_no_es_representante` habla del representante DEL TRÁMITE; decir que la compañía no
    // tiene representante legal sería inventar una causa.
    const copy = presentarMotivoNoEnvio('sujeto_no_es_representante', 'Comprador');
    expect(copy.detalle).toMatch(/en el trámite/i);
  });

  it('un código desconocido no rompe la pantalla ni se inventa la causa', () => {
    const copy = presentarMotivoNoEnvio('motivo_futuro', 'Comprador');
    expect(copy.naturaleza).toBe('bloqueo');
    expect(copy.accion).toBeNull();
    expect(copy.detalle).toContain('motivo_futuro');
  });
});

describe('motivoDeParte — contrato de la respuesta', () => {
  const motivos = [
    { parte: 'comprador', codigo: 'rl_sin_correo', informativo: false },
    { parte: 'vendedor', codigo: 'cubierto_por_baul', informativo: true },
  ];

  it('empareja por rol sin distinguir mayúsculas', () => {
    expect(motivoDeParte(motivos, 'COMPRADOR')?.codigo).toBe('rl_sin_correo');
  });

  it('devuelve null cuando la parte no reporta motivo', () => {
    expect(motivoDeParte(motivos, 'testigo')).toBeNull();
  });

  it('tolera null/undefined/lista vacía (campo opcional del contrato)', () => {
    expect(motivoDeParte(null, 'comprador')).toBeNull();
    expect(motivoDeParte(undefined, 'comprador')).toBeNull();
    expect(motivoDeParte([], 'comprador')).toBeNull();
  });
});
