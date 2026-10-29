using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wargame.Units
{
    /// <summary>
    /// 记录单位当前拥有的状态标记。HashSet 保存"是否拥有"，另用引用计数记录来源数量：
    /// 两个 Buff 同时授予燃烧时，移除其中一个不会提前清掉标记。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TagComponent : MonoBehaviour
    {
        [SerializeField, Tooltip("初始化时添加的常驻标记，例如机械单位的中毒免疫。")]
        private List<GameplayTag> initialTags = new List<GameplayTag>();

        private readonly HashSet<GameplayTag> activeTags = new HashSet<GameplayTag>();
        private readonly Dictionary<GameplayTag, int> tagCounts = new Dictionary<GameplayTag, int>();
        private bool initialized;

        /// <summary>标记从无到有（true）或从有到无（false）时触发；计数变化但状态不变时不触发。</summary>
        public event Action<GameplayTag, bool> TagChanged;

        /// <summary>当前拥有的全部标记。</summary>
        public IReadOnlyCollection<GameplayTag> ActiveTags
        {
            get
            {
                EnsureInitialized();
                return activeTags;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>写入初始标记；可重复调用，只执行一次。</summary>
        internal void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;
            foreach (GameplayTag tag in initialTags)
                AddTag(tag);
        }

        /// <summary>是否拥有指定标记。</summary>
        public bool HasTag(GameplayTag tag)
        {
            EnsureInitialized();
            return activeTags.Contains(tag);
        }

        /// <summary>是否拥有集合中的任意一个标记；空集合返回 false。</summary>
        public bool HasAny(IEnumerable<GameplayTag> tags)
        {
            EnsureInitialized();
            if (tags == null)
                return false;
            foreach (GameplayTag tag in tags)
                if (activeTags.Contains(tag))
                    return true;
            return false;
        }

        /// <summary>是否拥有集合中的全部标记；空集合返回 true。</summary>
        public bool HasAll(IEnumerable<GameplayTag> tags)
        {
            EnsureInitialized();
            if (tags == null)
                return true;
            foreach (GameplayTag tag in tags)
                if (!activeTags.Contains(tag))
                    return false;
            return true;
        }

        /// <summary>指定标记当前被多少个来源授予。</summary>
        public int GetTagCount(GameplayTag tag)
        {
            EnsureInitialized();
            return tagCounts.TryGetValue(tag, out int count) ? count : 0;
        }

        /// <summary>增加一次标记来源；None 会被忽略。</summary>
        public void AddTag(GameplayTag tag)
        {
            EnsureInitialized();
            if (tag == GameplayTag.None)
                return;

            tagCounts.TryGetValue(tag, out int count);
            tagCounts[tag] = count + 1;
            if (activeTags.Add(tag))
                TagChanged?.Invoke(tag, true);
        }

        /// <summary>减少一次标记来源，计数归零时移除标记。</summary>
        /// <returns>调用前是否拥有该标记。</returns>
        public bool RemoveTag(GameplayTag tag)
        {
            EnsureInitialized();
            if (!tagCounts.TryGetValue(tag, out int count))
                return false;

            if (count > 1)
            {
                tagCounts[tag] = count - 1;
                return true;
            }

            tagCounts.Remove(tag);
            activeTags.Remove(tag);
            TagChanged?.Invoke(tag, false);
            return true;
        }

        /// <summary>无视来源数量直接清除标记，用于驱散、复活等强制效果。</summary>
        /// <returns>调用前是否拥有该标记。</returns>
        public bool ClearTag(GameplayTag tag)
        {
            EnsureInitialized();
            if (!tagCounts.Remove(tag))
                return false;
            activeTags.Remove(tag);
            TagChanged?.Invoke(tag, false);
            return true;
        }
    }
}
