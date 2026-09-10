# Requerimiento funcional: Concesiones y Marca Blanca

## 1. Contexto

Se requiere ampliar la plataforma para soportar dos modelos de operación:

1. **Concesión:** empresa que administra uno o varios organismos de tránsito y que puede operar directamente o por medio de compañías cliente asociadas.
2. **Marca blanca:** cliente padre que administra su propia red de compañías y usuarios bajo una identidad visual personalizada.

Aunque ambos modelos manejan una relación padre-hijo, sus reglas de negocio son diferentes. En una concesión, la operación está limitada a los organismos de tránsito administrados por la concesión. En una marca blanca, los organismos disponibles se determinan mediante las restricciones asignadas al cliente padre y heredadas por sus compañías hijas.

## 2. Objetivo

Permitir la creación y administración de concesiones y marcas blancas, sus compañías hijas y sus usuarios, garantizando:

- La aplicación correcta de permisos y restricciones sobre organismos de tránsito.
- La trazabilidad de los trámites según la compañía que los radica.
- La consulta consolidada de trámites y documentos por parte de las empresas padre.
- La personalización visual y de notificaciones para las marcas blancas.
- La conservación del comportamiento actual de las notificaciones cuando no aplique personalización.

## 3. Actores

### Administrador de plataforma

- Crea y administra concesiones y marcas blancas.
- Asocia organismos de tránsito a las concesiones.
- Configura restricciones de organismos de tránsito para las marcas blancas.
- Consulta y administra la configuración general de cada organización.

### Administrador de concesión

- Crea y administra compañías hijas.
- Crea y administra usuarios de la concesión.
- Puede radicar trámites directamente como concesión.
- Consulta sus trámites y los trámites de sus compañías hijas.

### Compañía hija de concesión

- Radica trámites únicamente en los organismos de tránsito asociados a la concesión padre.
- Los trámites quedan registrados a nombre de la compañía hija que los radica.

### Administrador de marca blanca

- Configura la identidad visual de su marca blanca.
- Crea y administra compañías hijas y usuarios.
- Puede radicar trámites directamente.
- Consulta sus trámites y los de sus compañías hijas, incluidos sus documentos.

### Compañía hija de marca blanca

- Radica trámites en los organismos habilitados para la marca blanca padre.
- Hereda automáticamente las restricciones de organismos de tránsito del padre.
- Los trámites quedan registrados a nombre de la compañía hija que los radica.

## 4. Alcance funcional

## 4.1. Módulo de Concesiones

### RF-CON-01. Crear una concesión

El administrador de plataforma debe poder crear una organización de tipo **Concesión**.

La concesión debe almacenar, como mínimo:

- Nombre o razón social.
- Estado: activa o inactiva.
- Datos de contacto.
- Organismos de tránsito que administra.
- Administrador y usuarios asociados.

### RF-CON-02. Asociar organismos de tránsito

El administrador de plataforma debe poder asociar uno o varios organismos de tránsito a una concesión.

Reglas:

- La concesión solo podrá radicar trámites en los organismos asociados.
- Las compañías hijas solo podrán radicar trámites en los organismos asociados a la concesión padre.
- Si un organismo es retirado de la concesión, dejará de estar disponible para futuras radicaciones de la concesión y de todas sus compañías hijas.
- El retiro de un organismo no debe modificar los trámites históricos.

### RF-CON-03. Administrar compañías hijas

El administrador de la concesión debe poder:

- Crear compañías hijas.
- Consultar y editar sus datos.
- Activarlas o inactivarlas.
- Crear y administrar los usuarios asociados a cada compañía.

Las compañías hijas representan clientes de la concesión y no deben administrar organismos de tránsito propios.

### RF-CON-04. Radicar trámites

La plataforma debe permitir que:

- La concesión radique trámites directamente a su nombre.
- Las compañías hijas radiquen trámites a su propio nombre.
- La selección del organismo de tránsito se limite a los organismos asociados a la concesión.

Cada trámite debe conservar la trazabilidad de:

- La concesión padre.
- La compañía que realizó la radicación.
- El usuario que realizó la acción.
- El organismo de tránsito seleccionado.

### RF-CON-05. Consultar información consolidada

El administrador de la concesión debe poder consultar:

- Los trámites radicados directamente por la concesión.
- Los trámites radicados por cada compañía hija.
- El detalle y los documentos de los trámites que se encuentren bajo su estructura.
- Reportes y datos consolidados de su operación.

Una compañía hija solo debe consultar la información autorizada de su propia operación.

### RF-CON-06. Notificaciones

Para las concesiones se debe conservar el flujo actual de notificaciones, incluyendo las notificaciones relacionadas con:

- Validación de identidad.
- Comprador o interesado asociado.
- Aprobaciones o rechazos.
- Correos suministrados durante el trámite.

La implementación no debe alterar destinatarios, eventos ni reglas existentes, salvo que se defina posteriormente un requerimiento específico.

## 4.2. Módulo de Marca Blanca

### RF-MB-01. Crear una marca blanca

El administrador de plataforma debe poder crear una organización de tipo **Marca Blanca** que funcione como cliente padre.

