using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using Object = UnityEngine.Object;

namespace ithappy.RecolorToolWindow
{
    public class PreviewRenderer
    {
        private PreviewRenderUtility _previewUtility;
        private GameObject _previewInstance;
        private MeshFilter _previewMeshFilter;
        private SkinnedMeshRenderer _previewSkinnedRenderer;
        private Mesh _previewMesh;
        private Material[] _previewMaterials;
        private Light[] _previewLights;

        private Vector2 _previewDirection = new Vector2(220, 15);
        private float _previewZoom = .7f;
        private Vector2 _previewRotation;

        private Vector3 _modelOffset = new Vector3(0, 0f, 0);

        private float _cameraDistance = 4f;
        private Bounds _objectBounds;

        private Vector3 _cachedCameraPosition;
        private Quaternion _cachedCameraRotation;
        private bool _cameraDirty = true;

        private GameObject _originalPrefab;

        private Bounds _fullModelBounds;

        public bool IsInitialized { get; private set; }
        
        private Dictionary<string, MeshCollider> _meshColliders = new Dictionary<string, MeshCollider>();
        
        private bool _isHDRP = false;
        private Type _hdCameraDataType = null;
        private Type _hdLightDataType = null;

        public void Initialize(GameObject originalPrefab)
        {
            Cleanup();

            if (originalPrefab == null) return;

            _originalPrefab = originalPrefab;
            
            DetectHDRP();
            
            _previewUtility = new PreviewRenderUtility(false);

            _previewUtility.camera.transform.position = new Vector3(0, 0.5f, -5);
            _previewUtility.camera.transform.LookAt(Vector3.zero);
            _previewUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _previewUtility.camera.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            _previewUtility.camera.nearClipPlane = 0.01f;
            _previewUtility.camera.farClipPlane = 100f;
            _previewUtility.camera.fieldOfView = 30f;
            _previewUtility.camera.allowHDR = true;
            _previewUtility.camera.enabled = true;
            
            if (_isHDRP)
            {
                SetupHDRPCamera(_previewUtility.camera);
            }

            CreatePreviewInstance();

            CenterAndFitObject();
            SetupPreviewLights();

            _cachedCameraPosition = _previewUtility.camera.transform.position;
            _cachedCameraRotation = _previewUtility.camera.transform.rotation;
            _cameraDirty = true;

            IsInitialized = true;
        }

        private void DetectHDRP()
        {
            try
            {
                _hdCameraDataType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, Unity.RenderPipelines.HighDefinition.Runtime");
                _hdLightDataType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalLightData, Unity.RenderPipelines.HighDefinition.Runtime");
                _isHDRP = _hdCameraDataType != null && _hdLightDataType != null;
            }
            catch
            {
                _isHDRP = false;
                _hdCameraDataType = null;
                _hdLightDataType = null;
            }
        }

        private void SetupHDRPCamera(Camera camera)
        {
            if (_hdCameraDataType == null || camera == null) return;

            try
            {
                var hdData = camera.GetComponent(_hdCameraDataType);
                if (hdData == null)
                    hdData = camera.gameObject.AddComponent(_hdCameraDataType);

                var clearColorModeField = _hdCameraDataType.GetField("clearColorMode");
                if (clearColorModeField != null)
                {
                    var enumType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData+ClearColorMode, Unity.RenderPipelines.HighDefinition.Runtime");
                    if (enumType != null)
                    {
                        var colorMode = Enum.Parse(enumType, "Color");
                        clearColorModeField.SetValue(hdData, colorMode);
                    }
                }

                var bgColorField = _hdCameraDataType.GetField("backgroundColorHDR");
                if (bgColorField != null)
                {
                    bgColorField.SetValue(hdData, new Color(0.15f, 0.15f, 0.15f, 1f));
                }

                var volumeLayerMaskField = _hdCameraDataType.GetField("volumeLayerMask");
                if (volumeLayerMaskField != null)
                {
                    volumeLayerMaskField.SetValue(hdData, 0);
                }

                var customSettingsField = _hdCameraDataType.GetField("customRenderingSettings");
                if (customSettingsField != null)
                {
                    customSettingsField.SetValue(hdData, true);
                }
            }
            catch
            {
            }
        }

