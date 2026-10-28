using UnityEditor;
using Wargame.GridSystem;

namespace Wargame.Editor
{
    /// <summary>只读展示烘焙地图，避免绕过烘焙校验直接修改资产。</summary>
    [CustomEditor(typeof(MapData))]
    internal sealed class MapDataEditor : UnityEditor.Editor
    {
        /// <summary>显示地图尺寸、全部序列化格子及有效性检查结果。</summary>
        public override void OnInspectorGUI()
        {
            var map = (MapData)target;
            using (new EditorGUI.DisabledScope(true))
                DrawDefaultInspector();
            EditorGUILayout.HelpBox(map.TryValidate(out string error)
                ? "只读地图数据。请在 GridManager 中编辑草稿并重新烘焙。" : error,
                string.IsNullOrEmpty(error) ? MessageType.Info : MessageType.Error);
        }
    }
}
