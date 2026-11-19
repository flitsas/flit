# Acta técnica — Consultas, colas y comunicación entre servicios en Diagnóstico

> Documento generado el 2026-10-26 a partir de `conversacion.txt`.
> La fecha de la reunión no aparece en la transcripción.
> Participantes: Líder Técnico y desarrollador de Diagnóstico.

## 1. Objetivo de la conversación

Aclarar cómo debe ejecutar Diagnóstico las consultas requeridas para procesar lotes de vehículos, cuándo corresponde
una comunicación síncrona o asíncrona, cómo deben aislarse los servicios y dónde debe vivir la liquidación de impuestos.

## 2. Resumen ejecutivo

Se definió que las consultas en línea, cuyo resultado necesita el usuario para continuar una operación, pueden ejecutarse
de forma directa entre servicios. Para este caso ya se usa gRPC y se aprovecha caché cuando existe un resultado previo.

El procesamiento masivo de Diagnóstico tiene una necesidad distinta: la carga del lote debe aceptarse sin mantener al
usuario esperando mientras se consulta cada registro. Diagnóstico debe entregar el trabajo a RabbitMQ y continuar. El
procesamiento se realiza en segundo plano y el resultado se comunica cuando termina.

La separación busca tolerancia a fallos: si Diagnóstico, Notificaciones u otro servicio se cae, el resto de productos no
debe detenerse. En operaciones asíncronas, los trabajos deben permanecer en cola hasta que el servicio responsable pueda
procesarlos.

La liquidación de impuestos no se incorporará dentro de Diagnóstico. Debe permanecer como una capacidad independiente y
reutilizable por otros productos. Inicialmente se consumirá la solución existente; en una decisión posterior se evaluará
si se mejora, se reemplaza o se construye nuevamente.

## 3. Decisiones acordadas

### 3.1 Comunicación síncrona para operaciones en línea

- Una consulta requerida para continuar inmediatamente un trámite se resuelve mediante una llamada directa.
- La comunicación interna mostrada en la reunión usa gRPC por su formato binario y menor sobrecarga.
- El resultado puede almacenarse en caché para responder rápidamente ante consultas repetidas.
- Este patrón exige que el servicio consultado responda durante la interacción del usuario.

### 3.2 Comunicación asíncrona para trabajos que no bloquean al usuario

- Operaciones como el envío de correos no necesitan bloquear el trámite.
- Si el servicio responsable está caído, el trabajo queda pendiente en la cola y se procesa cuando el servicio vuelve a
  estar disponible.
- La indisponibilidad de un servicio no debe provocar la caída de los demás productos.

### 3.3 Los lotes de Diagnóstico se procesarán mediante RabbitMQ

- Diagnóstico recibe una carga de múltiples registros y no espera la finalización de todas las consultas para responder.
- Diagnóstico no debe ejecutar un bucle de llamadas directas por cada registro del lote.
- Debe publicar las solicitudes de trabajo en RabbitMQ.
- El procesamiento se ejecuta en segundo plano, respetando la disponibilidad del servicio que atiende cada consulta.
- Diagnóstico queda desacoplado del tiempo de ejecución del lote y recibe o consulta el resultado al finalizar.

El objetivo es evitar el comportamiento anterior descrito en la reunión: un proceso ejecutado en memoria simulaba una
cola, pero al reiniciarse o caerse el servicio perdía el punto de avance y requería intervención para reanudar.

### 3.4 Independencia de servicios

Se reafirmó que Diagnóstico, Trámites, Gestión de Comparendos, Notificaciones, Consultas y las demás capacidades deben
operar como servicios independientes.

La consecuencia esperada es aislamiento de fallos:

- si Diagnóstico falla, se afecta Diagnóstico, no Trámites ni Notificaciones;
- si Notificaciones falla, los trámites siguen avanzando y los correos quedan pendientes;
- los trabajos asíncronos se recuperan desde la cola cuando el consumidor vuelve a estar disponible.

### 3.5 Ubicación de las integraciones de consulta

- Diagnóstico no debe integrar directamente cada proveedor externo.
- La conversación ubica las integraciones de consulta en el servicio de Consultas.
- Se indicó que ya existen consultas de RUNT y una consulta directa de SIMIT mediante Verifik.
- Se mencionó como pendiente implementar una consulta adicional, aparentemente Intempo, dentro del servicio de
  Consultas y usarla como alternativa para la consulta de RUNT.

El nombre “Intempo” debe confirmarse con los participantes porque la transcripción automática no es totalmente clara en
ese fragmento.

### 3.6 Liquidación de impuestos

- La implementación de liquidación de impuestos no se migrará dentro de Diagnóstico.
- Debe existir como un servicio independiente porque será consumido por Diagnóstico y por otros procesos del negocio.
- Para la primera integración se usará el servicio actual y se validará que entregue la respuesta esperada.
- La decisión de mejorar el servicio actual, reemplazar su scraping o construir uno nuevo queda para una etapa posterior.

