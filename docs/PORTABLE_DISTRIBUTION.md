# Distribución portable Windows x64

## Para el técnico

Copiar **toda** la carpeta `dist/JPCFIX-Optimizer-Portable/` al pendrive, disco externo o carpeta local. Hacer doble clic en **JPCFIX-Optimizer.exe**. No se requiere instalar .NET, abrir terminales, ejecutar scripts ni instalar el producto.

```
JPCFIX-Optimizer-Portable/
  JPCFIX-Optimizer.exe
  Runner/
    JPCFIX.Optimizer.Runner.exe
  LEEME.txt
  SHA256SUMS.txt
```

JPCFIX-Optimizer.exe es el único ejecutable de inicio en la raíz. **No copiarlo solo**: las operaciones privilegiadas requieren la subcarpeta Runner. No iniciar el auxiliar directamente ni mezclar ejecutables de distintas publicaciones. Los archivos LEEME y SHA256SUMS son instrucciones e identificación del paquete, no dependencias de ejecución.

## Decisión de empaquetado

Se conserva la separación de privilegios existente: interfaz WPF asInvoker y ejecutor requireAdministrator con protocolo cerrado de acciones/GUID y confirmación propia. Una única imagen PE no puede declarar simultáneamente ambos niveles de manifiesto. Es posible rediseñar el arranque o extraer un auxiliar embebido, pero cambiaría el límite actual y agregaría un ciclo propio de extracción, permisos, actualización y validación de código antes de UAC. Esta entrega utiliza la alternativa autorizada de paquete portable con un solo archivo para iniciar y auxiliar independiente.

Ambos ejecutables se publican con .NET 10, win-x64, SelfContained=true, PublishSingleFile=true e IncludeNativeLibrariesForSelfExtract=true. Se incluyen runtime .NET y Windows Desktop; PublishTrimmed=false conserva reflexión, WPF y WMI; no se usa Native AOT ni trimming. Los símbolos de código se embeben en los ensamblados y no se distribuyen PDB sueltos. La publicación se rechaza si quedan dependencias externas inesperadas. Cada ejecutable pesa aproximadamente 134 MiB; ambos suman alrededor de 268 MiB.

El nombre del apphost principal se cambia a JPCFIX-Optimizer.exe después del bundling: las identidades internas de ensamblados y recursos XAML se conservan. Se incorpora un ícono JPCFIX propio de 16 a 256 px al PE y a la ventana. El encabezado muestra el logo original suministrado por JPCFIX, con sus colores y lema, sobre una tarjeta blanca. El PNG se conserva intacto como recurso embebido; WPF muestra el área de la marca sin los márgenes exteriores, sin depender del archivo adjunto ni de una ruta temporal. El ajuste de contraste de encabezados de pestañas mantiene legible la navegación con el tema de Windows. No cambian las operaciones de diagnóstico, energía ni Smart Backup/restauración.

## Seguridad y almacenamiento

RunnerClient resuelve el auxiliar relativo a AppContext.BaseDirectory, que corresponde al directorio del ejecutable distribuido. Antes de solicitar UAC, rechaza rutas con reparse points, compara el SHA-256 del auxiliar con el valor compilado dentro del principal y mantiene abierto el archivo sin compartir escritura o borrado hasta que termine el proceso. Si falta, fue modificado o corresponde a otra versión, no solicita elevación. Los tests usan payloads sintéticos y no ejecutan auxiliares.

Esta comprobación detecta corrupción o sustitución del auxiliar mientras el principal permanezca confiable; no sustituye firma Authenticode, no autentica al editor si se reemplazan ambos archivos ni protege frente a administradores/procesos maliciosos que controlen simultáneamente rutas o cachés. Distribuir desde un origen confiable y custodiar el paquete. La versión generada no está firmada. No se desactiva Defender, SmartScreen, UAC ni ninguna protección. Si Windows bloquea un archivo, verificar su procedencia y resolverlo mediante los procedimientos oficiales, sin desactivar controles.

La extracción nativa estándar de .NET puede utilizar `%TEMP%/.net`, o la ubicación configurada por el administrador mediante DOTNET_BUNDLE_EXTRACT_BASE_DIR. Es caché de código reconstruible, **no un respaldo ni almacén crítico**. No hay extracción personalizada del auxiliar. Los destinos persistentes conservan su comportamiento:

