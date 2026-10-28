using System;
using UnityEditor;
using UnityEngine;
using Wargame.GridSystem;

namespace Wargame.Editor
{
    /// <summary>棋盘的 Inspector 编辑面板及 Scene 单格笔刷。</summary>
    [CustomEditor(typeof(GridManager))]
    internal sealed class GridManagerEditor : UnityEditor.Editor
    {
        [SerializeField, Tooltip("笔刷修改的属性范围。")]
        private GridBrushMode brushMode = GridBrushMode.Both;
        [SerializeField, Tooltip("可通行格的笔刷移动代价；全障碍自动使用 -1。")]
        private int brushMoveCost = 1;
        [SerializeField, Tooltip("笔刷写入的障碍状态。")]
        private BlockState brushBlockState;
        [SerializeField, Tooltip("是否显示各格子的移动代价。")]
        private bool showCosts;
        [SerializeField, Tooltip("创建新地图资产时使用的名称。")]
        private string mapName;

        private bool editingEnabled;
        private string message;
        private MessageType messageType;
        private GridMapBrushStroke stroke;
        private int capturedControl;

        /// <summary>Scene 笔刷是否启用；关闭时立即结束当前笔画。</summary>
        internal bool EditingEnabled
        {
            get => editingEnabled;
            set
            {
                if (editingEnabled == value)
                    return;
                if (!value)
                    EndStroke();
                editingEnabled = value;
                SceneView.RepaintAll();
            }
        }

        /// <summary>初始化资产名称并订阅撤销后的界面刷新。</summary>
        private void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(mapName))
                mapName = target.name + "_Map";
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        /// <summary>释放鼠标控制及笔画，并移除编辑器事件订阅。</summary>
        private void OnDisable()
        {
            EndStroke();
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        /// <summary>绘制尺寸、笔刷、导入和烘焙面板；播放期间只允许查看。</summary>
        public override void OnInspectorGUI()
        {
            var grid = (GridManager)target;
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            bool canEdit = GridMapEditorActions.CanEdit(grid);
            using (new EditorGUI.DisabledScope(!canEdit))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceMap"), new GUIContent("地图资产"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("drawGizmos"), new GUIContent("显示棋盘 Gizmos"));
                serializedObject.ApplyModifiedProperties();

                DrawDimensions(grid);
                EditorGUILayout.Space();
                DrawBrushPanel();
                EditorGUILayout.Space();
                DrawBakePanel(grid);
            }

            if (!canEdit)
            {
                EditingEnabled = false;
                EditorGUILayout.HelpBox("播放期间或运行时棋盘初始化后，地图编辑、导入与烘焙均已禁用。", MessageType.Info);
            }
            else if (!grid.HasSupportedTransform())
                EditorGUILayout.HelpBox("棋盘应保持世界旋转为零，自身及父级缩放均为 1。", MessageType.Warning);
            else if (grid.SourceMap == null)
                EditorGUILayout.HelpBox("草稿随场景保存。烘焙后才会用于运行时；未关联资产时运行默认空棋盘。", MessageType.Info);
            else
                EditorGUILayout.HelpBox("运行时加载地图资产；草稿修改后请重新烘焙。蓝色高亮仅表示当前笔刷格。", MessageType.Info);

            if (!string.IsNullOrEmpty(message))
                EditorGUILayout.HelpBox(message, messageType);
        }