La marca blanca debe almacenar, como mínimo:

- Nombre o razón social.
- Estado: activa o inactiva.
- Datos de contacto.
- Administrador principal.
- Dominio o URL personalizada.
- Configuración de identidad visual.
- Restricciones de organismos de tránsito.

### RF-MB-02. Administrar compañías y usuarios

El administrador de la marca blanca debe poder:

- Crear compañías hijas.
- Crear usuarios propios.
- Crear usuarios para sus compañías hijas.
- Consultar, editar, activar o inactivar las compañías y usuarios bajo su estructura.

No debe tener acceso a organizaciones ni información que se encuentren por fuera de su marca blanca.

### RF-MB-03. Parametrizar organismos de tránsito

El administrador de plataforma debe poder restringir los organismos de tránsito disponibles para una marca blanca.

Reglas:

- La marca blanca podrá operar en todos los organismos habilitados por la plataforma, excepto aquellos que tenga restringidos.
- Las compañías hijas heredarán las restricciones de la marca blanca padre.
- Si se bloquea un organismo al padre, el bloqueo debe aplicarse automáticamente a todas sus compañías hijas.
- Una compañía hija no puede habilitar un organismo que esté bloqueado para su padre.
- Los cambios aplican a futuras radicaciones y no deben alterar trámites históricos.

### RF-MB-04. Radicar trámites

La marca blanca y sus compañías hijas deben poder radicar trámites.

La plataforma debe:

- Mostrar únicamente los organismos permitidos según las restricciones heredadas.
- Registrar el trámite a nombre de la compañía que lo radica.
- Conservar la relación del trámite con la marca blanca padre.
- Registrar el usuario responsable de la radicación.

### RF-MB-05. Consultar trámites y documentos

El administrador de la marca blanca debe poder consultar:

- Sus propios trámites.
- Los trámites de todas sus compañías hijas.
- El detalle completo de cada trámite.
- Los documentos asociados a cada trámite.

Las compañías hijas solo deben consultar la información autorizada de su propia operación.

### RF-MB-06. Personalizar la identidad visual

El administrador de la marca blanca debe poder configurar:

- Logo.
- Colores de la marca.
- URL o dominio personalizado.

La identidad configurada debe aplicarse, al menos, a:

- Acceso y navegación de la plataforma.
- Encabezados y componentes visuales definidos para la marca blanca.
- Notificaciones enviadas en nombre de la marca blanca.

La configuración debe contar con validaciones de formato, dimensiones y peso para los archivos gráficos, además de validaciones de formato para colores y dominio.

### RF-MB-07. Personalizar notificaciones

Las notificaciones generadas por trámites de una marca blanca o de sus compañías hijas deben usar:

- Nombre de la marca blanca.
- Logo configurado.
- Colores configurados.
- Plantilla o estructura visual definida para la marca.

Reglas:

- Las notificaciones podrán enviarse mediante la infraestructura actual de Flit.
- La comunicación visible para el destinatario no debe presentarse como una comunicación de Flit cuando corresponda a una marca blanca.
- Los datos funcionales del trámite y los eventos que generan la notificación deben conservar el comportamiento actual.
- Si la marca blanca no ha completado su configuración visual, el sistema debe aplicar una plantilla de respaldo definida por la plataforma.

### RF-MB-08. Administrar la configuración visual

La plataforma debe ofrecer un configurador sencillo para que el administrador de la marca blanca pueda:

- Cargar o reemplazar el logo.
- Seleccionar los colores de marca.
- Previsualizar la apariencia.
- Guardar y publicar los cambios.

Los cambios publicados deben aplicarse a las nuevas sesiones y notificaciones sin afectar el contenido de comunicaciones históricas.

## 5. Reglas de negocio transversales

1. Toda compañía hija debe pertenecer a una única organización padre.
2. Un usuario solo podrá actuar sobre organizaciones para las que tenga permisos.
3. Los trámites deben quedar a nombre de la compañía que realiza la radicación, no automáticamente a nombre de la empresa padre.
4. La empresa padre debe contar con una vista consolidada de su operación y la de sus hijas.
5. La inactivación de una empresa padre debe impedir nuevas radicaciones de sus compañías hijas.
6. La inactivación de una compañía hija debe impedir nuevas radicaciones de sus usuarios.
7. Los cambios de permisos o restricciones no deben eliminar ni modificar la trazabilidad histórica.
8. La plataforma debe registrar auditoría de los cambios de configuración, permisos, restricciones e identidad visual.

## 6. Criterios de aceptación

### CA-CON-01. Restricción de organismos en concesión

**Dado** que una concesión tiene asociados los organismos A y B  
**Cuando** la concesión o una de sus compañías hijas inicia una radicación  
**Entonces** solo debe poder seleccionar los organismos A y B.

### CA-CON-02. Radicación de compañía hija

**Dado** que una compañía hija pertenece a una concesión  
**Cuando** un usuario de la compañía completa una radicación  
**Entonces** el trámite debe quedar registrado a nombre de la compañía hija y relacionado con la concesión padre.

### CA-CON-03. Consulta consolidada

