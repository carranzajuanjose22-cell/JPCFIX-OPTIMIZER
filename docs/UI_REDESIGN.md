# Rediseño WPF de JPCFIX Optimizer

## Alcance

Se reemplazó la presentación de la aplicación con controles nativos WPF. La referencia guía la distribución y la identidad visual: no se utiliza como fondo ni se incrustan datos de ejemplo. No se añadieron WebView2, React, Electron, paquetes de UI externos ni funciones decorativas sin implementar.

Los motores en Core, Application e Infrastructure, el ejecutor UAC, sus reglas, los registros persistentes y el protocolo privilegiado permanecen iguales. Las adaptaciones de MainViewModel agregan proyecciones de lectura y notificaciones de propiedades. BackupViewModel solo comparte la construcción visual de su confirmación; sus comandos y flujo de copia/restauración no cambian.

## Sistema visual y estructura

Styles.xaml centraliza la paleta solicitada, tipografía Segoe UI, tarjetas, botones primarios/secundarios/peligrosos, campos, selectores, tablas, pestañas del asistente, badges, avisos, estados vacíos, progreso, modales, foco y desplazamiento. Se usan superficies planas, bordes tenues y esquinas moderadas. El verde se reserva a resultados Applied/Restored del historial; la disponibilidad de un ajuste nunca se pinta como éxito.

MainWindow contiene encabezado, navegación lateral y área de contenido. La barra lateral mide 240 DIP en ventanas amplias y 196 DIP en ventanas compactas. El pie muestra la versión real del ensamblado, no la versión ilustrativa del mockup. No hay botones ficticios de ayuda o notificaciones.

NavigationEntry conserva los índices existentes: Inicio 0, Diagnóstico 1, Optimizaciones 2, Historial 3, Smart Backup 5, Configuración 4. El orden visual coloca Backup antes de Configuración. Un ContentControl con PageTemplateSelector presenta la página elegida y la expone al teclado y automatización; las plantillas enlazan explícitamente con el ViewModel de la ventana.

UiControls proporciona una familia de iconos de trazos vectoriales, MetricCard y AdaptivePanel. Este último distribuye tarjetas en cuatro, dos o una columna según el ancho disponible; los paneles secundarios pasan de dos columnas a una. Las tablas tienen sus propios viewports de altura mínima y desplazamiento horizontal/vertical. Las páginas cortas no necesitan estirar controles para llenar toda la pantalla.

## Logo y escalado

Se conserva intacto Assets/Jpcfix-logo.png suministrado por el usuario. El encabezado muestra su región útil con proporciones constantes y una presentación monocroma blanca mediante BrandLogoEffect, integrado en WPF. No hay recuadro blanco ni recreación de letras por fuentes aproximadas. El efecto únicamente convierte el fondo del PNG en transparencia y la silueta original en blanco al dibujar; no toca el archivo original.

Assets/BrandLogo.hlsl y su pequeño recurso precompilado BrandLogo.ps corresponden a Shader Model 2.0. WPF también permite ejecutar PS 2.0 por software; no se instala una librería gráfica adicional. compile-logo-shader.ps1 permite regenerar el recurso utilizando el compilador de Windows durante desarrollo; el usuario final recibe el recurso dentro del EXE y no ejecuta ese script. Las variantes raster generadas durante exploración no forman parte del producto.

Se declara PerMonitorV2 con fallback PerMonitor en el manifiesto de Desktop, manteniendo asInvoker. UseLayoutRounding y medidas DIP acompañan el redimensionamiento y el escalado nativo. La ventana conserva minimizar, maximizar, cerrar y el marco estándar de Windows. El tamaño inicial se limita al área de trabajo disponible; el contenido tiene scroll en ventanas pequeñas. No se cambia la configuración de pantalla del usuario.

## Datos y estados reales

DashboardData transforma el DiagnosticReport existente sin consultar proveedores nuevos. Muestra carga de CPU cuando se obtuvo, memoria instalada y espacio libre del volumen del sistema; no atribuye un SSD físico a una letra de unidad ni calcula una puntuación de salud. Ante datos ausentes conserva «No disponible», «Sin datos» o «Sin análisis». Un diagnóstico leído sin advertencias no se describe como equipo optimizado o saludable.