        /// <summary>绘制尺寸输入，提交修改时保留草稿重叠区域并记录撤销。</summary>
        /// <param name="grid">当前棋盘。</param>
        private void DrawDimensions(GridManager grid)
        {
            EditorGUILayout.LabelField("棋盘草稿", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int width = Mathf.Max(1, EditorGUILayout.DelayedIntField(new GUIContent("宽度（X）"), grid.Width));
            int height = Mathf.Max(1, EditorGUILayout.DelayedIntField(new GUIContent("高度（Z）"), grid.Height));
            float size = EditorGUILayout.DelayedFloatField(new GUIContent("格子尺寸"), grid.CellSize);
            if (EditorGUI.EndChangeCheck())
            {
                EndStroke();
                try
                {
                    GridMapEditorActions.ResizeDraft(grid, width, height, size);
                    message = "草稿尺寸已调整，重叠区域数据已保留。";
                    messageType = MessageType.Info;
                }
                catch (Exception exception) { ShowError(exception); }
            }

            if (grid.Draft == null || grid.Draft.Width != grid.Width || grid.Draft.Height != grid.Height)
            {
                EditorGUILayout.HelpBox("草稿尺寸与棋盘不一致，请先同步。", MessageType.Warning);
                if (GUILayout.Button("初始化 / 同步草稿尺寸"))
                    GridMapEditorActions.ResizeDraft(grid, grid.Width, grid.Height, grid.CellSize);
            }
        }

        /// <summary>绘制笔刷开关、属性模式与移动代价文字开关。</summary>
        private void DrawBrushPanel()
        {
            EditorGUILayout.LabelField("Scene 笔刷", EditorStyles.boldLabel);
            EditingEnabled = EditorGUILayout.Toggle(new GUIContent("开启地图编辑", "左键拖动涂抹，Alt 保留场景导航。"), editingEnabled);
            brushMode = (GridBrushMode)EditorGUILayout.Popup("笔刷模式", (int)brushMode,
                new[] { "同时修改", "只修改 MoveCost", "只修改 BlockState" });
            using (new EditorGUI.DisabledScope(brushMode == GridBrushMode.MoveCost))
                brushBlockState = (BlockState)EditorGUILayout.EnumPopup("BlockState", brushBlockState);
            bool paintsFullBlocked = brushMode != GridBrushMode.MoveCost && brushBlockState == BlockState.FullBlocked;
            using (new EditorGUI.DisabledScope(brushMode == GridBrushMode.BlockState || paintsFullBlocked))
            {
                if (paintsFullBlocked)
                    EditorGUILayout.IntField("MoveCost（不可通行）", -1);
                else
                    brushMoveCost = Mathf.Max(1, EditorGUILayout.IntField("MoveCost", brushMoveCost));
            }
            EditorGUI.BeginChangeCheck();
            showCosts = EditorGUILayout.Toggle("显示移动代价", showCosts);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            EditorGUILayout.HelpBox("黄色：半障碍；红色：全障碍（代价 -1）。整次左键拖动可一次撤销。", MessageType.None);
        }

        /// <summary>绘制导入和烘焙按钮，显示输出资产路径。</summary>
        /// <param name="grid">当前棋盘。</param>
        private void DrawBakePanel(GridManager grid)
        {
            EditorGUILayout.LabelField("地图烘焙", EditorStyles.boldLabel);
            mapName = EditorGUILayout.TextField("新地图名称", mapName);
            EditorGUILayout.LabelField("输出目录", GridMapBaker.OutputFolder);
            using (new EditorGUI.DisabledScope(grid.SourceMap == null))
            {
                if (GUILayout.Button("从地图资产导入草稿"))
                {
                    EndStroke();
                    try
                    {
                        GridMapEditorActions.ImportDraft(grid);
                        message = "地图已复制为场景草稿，可撤销本次导入。";
                        messageType = MessageType.Info;
                    }
                    catch (Exception exception) { ShowError(exception); }
                }
            }

            if (GUILayout.Button("烘焙地图数据", GUILayout.Height(30)))
                Bake(grid, false);
            if (GUILayout.Button("另存为新地图"))
                Bake(grid, true);
            if (grid.SourceMap != null)
                EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(grid.SourceMap), GUILayout.Height(32));
        }

        /// <summary>完成当前笔画后烘焙，并将成功路径或错误原因显示在 Inspector。</summary>
        /// <param name="grid">当前棋盘。</param>
        /// <param name="saveAsNew">是否始终创建新资产。</param>
        private void Bake(GridManager grid, bool saveAsNew)
        {
            EndStroke();
            try
            {
                MapData map = GridMapBaker.Bake(grid, mapName, saveAsNew);
                message = "烘焙完成：" + AssetDatabase.GetAssetPath(map);
                messageType = MessageType.Info;
                EditorGUIUtility.PingObject(map);
            }
            catch (Exception exception) { ShowError(exception); }
        }

        /// <summary>显示失败原因，不将预期的输入错误写入 Unity 错误日志。</summary>
        /// <param name="exception">操作失败的异常。</param>
        private void ShowError(Exception exception)
        {
            message = exception.Message;
            messageType = MessageType.Error;
        }

