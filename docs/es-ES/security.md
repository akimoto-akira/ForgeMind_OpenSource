# Seguridad de ForgeMind

Este documento describe el diseño de seguridad y las recomendaciones de uso en la porción pública.

## 1. Límites de comunicación local

- **El servidor escucha solo loopback por defecto**: el modo HTTP escucha en `127.0.0.1:8765` y no se expone a la red externa.
- **El puente es TCP local**: entre el host y el adaptador de motor. El adaptador debe leer `%USERPROFILE%\.forgemind\bridge.json` (incluye host, puerto y credenciales) para completar el descubrimiento y la autenticación antes del protocolo de enlace.
- No exponga el puerto del servidor a internet público. Si necesita uso remoto, colóquelo en una red de confianza y transmítalo por un canal seguro (p. ej., túnel SSH).

## 2. Integridad de complementos

- Al cargar complementos, el `PluginLoader` del host realiza **verificación de integridad del manifiesto** (`PluginIntegrityVerifier`) y verificación de firmas (`PluginSigningKeys`): el paquete del complemento debe incluir el DLL principal + `.manifest.json` del mismo nombre. Los paquetes faltantes o inconsistentes se rechazan/omiten.
- Las herramientas inyectadas por complementos se ejecutan dentro del proceso del host. Instale solo complementos de fuentes de confianza (compilados desde la porción pública o lanzamientos oficiales).

## 3. Modelo de autorización

**Mecanismos de seguridad públicos**:
- `IAuthorizationService`: abstracción de entrada de autorización del host. La implementación predeterminada es `OpenAuthorizationService` (funciones fijas de la comunidad, no analiza ningún archivo de licencia y no genera licencias de prueba).
- ServiceOverride de confianza: los complementos pueden sustituir servicios del host mediante tipos abstractos en el ServiceOverride del manifiesto (el host no conoce el concepto 'comercial', solo sustituye por tipos).
- Verificación de identidad y manifiesto de complementos: `PluginIntegrityVerifier` / `PluginSigningKeys` verifican el manifiesto y la firma del complemento, garantizando que solo se carguen complementos de confianza.
- Autenticación de conexión del puente: el adaptador completa el descubrimiento local y el protocolo de enlace de credenciales a través de `bridge.json`, confirmando que el origen de la conexión es de confianza.

**Licencias comerciales**: Core proporciona la abstracción `IAuthorizationService` y el mecanismo de extensión ServiceOverride de confianza. La implementación de licencias comerciales es un complemento de código cerrado; el protocolo de licencias específico, la emisión, la verificación y la lógica de vinculación no pertenecen a la porción pública.

## 4. Permisos de herramientas

- Las herramientas de operación del motor están sujetas a `ServerPermissionChecker` (dentro de cada complemento) y verifican la autorización de funciones antes de la ejecución.
- Los fallos de `Execute` de las herramientas devuelven uniformemente el mensaje de error de `ToolResult`, sin filtrar detalles internos de la pila.

## 5. Recomendaciones de uso

- Abra el puerto MCP solo a clientes de confianza. Los clientes tienen capacidad de operación del motor, equivalente a privilegios de administrador local.
- Mantenga actualizados complementos, host y proyecto de ejemplo para recibir rápidamente correcciones de seguridad.
- En máquinas compartidas, no copie las credenciales de `bridge.json` a procesos no confiables.
- Reporte problemas de seguridad por los canales del distribuidor. No divulgue detalles de vulnerabilidades sin corregir.
