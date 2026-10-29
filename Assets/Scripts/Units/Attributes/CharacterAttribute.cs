using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wargame.Units
{
    /// <summary>Inspector 中配置的属性默认值。</summary>
    [Serializable]
    public struct AttributeDefault
    {
        [Tooltip("属性类型。")]
        public AttributeType Attribute;

        [Tooltip("初始基础值。")]
        public float Value;

        /// <summary>创建默认值配置。</summary>
        public AttributeDefault(AttributeType attribute, float value)
        {
            Attribute = attribute;
            Value = value;
        }
    }

    /// <summary>
    /// 单位数值属性，参照 GAS 的 BaseValue + Modifier 结构：
    /// 伤害、治疗、消耗行动点修改 BaseValue；Buff 通过修饰器影响 CurrentValue。
    /// CurrentValue = Override ?? (Base + ΣAdd) × max(0, 1 + Σ(Mul - 1))。
    /// Health 与 ActionPoints 的基础值和当前值都限制在 [0, 对应上限]。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAttribute : MonoBehaviour
    {
        [SerializeField, Tooltip("初始化时创建的属性及其基础值；未列出的属性视为不存在。")]
        private List<AttributeDefault> defaultValues = new List<AttributeDefault>
        {
            new AttributeDefault(AttributeType.MaxHealth, 10f),
            new AttributeDefault(AttributeType.Health, 10f),
            new AttributeDefault(AttributeType.MaxActionPoints, 2f),
            new AttributeDefault(AttributeType.ActionPoints, 2f),
            new AttributeDefault(AttributeType.Mobility, 6f),
            new AttributeDefault(AttributeType.Aim, 65f),
            new AttributeDefault(AttributeType.Defense, 0f),
            new AttributeDefault(AttributeType.Dodge, 0f),
            new AttributeDefault(AttributeType.Armor, 0f),
            new AttributeDefault(AttributeType.CritChance, 0f),
            new AttributeDefault(AttributeType.Will, 50f),
            new AttributeDefault(AttributeType.SightRange, 12f)
        };

        /// <summary>被上限约束的属性及其上限属性。</summary>
        private static readonly Dictionary<AttributeType, AttributeType> ClampedBy =
            new Dictionary<AttributeType, AttributeType>
            {
                { AttributeType.Health, AttributeType.MaxHealth },
                { AttributeType.ActionPoints, AttributeType.MaxActionPoints }
            };

        private sealed class AttributeState
        {
            public float BaseValue;
            public float CurrentValue;
            public readonly List<AttributeModifier> Modifiers = new List<AttributeModifier>();
        }

        private readonly Dictionary<AttributeType, AttributeState> states = new Dictionary<AttributeType, AttributeState>();
        private bool initialized;

        /// <summary>CurrentValue 变化时触发，参数为属性、旧值、新值。</summary>
        public event Action<AttributeType, float, float> AttributeChanged;

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>按 Inspector 默认值创建属性；可重复调用，只执行一次，不触发事件。</summary>
        internal void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;

            foreach (AttributeDefault entry in defaultValues)
            {
                if (states.ContainsKey(entry.Attribute))
                {
                    Debug.LogWarning($"属性 {entry.Attribute} 重复配置，已忽略后续条目。", this);
                    continue;
                }
                states.Add(entry.Attribute, new AttributeState { BaseValue = IsFinite(entry.Value) ? entry.Value : 0f });
            }

            // 先计算上限类属性，受约束属性在级联中按上限钳制。
            var types = new List<AttributeType>(states.Keys);
            foreach (AttributeType type in types)
                if (!ClampedBy.ContainsKey(type))
                    Recalculate(type, false);
            foreach (AttributeType type in types)
                if (ClampedBy.ContainsKey(type))
                    Recalculate(type, false);
        }

        /// <summary>是否拥有指定属性。</summary>
        public bool HasAttribute(AttributeType attribute)
        {
            EnsureInitialized();
            return states.ContainsKey(attribute);
        }

        /// <summary>读取叠加修饰后的当前值。</summary>
        /// <exception cref="KeyNotFoundException">单位没有该属性。</exception>
        public float GetValue(AttributeType attribute) => GetState(attribute).CurrentValue;

        /// <summary>读取基础值。</summary>
        /// <exception cref="KeyNotFoundException">单位没有该属性。</exception>
        public float GetBaseValue(AttributeType attribute) => GetState(attribute).BaseValue;

        /// <summary>尝试读取当前值，属性不存在时返回 false。</summary>
        public bool TryGetValue(AttributeType attribute, out float value)
        {
            EnsureInitialized();
            if (states.TryGetValue(attribute, out AttributeState state))
            {
                value = state.CurrentValue;
                return true;
            }
            value = 0f;
            return false;
        }

        /// <summary>创建属性或重置已有属性的基础值，用于运行时生成单位。保留已有修饰器。</summary>
        /// <exception cref="ArgumentOutOfRangeException">数值不是有限数。</exception>
        public void DefineAttribute(AttributeType attribute, float baseValue)
        {
            EnsureInitialized();
            if (!IsFinite(baseValue))
                throw new ArgumentOutOfRangeException(nameof(baseValue), "属性值必须是有限数。");
            if (!states.TryGetValue(attribute, out AttributeState state))
            {
                state = new AttributeState();
                states.Add(attribute, state);
            }
            state.BaseValue = ClampBase(attribute, baseValue);
            Recalculate(attribute, true);
        }

        /// <summary>设置基础值；受约束属性会被钳制到 [0, 上限]。</summary>
        /// <exception cref="KeyNotFoundException">单位没有该属性。</exception>
        /// <exception cref="ArgumentOutOfRangeException">数值不是有限数。</exception>
        public void SetBaseValue(AttributeType attribute, float value)
        {
            AttributeState state = GetState(attribute);
            if (!IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "属性值必须是有限数。");
            state.BaseValue = ClampBase(attribute, value);
            Recalculate(attribute, true);
        }

        /// <summary>按增量修改基础值，例如伤害传入负数。</summary>
        /// <returns>钳制后基础值的实际变化量。</returns>
        /// <exception cref="KeyNotFoundException">单位没有该属性。</exception>
        /// <exception cref="ArgumentOutOfRangeException">增量不是有限数。</exception>
        public float ApplyBaseDelta(AttributeType attribute, float delta)
        {
            AttributeState state = GetState(attribute);
            if (!IsFinite(delta))
                throw new ArgumentOutOfRangeException(nameof(delta), "增量必须是有限数。");
            float before = state.BaseValue;
            SetBaseValue(attribute, before + delta);
            return state.BaseValue - before;
        }

        /// <summary>添加修饰器；属性不存在时抛出异常。</summary>
        /// <exception cref="ArgumentNullException">修饰器为空。</exception>
        /// <exception cref="KeyNotFoundException">单位没有被修饰的属性。</exception>
        public void AddModifier(AttributeModifier modifier)
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));
            GetState(modifier.Attribute).Modifiers.Add(modifier);
            Recalculate(modifier.Attribute, true);
        }

        /// <summary>移除指定修饰器。</summary>
        /// <returns>修饰器原本是否存在。</returns>
        public bool RemoveModifier(AttributeModifier modifier)
        {
            EnsureInitialized();
            if (modifier == null || !states.TryGetValue(modifier.Attribute, out AttributeState state))
                return false;
            if (!state.Modifiers.Remove(modifier))
                return false;
            Recalculate(modifier.Attribute, true);
            return true;
        }

        /// <summary>移除来源相同的全部修饰器，Buff 移除时用它撤销效果。</summary>
        /// <returns>移除的数量。</returns>
        public int RemoveModifiersFromSource(object source)
        {
            EnsureInitialized();
            if (source == null)
                return 0;

            int removed = 0;
            var changed = new List<AttributeType>();
            foreach (KeyValuePair<AttributeType, AttributeState> pair in states)
            {
                int count = pair.Value.Modifiers.RemoveAll(m => ReferenceEquals(m.Source, source));
                if (count == 0)
                    continue;
                removed += count;
                changed.Add(pair.Key);
            }
            foreach (AttributeType type in changed)
                Recalculate(type, true);
            return removed;
        }

        /// <summary>读取属性上的全部修饰器。</summary>
        /// <exception cref="KeyNotFoundException">单位没有该属性。</exception>
        public IReadOnlyList<AttributeModifier> GetModifiers(AttributeType attribute) => GetState(attribute).Modifiers;

        private AttributeState GetState(AttributeType attribute)
        {
            EnsureInitialized();
            if (!states.TryGetValue(attribute, out AttributeState state))
                throw new KeyNotFoundException($"单位 {name} 没有属性 {attribute}。");
            return state;
        }

        /// <summary>受约束属性的基础值钳制到 [0, 上限当前值]；上限属性缺失时只限制下界。</summary>
        private float ClampBase(AttributeType attribute, float value)
        {
            if (!ClampedBy.TryGetValue(attribute, out AttributeType maxType))
                return value;
            value = Mathf.Max(0f, value);
            if (states.TryGetValue(maxType, out AttributeState maxState))
                value = Mathf.Min(value, Mathf.Max(0f, maxState.CurrentValue));
            return value;
        }

        /// <summary>重新计算当前值，并级联更新以它为上限的属性。</summary>
        private void Recalculate(AttributeType attribute, bool notify)
        {
            AttributeState state = states[attribute];
            float oldValue = state.CurrentValue;
            float newValue = Evaluate(state);

            if (ClampedBy.ContainsKey(attribute))
            {
                // 上限下降后，基础值同样被钳制，避免 Buff 结束后出现超过上限的基础值。
                state.BaseValue = ClampBase(attribute, state.BaseValue);
                newValue = ClampBase(attribute, newValue);
            }

            state.CurrentValue = newValue;
            if (notify && !Mathf.Approximately(oldValue, newValue))
                AttributeChanged?.Invoke(attribute, oldValue, newValue);

            foreach (KeyValuePair<AttributeType, AttributeType> pair in ClampedBy)
                if (pair.Value == attribute && states.ContainsKey(pair.Key))
                    Recalculate(pair.Key, notify);
        }

        private static float Evaluate(AttributeState state)
        {
            float additive = 0f;
            float multiplierBonus = 0f;
            AttributeModifier overrideModifier = null;
            foreach (AttributeModifier modifier in state.Modifiers)
            {
                switch (modifier.Operation)
                {
                    case ModifierOperation.Add:
                        additive += modifier.Magnitude;
                        break;
                    case ModifierOperation.Multiply:
                        multiplierBonus += modifier.Magnitude - 1f;
                        break;
                    case ModifierOperation.Override:
                        overrideModifier = modifier;
                        break;
                }
            }

            if (overrideModifier != null)
                return overrideModifier.Magnitude;
            return (state.BaseValue + additive) * Mathf.Max(0f, 1f + multiplierBonus);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