- Datos personales: carpeta elegida expresamente en Smart Backup; manifiesto e informe dentro de cada operación.
- Historial de energía: ProgramData/JPCFIX.Optimizer/Changes; auditoría en el directorio hermano Audit con permisos protegidos por Runner.
- Configuración e informes del usuario/restauración: LocalAppData/JPCFIX.Optimizer.
- El paquete de distribución no contiene datos de clientes ni logs personales.

No se depende del directorio de trabajo, SDK del desarrollador ni rutas absolutas del equipo de compilación. La aplicación puede leer configuración propia del usuario actual, como antes. El medio de origen debe permitir lectura/ejecución y Windows debe permitir escribir en las ubicaciones persistentes correspondientes.

## Publicar de nuevo (solo desarrollo)

Requisitos del equipo que compila: Windows, SDK .NET 10 y acceso a NuGet/runtime packs en la primera publicación. El técnico que recibe el resultado no necesita estas herramientas.

```powershell
.\scripts\publish.ps1
# Alternativa desde CMD:
scripts\publish.cmd
# Si el SDK no está en PATH:
.\scripts\publish.ps1 -DotnetPath 'RUTA_DEL_SDK\dotnet.exe'
```

El script ejecuta la suite completa, publica Runner, calcula su hash, publica Desktop con ese hash compilado, comprueba que cada salida sea un solo EXE y copia el paquete final a dist. Usa staging único en artifacts. Si existe una publicación anterior, la conserva con sufijo previous-GUID; no borra recursivamente paquetes previos. Rechaza directorios de publicación redirigidos. dist/artifacts están excluidos de Git. No realiza commits ni push.

Los perfiles Portable.pubxml son la configuración compartida por cada publicador. Publicar solamente Desktop con single-file sin el hash del auxiliar se bloquea para impedir entregar una distribución privilegiada incompleta. Para una futura publicación firmada, firmar Runner antes de calcular el hash; compilar Desktop con ese hash, firmar Desktop y luego actualizar los checksums.

El ícono fuente se conserva en Assets/Jpcfix.ico y puede regenerarse mediante scripts/create-icon.ps1. scripts/verify-portable.ps1 verifica PE x64/GUI, manifiestos UAC, recursos de ícono y checksums. Con -ApplicationProcessId agrega comprobación del proceso en ejecución sin elevación y bibliotecas nativas WPF; no abre ventanas ni ejecuta cambios del sistema. Guarda evidencia local en artifacts/portable-verification.json.

## Validación y límites

En esta entrega se ejecutó la suite de **88 casos**, incluidos los 82 anteriores y seis casos de identidad/bloqueo del auxiliar. La publicación se produjo con SDK 10.0.404 y runtime .NET/Windows Desktop 10.0.15. Se comprobó el arranque real del EXE publicado en Windows 11 x64, su token no elevado, carga de WPF y WMI, diagnóstico de solo lectura y simulación del plan de energía. También se copió el paquete completo a otra carpeta con espacios y se verificó su arranque, logo y navegación. El equipo tenía un único plan, ya activo: no se aplicó un cambio real de energía ni se crearon puntos de restauración.

Se probó desde la interfaz publicada una copia de dos archivos sintéticos (118 bytes) en artifacts/portable-smoke: CompleteVerified, dos hashes, cero fallidos/omitidos/pendientes. Se volvió a abrir desde Restaurar, se verificaron hashes y se generó el preview de cuatro rutas (dos directorios y dos archivos), sin conflictos. No se restauraron archivos en el perfil real; las restauraciones efectivas, conflictos y cancelación se verifican mediante tests en directorios temporales aislados.

No se ha probado esta entrega en un Windows limpio sin SDK/runtime instalados, en Windows 10, desde un USB físico ni en una VM con UAC aprobado y cambio real de energía. El carácter autocontenido se verifica por configuración, contenido publicado y arranque del bundle; esa evidencia no reemplaza la matriz de máquinas limpias. Tampoco se garantiza reputación SmartScreen de ejecutables sin firma.

Antes de distribución a clientes: comprobar doble clic en VMs limpias Windows 10/11 compatibles con .NET 10 x64, con cuenta estándar, ruta con espacios, pendrive/solo lectura, destinos de respaldo USB/UNC y desconexión controlada. Probar cancelar UAC; luego aplicar/revertir una alternativa existente únicamente en VM y comprobar ProgramData. Confirmar persistencia de datos después de cerrar, quitar el pendrive y limpiar solo la caché nativa de prueba. Seguir los procedimientos de SMART_BACKUP_TESTING.md para datos y restauración.

Referencia de empaquetado: [Single-file deployment de Microsoft](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview).
