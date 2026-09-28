using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace ithappy.RecolorToolWindow
{
    public class RecolorToolWindow : EditorWindow
    {
        public enum SelectionMode
        {
            Rect,
            Lasso
        }

        private const float MIN_ZOOM = 0.1f;
        private const float MAX_ZOOM = 5f;
        private const float MAX_CLICK_DISTANCE = 5;

        private UVEditorMeshData _uvEditorMeshData;
        private UVTransform _uvTransform;
        private ComponentsManager _componentsManager;
        private GizmoManager _gizmoManager;

        private float _zoomValue = 1.0f;
        private float _previousZoom;

        private Vector2 _anchor = Vector2.zero;
        private Vector2 _canvasMousePos = Vector2.zero;
        private Vector2 _textureMousePos = Vector2.zero;

        private Vector2 _uvMousePos = Vector2.zero;

        private bool _activeRectDrag;
        private Vector2 _activeRectStart;
        private Vector2 _activeRectEnd;
        private Vector2 _cacheWindowSize;

        private VisualElement _cursorTargetElement;
        private UnityEngine.UIElements.Cursor? _nextCursorOnMouseUp = null;

        private Vector2 _leftMouseDownPosition;

        private HashSet<int> _activeVerticesId = new HashSet<int>();
        private Dictionary<int, bool[]> _cacheActiveVertexesIndex = new();

        private bool _activeLassoDrag;
        private List<Vector2> _lassoPoints = new List<Vector2>();
        private bool _lassoSelectionActive;

        private SelectionMode _currentSelectionMode = SelectionMode.Rect;
        private PreviewWindow _previewWindow;

        private MeshVisibilityManager _meshVisibilityManager = new MeshVisibilityManager();

        private Vector2? _copiedUVCenter;
        private Material _masterMaterial;

        private bool _needsRefresh = true;
        private float _lastVertexSize = -1f;
        private int _lastDrawnCount = -1;
        private int _lastVertexCount = -1;

        [MenuItem("Tools/ithappy/Recolor Tool (UV Editor) by ithappy")]
        public static void ShowWindow()
        {
            RecolorToolWindow wnd = GetWindow<RecolorToolWindow>();
            wnd.titleContent = new GUIContent("Recolor Tool");
        }

        public void Update()
        {
            if (_uvEditorMeshData == null)
                return;

            var size = new Vector2(position.width, position.height);
            if (_cacheWindowSize != size)
            {
                _cacheWindowSize = size;
                _uvEditorMeshData.UVIsDirty = true;
            }
        }

        private void OnGUI()
        {
            if (focusedWindow == this && _uvTransform != null && _uvEditorMeshData != null)
            {
                Event e = Event.current;

                if (e != null && e.type == EventType.KeyDown)
                {
                    e.Use();
                    if (e.control && e.keyCode == KeyCode.Z && !e.shift)
                    {
                        _activeVerticesId.Clear();
                        _cacheActiveVertexesIndex.Clear();
                        OnActiveVerticesChanged();

                        _uvTransform.Undo();
                        _uvEditorMeshData.UVIsDirty = true;
                        RefreshContent();
                    }
                    else if (e.control && e.keyCode == KeyCode.Y)
                    {
                        _activeVerticesId.Clear();
                        _cacheActiveVertexesIndex.Clear();
                        OnActiveVerticesChanged();

                        _uvTransform.Redo();
                        _uvEditorMeshData.UVIsDirty = true;
                        RefreshContent();
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_previewWindow != null)
                _previewWindow.Close();

            if (_uvEditorMeshData != null)
            {
                _uvEditorMeshData.ResetUnsaveChanges();
                _uvEditorMeshData.Dispose();
            }
        }

        public void CreateGUI()
        {
            _componentsManager = new ComponentsManager(rootVisualElement);

            _componentsManager.Root.RegisterCallback<MouseEnterEvent>(evt =>
            {
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Repaint();
            });

            _componentsManager.Root.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Repaint();
            });

            _componentsManager.ObjectField.RegisterValueChangedCallback((e) => { GetObjectData(e.newValue); });

            _componentsManager.SubMeshField.RegisterValueChangedCallback((e) =>
            {
                if (_uvEditorMeshData == null) return;

                int index = _componentsManager.SubMeshField.index;

                if (index == 0)
                    _uvEditorMeshData.CurrentSubMeshIndex = -1;
                else
                    _uvEditorMeshData.CurrentSubMeshIndex = index - 1;

                ClearActiveVertices();
                _cacheActiveVertexesIndex.Clear();
                _uvEditorMeshData.UVIsDirty = true;
                _uvEditorMeshData.VerticesMoved = true;
            });

            _componentsManager.UVField.RegisterValueChangedCallback((e) =>
            {
                if (_uvEditorMeshData == null)
                    return;

                _uvEditorMeshData.CurrentUVChannel = int.Parse(e.newValue.Replace("UV", ""));
                _uvEditorMeshData.UVIsDirty = true;
                _uvEditorMeshData.VerticesMoved = true;
            });

            _componentsManager.MeshComponentField.RegisterValueChangedCallback((e) =>
            {
                if (_uvEditorMeshData == null)
                    return;

                int newIndex = _componentsManager.MeshComponentField.index;
                if (newIndex >= 0)
                {
                    SwitchMeshComponent(newIndex);
                    _uvEditorMeshData.VerticesMoved = true;
                }
            });

            _componentsManager.CenterButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (_uvEditorMeshData == null)
                    return;

                Recenter();
                _uvEditorMeshData.UVIsDirty = true;
            });

            _componentsManager.FocusButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (_uvEditorMeshData == null)
                    return;

                FocusSelection();
                _uvEditorMeshData.UVIsDirty = true;
            });

            _componentsManager.ColorButton.RegisterCallback<ClickEvent>((e) =>
            {
                _componentsManager.ChangeLinesColor();

                if (_uvEditorMeshData == null)
                    return;

                _uvEditorMeshData.UVIsDirty = true;
                _uvEditorMeshData.VerticesMoved = true;
            });

            _componentsManager.RectSelectButton.RegisterCallback<ClickEvent>((e) =>
            {
                _currentSelectionMode = SelectionMode.Rect;
                _componentsManager.SetSelectionModeActive(_currentSelectionMode);
            });

            _componentsManager.LassoSelectButton.RegisterCallback<ClickEvent>((e) =>
            {
                _currentSelectionMode = SelectionMode.Lasso;
                _componentsManager.SetSelectionModeActive(_currentSelectionMode);
            });

            _componentsManager.AssetViewerButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (_previewWindow == null)
                {
                    _previewWindow = PreviewWindow.ShowWindow(this);

                    var currentPrefab = _componentsManager.ObjectField.value as GameObject;
                    if (currentPrefab != null && _uvEditorMeshData != null)
                    {
                        _previewWindow.SetPrefab(currentPrefab);

                        var result = MeshResolver.Resolve(currentPrefab);
                        for (int i = 0; i < result.AllMeshComponents.Count; i++)
                        {
                            var workingMesh = _uvEditorMeshData.GetWorkingMesh(i);
                            if (workingMesh != null)
                            {
                                _previewWindow.UpdateMeshComponent(result.AllMeshComponents[i], workingMesh);
                            }
                        }
                    }
                }
                else
                {
                    _previewWindow.Close();
                    _previewWindow = null;
                }
            });

            _componentsManager.MasterMaterialButton.RegisterCallback<ClickEvent>((e) =>
            {
                ApplyMasterMaterialToAllMeshes();
            });

            _componentsManager.AllAssetsButton.RegisterCallback<ClickEvent>((e) =>
            {
                Application.OpenURL("https://ithappystudios.com/store");
            });

            _componentsManager.SupportButton.RegisterCallback<ClickEvent>((e) =>
            {
                Application.OpenURL("https://discord.com/invite/jxrQM8hXnd");
            });

            _componentsManager.TestAssetButton.RegisterCallback<ClickEvent>((e) =>
            {
                string[] guids = AssetDatabase.FindAssets("TestModel-Tank t:Prefab");

                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                    if (prefab != null)
                    {
                        _componentsManager.ObjectField.SetValueWithoutNotify(prefab);
                        GetObjectData(prefab);
                    }
                    else
                    {
                        Debug.LogWarning("TestAssetButton: Prefab 'TestModel-Tank' not found");
                    }
                }
                else
                {
                    Debug.LogWarning("TestAssetButton: Prefab 'TestModel-Tank' not found");
                }
            });

            _componentsManager.VisibilityButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (_uvEditorMeshData == null)
                {
                    return;
                }

                _meshVisibilityManager.ShowGenericMenu(() =>
                {
                    _uvEditorMeshData.UVIsDirty = true;
                    RefreshContent();
                    UpdatePreviewVisibility();
                });
            });

            _componentsManager.SetSelectionModeActive(_currentSelectionMode);

            _componentsManager.ApplyButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (_uvEditorMeshData == null)
                    return;

                _uvEditorMeshData.SaveAssetChanges();

                var currentPrefab = _componentsManager.ObjectField.value as GameObject;
                if (currentPrefab != null && _previewWindow != null)
                {
                    _previewWindow.SetPrefab(currentPrefab);

                    var result = MeshResolver.Resolve(currentPrefab);
                    _previewWindow.AddMeshColliders(result.AllMeshComponents);
                    for (int i = 0; i < result.AllMeshComponents.Count; i++)
                    {
                        var workingMesh = _uvEditorMeshData.GetWorkingMesh(i);
                        if (workingMesh != null)
                        {
                            _previewWindow.UpdateMeshComponent(result.AllMeshComponents[i], workingMesh);
                        }
                    }

                    hasUnsavedChanges = false;
                }
            });

            _componentsManager.CopyButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (_uvEditorMeshData == null || _activeVerticesId.Count == 0)
                    return;

                _copiedUVCenter = _uvEditorMeshData.MarkedBoundsUV.center;
            });

            _componentsManager.PasteButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (_uvEditorMeshData == null || _activeVerticesId.Count == 0 || !_copiedUVCenter.HasValue)
                    return;

                _uvTransform.PasteUVCenter(new List<int>(_activeVerticesId), _copiedUVCenter.Value);
                _uvEditorMeshData.UVIsDirty = true;
                _uvEditorMeshData.VerticesMoved = true;
                RefreshContent();
            });

            _gizmoManager = new GizmoManager(_componentsManager.Canvas, _componentsManager.TextureContainer);

            if (_componentsManager.RotateGizmoSprite != null)
            {
                _gizmoManager.SetRotateGizmoSprite(_componentsManager.RotateGizmoSprite);
            }

            _cursorTargetElement = _gizmoManager.RectUVGizmoContainer;

            _componentsManager.Canvas.RegisterCallback<MouseDownEvent>(OnMouseDown);
            _componentsManager.Canvas.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            _componentsManager.Canvas.RegisterCallback<MouseUpEvent>(OnMouseUp);
            _componentsManager.Canvas.RegisterCallback<WheelEvent>(OnScroll);
            _componentsManager.Canvas.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            RefreshContent();

            _componentsManager.RootLayout.schedule.Execute(RefreshContent).Every(1000 / 60);

            _previewWindow = PreviewWindow.ShowWindow(this);

            saveChangesMessage = "If you close the tool, changes will be lost.\nSave now?";
            hasUnsavedChanges = false;
        }

        public void GetObjectData(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                CleanupAllData();
                return;
            }

            var initialResult = MeshResolver.Resolve(obj, -1);
            _meshVisibilityManager.Initialize(initialResult.AllMeshComponents);

            if (!initialResult.Success && initialResult.AllMeshComponents.Count == 0)
            {
                MeshResolver.ShowErrorDialog(initialResult.ErrorMessage);
                _componentsManager.ObjectField.SetValueWithoutNotify(null);
                return;
            }

            if (_uvEditorMeshData != null)
            {
                ClearActiveVertices();
                _uvEditorMeshData.Dispose();
                _uvEditorMeshData = null;
            }

            var meshChoices = new List<string>();
            foreach (var comp in initialResult.AllMeshComponents)
            {
                meshChoices.Add(comp.DisplayName);
            }

            _componentsManager.MeshComponentField.choices = meshChoices;
            _componentsManager.MeshComponentField.SetValueWithoutNotify(meshChoices[0]);
            _componentsManager.MeshComponentField.SetEnabled(meshChoices.Count > 1);

            LoadMeshData(obj, 0);

            if (_previewWindow != null && obj is GameObject go)
            {
                _previewWindow.SetPrefab(go);
                var result = MeshResolver.Resolve(go);
                _previewWindow.AddMeshColliders(result.AllMeshComponents);
                for (int i = 0; i < result.AllMeshComponents.Count; i++)
                {
                    var workingMesh = _uvEditorMeshData.GetWorkingMesh(i);
                    if (workingMesh != null)
                    {
                        _previewWindow.UpdateMeshComponent(result.AllMeshComponents[i], workingMesh);
                    }
                }
            }
            
            _uvEditorMeshData.VerticesMoved = true;
        }

        private void SwitchMeshComponent(int meshIndex)
        {
            var currentObject = _componentsManager.ObjectField.value as GameObject;
            if (currentObject == null) return;

            ClearActiveVertices();
            _cacheActiveVertexesIndex.Clear();

            if (meshIndex >= 0 && meshIndex < _componentsManager.MeshComponentField.choices.Count)
            {
                _componentsManager.MeshComponentField.SetValueWithoutNotify(
                    _componentsManager.MeshComponentField.choices[meshIndex]);
            }

            LoadMeshData(currentObject, meshIndex);
        }

        private void LoadMeshData(UnityEngine.Object obj, int meshIndex)
        {
            MeshResolver.ResolveResult result = MeshResolver.Resolve(obj, meshIndex);

            if (!result.Success)
            {
                MeshResolver.ShowErrorDialog(result.ErrorMessage);
                return;
            }

            if (_uvEditorMeshData == null)
            {
                _uvEditorMeshData = new UVEditorMeshData(result);
            }
            else
            {
                _uvEditorMeshData.SwitchToMesh(meshIndex, result);
            }

            UpdateSubMeshField();

            var availableChannels = GetAvailableUVChannels();
            _componentsManager.UVField.choices = availableChannels;
            if (availableChannels.Count > 0)
            {
                _componentsManager.UVField.SetValueWithoutNotify(availableChannels[0]);
            }

            _uvTransform = new UVTransform(_uvEditorMeshData, _componentsManager.TextureContainer, this);
            _componentsManager.ObjectField.SetValueWithoutNotify(obj);

            if (_previewWindow != null && obj is GameObject go && _previewWindow.IsShowingPrefab(go))
            {
                var meshInfo = result.AllMeshComponents[result.SelectedMeshIndex];
                _previewWindow.UpdateMeshComponent(meshInfo, _uvEditorMeshData.CurrentMesh);
            }

            _cacheActiveVertexesIndex.Clear();
            _uvEditorMeshData.UVIsDirty = true;
            RefreshContent();
        }

        private void CleanupAllData()
        {
            if (_uvEditorMeshData != null)
            {
                ClearActiveVertices();
                _uvEditorMeshData.Dispose();
                _uvEditorMeshData = null;
            }

            if (_previewWindow != null)
            {
                _previewWindow.ClearPreview();
            }

            _uvTransform = null;

            _componentsManager.MeshComponentField.choices = new List<string> { "No mesh selected" };
            _componentsManager.MeshComponentField.SetValueWithoutNotify("No mesh selected");
            _componentsManager.MeshComponentField.SetEnabled(false);

            _meshVisibilityManager.Clear();
        }

        private void UpdatePreviewVisibility()
        {
            if (_previewWindow == null || _uvEditorMeshData == null) return;

            var currentPrefab = _componentsManager.ObjectField.value as GameObject;
            if (currentPrefab == null) return;

            var result = MeshResolver.Resolve(currentPrefab);

            for (int i = 0; i < result.AllMeshComponents.Count; i++)
            {
                bool visible = _meshVisibilityManager.IsVisible(i);
                _previewWindow.SetMeshVisibility(result.AllMeshComponents[i], visible);

                string targetName = null;
                if (result.AllMeshComponents[i].MeshFilter != null)
                    targetName = result.AllMeshComponents[i].MeshFilter.name;
                else if (result.AllMeshComponents[i].SkinnedRenderer != null)
                    targetName = result.AllMeshComponents[i].SkinnedRenderer.name;

                if (targetName != null)
                    _previewWindow.SetMeshColliderEnabled(targetName, visible);
            }
        }

        private List<string> GetAvailableUVChannels()
        {
            var channels = new List<string>();
            if (_uvEditorMeshData?.CurrentMesh == null) return channels;

            var mesh = _uvEditorMeshData.CurrentMesh;

            var uv0 = new List<Vector2>();
            mesh.GetUVs(0, uv0);
            if (uv0.Count > 0) channels.Add("UV0");

            var uv1 = new List<Vector2>();
            mesh.GetUVs(1, uv1);
            if (uv1.Count > 0) channels.Add("UV1");

            var uv2 = new List<Vector2>();
            mesh.GetUVs(2, uv2);
            if (uv2.Count > 0) channels.Add("UV2");

            var uv3 = new List<Vector2>();
            mesh.GetUVs(3, uv3);
            if (uv3.Count > 0) channels.Add("UV3");

            return channels;
        }

        private void ApplyMasterMaterialToAllMeshes()
        {
            if (_uvEditorMeshData == null) return;

            Texture2D texture = LoadAssetController.GetMainTexture();
            if (texture == null)
            {
                Debug.LogWarning("MainTexture not found in project");
                return;
            }

            Material masterMaterial = LoadAssetController.GetOrCreateMasterMaterial(texture);
            if (masterMaterial == null) return;

            _masterMaterial = masterMaterial;
            _uvEditorMeshData.SetMasterMaterial(masterMaterial);
            _uvEditorMeshData.TextureMaterial = masterMaterial;
            _uvEditorMeshData.Texture = texture;

            _componentsManager.TextureContainer.Clear();

            if (_previewWindow != null)
                _previewWindow.RefreshPreviewMaterials(masterMaterial);

            _uvEditorMeshData.UVIsDirty = true;
            RefreshContent();
        }

        private void UpdateSubMeshField()
        {
            if (_uvEditorMeshData == null || _uvEditorMeshData.CurrentMesh == null)
                return;

            var renderer = _uvEditorMeshData.SelectedRenderer;
            var materials = renderer != null ? renderer.sharedMaterials : null;

            var subMeshIndexDropDown = _componentsManager.SubMeshField;
            var indexes = new List<string>();

            indexes.Add("All");

            for (int i = 0; i < _uvEditorMeshData.CurrentMesh.subMeshCount; i++)
            {
                if (materials != null && i < materials.Length && materials[i] != null)
                {
                    indexes.Add(materials[i].name);
                }
                else
                {
                    indexes.Add($"SubMesh {i}");
                }
            }

            subMeshIndexDropDown.choices = indexes;
            _uvEditorMeshData.CurrentSubMeshIndex = -1;
            subMeshIndexDropDown.SetValueWithoutNotify("All");
        }

        private void UpdatePreviewMesh()
        {
            if (_uvEditorMeshData?.CurrentMesh == null || _previewWindow == null)
                return;

            if (_uvEditorMeshData.CurrentMeshFilter != null)
            {
                var meshInfo = new MeshResolver.MeshComponentInfo
                {
                    Mesh = _uvEditorMeshData.CurrentMesh,
                    MeshFilter = _uvEditorMeshData.CurrentMeshFilter,
                    SkinnedRenderer = null
                };
                _previewWindow.UpdateMeshComponent(meshInfo, _uvEditorMeshData.CurrentMesh);
            }
            else if (_uvEditorMeshData.CurrentSkinnedRenderer != null)
            {
                var meshInfo = new MeshResolver.MeshComponentInfo
                {
                    Mesh = _uvEditorMeshData.CurrentMesh,
                    MeshFilter = null,
                    SkinnedRenderer = _uvEditorMeshData.CurrentSkinnedRenderer
                };
                _previewWindow.UpdateMeshComponent(meshInfo, _uvEditorMeshData.CurrentMesh);
            }
        }

        public void Recenter()
        {
            _zoomValue = 1f;
            _anchor = Vector2.zero;
            _uvEditorMeshData.VerticesMoved = true;
            RefreshContent();
        }

        public void FocusSelection()
        {
            if (_activeVerticesId.Count == 0)
                return;

            _zoomValue =
                Mathf.Clamp(
                    1 / (_uvEditorMeshData.MarkedBoundsUV.min - _uvEditorMeshData.MarkedBoundsUV.max).magnitude * 0.9f,
                    MIN_ZOOM, MAX_ZOOM);
            _componentsManager.GetCanvasSize(_zoomValue, out float size);

            var offsetInUV = _uvEditorMeshData.MarkedBoundsUV.center - Vector2.one * 0.5f;
            offsetInUV.x = -offsetInUV.x * size;
            offsetInUV.y = offsetInUV.y * size;
            _anchor = offsetInUV;
            _uvEditorMeshData.VerticesMoved = true;
            RefreshContent();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (_uvEditorMeshData == null) return;
            
            _uvEditorMeshData.UVIsDirty = true;
            _uvEditorMeshData.VerticesMoved = true;
            RefreshContent();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (_uvEditorMeshData == null)
                return;

            if ((evt.pressedButtons & 4) > 0)
            {
                _componentsManager.Canvas.CaptureMouse();
            }

            if ((evt.pressedButtons & 1) > 0)
            {
                _leftMouseDownPosition = evt.mousePosition;
            }

            if ((evt.pressedButtons & 1) > 0)
            {
                if (_gizmoManager.RectGizmoDraggedScaleHandle == 0)
                {
                    if (_gizmoManager.TryGetGizmoAtPosition(_uvMousePos, out var type, out var corner))
                    {
                        _uvTransform.StartScalingUVs(_gizmoManager.RectGizmoDraggedScaleHandle,
                            new List<int>(_activeVerticesId),
                            _uvMousePos);
                    }
                }

                if (_gizmoManager.RectGizmoDraggedRotationHandle == 0)
                {
                    if (_gizmoManager.TryGetGizmoAtPosition(evt.mousePosition, out var type, out var corner))
                    {
                        _uvTransform.StartRotatingUVs(new List<int>(_activeVerticesId), _gizmoManager, _uvMousePos);
                    }
                }

                TryStartMovingUVs(evt, evt.mousePosition);

                if (_gizmoManager.RectGizmoDraggedScaleHandle > 0 || _gizmoManager.RectGizmoDraggedRotationHandle > 0 ||
                    _gizmoManager.RectGizmoDraggedMoveHandle > 0)
                    _componentsManager.Canvas.CaptureMouse();
            }

            if ((evt.pressedButtons & 1) > 0)
            {
                _activeRectStart = _uvMousePos;
                _activeRectEnd = _uvMousePos;

                if (_currentSelectionMode == SelectionMode.Rect && !_gizmoManager.AnyHandle() && !evt.shiftKey &&
                    !evt.ctrlKey)
                {
                    _componentsManager.Canvas.CaptureMouse();
                    _activeRectDrag = true;

                    if (_activeVerticesId.Count > 0)
                    {
                        ClearActiveVertices();
                    }
                }

                if (_currentSelectionMode == SelectionMode.Lasso && !_gizmoManager.AnyHandle() && !evt.shiftKey &&
                    !evt.ctrlKey)
                {
                    _componentsManager.Canvas.CaptureMouse();
                    _activeLassoDrag = true;
                    _lassoSelectionActive = true;
                    _lassoPoints.Clear();
                    _lassoPoints.Add(_uvMousePos);
                }
            }
        }

        private void TryStartMovingUVs(IMouseEvent evt, Vector2 mousePosition)
        {
            if (_gizmoManager.RectGizmoDraggedMoveHandle == 0 &&
                _gizmoManager.CenterMoveGizmo.worldBound.Contains(mousePosition) && !evt.altKey)
            {
                _gizmoManager.RectGizmoDraggedMoveHandle = 1;
                _uvTransform.StartMovingUVs();
                _componentsManager.Canvas.CaptureMouse();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            bool isLeftButtonPressed = (evt.pressedButtons & 1) > 0;

            bool isDragging = false;
            if (isLeftButtonPressed)
            {
                var moveDistanceSinceDown = (evt.mousePosition - _leftMouseDownPosition).magnitude;
                if (moveDistanceSinceDown > MAX_CLICK_DISTANCE)
                {
                    isDragging = true;
                }
            }

            var pos = evt.mousePosition -
                      new Vector2(rootVisualElement.resolvedStyle.left, rootVisualElement.resolvedStyle.top);
            _canvasMousePos = rootVisualElement.ChangeCoordinatesTo(_componentsManager.Canvas, pos);
            _textureMousePos = rootVisualElement.ChangeCoordinatesTo(_componentsManager.TextureContainer, pos);

            _uvMousePos = new Vector2(
                _textureMousePos.x / _componentsManager.TextureContainer.resolvedStyle.width,
                1f - (_textureMousePos.y / _componentsManager.TextureContainer.resolvedStyle.height));

            if (isDragging)
            {
                TryStartMovingUVs(evt, _leftMouseDownPosition);
            }

            if (_componentsManager.Canvas.HasMouseCapture())
            {
                if (_activeLassoDrag && _lassoSelectionActive)
                {
                    float stepThreshold = Mathf.Max(0.002f, 0.01f / _zoomValue);
                    if (_lassoPoints.Count == 0 || Vector2.Distance(_lassoPoints[_lassoPoints.Count - 1], _uvMousePos) >
                        stepThreshold)
                    {
                        _lassoPoints.Add(_uvMousePos);
                    }

                    _uvEditorMeshData.UVIsDirty = true;
                    return;
                }

                if (_activeRectDrag && !_gizmoManager.AnyHandle() && (evt.pressedButtons & 1) > 0)
                {
                    _activeRectEnd = _uvMousePos;
                }
                else if (!_activeRectDrag && !_gizmoManager.AnyHandle())
                {
                    _anchor += evt.mouseDelta;
                    _uvEditorMeshData.VerticesMoved = true;
                }
                else
                {
                    if (_gizmoManager.RectGizmoDraggedScaleHandle > 0)
                        _uvTransform.ScaleUVs(new List<int>(_activeVerticesId), _uvMousePos);
                    else if (_gizmoManager.RectGizmoDraggedRotationHandle > 0)
                        _uvTransform.RotateUVs(new List<int>(_activeVerticesId), _uvMousePos);
                    else if (_gizmoManager.RectGizmoDraggedMoveHandle > 0)
                        _uvTransform.MoveUVs(evt, new List<int>(_activeVerticesId));
                }
            }
        }

        private void OnScroll(WheelEvent evt)
        {
            float oldZoom = _zoomValue;
            _zoomValue -= (evt.delta.y * 0.01f) * _zoomValue;
            _zoomValue = Mathf.Clamp(_zoomValue, MIN_ZOOM, MAX_ZOOM);

            float zoomDelta = _zoomValue / oldZoom;
            var textureCenter = new Vector2(
                _componentsManager.TextureContainer.resolvedStyle.left +
                _componentsManager.TextureContainer.resolvedStyle.width * 0.5f,
                _componentsManager.TextureContainer.resolvedStyle.top +
                _componentsManager.TextureContainer.resolvedStyle.height * 0.5f
            );
            var deltaOfMouseToTextureCenter = _canvasMousePos - textureCenter;
            _anchor -= (deltaOfMouseToTextureCenter * zoomDelta - deltaOfMouseToTextureCenter);
            
            _uvEditorMeshData.UVIsDirty = true;
            _uvEditorMeshData.VerticesMoved = true;
            
            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (_uvEditorMeshData == null)
                return;

            if (_componentsManager.Canvas.HasMouseCapture())
            {
                _componentsManager.Canvas.ReleaseMouse();
            }

            bool didChangeActiveVertices = false;

            if (evt.button == 0)
            {
                if (_activeLassoDrag && _lassoSelectionActive)
                {
                    _activeLassoDrag = false;
                    _lassoSelectionActive = false;

                    if (!evt.ctrlKey && !evt.shiftKey)
                    {
                        _activeVerticesId.Clear();
                        OnActiveVerticesChanged();
                    }

                    if (_lassoPoints.Count >= 3)
                    {
                        var uvs = _uvEditorMeshData.CurrentUV;
                        if (uvs.Count > 0)
                        {
                            var subMeshTris = new List<int>();

                            if (_uvEditorMeshData.CurrentSubMeshIndex == -1)
                            {
                                for (int s = 0; s < _uvEditorMeshData.CurrentMesh.subMeshCount; s++)
                                {
                                    var tempTris = new List<int>();
                                    _uvEditorMeshData.CurrentMesh.GetTriangles(tempTris, s);
                                    subMeshTris.AddRange(tempTris);
                                }
                            }
                            else
                            {
                                _uvEditorMeshData.CurrentMesh.GetTriangles(subMeshTris,
                                    _uvEditorMeshData.CurrentSubMeshIndex);
                            }

                            for (int v = 0; v < subMeshTris.Count; v++)
                            {
                                int index = subMeshTris[v];

                                if (IsPointInLasso(uvs[index], _lassoPoints))
                                {
                                    if (!_activeVerticesId.Contains(index))
                                    {
                                        _activeVerticesId.Add(index);
                                        didChangeActiveVertices = true;
                                    }
                                }
                            }

                            if (didChangeActiveVertices)
                            {
                                OnActiveVerticesChanged();
                            }
                        }
                    }

                    _lassoPoints.Clear();
                    _uvEditorMeshData.UVIsDirty = true;
                    return;
                }

                if (!_gizmoManager.AnyHandle())
                {
                    var uvs = _uvEditorMeshData.CurrentUV;
                    if (uvs.Count > 0)
                    {
                        Rect rect = GetSelectionRect();

                        var subMeshTris = new List<int>();

                        if (_uvEditorMeshData.CurrentSubMeshIndex == -1)
                        {
                            for (int s = 0; s < _uvEditorMeshData.CurrentMesh.subMeshCount; s++)
                            {
                                var tempTris = new List<int>();
                                _uvEditorMeshData.CurrentMesh.GetTriangles(tempTris, s);
                                subMeshTris.AddRange(tempTris);
                            }
                        }
                        else
                        {
                            _uvEditorMeshData.CurrentMesh.GetTriangles(subMeshTris,
                                _uvEditorMeshData.CurrentSubMeshIndex);
                        }

                        for (int v = 0; v < subMeshTris.Count; v++)
                        {
                            int index = subMeshTris[v];

                            if (rect.Contains(uvs[index]))
                            {
                                if (evt.ctrlKey)
                                {
                                    if (_activeVerticesId.Contains(index))
                                    {
                                        _activeVerticesId.Remove(index);
                                        didChangeActiveVertices = true;
                                    }
                                    else
                                    {
                                        _activeVerticesId.Add(index);
                                        didChangeActiveVertices = true;
                                    }
                                }
                                else
                                {
                                    if (!_activeVerticesId.Contains(index))
                                    {
                                        _activeVerticesId.Add(index);
                                        didChangeActiveVertices = true;
                                    }
                                }
                            }
                        }

                        if (didChangeActiveVertices)
                        {
                            OnActiveVerticesChanged();
                        }
                    }

                    _activeRectStart = _uvMousePos;
                    _activeRectEnd = _uvMousePos;
                    _activeRectDrag = false;
                }

                if (_gizmoManager.RectGizmoDraggedScaleHandle != 0)
                {
                    _uvTransform.StopScalingUVs();
                    _gizmoManager.RectGizmoDraggedScaleHandle = 0;
                    RefreshContent();
                }

                if (_gizmoManager.RectGizmoDraggedMoveHandle != 0)
                {
                    _uvTransform.StopMovingUVs();
                    _gizmoManager.RectGizmoDraggedMoveHandle = 0;
                    RefreshContent();
                }

                if (_gizmoManager.RectGizmoDraggedRotationHandle != 0)
                {
                    _uvTransform.StopRotatingUVs();
                    _gizmoManager.RectGizmoDraggedRotationHandle = 0;
                    RefreshContent();
                }
            }

            if (_nextCursorOnMouseUp.HasValue && _cursorTargetElement != null)
            {
                _cursorTargetElement.style.cursor = _nextCursorOnMouseUp.Value;
            }
        }

        private void OnActiveVerticesChanged()
        {
            if (_activeVerticesId.Count > 0)
                _uvTransform.RefreshSelectionRect(_activeVerticesId);
            else
                _gizmoManager.RectUVGizmoContainer.style.display = DisplayStyle.None;

            _cacheActiveVertexesIndex.Clear();
        }

        private void ClearActiveVertices()
        {
            _activeVerticesId.Clear();
            _lassoPoints.Clear();
            _activeLassoDrag = false;
            _lassoSelectionActive = false;
            OnActiveVerticesChanged();
        }

        private void RefreshContent()
        {
            if (!IsDataValid()) return;

            UpdateZoomIfChanged();

            if (_masterMaterial == null)
            {
                UpdateTextureFromRenderer();
            }

            _componentsManager.GetCanvasSize(_zoomValue, out float size);

            _componentsManager.UpdateTextureAndGrid(size, _anchor, _uvEditorMeshData.Texture,
                _uvEditorMeshData.TextureMaterial);

            UpdateGizmoVisibility(size);

            bool hasUVs = HasUVs();

            if (hasUVs)
            {
                _componentsManager.UpdateUVLinesContainers();

                if (_uvEditorMeshData.UVIsDirty)
                {
                    _uvEditorMeshData.UVIsDirty = false;
                    DrawUVLines(size);
                    UpdatePreviewMesh();
                }
            }
            else
            {
                _componentsManager.HideAllUVLines();
                _gizmoManager.RectUVGizmoContainer.style.display = DisplayStyle.None;
            }

            UpdateVertices(hasUVs, size);

            _componentsManager.UpdateSelectionRect(_activeRectStart, _activeRectEnd, size, _zoomValue);

            if (_lassoPoints.Count > 0)
            {
                DrawLassoSelection(size);
            }
            else
            {
                _componentsManager.LassoContainer.style.display = DisplayStyle.None;
            }
        }

        private bool IsDataValid()
        {
            return _uvEditorMeshData != null && _uvEditorMeshData.CurrentObject != null;
        }

        private void UpdateZoomIfChanged()
        {
            if (_previousZoom == _zoomValue) return;

            _previousZoom = _zoomValue;
            _uvEditorMeshData.UVIsDirty = true;
        }

        private void UpdateTextureFromRenderer()
        {
            var renderer = _uvEditorMeshData.SelectedRenderer;
            if (renderer == null)
            {
                _uvEditorMeshData.Texture = null;
                _uvEditorMeshData.TextureMaterial = null;
                return;
            }

            var materials = renderer.sharedMaterials;
            int subMeshIndex = _uvEditorMeshData.CurrentSubMeshIndex;

            if (subMeshIndex == -1)
            {
                subMeshIndex = 0;
            }

            if (subMeshIndex >= materials.Length || materials[subMeshIndex] == null)
            {
                _uvEditorMeshData.Texture = null;
                _uvEditorMeshData.TextureMaterial = null;
                return;
            }

            var material = materials[subMeshIndex];
            var texture = material.GetMainTexture() as Texture2D;

            _uvEditorMeshData.Texture = texture;
            _uvEditorMeshData.TextureMaterial = material;
        }

        private void UpdateGizmoVisibility(float size)
        {
            bool hasSelection = _activeVerticesId != null && _activeVerticesId.Count > 0;
            _gizmoManager.RectUVGizmoContainer.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;

            if (hasSelection)
            {
                bool showScaleRotate = _activeVerticesId.Count > 1;
                _gizmoManager.ReDrawGizmo(size, _uvEditorMeshData.MarkedBoundsUV, showScaleRotate, showScaleRotate,
                    true);
            }
        }

        private bool HasUVs()
        {
            var uvs = _uvEditorMeshData.CurrentUV;
            return uvs != null && uvs.Count > 0;
        }

        private void DrawUVLines(float size)
        {
            _componentsManager.ClearAllUVLines();

            int currentMeshIndex = _uvEditorMeshData.CurrentMeshIndex;

            if (!_meshVisibilityManager.IsVisible(currentMeshIndex))
            {
                foreach (var container in _componentsManager.UVLinesContainers)
                {
                    container.MarkDirtyRepaint();
                }

                return;
            }

            var uvs = _uvEditorMeshData.CurrentUV;
            var lineColor = _componentsManager.UVLineColor;

            int containerIndex = 0;
            _componentsManager.CurrentUVLinesContainer = _componentsManager.GetOrCreateUVLinesContainer(containerIndex);
            _componentsManager.SetupUVLinesContainer(_componentsManager.CurrentUVLinesContainer, lineColor);

            if (_uvEditorMeshData.CurrentSubMeshIndex == -1)
            {
                for (int s = 0; s < _uvEditorMeshData.CurrentMesh.subMeshCount; s++)
                {
                    var tris = new List<int>();
                    _uvEditorMeshData.CurrentMesh.GetTriangles(tris, s);

                    for (int v = 0; v < tris.Count; v += 3)
                    {
                        int i0 = tris[v];
                        int i1 = tris[v + 1];
                        int i2 = tris[v + 2];

                        var uv0 = uvs[i0];
                        var uv1 = uvs[i1];
                        var uv2 = uvs[i2];

                        if (uv0 == uv1 || uv1 == uv2 || uv0 == uv2)
                            continue;

                        if (!_componentsManager.CurrentUVLinesContainer.NextLinesCanBeDrawn(3))
                        {
                            containerIndex++;
                            _componentsManager.CurrentUVLinesContainer =
                                _componentsManager.GetOrCreateUVLinesContainer(containerIndex);
                            _componentsManager.SetupUVLinesContainer(_componentsManager.CurrentUVLinesContainer,
                                lineColor);
                        }

                        var container = _componentsManager.CurrentUVLinesContainer;
                        container.StartNewLine();
                        container.AddVertex(uv0 * size, lineColor);
                        container.AddVertex(uv1 * size, lineColor);
                        container.AddVertex(uv2 * size, lineColor, true);
                    }
                }
            }
            else
            {
                int currentSubMesh = _uvEditorMeshData.CurrentSubMeshIndex;
                var tris = new List<int>();
                _uvEditorMeshData.CurrentMesh.GetTriangles(tris, currentSubMesh);

                if (tris.Count == 0)
                    return;

                for (int v = 0; v < tris.Count; v += 3)
                {
                    int i0 = tris[v];
                    int i1 = tris[v + 1];
                    int i2 = tris[v + 2];

                    var uv0 = uvs[i0];
                    var uv1 = uvs[i1];
                    var uv2 = uvs[i2];

                    if (uv0 == uv1 || uv1 == uv2 || uv0 == uv2)
                        continue;

                    if (!_componentsManager.CurrentUVLinesContainer.NextLinesCanBeDrawn(3))
                    {
                        containerIndex++;
                        _componentsManager.CurrentUVLinesContainer =
                            _componentsManager.GetOrCreateUVLinesContainer(containerIndex);
                        _componentsManager.SetupUVLinesContainer(_componentsManager.CurrentUVLinesContainer, lineColor);
                    }

                    var container = _componentsManager.CurrentUVLinesContainer;
                    container.StartNewLine();
                    container.AddVertex(uv0 * size, lineColor);
                    container.AddVertex(uv1 * size, lineColor);
                    container.AddVertex(uv2 * size, lineColor, true);
                }
            }

            foreach (var container in _componentsManager.UVLinesContainers)
            {
                container.MarkDirtyRepaint();
            }
        }

        private void UpdateVertices(bool hasUVs, float size)
        {
            if (!hasUVs)
            {
                _componentsManager.HideVertexContainer();
                _lastVertexCount = -1;
                return;
            }

            int currentMeshIndex = _uvEditorMeshData.CurrentMeshIndex;

            if (!_meshVisibilityManager.IsVisible(currentMeshIndex))
            {
                _componentsManager.HideVertexContainer();
                _lastVertexCount = -1;
                return;
            }

            if (!_uvEditorMeshData.UVIsDirty && !_uvEditorMeshData.VerticesMoved &&
                _activeVerticesId.Count == _lastVertexCount && Mathf.Approximately(size, _lastVertexSize))
                return;

            _uvEditorMeshData.VerticesMoved = false;
            _lastVertexCount = _activeVerticesId.Count;
            _lastVertexSize = size;

            _componentsManager.PrepareVertexContainer(size, _zoomValue);

            var uvs = _uvEditorMeshData.CurrentUV;
            var drawnVertices = new HashSet<int>();
            const int MAX_VERTICES = 65000;
            int drawnCount = 0;

            if (_uvEditorMeshData.CurrentSubMeshIndex == -1)
            {
                for (int s = 0; s < _uvEditorMeshData.CurrentMesh.subMeshCount; s++)
                {
                    var tris = new List<int>();
                    _uvEditorMeshData.CurrentMesh.GetTriangles(tris, s);

                    for (int i = 0; i < tris.Count; i++)
                    {
                        int index = tris[i];

                        if (!drawnVertices.Add(index))
                            continue;

                        if (drawnCount * 6 >= MAX_VERTICES)
                            break;

                        float halfWidth = _componentsManager.VertexContainer.LineWidth * 0.5f;
                        float x = uvs[index].x * size;
                        float y = uvs[index].y * size;

                        Color vertexColor = _activeVerticesId.Contains(index)
                            ? _componentsManager.CurrentSelectedVertexColor
                            : _componentsManager.CurrentDefaultVertexColor;

                        _componentsManager.VertexContainer.StartNewLine();
                        _componentsManager.VertexContainer.AddVertex(new Vector2(x - halfWidth, y), vertexColor);
                        _componentsManager.VertexContainer.AddVertex(new Vector2(x + halfWidth, y), vertexColor);

                        drawnCount++;
                    }
                }
            }
            else
            {
                int currentSubMesh = _uvEditorMeshData.CurrentSubMeshIndex;
                var tris = new List<int>();
                _uvEditorMeshData.CurrentMesh.GetTriangles(tris, currentSubMesh);

                for (int i = 0; i < tris.Count; i++)
                {
                    int index = tris[i];

                    if (!drawnVertices.Add(index))
                        continue;

                    if (drawnCount * 6 >= MAX_VERTICES)
                        break;

                    float halfWidth = _componentsManager.VertexContainer.LineWidth * 0.5f;
                    float x = uvs[index].x * size;
                    float y = uvs[index].y * size;

                    Color vertexColor = _activeVerticesId.Contains(index)
                        ? _componentsManager.CurrentSelectedVertexColor
                        : _componentsManager.CurrentDefaultVertexColor;

                    _componentsManager.VertexContainer.StartNewLine();
                    _componentsManager.VertexContainer.AddVertex(new Vector2(x - halfWidth, y), vertexColor);
                    _componentsManager.VertexContainer.AddVertex(new Vector2(x + halfWidth, y), vertexColor);

                    drawnCount++;
                }
            }

            _componentsManager.VertexContainer.MarkDirtyRepaint();
        }

        private Rect GetSelectionRect()
        {
            float width = _activeRectEnd.x - _activeRectStart.x;
            float height = _activeRectEnd.y - _activeRectStart.y;

            return new Rect(
                width > 0 ? _activeRectStart.x : _activeRectEnd.x,
                height > 0 ? _activeRectStart.y : _activeRectEnd.y,
                Mathf.Abs(width),
                Mathf.Abs(height)
            );
        }

        private bool IsPointInLasso(Vector2 point, List<Vector2> lassoPoints)
        {
            if (lassoPoints.Count < 3) return false;

            bool inside = false;
            for (int i = 0, j = lassoPoints.Count - 1; i < lassoPoints.Count; j = i++)
            {
                var pi = lassoPoints[i];
                var pj = lassoPoints[j];

                if (((pi.y > point.y) != (pj.y > point.y)) &&
                    (point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private void DrawLassoSelection(float size)
        {
            if (_lassoPoints.Count < 2) return;

            _componentsManager.LassoContainer.style.display = DisplayStyle.Flex;
            _componentsManager.LassoContainer.style.width = _componentsManager.TextureContainer.style.width;
            _componentsManager.LassoContainer.style.height = _componentsManager.TextureContainer.style.height;
            _componentsManager.LassoContainer.style.left = _componentsManager.TextureContainer.style.left;
            _componentsManager.LassoContainer.style.bottom = _componentsManager.TextureContainer.style.bottom;
            _componentsManager.LassoContainer.InvertVertical = true;
            _componentsManager.LassoContainer.LineWidth = 2f;
            _componentsManager.LassoContainer.LineColor = new Color(0f, .3f, 1f, 0.8f);
            _componentsManager.LassoContainer.ClearVertices();

            _componentsManager.LassoContainer.StartNewLine();

            for (int i = 0; i < _lassoPoints.Count; i++)
            {
                var point = _lassoPoints[i] * size;
                _componentsManager.LassoContainer.AddVertex(point);
            }

            if (!_lassoSelectionActive && _lassoPoints.Count > 2)
            {
                _componentsManager.LassoContainer.AddVertex(_lassoPoints[0] * size, closeLoop: true);
            }

            _componentsManager.LassoContainer.MarkDirtyRepaint();
        }

        public override void SaveChanges()
        {
            _uvEditorMeshData?.SaveAssetChanges();
            hasUnsavedChanges = false;
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }

        public void SetWindowChanges()
        {
            hasUnsavedChanges = true;
        }

        public void SelectMeshFromPreview(string meshName, int subMeshIndex)
        {
            if (_uvEditorMeshData == null || string.IsNullOrEmpty(meshName)) return;

            var choices = _componentsManager.MeshComponentField.choices;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].Contains(meshName))
                {
                    _componentsManager.MeshComponentField.SetValueWithoutNotify(choices[i]);
                    SwitchMeshComponent(i);
                    break;
                }
            }

            if (subMeshIndex >= 0)
            {
                _uvEditorMeshData.CurrentSubMeshIndex = subMeshIndex;
                if (subMeshIndex + 1 < _componentsManager.SubMeshField.choices.Count)
                    _componentsManager.SubMeshField.SetValueWithoutNotify(
                        _componentsManager.SubMeshField.choices[subMeshIndex + 1]);
            }

            _uvEditorMeshData.UVIsDirty = true;
            _uvEditorMeshData.VerticesMoved = true;
            RefreshContent();
        }
    }
}