        /// <summary>绘制 Scene 辅助信息，并仅在启用笔刷时申请空白区域的左键控制。</summary>
        private void OnSceneGUI()
        {
            var grid = (GridManager)target;
            if (!GridMapEditorActions.CanEdit(grid) || !grid.HasSupportedTransform() ||
                !grid.gameObject.scene.IsValid() || grid.Draft == null)
            {
                EndStroke();
                return;
            }

            Event current = Event.current;
            int control = GUIUtility.GetControlID("Wargame.MapBrush".GetHashCode(), FocusType.Passive);
            if (editingEnabled && !current.alt && current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(control);

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (current.type == EventType.Repaint)
                DrawSceneOverlay(grid, ray);
            HandlePointerEvent(current, ray, control, HandleUtility.nearestControl == control);
            if (editingEnabled && (current.type == EventType.MouseMove || current.type == EventType.Used))
                SceneView.RepaintAll();
        }

        /// <summary>处理笔刷鼠标事件；关闭编辑、Alt 导航和其他控件操作均不被消耗。</summary>
        /// <param name="current">当前 Scene 输入事件。</param>
        /// <param name="ray">鼠标对应的世界射线。</param>
        /// <param name="control">本笔刷的 GUI 控件编号。</param>
        /// <param name="canAcquire">鼠标未被其他场景控件优先占用时为 true。</param>
        /// <returns>本次事件被笔刷处理并消耗时返回 true。</returns>
        internal bool HandlePointerEvent(Event current, Ray ray, int control, bool canAcquire)
        {
            var grid = (GridManager)target;
            if (!editingEnabled || !GridMapEditorActions.CanEdit(grid) || current.alt)
            {
                EndStroke();
                return false;
            }
            if (current.type == EventType.Ignore || current.type == EventType.MouseLeaveWindow)
            {
                EndStroke();
                return false;
            }
            if (current.type == EventType.MouseUp && current.button == 0 && stroke != null)
            {
                EndStroke();
                current.Use();
                return true;
            }

            bool hit = TryGetDraftCoordinates(grid, ray, out Vector2Int coordinates);
            if (current.type == EventType.MouseDown && current.button == 0 && hit && canAcquire && GUIUtility.hotControl == 0)
            {
                stroke = new GridMapBrushStroke(grid, brushMode, brushMoveCost, brushBlockState);
                capturedControl = control;
                GUIUtility.hotControl = control;
                stroke.Paint(coordinates);
                current.Use();
                return true;
            }
            if (current.type == EventType.MouseDrag && current.button == 0 && stroke != null &&
                GUIUtility.hotControl == capturedControl)
            {
                if (hit) stroke.Paint(coordinates);
                else stroke.BreakLine();
                current.Use();
                return true;
            }

            return false;
        }

        /// <summary>用棋盘水平面拾取草稿格子，不依赖场景碰撞体。</summary>
        /// <param name="grid">目标棋盘。</param>
        /// <param name="ray">鼠标对应世界射线。</param>
        /// <param name="coordinates">成功时为草稿坐标，失败时为默认值。</param>
        /// <returns>射线命中有效草稿格子时返回 true。</returns>
        internal static bool TryGetDraftCoordinates(GridManager grid, Ray ray, out Vector2Int coordinates)
        {
            coordinates = default;
            if (grid == null || grid.Draft == null || !grid.HasSupportedTransform() ||
                !new Plane(Vector3.up, grid.Origin).Raycast(ray, out float distance))
                return false;
            Vector3 point = ray.GetPoint(distance) - grid.Origin;
            float x = point.x / grid.CellSize;
            float y = point.z / grid.CellSize;
            if (float.IsNaN(x) || float.IsNaN(y) || x < 0 || y < 0 || x >= grid.Width || y >= grid.Height)
                return false;
            coordinates = new Vector2Int(Mathf.FloorToInt(x), Mathf.FloorToInt(y));
            return grid.Draft.TryGetCell(coordinates, out _);
        }

        /// <summary>显示移动代价与悬停格，保存并恢复 Handles 绘制颜色。</summary>
        /// <param name="grid">草稿棋盘。</param>
        /// <param name="ray">鼠标世界射线。</param>
        private void DrawSceneOverlay(GridManager grid, Ray ray)
        {
            if (showCosts)
            {
                for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    var position = new Vector2Int(x, y);
                    if (grid.Draft.TryGetCell(position, out MapCellData cell))
                        Handles.Label(DraftCenter(grid, position), cell.MoveCost.ToString(), EditorStyles.boldLabel);
                }
            }

            if (!editingEnabled || !TryGetDraftCoordinates(grid, ray, out Vector2Int hovered))
                return;
            Vector3 center = DraftCenter(grid, hovered);
            float half = grid.CellSize * 0.5f;
            var corners = new[]
            {
                center + new Vector3(-half, 0, -half), center + new Vector3(-half, 0, half),
                center + new Vector3(half, 0, half), center + new Vector3(half, 0, -half)
            };
            Handles.DrawSolidRectangleWithOutline(corners, new Color(0.1f, 0.7f, 1f, 0.2f), Color.cyan);
            Handles.Label(center + Vector3.forward * half, hovered.ToString());
        }

        /// <summary>计算草稿格子的世界中心，并略微抬高避免地面重叠。</summary>
        /// <param name="grid">草稿棋盘。</param>
        /// <param name="coordinates">格子逻辑坐标。</param>
        /// <returns>供 Scene 辅助绘制使用的世界中心。</returns>
        private static Vector3 DraftCenter(GridManager grid, Vector2Int coordinates)
        {
            return grid.Origin + new Vector3((coordinates.x + 0.5f) * grid.CellSize,
                grid.CellSize * 0.06f, (coordinates.y + 0.5f) * grid.CellSize);
        }

        /// <summary>结束当前笔画并释放本编辑器持有的鼠标控制。</summary>
        private void EndStroke()
        {
            stroke?.Dispose();
            stroke = null;
            if (capturedControl != 0 && GUIUtility.hotControl == capturedControl)
                GUIUtility.hotControl = 0;
            capturedControl = 0;
        }

        /// <summary>撤销重做时停止旧笔画并刷新 Inspector 与 Scene。</summary>
        private void OnUndoRedo()
        {
            EndStroke();
            serializedObject.Update();
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
