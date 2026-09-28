using UnityEngine;
using UnityEngine.UIElements;

namespace ithappy.RecolorToolWindow
{
    public class GizmoManager
    {
        private const float GIZMO_SIZE_FACTOR = 4f;
        private const float ROTATE_SIZE_FACTOR = 6f;
        private const float MOVE_AREA_MARGIN = 0f;
        private const float OUTWARD_MARGIN_SMALL = 4f;
        private const float OUTWARD_MARGIN_LARGE = 10f;
        private const float LINE_WIDTH = 2f;
        private const float ROTATE_VERTICAL_OFFSET = 15f;

        private readonly Color UVGizmoColor = new Color(0, 1f, 0f);

        public enum GizmoCorner
        {
            None = 0,
            BL = 1,
            TL = 2,
            TR = 3,
            BR = 4
        }

        public enum GizmoType
        {
            None,
            Scale,
            Rotate,
            Move
        }

        private VisualElement _canvas;
        private VisualElement _textureContainer;

        private LineDrawer _rectUVGizmoContainer;

        private VisualElement _blScaleGizmo;
        private VisualElement _tlScaleGizmo;
        private VisualElement _trScaleGizmo;
        private VisualElement _brScaleGizmo;
        
        private Rect _gizmoScaleRectBL;
        private Rect _gizmoScaleRectTL;
        private Rect _gizmoScaleRectTR;
        private Rect _gizmoScaleRectBR;

        private VisualElement _rotateGizmo;
        private VisualElement _rotateGizmoIcon;
        private Texture2D _rotateGizmoSprite;

        private VisualElement _centerMoveGizmo;

        private GizmoCorner _draggedScaleCorner = GizmoCorner.None;
        private bool _isRotating;
        private bool _isMoving;

        private float _cacheTextureWidth;
        private float _cacheTextureHeight;
        private float _cacheSize;
        private Rect _cacheSelRect;
        private bool _cacheShowScaleAndRotate;
        private float _cacheLeft;
        private float _cacheBottom;

        public VisualElement CenterMoveGizmo => _centerMoveGizmo;
        public LineDrawer RectUVGizmoContainer => _rectUVGizmoContainer;

        public int RectGizmoDraggedScaleHandle
        {
            get => (int)_draggedScaleCorner;
            set => _draggedScaleCorner = (GizmoCorner)value;
        }

        public int RectGizmoDraggedRotationHandle
        {
            get => _isRotating ? 1 : 0;
            set => _isRotating = value == 1;
        }

        public int RectGizmoDraggedMoveHandle
        {
            get => _isMoving ? 1 : 0;
            set => _isMoving = value == 1;
        }

        public Vector2 RotationCenterUV => _gizmoScaleRectBL.max + (_gizmoScaleRectTR.min - _gizmoScaleRectBL.max) * 0.5f;

        public GizmoManager(VisualElement canvas, VisualElement textureContainer)
        {
            _canvas = canvas;
            _textureContainer = textureContainer;
            Initialize();
        }

        private void Initialize()
        {
            _rectUVGizmoContainer = new LineDrawer
            {
                name = "RectUVGizmoContainer",
                style = { position = Position.Absolute, display = DisplayStyle.Flex }
            };
            _canvas.Add(_rectUVGizmoContainer);

            _blScaleGizmo = CreateGizmoElement(_rectUVGizmoContainer, UnityDefaultCursor.CursorType.ResizeUpRight, "gizmo-style");
            _tlScaleGizmo = CreateGizmoElement(_rectUVGizmoContainer, UnityDefaultCursor.CursorType.ResizeUpLeft, "gizmo-style");
            _trScaleGizmo = CreateGizmoElement(_rectUVGizmoContainer, UnityDefaultCursor.CursorType.ResizeUpRight, "gizmo-style");
            _brScaleGizmo = CreateGizmoElement(_rectUVGizmoContainer, UnityDefaultCursor.CursorType.ResizeUpLeft, "gizmo-style");
            
            _rotateGizmo = CreateRotateGizmoElement(_rectUVGizmoContainer);
    
            _centerMoveGizmo = CreateGizmoElement(_rectUVGizmoContainer, UnityDefaultCursor.CursorType.MoveArrow);
        }

        private VisualElement CreateRotateGizmoElement(VisualElement container)
        {
            var gizmo = new VisualElement
            {
                style =
                {
                    display = DisplayStyle.None,
                    position = Position.Absolute,
                    cursor = UnityDefaultCursor.DefaultCursor(UnityDefaultCursor.CursorType.RotateArrow),
                    backgroundColor = new Color(0, 0, 0, 0)
                }
            };
    
            _rotateGizmoIcon = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = new Length(100, LengthUnit.Percent),
                    height = new Length(100, LengthUnit.Percent),
                }
            };
    
#if UNITY_6000_0_OR_NEWER
    _rotateGizmoIcon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
#else
            _rotateGizmoIcon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
#endif
    
