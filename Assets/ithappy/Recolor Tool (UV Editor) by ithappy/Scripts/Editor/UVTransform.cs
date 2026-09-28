using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ithappy.RecolorToolWindow
{
    public class UVTransform
    {
        private UVEditorMeshData _uvEditorMeshData;
        private VisualElement _textureContainer;
        private CustomUndoManager _undoManager;
        private RecolorToolWindow _recolorToolWindow;
        
        public UVTransform(UVEditorMeshData uvEditorMeshData, VisualElement textureContainer, RecolorToolWindow recolorToolWindow)
        {
            _uvEditorMeshData = uvEditorMeshData;
            _textureContainer = textureContainer;
            _undoManager = new CustomUndoManager(uvEditorMeshData);
            _recolorToolWindow = recolorToolWindow;
        }

        public void StartMovingUVs()
        {
            _undoManager.RecordState();
        }

        public void MoveUVs(MouseMoveEvent evt, List<int> activeVertices)
        {
            var uvMouseDelta = new Vector2(
                evt.mouseDelta.x / _textureContainer.resolvedStyle.width,
                -evt.mouseDelta.y / _textureContainer.resolvedStyle.height
            );

            var uvs = _uvEditorMeshData.CurrentUV;

            foreach (int vertex in activeVertices)
            {
                uvs[vertex] += uvMouseDelta;
            }

            _uvEditorMeshData.SaveTempChanges();
            _recolorToolWindow?.SetWindowChanges();

            RefreshSelectionRect(activeVertices);

            _uvEditorMeshData.UVIsDirty = true;
            _uvEditorMeshData.VerticesMoved = true;
        }

        public void StopMovingUVs()
        {
            _uvEditorMeshData.UVIsDirty = true;
        }

        private Dictionary<int, Vector2> _rotationStartUVs = new Dictionary<int, Vector2>();
        private Vector2 _rotationCenterUV;
        private Vector2 _rotationStartMouseUVPos;
        private float _rotationLastAppliedRotation;

        public void StartRotatingUVs(List<int> activeVertices, GizmoManager gizmoManager, Vector2 uvMousePos)
        {
            _undoManager.RecordState();

            _rotationCenterUV = gizmoManager.RotationCenterUV;
            _rotationStartMouseUVPos = uvMousePos;
            _rotationLastAppliedRotation = float.MinValue;

            List<Vector2> uvs = _uvEditorMeshData.CurrentUV;

            _rotationStartUVs.Clear();

            foreach (var v in activeVertices)
            {
                _rotationStartUVs[v] = uvs[v];
            }
        }

        public void RotateUVs(List<int> activeVertices, Vector2 uvMousePos)
        {
            float angleDeltaToStartInDeg = Vector2.SignedAngle(
                _rotationStartMouseUVPos - _rotationCenterUV,
                uvMousePos - _rotationCenterUV
            );

            if (!Mathf.Approximately(_rotationLastAppliedRotation, angleDeltaToStartInDeg))
            {
                _rotationLastAppliedRotation = angleDeltaToStartInDeg;

                var uvs = _uvEditorMeshData.CurrentUV;

                var angleInRad = angleDeltaToStartInDeg * Mathf.Deg2Rad;
                float cosAngle = Mathf.Cos(angleInRad);
                float sinAngle = Mathf.Sin(angleInRad);

                RotateActiveVertices(activeVertices, uvs, cosAngle, sinAngle);

                _uvEditorMeshData.SaveTempChanges();
                _recolorToolWindow?.SetWindowChanges();
                RefreshSelectionRect(activeVertices);
                
                _uvEditorMeshData.UVIsDirty = true;
                _uvEditorMeshData.VerticesMoved = true;
            }
        }

        private void RotateActiveVertices(List<int> activeVertices, List<Vector2> uvs, float cosAngle, float sinAngle)
        {
            Vector2 center = _rotationCenterUV;

            foreach (var vertex in activeVertices)
            {
                Vector2 startUV = _rotationStartUVs[vertex];
                float translatedX = startUV.x - center.x;
                float translatedY = startUV.y - center.y;

                uvs[vertex] = new Vector2(
                    translatedX * cosAngle - translatedY * sinAngle + center.x,
                    translatedX * sinAngle + translatedY * cosAngle + center.y
                );
            }
        }

        public void StopRotatingUVs()
        {
            _uvEditorMeshData.UVIsDirty = true;
        }

        private Vector2 _scaleCenterUV;
        private Vector2 _scaleStartMouseUVPos;
        private Dictionary<int, Vector2> _scaleStartUVs = new Dictionary<int, Vector2>();

        public void StartScalingUVs(int cornerHandle, List<int> activeVertices, Vector2 uvMousePos)
        {
            _undoManager.RecordState();

            _scaleCenterUV = GetScaleCenter(cornerHandle);
            _scaleStartMouseUVPos = uvMousePos;

            var uvs = _uvEditorMeshData.CurrentUV;
            _scaleStartUVs.Clear();
            foreach (var v in activeVertices)
            {
                _scaleStartUVs[v] = uvs[v];
            }
        }

        private Vector2 GetScaleCenter(int cornerHandle)
        {
            var rect = _realBoundsUV;
            return cornerHandle switch
            {
                1 => new Vector2(rect.xMax, rect.yMax),
                2 => new Vector2(rect.xMax, rect.yMin),
                3 => new Vector2(rect.xMin, rect.yMin),
                4 => new Vector2(rect.xMin, rect.yMax),
                _ => rect.center
            };
        }

        public void ScaleUVs(List<int> activeVertices, Vector2 uvMousePos)
        {
            Vector2 oldDir = _scaleStartMouseUVPos - _scaleCenterUV;
            Vector2 newDir = uvMousePos - _scaleCenterUV;

            float minDir = 0.0001f;
            float oldDirX = Mathf.Abs(oldDir.x) < minDir ? minDir * Mathf.Sign(oldDir.x) : oldDir.x;
            float oldDirY = Mathf.Abs(oldDir.y) < minDir ? minDir * Mathf.Sign(oldDir.y) : oldDir.y;

            if (Mathf.Abs(oldDirX) < minDir) oldDirX = oldDirX >= 0 ? minDir : -minDir;
            if (Mathf.Abs(oldDirY) < minDir) oldDirY = oldDirY >= 0 ? minDir : -minDir;

            float scaleX = newDir.x / oldDirX;
            float scaleY = newDir.y / oldDirY;

            scaleX = Mathf.Max(0.01f, scaleX);
            scaleY = Mathf.Max(0.01f, scaleY);

            var uvs = _uvEditorMeshData.CurrentUV;
            Vector2 center = _scaleCenterUV;

            int count = activeVertices.Count;
            
            bool useVirtualScale = false;

            if (count > 1)
            {
                bool allSamePoint = true;
                Vector2 firstUV = _scaleStartUVs[activeVertices[0]];
                foreach (var v in activeVertices)
                {
                    if (Vector2.Distance(_scaleStartUVs[v], firstUV) > 0.0001f)
                    {
                        allSamePoint = false;
                        break;
                    }
                }

                if (allSamePoint)
                {
                    useVirtualScale = true;
                }
                else
                {
                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;
                    foreach (var v in activeVertices)
                    {
                        var uv = _scaleStartUVs[v];
                        if (uv.x < minX) minX = uv.x;
                        if (uv.y < minY) minY = uv.y;
                        if (uv.x > maxX) maxX = uv.x;
                        if (uv.y > maxY) maxY = uv.y;
                    }
                    
                    if (maxX - minX < 0.0001f || maxY - minY < 0.0001f)
                    {
                        useVirtualScale = true;
                    }
                }
            }

            if (useVirtualScale)
            {
                bool allSamePoint = true;
                Vector2 firstUV = _scaleStartUVs[activeVertices[0]];
                foreach (var v in activeVertices)
                {
                    if (Vector2.Distance(_scaleStartUVs[v], firstUV) > 0.0001f)
                    {
                        allSamePoint = false;
                        break;
                    }
                }
    
                if (!allSamePoint)
                {
                    float realWidth = _realBoundsUV.width;
                    float realHeight = _realBoundsUV.height;
        
                    for (int i = 0; i < count; i++)
                    {
                        int v = activeVertices[i];
                        Vector2 startUV = _scaleStartUVs[v];
        
                        float x = startUV.x - center.x;
                        float y = startUV.y - center.y;
                        
                        float newX = center.x + x * (realWidth > 0.0001f ? scaleX : 1f);
                        float newY = center.y + y * (realHeight > 0.0001f ? scaleY : 1f);
            
                        uvs[v] = new Vector2(newX, newY);
                    }
                }
    
                _virtualScale = new Vector2(scaleX, scaleY);
            }
            else
            {
                _virtualScale = Vector2.one;
    
                for (int i = 0; i < count; i++)
                {
                    int v = activeVertices[i];
                    Vector2 startUV = _scaleStartUVs[v];
    
                    float x = startUV.x - center.x;
                    float y = startUV.y - center.y;
    
                    uvs[v] = new Vector2(
                        center.x + x * scaleX,
                        center.y + y * scaleY
                    );
                }
            }

            _uvEditorMeshData.SaveTempChanges();
            _recolorToolWindow?.SetWindowChanges();
            RefreshSelectionRect(activeVertices);
            
            _uvEditorMeshData.UVIsDirty = true;
            _uvEditorMeshData.VerticesMoved = true;
        }

        public void StopScalingUVs()
        {
            _virtualScale = Vector2.one;
            _uvEditorMeshData.UVIsDirty = true;
        }

        public void PasteUVCenter(List<int> activeVertices, Vector2 targetCenter)
        {
            if (activeVertices.Count == 0)
                return;
    
            _undoManager.RecordState();
    
            var currentCenter = _uvEditorMeshData.MarkedBoundsUV.center;
            var offset = targetCenter - currentCenter;
    
            var uvs = _uvEditorMeshData.CurrentUV;
            foreach (var v in activeVertices)
            {
                uvs[v] += offset;
            }
    
            _uvEditorMeshData.SaveTempChanges();
            RefreshSelectionRect(activeVertices);
            _uvEditorMeshData.UVIsDirty = true;
    
            _recolorToolWindow?.SetWindowChanges();
        }

        private Rect _realBoundsUV;
        private Vector2 _virtualScale = Vector2.one;
        
        public void RefreshSelectionRect(HashSet<int> activeVertices)
        {
            RefreshSelectionRect(new List<int>(activeVertices));
        }

        public void RefreshSelectionRect(List<int> activeVertices)
        {
            var uvs = _uvEditorMeshData.CurrentUV;

            if (uvs == null)
                return;

            _uvEditorMeshData.MarkedBoundsUV = new Rect();
            _uvEditorMeshData.MarkedBoundsUV.xMin = 10f;
            _uvEditorMeshData.MarkedBoundsUV.yMin = 10f;
            _uvEditorMeshData.MarkedBoundsUV.xMax = -10f;
            _uvEditorMeshData.MarkedBoundsUV.yMax = -10f;

            if (activeVertices.Count > 0)
            {
                foreach (var v in activeVertices)
                {
                    var uv = uvs[v];

                    if (uv.x < _uvEditorMeshData.MarkedBoundsUV.xMin) _uvEditorMeshData.MarkedBoundsUV.xMin = uv.x;
                    if (uv.y < _uvEditorMeshData.MarkedBoundsUV.yMin) _uvEditorMeshData.MarkedBoundsUV.yMin = uv.y;
                    if (uv.x > _uvEditorMeshData.MarkedBoundsUV.xMax) _uvEditorMeshData.MarkedBoundsUV.xMax = uv.x;
                    if (uv.y > _uvEditorMeshData.MarkedBoundsUV.yMax) _uvEditorMeshData.MarkedBoundsUV.yMax = uv.y;
                }
                
                _realBoundsUV = _uvEditorMeshData.MarkedBoundsUV;

                float cx = _uvEditorMeshData.MarkedBoundsUV.center.x;
                float cy = _uvEditorMeshData.MarkedBoundsUV.center.y;
                float width = _uvEditorMeshData.MarkedBoundsUV.width;
                float height = _uvEditorMeshData.MarkedBoundsUV.height;

                const float MIN_SIZE = 0.02f;
                
                if (width < MIN_SIZE)
                {
                    width = MIN_SIZE * _virtualScale.x;
                }

                if (height < MIN_SIZE)
                {
                    height = MIN_SIZE * _virtualScale.y;
                }

                _uvEditorMeshData.MarkedBoundsUV.xMin = cx - width * 0.5f;
                _uvEditorMeshData.MarkedBoundsUV.xMax = cx + width * 0.5f;
                _uvEditorMeshData.MarkedBoundsUV.yMin = cy - height * 0.5f;
                _uvEditorMeshData.MarkedBoundsUV.yMax = cy + height * 0.5f;
            }
            else
            {
                _uvEditorMeshData.MarkedBoundsUV.xMin = 0f;
                _uvEditorMeshData.MarkedBoundsUV.yMin = 0f;
                _uvEditorMeshData.MarkedBoundsUV.xMax = 0f;
                _uvEditorMeshData.MarkedBoundsUV.yMax = 0f;
            }
        }

        public void Undo()
        {
            _undoManager.Undo();
        }

        public void Redo()
        {
            _undoManager.Redo();
        }
    }
}