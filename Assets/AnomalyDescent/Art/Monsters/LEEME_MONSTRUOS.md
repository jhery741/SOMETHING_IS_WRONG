# Monstruos — integración de los modelos de Meshy

Origen: modelos generados por el autor del proyecto con Meshy AI (no son recursos CC0 de terceros). Exportaciones sin modificar en `Meshy_Export/`.

## Separación gameplay / visual
- Raíz de gameplay en cada escena (`ElCiego`, `ElTorcido`, `LaPresencia`): `CharacterController` + controlador (IA, oído/vista, daño, puertas, escondites). No contiene mallas.
- Visual: campo **Visual Prefab** y **Animator Controller** del Inspector del controlador (sección «Visual (Meshy) — vacío = figura PROVISIONAL»). Se instancia como hijo `Visual`, sin colisionadores ni root motion. Si está vacío aparece la figura `PROVISIONAL_<nombre>`.
- Para sustituir un modelo: copiar la nueva exportación en `Art/Monsters/<Monstruo>/Meshy_Export/` y ejecutar *ANOMALY DESCENT › Monstruos › Integrar modelo Meshy · Piso N* (y *Asignar prefabs visuales existentes a la escena abierta* en cada escena). No se toca la escena ni la lógica.

## Estado por monstruo (comprobado en Unity 18-10-2026)

| | El Ciego (Piso 1) | El Torcido (Piso 2) | La Presencia (Piso 3) |
|---|---|---|---|
| FBX | `..._the_listener_charcoal_biped_Meshy_Merged_Animations.fbx` | `..._Freddy_Monster_Rigged_biped_...fbx` | `..._orange_creature_rigge_biped_...fbx` |
| Esqueleto | mixamorig, 22 huesos + headfront | mixamorig, sin LeftToeBase | mixamorig completo |
| Rig elegido | **Humanoid** (Avatar válido) | **Generic** (clip a gatas; retarget humanoide deformaría) | **Humanoid** (Avatar válido) |
| Triángulos / altura | 31 003 · 2.20→2.15 m | 31 182 · 1.70→1.85 m | 31 239 · 2.10→2.20 m |
| Orientación | headfront +Z, sin giro | +Z, sin giro | +Z, sin giro |
| Texturas | albedo, normal, rugosidad, metal | albedo, normal, rugosidad, metal | albedo, rugosidad, metal (**sin normal**) |
| Clips presentes | Listening_Gesture (idle/search), Walking, Running | Crawl_and_Look_Back (idle/search/awaken), Walking, Running | Walking, Running (reposo provisional = Walk ×0.15) |
| Clips que faltan | Awaken, Attack | Attack | Idle, Search, Awaken, Attack |
| Asignado en escena | Piso1 | Piso2 | Piso3 |
| Visto en Play | ✔ (captura) | ✔ (captura 18-10, fase 3) | ✔ (captura 18-10, fase 3) |

Parámetros del Animator (los que lee MonsterBase): Speed (float), Chasing, Searching (bool), Attack, Awaken (trigger).
Generado por la integración: `Materials/M_<Monstruo>.mat`, `Animator/<Id>_Animator.controller`, `Prefabs/<Id>_Visual.prefab`.
