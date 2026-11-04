using System;
using UnityEngine;

namespace Wargame.Battle
{
    /// <summary>统一射击配置；曲线横轴为格距，纵轴为命中率修正百分点。</summary>
    [Serializable]
    public sealed class ShootSettings
    {
        [Tooltip("阶梯命中修正：X 为整数格距，Y 为百分点；10 格默认无修正。")]
        public AnimationCurve DistanceHitChanceModifier = CreateDefaultCurve();

        public bool TryValidate(out string error)
        {
            error = null;
            if (DistanceHitChanceModifier == null || DistanceHitChanceModifier.length == 0)
            {
                error = "ShootSettings 需要至少含一个节点的距离命中率曲线。";
                return false;
            }

            Keyframe[] keys = DistanceHitChanceModifier.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!IsFinite(keys[i].time) || !IsFinite(keys[i].value) || keys[i].time < 0 ||
                    keys[i].time != Mathf.Floor(keys[i].time) || (i > 0 && keys[i].time <= keys[i - 1].time))
                {
                    error = "距离曲线节点必须使用递增的非负整数格距和有限的修正百分点。";
                    return false;
                }
            }

            return true;
        }

        /// <summary>编辑器配置时统一为阶梯切线和端点保持，不补造缺失配置。</summary>
        public void NormalizeCurve()
        {
            if (!TryValidate(out _)) return;
            DistanceHitChanceModifier.keys = StepKeys(DistanceHitChanceModifier.keys);
            DistanceHitChanceModifier.preWrapMode = WrapMode.ClampForever;
            DistanceHitChanceModifier.postWrapMode = WrapMode.ClampForever;
#if UNITY_EDITOR
            for (int i = 0; i < DistanceHitChanceModifier.length; i++)
            {
                UnityEditor.AnimationUtility.SetKeyLeftTangentMode(DistanceHitChanceModifier, i, UnityEditor.AnimationUtility.TangentMode.Constant);
                UnityEditor.AnimationUtility.SetKeyRightTangentMode(DistanceHitChanceModifier, i, UnityEditor.AnimationUtility.TangentMode.Constant);
            }
#endif
        }

        public Snapshot CreateSnapshot()
        {
            if (!TryValidate(out string error)) throw new ArgumentException(error);
            return new Snapshot(StepKeys(DistanceHitChanceModifier.keys));
        }

        private static AnimationCurve CreateDefaultCurve() => new AnimationCurve(StepKeys(new[]
        {
            new Keyframe(0, 20), new Keyframe(5, 10), new Keyframe(10, 0),
            new Keyframe(15, -10), new Keyframe(20, -20)
        })) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };

        private static Keyframe[] StepKeys(Keyframe[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].inTangent = keys[i].outTangent = float.PositiveInfinity;
                keys[i].weightedMode = WeightedMode.None;
            }
            return keys;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>能力持有的独立只读快照，不暴露可变曲线。</summary>
        public sealed class Snapshot
        {
            private readonly AnimationCurve curve;
            internal Snapshot(Keyframe[] keys)
            {
                curve = new AnimationCurve(keys)
                { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
            }

            public float GetHitChanceModifier(int distance) => curve.Evaluate(distance);
        }
    }
}
