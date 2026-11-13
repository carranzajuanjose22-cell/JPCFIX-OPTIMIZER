# Alcance y pendientes

Entrega A implementa el respaldo/restauración de archivos y carpetas personales seleccionados, manifiesto portable v1, verificación SHA-256, estados e informes, progreso/cancelación y conflictos no destructivos por defecto. Opera con el perfil de la sesión, sin administrador. Para respaldar/restaurar otra cuenta hay que iniciar sesión con esa cuenta; no hay exploración ni carga de hives de otros usuarios.

## Qué incluye y qué exige revisión

- Carpetas conocidas actuales de Windows, incluidas ubicaciones redirigidas accesibles. Favoritos de Windows no equivale a marcadores Chrome/Edge/Firefox.
- Carpetas/archivos individuales agregados manualmente, sin filtros por extensión, fecha o tamaño personal. Los límites son del medio y del inventario, no de la extensión.
- Todos los archivos accesibles enumerados en el análisis, directorios vacíos y estructura relativa. Los archivos agregados después del análisis requieren analizar nuevamente. No es una instantánea de todo el volumen.
- Errores por archivo/directorio, exclusiones técnicas, ubicaciones sin resolver y advertencias de almacenamiento. Una enumeración fallida puede impedir conocer el tamaño/cantidad de descendientes: las cifras son estimaciones parciales, nunca prueba de que se detectó todo.

AppData, Windows y otras ubicaciones técnicas quedan excluidos y visibles como tales. Las selecciones contenidas se deduplican bajo su categoría contenedora. Si Documentos y Escritorio tienen la misma ubicación, se conserva una sola categoría; revisar ese mapeo antes de restaurar. Alias de red que el proveedor no identifique como la misma ubicación pueden requerir revisión manual. Los directorios enlazados no se siguen.

## Entrega B pendiente

Adaptadores Chrome/Edge/Firefox y múltiples perfiles con marcadores HTML/originales consistentes; Mis aplicaciones mediante registro/WinGet, applications.json/csv y winget-packages.json; previsualización seleccionable de reinstalación; fondo de pantalla y preferencias; candidatos adicionales con exploración acotada. No se importan actualmente marcadores ni se inventarían programas, licencias, activaciones, contraseñas o configuraciones privadas. No se ejecutan instaladores.

## Entrega C pendiente

Reanudación automática de copias interrumpidas, recuperación de temporales, estrategias avanzadas de conflicto y soporte de descarga autorizado de OneDrive. A persiste checkpoints: puede abrir una operación interrumpida y recuperar el subconjunto verificado, pero para completar datos pendientes hay que crear un respaldo nuevo y conservar los anteriores. Nunca fusiona o borra operaciones automáticamente.

Los indicadores Offline/Recall requieren descarga y nuevo análisis; no se hidratan archivos. Por precaución se rechazan todos los reparse points, incluidos algunos archivos OneDrive disponibles localmente. Para esta entrega, descargar con los mecanismos oficiales y copiar explícitamente los datos a una carpeta local sin reparse points, luego agregarla y volver a verificar. No se modifica sincronización. No se garantiza detección de todos los proveedores de nube.

## Otros límites

No hay VSS: cerrar aplicaciones que mantengan archivos abiertos, incluidos Outlook y bases de datos. Las garantías de compartición dependen del sistema de archivos y el servidor. No se preservan ACL antiguas, EFS, ADS, enlaces duros como tales, sparse/compression, atributos de nube ni fechas de directorios. Una copia EFS que el usuario pueda leer se guarda en claro. Las fechas/atributos de archivos se conservan si el destino lo permite; un fallo de metadatos se informa, aun si los bytes se publicaron.

El margen de espacio es estimado, no reserva física; cambios de cuota, otros escritores o desconexiones pueden causar un resultado incompleto. Identificación de disco físico y formato puede ser parcial, especialmente en red, RAID, Storage Spaces o montajes. No se garantiza independencia del destino por recibir una letra distinta.

Formato v1: máximo 10.000 categorías, 500.000 entradas y manifiesto de 64 MiB. Selecciones mayores deben dividirse. Nombres no portables y colisiones que solo difieran por mayúsculas bloquean el mapeo; nunca se resuelven descartando un archivo silenciosamente. El manifiesto se mantiene en memoria y se guarda por lotes; no es un catálogo para migrar volúmenes completos. No hay compresión ni cifrado. Las versiones desconocidas se rechazan; no se migran implícitamente.

Los tests automatizados no sustituyen las pruebas de USB/SMB, UI, pérdida real de energía y reinstalación de Windows en máquinas virtuales. La matriz Windows 10/11 sigue pendiente de ejecución manual antes de uso con clientes.