        private void SetupHDRPLight(GameObject lightGO, float intensity)
        {
            if (_hdLightDataType == null || lightGO == null) return;

            try
            {
                var hdLight = lightGO.GetComponent(_hdLightDataType);
                if (hdLight == null)
                    hdLight = lightGO.AddComponent(_hdLightDataType);
                
                
#if !UNITY_6000_0_OR_NEWER
                intensity *= 10f;
#endif

                var intensityProperty = _hdLightDataType.GetProperty("intensity");
                if (intensityProperty != null)
                {
                    intensityProperty.SetValue(hdLight, intensity);
                }
                else
                {
                    var setIntensityMethod = _hdLightDataType.GetMethod("SetIntensity");
                    if (setIntensityMethod != null)
                    {
                        var lightUnitType = Type.GetType("UnityEngine.Rendering.HighDefinition.LightUnit, Unity.RenderPipelines.HighDefinition.Runtime");
                        if (lightUnitType != null)
                        {
                            var lux = Enum.Parse(lightUnitType, "Lux");
                            setIntensityMethod.Invoke(hdLight, new object[] { intensity, lux });
                        }
                    }
                }
                
                var lightUnitProperty = _hdLightDataType.GetProperty("lightUnit");
                if (lightUnitProperty != null)
                {
                    var lightUnitType = Type.GetType("UnityEngine.Rendering.HighDefinition.LightUnit, Unity.RenderPipelines.HighDefinition.Runtime");
                    if (lightUnitType != null)
                    {
                        var lux = Enum.Parse(lightUnitType, "Lux");
                        lightUnitProperty.SetValue(hdLight, lux);
                    }
                }
            }
            catch
            {
            }
        }

        private void CreatePreviewInstance()
        {
            if (_originalPrefab == null)
                return;

            if (_previewInstance != null)
            {
                Object.DestroyImmediate(_previewInstance);
            }

            string startName = _originalPrefab.name;
            _previewInstance = Object.Instantiate(_originalPrefab);
            _previewInstance.name = startName;
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;

            _previewInstance.transform.position = Vector3.zero;
            _previewInstance.transform.rotation = Quaternion.identity;
            _previewInstance.transform.localScale = Vector3.one;
            _previewInstance.SetActive(true);

            var allRenderers = _previewInstance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in allRenderers)
            {
                renderer.enabled = true;
            }

            _previewMeshFilter = _previewInstance.GetComponentInChildren<MeshFilter>(true);
            _previewSkinnedRenderer = _previewInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);

