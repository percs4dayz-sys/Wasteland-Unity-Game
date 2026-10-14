# Wastelanders (Unreal Engine 5)

Unreal Engine 5 port of the Unity game in `../Assets`, rebuilt as a server-authoritative open world
MMORPG. Code, systems and data are ported; the Unity art is not (new art direction).

- `Docs/ConversionPlan.md` - architecture, Unity-to-UE mapping, phases, how to verify in the editor
- `Docs/PortingConventions.md` - rules every ported system follows (read before adding code)
- `Source/Wastelanders` - game module; `Source/WastelandersEditor` - editor tools (data import)
- `Content/Data` - DataTable JSON generated from the Unity scripts by `Tools/UnityDataExport`

Quick start: generate project files from `Wastelanders.uproject`, build `WastelandersEditor`, open the
editor and run Tools > Wastelanders > Import Game Data.
