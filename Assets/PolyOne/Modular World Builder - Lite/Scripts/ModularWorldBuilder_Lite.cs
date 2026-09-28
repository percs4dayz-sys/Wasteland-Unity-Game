#if UNITY_EDITOR   // editor-only tool living outside an Editor folder: keep it out of player builds
using UnityEditor;
using UnityEngine;

namespace PolyOne.ModularWorldBuilder
{
    public class ModularWorldBuilderLite : EditorWindow
    {
        // =========================
        // UI STYLE
        // =========================
        static class UI
        {
            public static GUIStyle H1, H2, Sub, Version;
            public static GUIStyle BtnNormal, BtnSelected;
            public static GUIStyle Panel;

            static Texture2D Tex(Color c)
            {
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }

            public static void Init()
            {
                if (H1 != null) return;

                Color liteBlue    = new Color(0.32f, 0.62f, 1.00f);
                Color liteBlueSub = new Color(0.45f, 0.70f, 1.00f);
                Color bgDarkSoft  = new Color(0.18f, 0.18f, 0.18f);

                H1 = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 20,
                    normal = { textColor = liteBlue }
                };

                Version = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = liteBlueSub }
                };

                H2 = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    normal = { textColor = liteBlue }
                };

                Sub = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = liteBlueSub }
                };

                Panel = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 10, 10)
                };

                BtnNormal = new GUIStyle(GUI.skin.button)
                {
                    normal =
                    {
                        textColor = Color.gray,
                        background = Tex(bgDarkSoft)
                    }
                };

                BtnSelected = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold,
                    normal =
                    {
                        textColor = Color.white,
                        background = Tex(liteBlue)
                    }
                };
            }
        }

        // =========================
        // DATA
        // =========================
        PrefabCollection collection;
        int selectedPrefabIndex;

        bool isBuilding;
        bool isPaintMode;

        float manualScale = 1f;
        Quaternion currentRotation = Quaternion.identity;

        float heightOffset = 0f;
        float paintSpacing = 1.2f;

        Transform hierarchyGroup;

        GameObject preview;
        Vector3 lastPaintPos;
        bool isPainting;

        // ✅ MenuItem FIX
        [MenuItem("Tools/PolyOne/Modular World Builder (Lite)")]
        static void Open()
        {
            GetWindow<ModularWorldBuilderLite>(
                "POLYONE - Modular World Builder (Lite)");
        }

        // =========================
        // GUI
        // =========================
        void OnGUI()
        {
            UI.Init();

            GUILayout.BeginVertical(UI.Panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("POLYONE – Modular World Builder", UI.H1);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Lite v1.1", UI.Version);
            GUILayout.EndHorizontal();
            GUILayout.Label("Manual-first world placement tool", UI.Sub);
            GUILayout.EndVertical();

            GUILayout.Space(10);

            GUILayout.Label("BUILD MODE", UI.H2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Place", !isPaintMode ? UI.BtnSelected : UI.BtnNormal))
                isPaintMode = false;
            if (GUILayout.Button("Paint", isPaintMode ? UI.BtnSelected : UI.BtnNormal))
                isPaintMode = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.Label("CONTENT", UI.H2);
            collection = (PrefabCollection)EditorGUILayout.ObjectField(
                "Prefab Collection", collection,
                typeof(PrefabCollection), false);

            if (collection != null)
                DrawPrefabGrid();

            GUILayout.Space(10);

            GUILayout.Label("PLACEMENT", UI.H2);
            heightOffset = EditorGUILayout.Slider(
                "Height Offset", heightOffset, -2f, 2f);

            if (isPaintMode)
                paintSpacing = EditorGUILayout.Slider(
                    "Paint Spacing", paintSpacing, 0.3f, 5f);

            GUILayout.Space(10);

            GUILayout.Label("ORGANIZATION", UI.H2);
            hierarchyGroup = (Transform)EditorGUILayout.ObjectField(
                "Hierarchy Group", hierarchyGroup,
                typeof(Transform), true);

            GUILayout.Space(12);

            bool canBuild =
                collection != null &&
                collection.prefabs != null &&
                collection.prefabs.Count > 0;

            EditorGUI.BeginDisabledGroup(!canBuild);

            if (!isBuilding)
            {
                if (GUILayout.Button("START BUILD", GUILayout.Height(30)))
                {
                    isBuilding = true;
                    SceneView.duringSceneGui += OnSceneGUI;
                    CreatePreview();
                }
            }
            else
            {
                if (GUILayout.Button("STOP BUILD", GUILayout.Height(30)))
                {
                    SceneView.duringSceneGui -= OnSceneGUI;
                    DestroyPreview();
                    isBuilding = false;
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        // =========================
        // PREFAB GRID
        // =========================
        void DrawPrefabGrid()
        {
            selectedPrefabIndex =
                Mathf.Clamp(selectedPrefabIndex, 0, collection.prefabs.Count - 1);

            int cols = 4;
            for (int i = 0; i < collection.prefabs.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = 0; j < cols && i + j < collection.prefabs.Count; j++)
                {
                    int idx = i + j;
                    Texture2D t =
                        AssetPreview.GetAssetPreview(collection.prefabs[idx]) ??
                        AssetPreview.GetMiniThumbnail(collection.prefabs[idx]);

                    GUIStyle s =
                        idx == selectedPrefabIndex ? UI.BtnSelected : UI.BtnNormal;

                    if (GUILayout.Button(t, s, GUILayout.Width(70), GUILayout.Height(70)))
                    {
                        selectedPrefabIndex = idx;
                        if (isBuilding) CreatePreview();
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // =========================
        // SCENE GUI (Snap + Paint đúng)
        // =========================
        void OnSceneGUI(SceneView view)
        {
            if (!isBuilding || preview == null) return;

            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit)) return;

            // ✅ SNAP theo mặt va chạm
            preview.transform.position =
                hit.point + hit.normal * heightOffset;

            preview.transform.rotation = currentRotation;
            preview.transform.localScale = Vector3.one * manualScale;

            // Manual transform
            if (e.alt && e.type == EventType.ScrollWheel)
            {
                manualScale = Mathf.Max(0.1f, manualScale - e.delta.y * 0.05f);
                e.Use();
            }

            if (e.alt && e.type == EventType.MouseDrag && e.button == 2)
            {
                currentRotation *= Quaternion.Euler(0, e.delta.x * 1.5f, 0);
                e.Use();
            }

            // Place
            if (!isPaintMode)
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    Place(hit);
                    e.Use();
                }
                view.Repaint();
                return;
            }

            // Paint
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isPainting = true;
                lastPaintPos = hit.point;
                e.Use();
            }

            if (isPainting &&
                e.type == EventType.MouseDrag &&
                Vector3.Distance(hit.point, lastPaintPos) >= paintSpacing)
            {
                lastPaintPos = hit.point;
                Place(hit);
                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                isPainting = false;
                e.Use();
            }

            view.Repaint();
        }

        // =========================
        // PLACE
        // =========================
        void Place(RaycastHit hit)
        {
            GameObject prefab = collection.prefabs[selectedPrefabIndex];
            GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(obj, "Place Prefab");

            obj.transform.position =
                hit.point + hit.normal * heightOffset;

            obj.transform.rotation = currentRotation;
            obj.transform.localScale = Vector3.one * manualScale;

            if (hierarchyGroup)
                obj.transform.SetParent(hierarchyGroup);
        }

        // =========================
        // PREVIEW
        // =========================
        void CreatePreview()
        {
            DestroyPreview();

            preview = (GameObject)PrefabUtility.InstantiatePrefab(
                collection.prefabs[selectedPrefabIndex]);

            DisableColliders(preview);
            manualScale = 1f;
            currentRotation = Quaternion.identity;
        }

        void DestroyPreview()
        {
            if (preview)
                DestroyImmediate(preview);
            preview = null;
        }

        void DisableColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>())
                c.enabled = false;
        }
    }
}

#endif