            gizmo.Add(_rotateGizmoIcon);
            container.Add(gizmo);
            return gizmo;
        }

        public void SetRotateGizmoSprite(Texture2D sprite)
        {
            _rotateGizmoSprite = sprite;
            if (_rotateGizmoIcon != null && sprite != null)
            {
                _rotateGizmoIcon.style.backgroundImage = sprite;
        
#if UNITY_6000_0_OR_NEWER
        _rotateGizmoIcon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
#else
#pragma warning disable CS0618
                _rotateGizmoIcon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
#pragma warning restore CS0618
#endif
            }
        }

        private VisualElement CreateGizmoElement(VisualElement container, UnityDefaultCursor.CursorType cursorType, string cssClass = "")
        {
            var gizmo = new VisualElement
            {
                style =
                {
                    display = DisplayStyle.None,
                    position = Position.Absolute,
                    cursor = UnityDefaultCursor.DefaultCursor(cursorType)
                }
            };

            if (!string.IsNullOrEmpty(cssClass))
            {
                gizmo.AddToClassList(cssClass);
            }

            container.Add(gizmo);
            return gizmo;
        }
        
        private void UpdateGizmo(VisualElement gizmo, bool show, Vector2 uvPosition, float widthInPx, float heightInPx)
        {
            if (gizmo == null) return;

            gizmo.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                gizmo.style.left = new Length(uvPosition.x * 100f, LengthUnit.Percent);
                gizmo.style.bottom = new Length(uvPosition.y * 100f, LengthUnit.Percent);
                gizmo.style.width = widthInPx;
                gizmo.style.height = heightInPx;
            }
        }

        public bool TryGetGizmoAtPosition(Vector2 pos, out GizmoType type, out GizmoCorner corner)
        {
            type = GizmoType.None;
            corner = GizmoCorner.None;

            if (_gizmoScaleRectBL.Contains(pos))
            {
                type = GizmoType.Scale;
                corner = GizmoCorner.BL;
                _draggedScaleCorner = GizmoCorner.BL;
                return true;
            }

            if (_gizmoScaleRectTL.Contains(pos))
            {
                type = GizmoType.Scale;
                corner = GizmoCorner.TL;
                _draggedScaleCorner = GizmoCorner.TL;
                return true;
            }

            if (_gizmoScaleRectTR.Contains(pos))
            {
                type = GizmoType.Scale;
                corner = GizmoCorner.TR;
                _draggedScaleCorner = GizmoCorner.TR;
                return true;
            }

            if (_gizmoScaleRectBR.Contains(pos))
            {
                type = GizmoType.Scale;
                corner = GizmoCorner.BR;
                _draggedScaleCorner = GizmoCorner.BR;
                return true;
            }

            if (_rotateGizmo?.worldBound.Contains(pos) == true)
            {
                type = GizmoType.Rotate;
                _isRotating = true;
                return true;
            }

            if (_centerMoveGizmo?.worldBound.Contains(pos) == true)
            {
                type = GizmoType.Move;
                _isMoving = true;
                return true;
            }

            return false;
        }

        public void ReDrawGizmo(float size, Rect activeRectInUVs, bool showScaleGizmos, bool showRotateGizmo, bool showMoveGizmo)
        {
            float currentLeft = _textureContainer.style.left.value.value;
            float currentBottom = _textureContainer.style.bottom.value.value;
    
            if (!NeedsRedraw(size, activeRectInUVs, showScaleGizmos, currentLeft, currentBottom))
                return;

            _cacheSize = size;
            _cacheSelRect = activeRectInUVs;
            _cacheShowScaleAndRotate = showScaleGizmos;
            _cacheTextureWidth = _textureContainer.resolvedStyle.width;
            _cacheTextureHeight = _textureContainer.resolvedStyle.height;
            _cacheLeft = currentLeft;
            _cacheBottom = currentBottom;

            DrawSelectionRect(size, activeRectInUVs);
    
            UpdateScaleGizmos(activeRectInUVs, size, showScaleGizmos);
            UpdateRotateGizmos(showRotateGizmo);
            UpdateMoveGizmo(showMoveGizmo);
        }

        private bool NeedsRedraw(float size, Rect selRect, bool showScaleAndRotate, float currentLeft, float currentBottom)
        {
            return size != _cacheSize ||
                   selRect != _cacheSelRect ||
                   showScaleAndRotate != _cacheShowScaleAndRotate ||
                   _textureContainer.resolvedStyle.width != _cacheTextureWidth ||
                   _textureContainer.resolvedStyle.height != _cacheTextureHeight ||
                   currentLeft != _cacheLeft ||
                   currentBottom != _cacheBottom;
        }
        
        private void DrawSelectionRect(float size, Rect selRectInUVs)
        {
            _rectUVGizmoContainer.style.width = _textureContainer.style.width;
            _rectUVGizmoContainer.style.height = _textureContainer.style.height;
            _rectUVGizmoContainer.style.left = _textureContainer.style.left;
            _rectUVGizmoContainer.style.bottom = _textureContainer.style.bottom;
            _rectUVGizmoContainer.InvertVertical = true;
            _rectUVGizmoContainer.LineWidth = LINE_WIDTH;
            _rectUVGizmoContainer.LineColor = UVGizmoColor;
            _rectUVGizmoContainer.ClearVertices();

            _rectUVGizmoContainer.StartNewLine();

            float visualPadding = LINE_WIDTH * 2f;
            float paddingUV = visualPadding / size;
            
            float xMin = (selRectInUVs.xMin - paddingUV) * size;
            float xMax = (selRectInUVs.xMax + paddingUV) * size;
            float yMin = (selRectInUVs.yMin - paddingUV) * size;
            float yMax = (selRectInUVs.yMax + paddingUV) * size;
            
            _rectUVGizmoContainer.AddVertex(new Vector2(xMin, yMin));
            _rectUVGizmoContainer.AddVertex(new Vector2(xMin, yMax));
            _rectUVGizmoContainer.AddVertex(new Vector2(xMax, yMax));
            _rectUVGizmoContainer.AddVertex(new Vector2(xMax, yMin), closeLoop: true);

            _rectUVGizmoContainer.MarkDirtyRepaint();
        }
        
        private void UpdateScaleGizmos(Rect activeRect, float size, bool show)
        {
            float lineWidth = _rectUVGizmoContainer.LineWidth;
            float gizmoSize = lineWidth * GIZMO_SIZE_FACTOR;
            float halfSize = lineWidth * 2f;
            float margin = !show ? OUTWARD_MARGIN_LARGE : OUTWARD_MARGIN_SMALL;

            float invSize = 1f / size;
            float halfSizeUV = halfSize * invSize;
            float marginUV = margin * invSize;
    
            _gizmoScaleRectBL = CalculateScaleRect(activeRect.xMin, activeRect.yMin, halfSizeUV, -marginUV, -marginUV);
            UpdateGizmo(_blScaleGizmo, show, _gizmoScaleRectBL.min, gizmoSize, gizmoSize);

            _gizmoScaleRectTL = CalculateScaleRect(activeRect.xMin, activeRect.yMax, halfSizeUV, -marginUV, marginUV);
            UpdateGizmo(_tlScaleGizmo, show, _gizmoScaleRectTL.min, gizmoSize, gizmoSize);

            _gizmoScaleRectTR = CalculateScaleRect(activeRect.xMax, activeRect.yMax, halfSizeUV, marginUV, marginUV);
            UpdateGizmo(_trScaleGizmo, show, _gizmoScaleRectTR.min, gizmoSize, gizmoSize);

            _gizmoScaleRectBR = CalculateScaleRect(activeRect.xMax, activeRect.yMin, halfSizeUV, marginUV, -marginUV);
            UpdateGizmo(_brScaleGizmo, show, _gizmoScaleRectBR.min, gizmoSize, gizmoSize);
        }
        
        private Rect CalculateScaleRect(float x, float y, float halfSize, float offsetX, float offsetY)
        {
            return new Rect
            {
                min = new Vector2(x - halfSize + offsetX, y - halfSize + offsetY),
                max = new Vector2(x + halfSize + offsetX, y + halfSize + offsetY)
            };
        }
        
        private void UpdateRotateGizmos(bool show)
        {
            float rotateSizePx = _rectUVGizmoContainer.LineWidth * ROTATE_SIZE_FACTOR;
            
            float centerX = _cacheSelRect.center.x;
            float rotateWidthUV = rotateSizePx / _cacheSize;
            
            float verticalOffsetUV = ROTATE_VERTICAL_OFFSET / _cacheSize;

            Vector2 position = new Vector2(
                centerX - (rotateWidthUV * 0.5f),
                _gizmoScaleRectTL.min.y + verticalOffsetUV
            );

            UpdateGizmo(_rotateGizmo, show, position, rotateSizePx, rotateSizePx);
        }

        private void UpdateMoveGizmo(bool show)
        {
            if (!show)
            {
                UpdateGizmo(_centerMoveGizmo, false, Vector2.zero, 0, 0);
                return;
            }
    
            float padding = (LINE_WIDTH * 1.5f);
    
            float mgWidth = (_cacheSelRect.width * _cacheSize) + (padding * 2f) - MOVE_AREA_MARGIN;
            float mgHeight = (_cacheSelRect.height * _cacheSize) + (padding * 2f) - MOVE_AREA_MARGIN;

            if (mgWidth > 0 && mgHeight > 0)
            {
                float paddingUV = padding / _cacheSize;
                float marginUVX = (MOVE_AREA_MARGIN * 0.5f) / _cacheSize;
                float marginUVY = (MOVE_AREA_MARGIN * 0.5f) / _cacheSize;

                Vector2 uvPos = new Vector2(
                    _cacheSelRect.xMin - paddingUV + marginUVX,
                    _cacheSelRect.yMin - paddingUV + marginUVY
                );

                UpdateGizmo(_centerMoveGizmo, true, uvPos, mgWidth, mgHeight);
            }
        }

        public bool AnyHandle()
        {
            return _draggedScaleCorner != GizmoCorner.None || _isMoving || _isRotating;
        }
    }
}