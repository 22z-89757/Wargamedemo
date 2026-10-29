using System;
using UnityEngine;

namespace Wargame.Units
{
    /// <summary>修饰器的运算方式。</summary>
    public enum ModifierOperation
    {
        /// <summary>加到基础值上。</summary>
        Add,
        /// <summary>倍率，多个倍率按 (倍率 - 1) 累加后统一相乘，两个 ×1.2 得到 ×1.4。</summary>
        Multiply,
        /// <summary>直接覆盖计算结果；存在多个时以最后添加的为准。</summary>
        Override
    }

    /// <summary>挂在属性上的一条运行时修饰，可通过 Source 整组移除。</summary>
    public sealed class AttributeModifier
    {
        /// <summary>创建修饰器。</summary>
        /// <param name="attribute">被修饰的属性。</param>
        /// <param name="operation">运算方式。</param>
        /// <param name="magnitude">数值，必须是有限数。</param>
        /// <param name="source">来源对象，通常是施加它的 Buff。</param>
        /// <exception cref="ArgumentOutOfRangeException">数值不是有限数。</exception>
        public AttributeModifier(AttributeType attribute, ModifierOperation operation, float magnitude, object source = null)
        {
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                throw new ArgumentOutOfRangeException(nameof(magnitude), "修饰数值必须是有限数。");
            Attribute = attribute;
            Operation = operation;
            Magnitude = magnitude;
            Source = source;
        }

        /// <summary>被修饰的属性。</summary>
        public AttributeType Attribute { get; }

        /// <summary>运算方式。</summary>
        public ModifierOperation Operation { get; }

        /// <summary>修饰数值。</summary>
        public float Magnitude { get; }

        /// <summary>来源对象，可为空。</summary>
        public object Source { get; }
    }

    /// <summary>可序列化的修饰配置，由 Buff 按层数生成运行时修饰器。</summary>
    [Serializable]
    public struct AttributeModifierSpec
    {
        [Tooltip("被修饰的属性。")]
        public AttributeType Attribute;

        [Tooltip("运算方式。")]
        public ModifierOperation Operation;

        [Tooltip("单层数值。Add 乘以层数；Multiply 的 (倍率 - 1) 乘以层数；Override 不随层数变化。")]
        public float Magnitude;

        /// <summary>创建修饰配置。</summary>
        public AttributeModifierSpec(AttributeType attribute, ModifierOperation operation, float magnitude)
        {
            Attribute = attribute;
            Operation = operation;
            Magnitude = magnitude;
        }

        /// <summary>按层数生成运行时修饰器。</summary>
        /// <param name="stacks">当前层数，至少为 1。</param>
        /// <param name="source">来源对象。</param>
        public AttributeModifier CreateModifier(int stacks, object source)
        {
            stacks = Mathf.Max(1, stacks);
            float magnitude = Operation switch
            {
                ModifierOperation.Add => Magnitude * stacks,
                ModifierOperation.Multiply => 1f + (Magnitude - 1f) * stacks,
                _ => Magnitude
            };
            return new AttributeModifier(Attribute, Operation, magnitude, source);
        }
    }
}