**Dado** que una concesión tiene trámites propios y trámites de sus compañías hijas  
**Cuando** su administrador consulta la operación  
**Entonces** debe visualizar ambos grupos de trámites y sus documentos.

### CA-MB-01. Herencia de restricciones

**Dado** que el organismo Sabaneta está bloqueado para una marca blanca  
**Cuando** una compañía hija inicia una radicación  
**Entonces** Sabaneta no debe estar disponible para selección.

### CA-MB-02. Propagación de un nuevo bloqueo

**Dado** que una marca blanca y sus compañías hijas podían operar en un organismo  
**Cuando** el administrador de plataforma bloquea dicho organismo para la marca blanca  
**Entonces** el bloqueo debe aplicarse a la marca blanca y a todas sus compañías hijas para futuras radicaciones.

### CA-MB-03. Consulta jerárquica

**Dado** que existen trámites radicados por la marca blanca y por sus compañías hijas  
**Cuando** el administrador de la marca blanca consulta los trámites  
**Entonces** debe poder ver el detalle y los documentos de todos los trámites de su estructura.

### CA-MB-04. Aplicación de identidad visual

**Dado** que una marca blanca configuró un logo, colores y dominio  
**Cuando** un usuario ingresa mediante su URL  
**Entonces** la plataforma debe mostrar la identidad visual configurada.

### CA-MB-05. Notificación personalizada

**Dado** que se genera una notificación para un trámite de una marca blanca  
**Cuando** la notificación es enviada  
**Entonces** debe utilizar el nombre, logo y colores de la marca blanca, aunque el envío se realice mediante la infraestructura de Flit.

### CA-TRANS-01. Conservación del histórico

**Dado** que existen trámites radicados en un organismo  
**Cuando** el organismo es retirado o bloqueado para la organización  
**Entonces** los trámites históricos deben continuar disponibles para consulta y el organismo no debe estar disponible para nuevas radicaciones.

## 7. Requerimientos no funcionales

- **Seguridad:** aplicar autorización por rol, organización y jerarquía padre-hijo.
- **Aislamiento de datos:** impedir el acceso a información de otras concesiones, marcas blancas o compañías.
- **Auditoría:** registrar usuario, fecha y valores anteriores y nuevos de cada cambio de configuración.
- **Usabilidad:** permitir que la configuración visual se realice sin conocimientos técnicos.
- **Compatibilidad:** validar la operación de la identidad visual en los navegadores y clientes de correo soportados.
- **Rendimiento:** las consultas consolidadas deben usar paginación y filtros para evitar degradación al aumentar la volumetría.
- **Disponibilidad:** una falla al cargar la personalización no debe impedir el acceso; debe utilizarse la configuración de respaldo.

## 8. Fuera de alcance

Salvo definición posterior, este requerimiento no contempla:

- La creación de una infraestructura de correo independiente para cada marca blanca.
- La modificación del flujo funcional de validación de identidad.
- La modificación de trámites históricos por cambios en la jerarquía o en los organismos permitidos.
- Que una compañía hija configure permisos superiores a los de su empresa padre.
- La administración directa de organismos de tránsito por una marca blanca.

## 9. Pendientes por definición

Antes de iniciar el desarrollo se deben confirmar los siguientes puntos:

1. Roles exactos y matriz detallada de permisos para administradores y usuarios operativos.
2. Datos obligatorios para crear concesiones, marcas blancas y compañías hijas.
3. Si una compañía hija puede pertenecer simultáneamente a más de una organización padre.
4. Alcance exacto de los reportes y métricas disponibles para la empresa padre.
5. Si las compañías hijas pueden recibir restricciones adicionales a las heredadas del padre.
6. Proceso de configuración técnica, validación y activación del dominio personalizado.
7. Formatos, dimensiones y peso máximo permitidos para logos.
8. Reglas de accesibilidad y contraste para los colores configurados.
9. Nivel de personalización de las plantillas de correo: solo logo y colores o también textos, estructura y componentes.
10. Nombre y dirección visibles del remitente, dominio de envío y reglas de respuesta para los correos.
11. Comportamiento esperado al cambiar la identidad visual después de haber enviado notificaciones.
12. Plantilla visual de respaldo que se utilizará cuando la marca blanca no tenga configuración completa.
13. Confirmación de si el administrador de concesión puede acceder a los documentos de sus compañías hijas con el mismo alcance definido para la marca blanca.

## 10. Definición de terminado

El requerimiento se considerará terminado cuando:

- Se puedan crear organizaciones de tipo Concesión y Marca Blanca.
- Se puedan administrar sus compañías hijas y usuarios.
- Las reglas de disponibilidad y herencia de organismos funcionen según el tipo de organización.
- Los trámites conserven la trazabilidad de padre, compañía radicadora, usuario y organismo.
- Los administradores padre puedan consultar los trámites y documentos autorizados de sus compañías hijas.
- La identidad visual de una marca blanca se aplique en la plataforma y en sus notificaciones.
- Los criterios de aceptación cuenten con pruebas funcionales.
- Las acciones administrativas relevantes queden registradas en auditoría.
- No se presenten regresiones en el flujo actual de trámites y notificaciones.
