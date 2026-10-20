# Animaciones separadas de El Ciego

Archivos aportados por el usuario el 18/10/2026 desde Downloads/Animations_Monster.
Origen: exportaciones Meshy del personaje existente «the_listener_charcoal_biped».
No se sustituyó la malla, materiales, escala ni prefab visual existente.

| Toma suministrada | Función en Animator |
|---|---|
| Idle_9 | Meshy_Idle, reposo en BlendTree |
| Walking | Meshy_Walk, 1,4 m/s en BlendTree |
| Running | Meshy_Run, 3,6 m/s en BlendTree |
| Listening_Gesture | Meshy_Search, estado Buscar |
| Stand_Up4 | Meshy_Awaken, estado Despertar; ajustado a 3 s |
| Right_Hand_Sword_Slash | Meshy_Attack, estado Atacar; sin añadir arma |
| Walk_Backward | Meshy_Retreat, estado Retirarse |

Las nuevas exportaciones usan Hips/Spine02/Spine01/Spine y el modelo original usa mixamorig:Hips/Spine/Spine1/Spine2. Cada exportación tiene su propio Avatar humanoide para adaptar el movimiento al Avatar original. La raíz queda bloqueada y el Animator no aplica root motion. Las tomas Awaken y Attack no se repiten; las otras usan bucle. Los clips PROV_* se retiraron del proyecto; la copia de recuperación está fuera de la carpeta del juego.

El Animator utiliza los siete clips definitivos. Las herramientas de integración y pruebas de la etapa anterior se retiraron tras la entrega.

Los FBX son contenido Meshy aportado por el usuario; no se etiquetan como CC0. Conservar las condiciones de uso de la cuenta/exportación original.

