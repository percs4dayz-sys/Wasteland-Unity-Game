using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ithappy.RecolorToolWindow
{
    public class LineDrawer : ImmediateModeElement
    {
        public struct SimpleVertex
        {
            public Vector2 Pos;
            public Color Tint;

            public SimpleVertex(Vector2 pos, Color tint)
            {
                Pos = pos;
                Tint = tint;
            }
        }

        private struct LineState
        {
            public Vector2? StartVertex;
            public Vector2? PreviousVertex;
            public int VertexCount;
        }

        private const int MAX_NUM_OF_VERTICES = 65000;
        private const int VERTICES_PER_LINE = 6;
        private const int DEFAULT_POOL_CAPACITY = 1024;

        private List<SimpleVertex> _vertices;
        private float _cachedHeight;
        private LineState _state;

        public bool InvertVertical;
        public List<SimpleVertex> Vertices => _vertices;
        public float LineWidth = 2f;
        public Color LineColor = new Color(1f, 1f, 1f, 1f);

        private static Stack<List<SimpleVertex>> _vertexPool = new Stack<List<SimpleVertex>>();
        private static int _poolMaxSize = 10;

        private Vector2 _lastFrom;
        private Vector2 _lastTo;
        private Vector2 _lastNormal;
        private float _lastLineWidth;

        private static int _opacityPropertyId;
        private static bool _shaderPropertiesCached;

        private Material _cachedMaterial;
        private bool _isDisposed;

        public LineDrawer()
        {
            _vertices = GetVertexList();
        }

        public bool NextLinesCanBeDrawn(int numOfLinesToDraw = 1)
        {
            return _state.VertexCount + numOfLinesToDraw * VERTICES_PER_LINE <= MAX_NUM_OF_VERTICES;
        }

        public void ClearVertices()
        {
            ReturnVertexListToPool(_vertices);
            _vertices = GetVertexList();
            _state = new LineState();
            _cachedHeight = style.height.value.value;
        }

        private List<SimpleVertex> GetVertexList()
        {
            return _vertexPool.Count > 0 ? _vertexPool.Pop() : new List<SimpleVertex>(DEFAULT_POOL_CAPACITY);
        }

        private void ReturnVertexListToPool(List<SimpleVertex> list)
        {
            if (list == null) return;

            list.Clear();
            if (_vertexPool.Count < _poolMaxSize)
            {
                _vertexPool.Push(list);
            }
        }

        public void StartNewLine()
        {
            _state.StartVertex = null;
            _state.PreviousVertex = null;
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void AddVertex(float x, float y)
        {
            AddVertex(new Vector2(x, y), LineColor, false);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void AddVertex(Vector2 vertex, bool closeLoop = false)
        {
            AddVertex(vertex, LineColor, closeLoop);
        }

        public void AddVertex(Vector2 vertex, Color color, bool closeLoop = false)
        {
            if (InvertVertical && _cachedHeight > 0)
            {
                vertex.y = _cachedHeight - vertex.y;
            }

            if (_state.PreviousVertex.HasValue)
            {
                if (_state.VertexCount + VERTICES_PER_LINE > MAX_NUM_OF_VERTICES)
                    throw new System.Exception(
                        $"LineDrawer exceeded vertex limit: {_state.VertexCount + VERTICES_PER_LINE} > {MAX_NUM_OF_VERTICES}");

                DrawLineSegment(_state.PreviousVertex.Value, vertex, color);
            }

            _state.PreviousVertex = vertex;

            if (closeLoop && _state.StartVertex.HasValue)
            {
                DrawLineSegment(_state.PreviousVertex.Value, _state.StartVertex.Value, color);
            }

            if (!_state.StartVertex.HasValue)
                _state.StartVertex = vertex;
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawLineSegment(Vector2 from, Vector2 to, Color color)
        {
            Vector2 normal;
            if (_lastFrom == from && _lastTo == to && Mathf.Approximately(_lastLineWidth, LineWidth))
            {
                normal = _lastNormal;
            }
            else
            {
                Vector2 vector = to - from;
                float sqrMagnitude = vector.sqrMagnitude;
                if (sqrMagnitude < 0.0001f)
                    return;

                float invMagnitude = 1f / Mathf.Sqrt(sqrMagnitude);
                normal = new Vector2(vector.y * invMagnitude, -vector.x * invMagnitude);

                _lastFrom = from;
                _lastTo = to;
                _lastNormal = normal;
                _lastLineWidth = LineWidth;
            }

            float halfWidth = LineWidth * 0.5f;
            Vector2 offset = normal * halfWidth;
            Vector2 fromPlusOffset = from + offset;
            Vector2 fromMinusOffset = from - offset;
            Vector2 toPlusOffset = to + offset;
            Vector2 toMinusOffset = to - offset;

            if (InvertVertical)
            {
                _vertices.Add(new SimpleVertex(fromPlusOffset, color));
                _vertices.Add(new SimpleVertex(toPlusOffset, color));
                _vertices.Add(new SimpleVertex(fromMinusOffset, color));

                _vertices.Add(new SimpleVertex(toPlusOffset, color));
                _vertices.Add(new SimpleVertex(toMinusOffset, color));
                _vertices.Add(new SimpleVertex(fromMinusOffset, color));
            }
            else
            {
                _vertices.Add(new SimpleVertex(fromMinusOffset, color));
                _vertices.Add(new SimpleVertex(toPlusOffset, color));
                _vertices.Add(new SimpleVertex(fromPlusOffset, color));

                _vertices.Add(new SimpleVertex(fromMinusOffset, color));
                _vertices.Add(new SimpleVertex(toMinusOffset, color));
                _vertices.Add(new SimpleVertex(toPlusOffset, color));
            }

            _state.VertexCount += VERTICES_PER_LINE;
        }

        private Material GetMaterial()
        {
            if (_cachedMaterial == null && !_isDisposed)
            {
                _cachedMaterial = new Material(Shader.Find("UI/Default"));
            }

            return _cachedMaterial;
        }

        private void CacheShaderProperties()
        {
            if (_shaderPropertiesCached) return;

            _opacityPropertyId = Shader.PropertyToID("_Opacity");
            _shaderPropertiesCached = true;
        }

        private float GetEffectiveOpacity()
        {
            float effectiveOpacity = 1.0f;
            VisualElement current = this;
            while (current != null)
            {
                effectiveOpacity *= current.resolvedStyle.opacity;
                current = current.parent;
            }

            return effectiveOpacity;
        }

        protected override void ImmediateRepaint()
        {
            if (_isDisposed) return;

            var material = GetMaterial();
            if (material == null || _vertices.Count == 0)
                return;

            CacheShaderProperties();

            if (material.HasProperty(_opacityPropertyId))
            {
                material.SetFloat(_opacityPropertyId, GetEffectiveOpacity());
            }

            GL.PushMatrix();

            var cr = parent.ChangeCoordinatesTo(this, parent.contentRect);

            for (int p = 0; p < material.passCount; p++)
            {
                if (!material.SetPass(p))
                    continue;

                GL.Begin(GL.TRIANGLES);

                int vertexCount = _vertices.Count;
                var verticesArray = _vertices;

                for (int i = 0; i < vertexCount; i += 3)
                {
                    var v0 = verticesArray[i];
                    var v1 = verticesArray[i + 1];
                    var v2 = verticesArray[i + 2];

                    var pos0 = v0.Pos;
                    var pos1 = v1.Pos;
                    var pos2 = v2.Pos;

                    var tint0 = v0.Tint;
                    var tint1 = v1.Tint;
                    var tint2 = v2.Tint;
                    
                    if (pos0.y < cr.yMin && pos1.y < cr.yMin && pos2.y < cr.yMin) continue;
                    if (pos0.y > cr.yMax && pos1.y > cr.yMax && pos2.y > cr.yMax) continue;
                    if (pos0.x < cr.xMin && pos1.x < cr.xMin && pos2.x < cr.xMin) continue;
                    if (pos0.x > cr.xMax && pos1.x > cr.xMax && pos2.x > cr.xMax) continue;
                    
                    if (pos0.y < cr.yMin && pos1.y < cr.yMin)
                    {
                        pos0 = ProjectAndLimitTo(pos0, pos2, cr.yMin);
                        pos1 = ProjectAndLimitTo(pos1, pos2, cr.yMin);
                    }
                    else if (pos1.y < cr.yMin && pos2.y < cr.yMin)
                    {
                        pos1 = ProjectAndLimitTo(pos1, pos0, cr.yMin);
                        pos2 = ProjectAndLimitTo(pos2, pos0, cr.yMin);
                    }
                    else if (pos2.y < cr.yMin && pos0.y < cr.yMin)
                    {
                        pos2 = ProjectAndLimitTo(pos2, pos1, cr.yMin);
                        pos0 = ProjectAndLimitTo(pos0, pos1, cr.yMin);
                    }
                    else
                    {
                        if (pos0.y < cr.yMin) pos0 = ProjectAndLimitTo(pos0, pos1, cr.yMin);
                        else if (pos1.y < cr.yMin) pos1 = ProjectAndLimitTo(pos1, pos2, cr.yMin);
                        else if (pos2.y < cr.yMin) pos2 = ProjectAndLimitTo(pos2, pos0, cr.yMin);
                    }
                    
                    if (pos0.y > cr.yMax && pos1.y > cr.yMax)
                    {
                        pos0 = ProjectAndLimitTo(pos0, pos2, cr.yMax);
                        pos1 = ProjectAndLimitTo(pos1, pos2, cr.yMax);
                    }
                    else if (pos1.y > cr.yMax && pos2.y > cr.yMax)
                    {
                        pos1 = ProjectAndLimitTo(pos1, pos0, cr.yMax);
                        pos2 = ProjectAndLimitTo(pos2, pos0, cr.yMax);
                    }
                    else if (pos2.y > cr.yMax && pos0.y > cr.yMax)
                    {
                        pos2 = ProjectAndLimitTo(pos2, pos1, cr.yMax);
                        pos0 = ProjectAndLimitTo(pos0, pos1, cr.yMax);
                    }
                    else
                    {
                        if (pos0.y > cr.yMax) pos0 = ProjectAndLimitTo(pos0, pos1, cr.yMax);
                        else if (pos1.y > cr.yMax) pos1 = ProjectAndLimitTo(pos1, pos2, cr.yMax);
                        else if (pos2.y > cr.yMax) pos2 = ProjectAndLimitTo(pos2, pos0, cr.yMax);
                    }

                    GL.Color(tint0);
                    GL.Vertex3(pos0.x, pos0.y, 0f);
                    GL.Color(tint1);
                    GL.Vertex3(pos1.x, pos1.y, 0f);
                    GL.Color(tint2);
                    GL.Vertex3(pos2.x, pos2.y, 0f);
                }

                GL.End();
            }

            GL.PopMatrix();
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private Vector2 ProjectAndLimitTo(Vector2 a, Vector2 b, float yTarget)
        {
            float dy = b.y - a.y;
            if (Mathf.Abs(dy) < 0.0001f)
                return new Vector2(a.x, yTarget);

            float t = (yTarget - a.y) / dy;
            return new Vector2(a.x + t * (b.x - a.x), yTarget);
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<LineDrawer, UxmlTraits>
        {
        }
#endif
    }
}
