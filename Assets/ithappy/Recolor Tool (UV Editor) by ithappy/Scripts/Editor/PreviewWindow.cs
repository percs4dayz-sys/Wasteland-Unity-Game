using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ithappy.RecolorToolWindow
{
    public class PreviewWindow : EditorWindow
    {
        private PreviewRenderer _previewRenderer;
        private GameObject _currentPrefab;
        private RecolorToolWindow _parentWindow;
        
        private Rect _lastPreviewRect;
        private bool _needsRepaint = true;
        private Vector2 _mouseDownPos;
        private bool _isDragging;
        
        public static PreviewWindow ShowWindow(RecolorToolWindow parent)
        {
            PreviewWindow window = GetWindow<PreviewWindow>("Preview");
            window._parentWindow = parent;
            window.minSize = new Vector2(300, 300);
            window.Show();
            return window;
        }
        
        private void OnEnable()
        {
            if (_previewRenderer == null)
                _previewRenderer = new PreviewRenderer();
            
            EditorApplication.update += OnEditorUpdate;
        }
        
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (_previewRenderer != null)
            {
                _previewRenderer.RemoveMeshColliders();
                _previewRenderer.Cleanup();
                _previewRenderer = null;
            }
        }
        
        private void OnEditorUpdate()
        {
            if (this == null) return;
            
            if (_parentWindow != null && _parentWindow.hasFocus && _needsRepaint)
            {
                Repaint();
            }
        }
        
        public void SetPrefab(GameObject prefab)
        {
            if (_currentPrefab == prefab) return;
    
            _currentPrefab = prefab;
            if (_previewRenderer != null)
            {
                _previewRenderer.Initialize(prefab);
            }
            _needsRepaint = true;
            Repaint();
        }
        
        public void UpdateMeshComponent(MeshResolver.MeshComponentInfo meshInfo, Mesh workingMesh)
        {
            if (_previewRenderer != null && meshInfo != null && workingMesh != null)
            {
                _previewRenderer.UpdateMeshComponent(meshInfo, workingMesh);
                _needsRepaint = true;
            }
        }
        
        public void RefreshPreviewMaterials(Material material)
        {
            if (_previewRenderer != null)
            {
                _previewRenderer.RefreshMaterials(material);
                _needsRepaint = true;
            }
        }
        
        public bool IsShowingPrefab(GameObject prefab)
        {
            return _currentPrefab == prefab;
        }
        
        public void SetMeshVisibility(MeshResolver.MeshComponentInfo meshInfo, bool visible)
        {
            if (_previewRenderer != null)
            {
                _previewRenderer.SetMeshVisibility(meshInfo, visible);
                _needsRepaint = true;
            }
        }
        
        public void SetMeshColliderEnabled(string targetName, bool enabled)
        {
            _previewRenderer?.SetMeshColliderEnabled(targetName, enabled);
        }
        
        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                _previewRenderer?.ResetView();
                _needsRepaint = true;
            }
            
            GUILayout.Space(5);
            EditorGUILayout.LabelField("Drag to rotate • Scroll to zoom", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            
            if (_currentPrefab != null)
            {
                GUILayout.Label(_currentPrefab.name, EditorStyles.toolbarButton);
            }
            
            EditorGUILayout.EndHorizontal();
            
            Rect previewRect = new Rect(
                2,
                EditorGUIUtility.singleLineHeight + 4,
                position.width - 4,
                position.height - EditorGUIUtility.singleLineHeight - 8
            );
            
            if (_lastPreviewRect != previewRect)
            {
                _lastPreviewRect = previewRect;
                _needsRepaint = true;
            }
            
            if (_previewRenderer != null && _previewRenderer.IsInitialized)
            {
                _previewRenderer.Render(previewRect);
            }
            else
            {
                EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f, 1f));
                GUI.Label(previewRect, "No preview available\nDrop a prefab to preview", 
                    new GUIStyle(GUI.skin.label) 
                    { 
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap = true,
                        fontSize = 12
                    });
            }
            
            HandleInput(previewRect);
        }
        
        public void AddMeshColliders(List<MeshResolver.MeshComponentInfo> meshComponents)
        {
            _previewRenderer?.AddMeshColliders(meshComponents);
        }

        private void HandleInput(Rect previewRect)
        {
            Event e = Event.current;

            if (!previewRect.Contains(e.mousePosition))
                return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        _mouseDownPos = e.mousePosition;
                        _isDragging = false;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (e.button == 0 || e.button == 1)
                    {
                        if (Vector2.Distance(e.mousePosition, _mouseDownPos) > 3f)
                            _isDragging = true;

                        if (_isDragging)
                        {
                            _previewRenderer?.HandleDrag(e.delta);
                            _needsRepaint = true;
                            e.Use();
                            Repaint();
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0 && !_isDragging)
                    {
                        if (_previewRenderer != null)
                        {
                            int result = _previewRenderer.RaycastMesh(e.mousePosition, previewRect, out string meshName, out int tri, out int subMesh);
                            if (!string.IsNullOrEmpty(meshName))
                            {
                                _parentWindow.SelectMeshFromPreview(meshName, subMesh);
                                e.Use();
                            }
                        }
                    }
                    break;

                case EventType.ScrollWheel:
                    _previewRenderer?.HandleScroll(e.delta.y);
                    _needsRepaint = true;
                    e.Use();
                    Repaint();
                    break;
            }
        }
        
        public void ClearPreview()
        {
            if (_previewRenderer != null)
            {
                _previewRenderer.Cleanup();
            }
            _currentPrefab = null;
            Repaint();
        }
    }
}