Detalles del sistema utiliza edición/versión de Windows, nombre de CPU, RAM e información de volumen del reporte. La actividad reciente toma los últimos tres ChangeRecord reales, ordenados por fecha. El estado de compatibilidad del ajuste proviene de la previsualización existente; antes de ella se presenta el texto real de alternativas/recomendaciones. No se crean eventos por mostrar el dashboard.

Diagnóstico agrupa las filas por Category y permite actualizar con ScanCommand. Optimizaciones conserva selección de planes, refresh, dry run y confirmación. Historial mantiene identificadores originales, resultados, selección y RollbackCommand. SettingsView conserva reportes y puntos de restauración. BackupView conserva ambas rutas de trabajo y todos sus comandos, categorías, resumen, destino, progreso, conflictos e informes.

AppDialog unifica únicamente la presentación de confirmaciones de Desktop: Confirmar es una acción explícita; Cancelar recibe el foco y Escape cancela. El ejecutor privilegiado conserva su confirmación independiente. Los estados de habilitación se alimentan de los ICommand existentes.

## Archivos principales

- MainWindow.xaml/.cs: shell y ajuste del tamaño inicial.
- Styles.xaml: recursos y plantillas comunes.
- DashboardView, DiagnosticsView, OptimizationView, HistoryView y SettingsView: vistas independientes, con constructores en PageViews.xaml.cs.
- BackupView.xaml: identidad visual del asistente existente.
- UiControls.cs, UiPresentation.cs y PageTemplateSelector.cs: controles, proyecciones y selección visual.
- MainViewModel.cs: navegación, dashboard e indicadores de lectura.
- AppDialog.cs y el delegado BackupDialog: confirmaciones visuales compartidas.
- BrandLogoEffect.cs, Assets/BrandLogo.hlsl/.ps y compile-logo-shader.ps1: presentación del logo original.
- app.manifest: DPI por monitor, sin alterar el nivel de privilegio.
- DesktopSmokeTests.cs y DashboardPresentationTests.cs: navegación, layout, selectores, comandos y datos ausentes/reales.

## Validación

La suite contiene 93 casos: se conservan las regresiones de energía, copia, restauración y distribución, y se agregan cinco comprobaciones de presentación. La prueba STA construye las seis vistas, verifica índices de navegación y que los botones tengan comandos, comprueba la plantilla de DisplayMemberPath y mide el contenido con viewports DIP equivalentes a 1366×768 y 1920×1080 al 125%, 150% y 200%. Es una simulación de espacio de layout; no equivale a probar físicamente todos los monitores/DPI.

La revisión visual del EXE publicado se realiza con cuenta sin elevar, en ventana normal y maximizada sobre Windows 11. Se verifican dashboard vacío, actualización mediante diagnóstico real, tablas y simulación de energía, navegación, historial vacío, ajustes y pantallas de Smart Backup/restauración. No se ejecutan cambios reales de energía, puntos de restauración ni restauraciones sobre documentos personales para validar el diseño.

Pendiente de laboratorio: mover la ventana entre monitores con distintas escalas nativas, lector de pantalla externo y matriz Windows 10/11 limpia. Revisar 1366×768 y 1920×1080 con 125/150/200% real, foco mediante Tab y flechas, rutas largas, muchos errores, cancelación de modales, datos voluminosos y actualización de un backup sintético. Los motores se cubren por sus tests aislados; no se anuncian pruebas físicas que no se realizaron.

Publicación: scripts/publish.ps1 genera dist/JPCFIX-Optimizer-Portable/JPCFIX-Optimizer.exe con Runner al lado en su subcarpeta. Copiar toda la carpeta portable. No se realizan commits ni push.

Referencias: [DPI por manifiesto](https://learn.microsoft.com/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process), [ShaderEffect de WPF](https://learn.microsoft.com/dotnet/api/system.windows.media.effects.shadereffect?view=windowsdesktop-10.0).
