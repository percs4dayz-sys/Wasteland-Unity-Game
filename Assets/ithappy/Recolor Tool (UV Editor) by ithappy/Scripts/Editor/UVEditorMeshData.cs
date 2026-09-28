using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ithappy.RecolorToolWindow
{
    public class UVEditorMeshData
    {
        private int _currentSubMeshIndex = 0;
        private int _currentUVChannel = 0;

        private List<Vector2> _startUV;
        private List<Vector2> _currentUV;

        public UnityEngine.Object CurrentObject;
        public MeshFilter CurrentMeshFilter;
        public SkinnedMeshRenderer CurrentSkinnedRenderer;
        public Texture2D Texture = null;
        public Material TextureMaterial = null;
        public Rect MarkedBoundsUV;

        private Mesh _originalMesh;
        private Mesh _currentMesh;
        private MeshResolver.ResolveResult _result;
        private Material _masterMaterial;
        
        private Dictionary<int, Mesh> _workingMeshes = new Dictionary<int, Mesh>();
        private Dictionary<int, Mesh> _originalMeshes = new Dictionary<int, Mesh>();
        private Dictionary<int, bool> _changedMeshes = new Dictionary<int, bool>();

        public bool VerticesMoved { get; set; } = false;
        public bool UVIsDirty { get; set; } = true;
        public int CurrentMeshIndex => _result?.SelectedMeshIndex ?? 0;

        public List<Vector2> CurrentUV
        {
            get
            {
                if (_currentUV == null)
                {
                    _currentUV = GetMeshUV();
                }

                return _currentUV;
            }
            private set => _currentUV = value;
        }

        public Mesh CurrentMesh
        {
            get => _currentMesh;
            private set
            {
                _currentMesh = value;
                CacheStartUV();
            }
        }

        public int CurrentSubMeshIndex
        {
            get => _currentSubMeshIndex;
            set
            {
                if (_currentSubMeshIndex != value)
                {
                    _currentUV = null;
                    _currentSubMeshIndex = value;
                    CacheStartUV();
                }
            }
        }

        public int CurrentUVChannel
        {
            get => _currentUVChannel;
            set
            {
                if (_currentUVChannel != value)
                {
                    _currentUV = null;
                    _currentUVChannel = value;
                    CacheStartUV();
                }
            }
        }

        public Renderer SelectedRenderer
        {
            get
            {
                if (CurrentMeshFilter != null)
                {
                    return CurrentMeshFilter.GetComponent<MeshRenderer>();
                }

                return CurrentSkinnedRenderer;
            }
        }

        public UVEditorMeshData(MeshResolver.ResolveResult result)
        {
            _result = result;
            CurrentObject = result.OriginalObject;
            
            for (int i = 0; i < result.AllMeshComponents.Count; i++)
            {
                var comp = result.AllMeshComponents[i];
                if (comp.Mesh != null)
                {
                    _originalMeshes[i] = comp.Mesh;

                    var workingCopy = Object.Instantiate(comp.Mesh);
                    workingCopy.name = comp.Mesh.name + "_WorkingCopy";
                    workingCopy.hideFlags = HideFlags.HideAndDontSave;
                    _workingMeshes[i] = workingCopy;
                    
                    _changedMeshes[i] = false;
                }
            }
            
            SwitchToMesh(result.SelectedMeshIndex, result);
        }

        public void SwitchToMesh(int meshIndex, MeshResolver.ResolveResult result = null)
        {
            if (result != null)
                _result = result;

            if (!_workingMeshes.ContainsKey(meshIndex))
                return;

            _currentMesh = _workingMeshes[meshIndex];
            _originalMesh = _originalMeshes[meshIndex];
            
            if (_result != null && meshIndex < _result.AllMeshComponents.Count)
            {
                var comp = _result.AllMeshComponents[meshIndex];
                CurrentMeshFilter = comp.MeshFilter;
                CurrentSkinnedRenderer = comp.SkinnedRenderer;
            }
            
            _currentUV = null;
            CacheStartUV();
            UVIsDirty = true;
        }

        public Mesh GetWorkingMesh(int meshIndex)
        {
            return _workingMeshes.ContainsKey(meshIndex) ? _workingMeshes[meshIndex] : null;
        }

        public Mesh GetOriginalMesh(int meshIndex)
        {
            return _originalMeshes.ContainsKey(meshIndex) ? _originalMeshes[meshIndex] : null;
        }

        public bool IsMeshChanged(int meshIndex)
        {
            return _changedMeshes.ContainsKey(meshIndex) && _changedMeshes[meshIndex];
        }

        public List<int> GetChangedMeshIndices()
        {
            var changedIndices = new List<int>();
            foreach (var kvp in _changedMeshes)
            {
                if (kvp.Value)
                    changedIndices.Add(kvp.Key);
            }

            return changedIndices;
        }

        public void Dispose()
        {
            foreach (var mesh in _workingMeshes.Values)
            {
                if (mesh != null)
                    Object.DestroyImmediate(mesh);
            }

            _workingMeshes.Clear();
            _originalMeshes.Clear();
            _changedMeshes.Clear();
            _currentMesh = null;
        }

        private List<Vector2> GetMeshUV()
        {
            if (CurrentObject == null || CurrentMesh == null)
            {
                return null;
            }

            var uvs = new List<Vector2>();
            CurrentMesh.GetUVs(CurrentUVChannel, uvs);
            return new List<Vector2>(uvs);
        }

        public void ResetUnsaveChanges()
        {
            if (_startUV != null && CurrentMesh != null)
            {
                CurrentMesh.SetUVs(CurrentUVChannel, _startUV);
                _currentUV = null;
            }
        }

        public void SaveTempChanges()
        {
            var uvWorkingCopy = CurrentUV;
            if (uvWorkingCopy != null && CurrentMesh != null)
            {
                CurrentMesh.SetUVs(CurrentUVChannel, uvWorkingCopy);
                
                if (_result != null)
                {
                    _changedMeshes[_result.SelectedMeshIndex] = true;
                }

                UVIsDirty = true;
            }
        }

        public void SaveAssetChanges()
        {
            var changedIndices = GetChangedMeshIndices();

            if (changedIndices.Count == 0)
            {
                Debug.Log("No meshes were changed. Nothing to save.");
                return;
            }

            Debug.Log($"Saving {changedIndices.Count} changed mesh(es)...");
            
            var newMeshes = new Dictionary<int, Mesh>();
            foreach (int index in changedIndices)
            {
                if (!_workingMeshes.ContainsKey(index) || !_originalMeshes.ContainsKey(index))
                    continue;

                var workingMesh = _workingMeshes[index];
                var originalMesh = _originalMeshes[index];

                var savedMesh = MeshCloneService.SaveMeshAsAsset(workingMesh, originalMesh);

                if (savedMesh != null)
                {
                    Debug.Log($"Saved mesh: {originalMesh.name} -> {savedMesh.name}");
                    newMeshes[index] = savedMesh;
                    _originalMeshes[index] = savedMesh;
                    
                    var oldWorkingCopy = _workingMeshes[index];
                    _workingMeshes[index] = Object.Instantiate(savedMesh);
                    _workingMeshes[index].name = savedMesh.name + "_WorkingCopy";
                    _workingMeshes[index].hideFlags = HideFlags.HideAndDontSave;

                    if (oldWorkingCopy != null)
                        Object.DestroyImmediate(oldWorkingCopy);

                    _changedMeshes[index] = false;
                }
            }
            
            if (_result?.GameObject != null && _result.AllMeshComponents != null)
            {
                var newPrefab = MeshCloneService.CreateNewPrefab(
                    _result.GameObject, 
                    _originalMeshes, 
                    _result.AllMeshComponents,
                    _masterMaterial != null ? new Material[] { _masterMaterial } : null
                );

                if (newPrefab != null)
                {
                    Selection.activeObject = newPrefab;
                    EditorGUIUtility.PingObject(newPrefab);
                }
            }
            
            if (_result != null && _workingMeshes.ContainsKey(_result.SelectedMeshIndex))
            {
                _currentMesh = _workingMeshes[_result.SelectedMeshIndex];
                _originalMesh = _originalMeshes[_result.SelectedMeshIndex];
            }

            CacheStartUV();
        }
        
        public void SetMasterMaterial(Material material)
        {
            _masterMaterial = material;
        }

        private void CacheStartUV(List<Vector2> uv = null)
        {
            if (uv == null)
            {
                if (CurrentUV != null)
                    _startUV = new List<Vector2>(CurrentUV);
            }
            else
            {
                _startUV = new List<Vector2>(uv);
            }
        }
    }
}