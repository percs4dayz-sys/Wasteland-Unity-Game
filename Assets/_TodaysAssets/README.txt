_TodaysAssets — everything generated on 2026-07-03
====================================================

Drag any FBX from Models/ or SplitModels/ straight into a scene.

Models/            8 single models, ready to drop in:
                     AK-Stylized, Buzz-Saw-Axe, Luger-P08, Red-Zero-Katana,
                     Scifi-SMG, WW2-Thompson (weapons), Borderlands-Character,
                     Dragonhead-Galleon (heavy, 682k tris)

SplitModels/       the 5 "collection" files split into individual pieces:
                     Clockwork-Grove          (4 trees)
                     Bloom-and-Bush           (12: 6 flowers + 6 bushes)
                     The-Augmented-Collective (10 figures)
                     Clockwork-Garden         (11 plants — also used as world foliage)
                     Uploaded-Youre-Welcome   (1 character, T-posed)

Textures/          extracted PBR maps (BaseColor / Normal / MetallicRoughness /
                   Emission) for every source file, one folder each.
                   Models auto-pull their BaseColor from here on import.

NOT in this folder (they have to live elsewhere):
  * World foliage plants  -> Assets/Resources/Foliage/  (the Fill World With
        Foliage tool loads them from Resources; scatter via Wasteland > World)
  * Toon shader           -> Assets/Toon Shader FX/Shaders/ToonURP.shader
        (material shader name: "Wasteland/Toon (URP)")

Still to do by hand: Rusted-Armory-Workshop (packed machines, needs manual split).
