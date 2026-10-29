using System;
using UnityEngine;

namespace Wargame.Units
{
    /// <summary>单位所属阵营。</summary>
    public enum Faction
    {
        /// <summary>玩家小队。</summary>
        Player,
        /// <summary>敌人。</summary>
        Enemy,
        /// <summary>中立单位，例如平民。</summary>
        Neutral
    }

    /// <summary>
    /// 可行动单位的根组件与访问中枢：持有标记、属性、Buff、能力四个组件的引用，
    /// 提供运行时唯一 ID，并向回合系统暴露回合开始/结束钩子。棋盘占用也以它为引用。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TagComponent), typeof(CharacterAttribute), typeof(BuffComponent))]
    [RequireComponent(typeof(AbilityComponent))]
    public class Unit : MonoBehaviour
    {
        private static int nextUnitId = 1;

        [SerializeField, Tooltip("所属阵营。")]
        private Faction faction = Faction.Player;

        [SerializeField, Tooltip("显示名称供后期UI使用，为空时使用物体名称。")]
        private string displayName;

        private int unitId;
        private TagComponent tags;
        private CharacterAttribute attributes;
        private BuffComponent buffs;
        private AbilityComponent abilities;
        private bool initialized;

        /// <summary>单位死亡（获得 Dead 标记）时触发。</summary>
        public event Action<Unit> Died;
        /// <summary>根物体销毁前触发，用于释放棋盘占用与注册。</summary>
        public event Action<Unit> Destroyed;

        /// <summary>单位回合开始，行动点恢复、Buff 与冷却结算之后触发。</summary>
        public event Action<Unit> TurnStarted;

        /// <summary>单位回合结束，Buff 倒计时之后触发。</summary>
        public event Action<Unit> TurnEnded;

        /// <summary>运行时唯一 ID，本次运行内不重复，不用于存档。</summary>
        public int UnitId
        {
            get
            {
                EnsureInitialized();
                return unitId;
            }
        }

        /// <summary>所属阵营。</summary>
        public Faction Faction
        {
            get => faction;
            set => faction = value;
        }

        /// <summary>显示名称。</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        // 访问组件前先完成初始化，保证 Awake 未执行（EditMode、提前访问）时死亡监听也已就绪。

        /// <summary>状态标记。</summary>
        public TagComponent Tags => Get(ref tags);

        /// <summary>数值属性。</summary>
        public CharacterAttribute Attributes => Get(ref attributes);

        /// <summary>Buff 管理器。</summary>
        public BuffComponent Buffs => Get(ref buffs);

        /// <summary>能力管理器。</summary>
        public AbilityComponent Abilities => Get(ref abilities);

        /// <summary>是否存活（没有 Dead 标记）。</summary>
        public bool IsAlive => !Tags.HasTag(GameplayTag.Dead);

        /// <summary>是否处于自己的回合，由 BeginTurn/EndTurn 维护。</summary>
        public bool IsTurnActive { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (abilities != null) abilities.CancelAll();
            Destroyed?.Invoke(this);
            if (attributes != null)
                attributes.AttributeChanged -= HandleAttributeChanged;
        }

        private void OnDisable()
        {
            if (abilities != null) abilities.CancelAll();
        }

        /// <summary>分配 ID 并订阅生命变化；可重复调用，只执行一次。</summary>
        internal void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;
            unitId = nextUnitId++;
            // initialized 已置位，Get 内的 EnsureInitialized 会直接返回。
            Tags.EnsureInitialized();
            Attributes.EnsureInitialized();
            Attributes.AttributeChanged += HandleAttributeChanged;
        }

        /// <summary>
        /// 开始本单位的回合：行动点回满 → Buff 回合开始钩子（眩晕等可在此清空行动点）→ 冷却递减。
        /// 已死亡或已在回合中时忽略。
        /// </summary>
        public void BeginTurn()
        {
            EnsureInitialized();
            if (!IsAlive || IsTurnActive)
                return;

            IsTurnActive = true;
            CharacterAttribute attrs = Attributes;
            if (attrs.HasAttribute(AttributeType.ActionPoints) && attrs.TryGetValue(AttributeType.MaxActionPoints, out float maxAp))
                attrs.SetBaseValue(AttributeType.ActionPoints, maxAp);
            Buffs.HandleOwnerTurnStarted();
            Abilities.HandleOwnerTurnStarted();
            TurnStarted?.Invoke(this);
        }

        /// <summary>结束本单位的回合：Buff 回合结束钩子与倒计时。不在回合中时忽略；回合内死亡仍会正常结束。</summary>
        public void EndTurn()
        {
            EnsureInitialized();
            if (!IsTurnActive)
                return;

            Buffs.HandleOwnerTurnEnded();
            IsTurnActive = false;
            TurnEnded?.Invoke(this);
        }

        /// <summary>立即死亡：添加 Dead 标记并取消激活中的能力。已死亡时忽略。</summary>
        public void Kill()
        {
            EnsureInitialized();
            if (!IsAlive)
                return;
            Tags.AddTag(GameplayTag.Dead);
            Abilities.CancelAll();
            Died?.Invoke(this);
        }

        private void HandleAttributeChanged(AttributeType attribute, float oldValue, float newValue)
        {
            if (attribute == AttributeType.Health && newValue <= 0f && oldValue > 0f)
                Kill();
        }

        private T Get<T>(ref T cache) where T : Component
        {
            EnsureInitialized();
            if (cache == null)
                cache = GetComponent<T>();
            if (cache == null)
                throw new InvalidOperationException($"{name} 缺少 {typeof(T).Name} 组件。");
            return cache;
        }
    }
}
