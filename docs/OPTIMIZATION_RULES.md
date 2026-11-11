# Reglas de optimización

Único módulo habilitado: windows.power-plan. Disponible para Windows 10/11 workstation con compilación mínima 10240, API legible y planes original/destino existentes. No se asume una edición concreta ni GUID estándar. Si no puede confirmarse ProductType/Windows, se bloquea. No hay dependencias en otras optimizaciones. Riesgo moderado por consumo y temperatura; requiere elevación, no reinicio. Un destino igual al original es idempotente, sin escritura ni registro de cambio ficticio.

Una alternativa no implica una recomendación. Esta versión muestra cero recomendaciones automáticas y cuenta por separado alternativas disponibles. No clasifica planes por rendimiento a partir de sus nombres, que pueden ser personalizados.

Todo módulo futuro debe implementar IOptimization: identidad estable, descripción, categoría, riesgo, compatibilidad explicable, requisitos, lectura, preview obligatorio, backup, apply, verify y rollback. La simulación no modifica ni solicita elevación. La ejecución no debe confiar en los flags del preview: revalidar entradas y estado dentro del límite privilegiado. Registrar el original antes del primer cambio y demostrar que existe restauración exacta. Definir versión/edición/hardware/dependencias, comportamiento idempotente, conflictos externos, compensación y recuperación tras interrupciones.

Agregar pruebas con APIs simuladas, documentación y una acción tipada específica al protocolo. Nunca extender el runner con scripts o comandos generales. No incorporar módulos de servicios, limpieza o inicio durante esta fase.
