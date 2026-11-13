# Pruebas de Smart Backup

## Automatización

Validación del 11/11/2026: SDK 10.0.404, ejecución Release en Windows, **82 casos aprobados, 0 fallidos y 0 omitidos**, incluidos los de la fase anterior. La prueba STA de Desktop carga recursos compartidos y los enlaces iniciales de la vista sin mostrar ventanas ni disparar el arranque real del producto. Es una comprobación de carga, no una validación visual ni de los diálogos nativos. El procedimiento en VMs que sigue permanece pendiente.

Ejecutar con SDK .NET 10 en Windows:

```powershell
dotnet restore JPCFIX.Optimizer.slnx
dotnet build JPCFIX.Optimizer.slnx -c Release --no-restore
dotnet test JPCFIX.Optimizer.slnx -c Release --no-build
```

SmartBackupTests usa un IBackupEnvironment simulado para carpetas redirigidas, identidad física, capacidad, formato y fallo de permisos de destino. Los bytes se leen/escriben exclusivamente en fixtures temporales únicos JPCFIX-backup-tests-GUID. Las pruebas de bloqueo y ACL solo afectan archivos de esos fixtures; se restaura la ACL antes de borrarlos. Las junctions se crean exclusivamente dentro del fixture hacia otro directorio del mismo fixture, y se retiran como enlaces antes de limpiar. Una prueba nativa de solo lectura comprueba Known Folders e identidad de los directorios temporales. No se invoca el ejecutor de energía, instaladores ni APIs que alteren el sistema.

Se cubren rutas redirigidas; duplicados y alias; carpetas técnicas frente a archivos personales .tmp/zip/exe/iso/pst/sqlite; archivos individuales; carpetas vacías; estructura; tamaño/hash; escritura temporal; destino solapado/sin espacio/sin permisos/FAT32; origen inexistente, bloqueado, sin permisos o modificado; directorio desaparecido; indicador Offline sin copiar su contenido; exclusión de escritores durante copia; cancelación; checkpoint interrumpido; corrupción y archivos faltantes; backup trasladado y usuario distinto; restauración parcial; todas las políticas de conflicto; confirmación de reemplazo; conflictos nuevos después del preview o durante la copia; preview alterado; JSON malformado/versiones desconocidas/duplicados; traversal/UNC/ADS/dispositivos; junctions de ciclo, destino y escape; fechas y preservación de archivos existentes.

Las pruebas PowerTests de la fase anterior se ejecutan en la misma suite. Los adaptadores de preferencias, inventario de aplicaciones y fondo de pantalla todavía no existen en A y sus tests corresponden a B. La reanudación automática y proveedores de nube requieren C.

## Procedimiento en VM Windows 10 y 11

Usar snapshots descartables, usuarios ficticios y datos sintéticos; nunca datos de clientes. Registrar build de Windows, sistema de archivos, medio, resultado y capturas.

1. Compilar/publicar en desarrollo con scripts/publish.cmd. Ejecutar la carpeta portable completa en una VM sin SDK, con cuenta estándar. Verificar que energía conserva diagnóstico/simulación y que solo sus cambios solicitan UAC. Smart Backup no debe elevar.
2. Revisar UI a 850×600 y 1180×780, escalas 100/150/200%, navegación lateral, scroll horizontal/vertical, casillas, botones deshabilitados durante trabajo, cancelación, selector de usuario actual y confirmaciones propias. Personalizar la selección debe invalidar el análisis o preview anterior.
3. Crear Escritorio/Documentos/Descargas, archivos sin extensión, instaladores, ZIP/ISO, base de datos sintética, nombres Unicode, rutas largas y directorios vacíos. Comparar tamaños y hashes con herramientas independientes; archivos personales no deben descartarse por extensión.
4. Redirigir Documentos/Escritorio mediante Windows a otra unidad y a un recurso SMB de pruebas. Probar misma ruta para dos carpetas conocidas, categorías anidadas y alias. Comparar con las propiedades de Windows. No deben duplicarse ubicaciones que se identifiquen como idénticas.
5. Agregar carpeta fuera del perfil y archivo individual. Revisar que no se busquen otros discos automáticamente ni se anuncie detección exhaustiva. Agregar carpeta técnica y comprobar advertencia/exclusión sin eliminación de origen.
6. Seleccionar USB NTFS/exFAT, FAT32 y recurso UNC con permisos/cuota. Probar espacio insuficiente, archivo sintético >4 GiB en FAT32, destino dentro del origen, destino con junction, medio de solo lectura y desconexión durante copia. Revisar mensajes y ausencia de éxito incompleto.
7. Elegir destino en el mismo volumen y en otra partición del mismo disco. Debe advertir peligro de formateo. Probar proveedor WMI inaccesible: debe advertir que no comprobó separación física. Nunca interpretar otra letra como prueba de seguridad.
8. Abrir archivos sintéticos con escritura/bloqueo exclusivo, cambiar uno entre análisis y copia, denegar acceso en fixture y retirar un origen. Comprobar errores individualizados y conservación de otros archivos. Cerrar la aplicación de origen y crear un nuevo respaldo para completar.
9. Cancelar en copia y verificación; luego matar el proceso durante una copia grande, reabrir la carpeta y revisar estado no completo. A permite recuperación parcial de entradas verificadas, no reanudar automáticamente. No borrar el origen ni las operaciones previas.
10. Alterar un byte, eliminar un archivo y manipular manifest.json con rutas ../, absolutas, UNC, ADS, nombres de dispositivo, GUID vacío y versión desconocida. Debe detectar corrupción o rechazar el manifiesto, sin escribir fuera de las carpetas autorizadas. Probar junction creada en un destino tras el preview.
11. Trasladar toda la carpeta de backup a otra VM y abrirla desde un medio de solo lectura. Crear usuario con nombre/SID diferente, redirigir sus carpetas y previsualizar. Documentos debe apuntar a su ubicación actual, Extras a JPCFIX Recuperados. Nunca al SourcePath del Windows anterior.
12. Crear conflictos con documentos sintéticos. Conservar ambos debe mantener la versión actual y agregar otra; omitir debe dejar la actual; reemplazar solo debe funcionar después de confirmación específica. Cancelar la confirmación no debe modificar datos.
13. Restaurar categorías parciales, cancelar durante restauración, probar permisos insuficientes y destino desconectado. Revisar informe en LocalAppData/JPCFIX.Optimizer/BackupReports y comparar hashes. Ninguna omisión o error debe anunciar restauración total.
14. Con una cuenta OneDrive de laboratorio, comparar disponibles localmente/solo en línea/inaccesibles. No debe descargar ni contar placeholders como datos respaldados. Revisar el límite conservador de reparse points documentado.

## Antes de formatear

El técnico debe comprobar Completo y verificado, cero fallidos/omitidos/pendientes/descargas, revisar todas las advertencias, abrir la copia desde otro equipo y comparar muestras recuperadas. Verificar físicamente que el medio se desconectará/conservará durante instalación o cambio de disco; custodiarlo porque no está cifrado. Una copia parcial, una barra completa o un manifiesto sin comprobar no autorizan borrar originales.

Después de instalar Windows: iniciar sesión como propietario, abrir Smart Backup → Restaurar → seleccionar carpeta → verificar → seleccionar categorías → previsualizar rutas y conflictos → confirmar → revisar informe y abrir archivos recuperados. Conservar el respaldo hasta que el propietario confirme los datos.