            _fullModelBounds = CalculateFullBounds();
        }

        private Bounds CalculateFullBounds()
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            bool hasBounds = false;

            var allRenderers = _previewInstance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in allRenderers)
            {
                if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    Mesh bakedMesh = new Mesh();
                    smr.BakeMesh(bakedMesh);
                    if (!hasBounds)
                    {
                        bounds = bakedMesh.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(bakedMesh.bounds);
                    }

                    Object.DestroyImmediate(bakedMesh);
                }
                else if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() is MeshFilter mf &&
                         mf.sharedMesh != null)
                {
                    if (!hasBounds)
                    {
                        bounds = mf.sharedMesh.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(mf.sharedMesh.bounds);
                    }
                }
            }

            return bounds;
        }

        private void CenterAndFitObject()
        {
            if (_previewInstance == null || _previewUtility == null) return;

            Vector3 geometricCenter = _fullModelBounds.center;
            _previewInstance.transform.position = -geometricCenter + _modelOffset;

            float size = _fullModelBounds.extents.magnitude;
            if (size < 0.001f) size = 1f;

            float fov = _previewUtility.camera.fieldOfView * Mathf.Deg2Rad;
            _cameraDistance = size / Mathf.Tan(fov * 0.5f);
            _cameraDistance = Mathf.Max(_cameraDistance, size * 1.5f);

            _previewUtility.camera.transform.position = new Vector3(0, 0, -_cameraDistance);
            _previewUtility.camera.transform.LookAt(Vector3.zero);

            _cachedCameraPosition = _previewUtility.camera.transform.position;
            _cachedCameraRotation = _previewUtility.camera.transform.rotation;
        }

        private void SetupPreviewLights()
        {
            if (_previewLights != null)
            {
                foreach (var light in _previewLights)
                {
                    if (light != null && light.gameObject != null)
                        Object.DestroyImmediate(light.gameObject);
                }
            }
            
            Light[] existingLights = _previewUtility.lights;
            foreach (Light light in existingLights)
            {
                light.intensity = 0f;
                light.enabled = false;
            }
            
            _previewLights = new Light[2];
            
            GameObject mainLightGO = new GameObject("Preview Main Light");
            mainLightGO.hideFlags = HideFlags.HideAndDontSave;
            Light mainLight = mainLightGO.AddComponent<Light>();
            mainLight.type = LightType.Directional;
            mainLight.transform.rotation = Quaternion.Euler(45, -30, 0);
            mainLight.color = Color.white;
            mainLight.intensity = _isHDRP ? 3f : 0.7f;
            _previewLights[0] = mainLight;
            
            GameObject fillLightGO = new GameObject("Preview Fill Light");
            fillLightGO.hideFlags = HideFlags.HideAndDontSave;
            Light fillLight = fillLightGO.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.transform.rotation = Quaternion.Euler(-10, 150, 0);
            fillLight.color = new Color(0.9f, 0.9f, 1f);
            fillLight.intensity = _isHDRP ? 1f : 0.3f;
            _previewLights[1] = fillLight;
            
            if (_isHDRP)
            {
                SetupHDRPLight(mainLightGO, mainLight.intensity);
                SetupHDRPLight(fillLightGO, fillLight.intensity);
            }

            _previewUtility.AddSingleGO(mainLightGO);
            _previewUtility.AddSingleGO(fillLightGO);
            _previewUtility.AddSingleGO(_previewInstance);
        }

        public void Render(Rect previewRect)
        {
            if (!IsInitialized || _previewUtility == null)
                return;

            if (previewRect.width <= 1 || previewRect.height <= 1)
                return;

            if (_cameraDirty)
            {
                UpdateCameraPosition();
                _cameraDirty = false;
            }

            _previewUtility.camera.transform.position = _cachedCameraPosition;
            _previewUtility.camera.transform.rotation = _cachedCameraRotation;

            bool fog = RenderSettings.fog;
            Unsupported.SetRenderSettingsUseFogNoDirty(false);

            _previewUtility.BeginPreview(previewRect, GUIStyle.none);

            if (_previewSkinnedRenderer != null)
            {
                _previewSkinnedRenderer.updateWhenOffscreen = true;
            }

            _previewUtility.camera.Render();
            _previewUtility.EndAndDrawPreview(previewRect);

            Unsupported.SetRenderSettingsUseFogNoDirty(fog);
        }

        private void UpdateCameraPosition()
        {
            Vector2 angles = _previewDirection + _previewRotation;
            Quaternion rotation = Quaternion.Euler(angles.y, angles.x, 0);

            float distance = _cameraDistance / Mathf.Max(_previewZoom, 0.01f);

            _cachedCameraPosition = rotation * new Vector3(0, 0, -distance);
            _cachedCameraRotation = rotation;
        }

        public void HandleDrag(Vector2 delta)
        {
            _previewRotation += delta * 0.3f;
            _cameraDirty = true;
        }

        public void HandleScroll(float delta)
        {
            _previewZoom = Mathf.Clamp(_previewZoom - delta * 0.05f, 0.1f, 10f);
            _cameraDirty = true;
        }

        public void ResetView()
        {
            _previewDirection = new Vector2(220, 15);
            _previewRotation = Vector2.zero;
            _previewZoom = .7f;
            _modelOffset = new Vector3(0, 0f, 0);
            _cameraDirty = true;
        }

        public void UpdateMeshComponent(MeshResolver.MeshComponentInfo meshInfo, Mesh workingMesh)
        {
            if (_previewInstance == null || meshInfo == null || workingMesh == null)
                return;

            GameObject target = null;

            if (meshInfo.MeshFilter != null)
            {
                var allMeshFilters = _previewInstance.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in allMeshFilters)
                {
                    if (mf.name == meshInfo.MeshFilter.name)
                    {
                        mf.sharedMesh = workingMesh;
                        _previewMeshFilter = mf;
                        _previewSkinnedRenderer = null;
                        _previewMesh = workingMesh;
                        target = mf.gameObject;
                        break;
                    }
                }
            }
            else if (meshInfo.SkinnedRenderer != null)
            {
                var allSkinnedRenderers = _previewInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in allSkinnedRenderers)
                {
                    if (smr.name == meshInfo.SkinnedRenderer.name)
                    {
                        smr.sharedMesh = workingMesh;
                        _previewSkinnedRenderer = smr;
                        _previewMeshFilter = null;
                        _previewMesh = workingMesh;
                        target = smr.gameObject;
                        break;
                    }
                }
            }

            if (target != null && meshInfo.MeshFilter != null)
            {
                var collider = target.GetComponent<MeshCollider>();
                if (collider == null)
                    collider = target.AddComponent<MeshCollider>();
                collider.sharedMesh = workingMesh;
                _meshColliders[target.name] = collider;
            }

            _cameraDirty = true;
        }

        public void Cleanup()
        {
            IsInitialized = false;

            RemoveMeshColliders();

            if (_previewLights != null)
            {
                foreach (var light in _previewLights)
                {
                    if (light != null && light.gameObject != null)
                    {
                        light.enabled = false;
                        light.intensity = 0f;
                        Object.DestroyImmediate(light.gameObject);
                    }
                }
                _previewLights = null;
            }

            if (_previewInstance != null)
            {
                var renderers = _previewInstance.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    renderer.enabled = false;
                }
        
                Object.DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }

            if (_previewMaterials != null)
            {
                foreach (var mat in _previewMaterials)
                {
                    if (mat != null)
                        Object.DestroyImmediate(mat);
                }
                _previewMaterials = null;
            }

            if (_previewUtility != null)
            {
                if (_previewUtility.camera != null)
                {
                    var rt = _previewUtility.camera.targetTexture;
                    if (rt != null)
                    {
                        _previewUtility.camera.targetTexture = null;
                        rt.Release();
                        Object.DestroyImmediate(rt);
                    }
                }
        
                _previewUtility.Cleanup();
                _previewUtility = null;
            }

            _previewMeshFilter = null;
            _previewSkinnedRenderer = null;
            _previewMesh = null;
            _originalPrefab = null;
        }

        public void SetMeshVisibility(MeshResolver.MeshComponentInfo meshInfo, bool visible)
        {
            if (_previewInstance == null || meshInfo == null) return;

            if (meshInfo.MeshFilter != null)
            {
                var allMF = _previewInstance.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in allMF)
                {
                    if (mf.name == meshInfo.MeshFilter.name)
                    {
                        var renderer = mf.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderer.enabled = visible;
                        }

                        break;
                    }
                }
            }
            else if (meshInfo.SkinnedRenderer != null)
            {
                var allSMR = _previewInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in allSMR)
                {
                    if (smr.name == meshInfo.SkinnedRenderer.name)
                    {
                        smr.enabled = visible;
                        break;
                    }
                }
            }
        }

        public void RefreshMaterials(Material material)
        {
            if (_previewInstance == null || material == null) return;

            var allRenderers = _previewInstance.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in allRenderers)
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        public void AddMeshColliders(List<MeshResolver.MeshComponentInfo> meshComponents)
        {
            RemoveMeshColliders();

            for (int i = 0; i < meshComponents.Count; i++)
            {
                var comp = meshComponents[i];
                GameObject target = null;

                if (comp.MeshFilter != null)
                {
                    var allMF = _previewInstance.GetComponentsInChildren<MeshFilter>(true);
                    foreach (var mf in allMF)
                    {
                        if (mf.name == comp.MeshFilter.name)
                        {
                            target = mf.gameObject;
                            break;
                        }
                    }
                }

                if (target != null)
                {
                    var collider = target.GetComponent<MeshCollider>();
                    if (collider == null)
                        collider = target.AddComponent<MeshCollider>();
                    collider.sharedMesh = comp.Mesh;
                    _meshColliders[target.name] = collider;
                }
            }
        }

        public void RemoveMeshColliders()
        {
            foreach (var kvp in _meshColliders)
            {
                if (kvp.Value != null && kvp.Value.gameObject != null)
                    Object.DestroyImmediate(kvp.Value);
            }
            _meshColliders.Clear();
        }
        
        public void SetMeshColliderEnabled(string targetName, bool enabled)
        {
            if (_meshColliders.TryGetValue(targetName, out MeshCollider collider) && collider != null)
            {
                collider.enabled = enabled;
            }
        }

        public int RaycastMesh(Vector2 mousePosition, Rect previewRect, out string meshName, out int triangleIndex, out int subMeshIndex)
        {
            triangleIndex = -1;
            subMeshIndex = -1;
            meshName = null;

            if (_previewUtility == null) return -1;

            Camera cam = _previewUtility.camera;
    
            float localX = mousePosition.x - previewRect.x;
            float localY = previewRect.height - (mousePosition.y - previewRect.y);
    
            float normalizedX = localX / previewRect.width;
            float normalizedY = localY / previewRect.height;
    
            Vector3 viewportPoint = new Vector3(normalizedX, normalizedY, 0f);
            Ray ray = cam.ViewportPointToRay(viewportPoint);

            float closestDist = float.MaxValue;
            string closestName = null;

            foreach (var kvp in _meshColliders)
            {
                RaycastHit hit;
                if (kvp.Value.Raycast(ray, out hit, 1000f))
                {
                    if (hit.distance < closestDist)
                    {
                        closestDist = hit.distance;
                        closestName = kvp.Key;
                        triangleIndex = hit.triangleIndex;
                        subMeshIndex = GetSubMeshFromTriangle(kvp.Value.sharedMesh, triangleIndex);
                    }
                }
            }

            meshName = closestName;
            return closestName != null ? 0 : -1;
        }

        private int GetSubMeshFromTriangle(Mesh mesh, int triangleIndex)
        {
            int triCount = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                if (triangleIndex < triCount + tris.Length / 3)
                    return s;
                triCount += tris.Length / 3;
            }

            return 0;
        }
    }
}