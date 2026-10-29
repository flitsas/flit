-- =============================================================================
-- HU #14073 (Feature J, Épica #14021) — las preguntas frecuentes que el portal muestra por defecto
-- pasan al contenido de trabajo, para editarlas en Plataforma > Persona Natural > Contenido.
-- Migración: 20261028160000_HU14073_PreguntasFrecuentesIniciales.
--
-- Aditivo e idempotente: solo inserta si la tabla está vacía. No publica (el portal sigue con su texto
-- de siempre hasta que un superadministrador publique). No toca banners ni enlaces. Sin PII.
-- =============================================================================

INSERT INTO admin.persona_natural_faqs (id, question, answer, sort_order, is_active)
SELECT v.id, v.question, v.answer, v.sort_order, true
  FROM (VALUES
    ('a1407300-0001-4000-8000-000000000001'::uuid, 1,
     '¿Cómo me entregan la tarjeta de propiedad?',
     'Una vez el Organismo de Tránsito aprueba la solicitud, la licencia de tránsito (tarjeta de propiedad) física se envía por mensajería especializada a la dirección registrada, o puedes descargar el documento digital en PDF certificado.'),
    ('a1407300-0001-4000-8000-000000000002'::uuid, 2,
     '¿Cuánto tarda la aprobación de mi trámite?',
     'El tiempo estimado de respuesta por parte de la Secretaría de Movilidad es de 24 a 48 horas hábiles a partir del pago y validación biométrica de los participantes.'),
    ('a1407300-0001-4000-8000-000000000003'::uuid, 3,
     '¿Cómo hago el pago de mi trámite?',
     'Pagas los derechos de tránsito y el servicio digital por transferencia a la cuenta Bancolombia que verás en el Paso 4 del proceso de radicación, y cargas allí el comprobante del pago para que lo revisemos.'),
    ('a1407300-0001-4000-8000-000000000004'::uuid, 4,
     '¿Qué pasa si no tengo SOAT o Técnico-Mecánica vigente?',
     'Nuestro sistema realiza un diagnóstico automático frente al RUNT. En caso de presentar inconsistencias o faltantes, te notificaremos en el Paso 1 para que puedas ponerte al día antes de radicar.'),
    ('a1407300-0001-4000-8000-000000000005'::uuid, 5,
     '¿Cómo se realiza la validación de identidad biométrica?',
     'Recibirás un enlace seguro a tu correo electrónico o celular para tomar una fotografía de tu rostro y la foto de tu cédula por ambos lados en cuestión de minutos.')
  ) AS v(id, sort_order, question, answer)
 WHERE NOT EXISTS (SELECT 1 FROM admin.persona_natural_faqs);