## 4. Flujo funcional acordado

1. El usuario carga un archivo con múltiples registros en Diagnóstico.
2. Diagnóstico valida y acepta el lote.
3. Diagnóstico envía a RabbitMQ las solicitudes de consulta necesarias, en lugar de invocar cada proveedor
   secuencialmente.
4. El componente responsable toma los trabajos de la cola cuando tiene capacidad.
5. Las integraciones correspondientes ejecutan las consultas, incluyendo RUNT, SIMIT, avaluos y, cuando aplique, impuestos.
6. Si un consumidor no está disponible, los trabajos permanecen pendientes.
7. Cuando termina el procesamiento, Diagnóstico obtiene la respuesta o recibe una notificación de finalización.
8. La interfaz presenta el avance y los resultados sin haber mantenido bloqueada la solicitud inicial.

La reunión no definió el mecanismo técnico exacto del paso 7.

## 5. Responsabilidades identificadas

### Diagnóstico

- Recibir cargas y administrar el lote.
- Solicitar el procesamiento por medio de RabbitMQ.
- Mantener el seguimiento funcional del lote y presentar sus resultados.
- No contener las implementaciones específicas de proveedores ni la lógica interna del liquidador de impuestos.

### Servicio de Consultas

- Concentrar las integraciones de consulta mencionadas en la reunión.
- Exponer la consulta de RUNT existente.
- Mantener la integración directa de SIMIT mediante Verifik.
- Incorporar la consulta adicional mencionada como Intempo, pendiente de validación nominal.

### RabbitMQ y consumidores

- Desacoplar la recepción del lote de su procesamiento.
- Conservar trabajos pendientes durante indisponibilidades temporales.
- Entregar trabajo conforme los consumidores recuperen capacidad.

La conversación no asigna de manera inequívoca cuál servicio será propietario de cada consumidor.

### Servicio de liquidación de impuestos

- Resolver la liquidación de impuestos como capacidad reutilizable.
- Permanecer separado de Diagnóstico.
- Atender inicialmente las solicitudes usando la solución existente.

## 6. Pendientes y decisiones abiertas

1. Confirmar el nombre y alcance exactos de la integración mencionada como **Intempo**.
2. Definir quién consume cada mensaje y quién ejecuta finalmente la llamada al servicio de Consultas.
3. Definir cómo se informa a Diagnóstico que un lote o una consulta terminó.
4. Definir contratos de mensajes, colas, exchanges, claves de enrutamiento y versionado.
5. Definir política de reintentos, confirmaciones, tiempo máximo, mensajes fallidos y DLQ.
6. Definir idempotencia y recuperación para evitar pérdida o duplicación de trabajo.
7. Definir concurrencia, prioridades y límites de procesamiento por lote o empresa.
8. Revisar si el servicio actual de impuestos se optimiza, se reemplaza o se reimplementa.
9. Evaluar una alternativa más eficiente al scraping actual de impuestos, evitando automatización pesada de navegador si
   es posible.
10. Presentar al equipo el flujo funcional y visual de Diagnóstico para recoger oportunidades de mejora.

## 7. Aspectos mencionados, pero no definidos

La reunión no cerró decisiones sobre:

- topología concreta de RabbitMQ;
- contenido de los mensajes;
- garantía de entrega o semántica de procesamiento;
- política exacta de caché;
- persistencia del progreso y estados de cada consulta;
- mecanismo de callback, evento de respuesta o polling;
- observabilidad, métricas y alertas;
- seguridad, autenticación y autorización entre servicios;
- tecnología final del servicio de impuestos.

Estos puntos no deben presentarse como acuerdos hasta ser diseñados y ratificados.

## 8. Aclaraciones sobre la transcripción

La transcripción contiene errores de reconocimiento de voz. Para redactar esta acta se interpretaron por contexto, sin
convertirlos en decisiones adicionales:

- “room”, “run” o “ROM” parece referirse a **RUNT**;
- “verifica” parece referirse a **Verifik**;
- “crapping” parece referirse a **scraping**;
- “Landa” podría referirse a una ejecución en memoria o a una tecnología concreta, pero no es posible confirmarlo;
- “Free uno” parece ser un sistema anterior o legado, pero no puede identificarse con certeza;
- “Intempo” es probable, pero debe validarse.

## 9. Conclusión

La decisión central es separar las consultas interactivas de los procesos masivos. Las primeras pueden continuar como
llamadas directas y rápidas; los lotes de Diagnóstico deben procesarse de forma asíncrona mediante RabbitMQ. La
arquitectura debe preservar la independencia de los servicios y ubicar cada integración en su capacidad especializada,
incluido un servicio de impuestos separado y reutilizable.
