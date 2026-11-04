using System;
using System.Collections.Generic;
using UnityEngine;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>战斗状态。</summary>
    public enum BattleState
    {
        /// <summary>尚未开始。</summary>
        NotStarted,
        /// <summary>进行中。</summary>
        Running,
        /// <summary>已结束。</summary>
        Ended
    }

    /// <summary>
    /// 总回合管理器的最小骨架：维护单位注册表，按阵营顺序轮转回合，
    /// 并在阵营回合开始/结束时调用单位的 BeginTurn/EndTurn。
    /// 胜负判定、AI 调度、行动阶段等尚未设计。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleTurnManager : MonoBehaviour
    {
        [SerializeField, Tooltip("阵营行动顺序，所有阵营各行动一次为一轮。")]
        private List<Faction> factionOrder = new List<Faction> { Faction.Player, Faction.Enemy };

        private readonly List<Unit> units = new List<Unit>();
        private int factionIndex;

        /// <summary>阵营回合开始，所属单位 BeginTurn 之后触发。</summary>
        public event Action<Faction> FactionTurnStarted;

        /// <summary>阵营回合结束，所属单位 EndTurn 之后触发。</summary>
        public event Action<Faction> FactionTurnEnded;

        /// <summary>单位加入注册表后触发。</summary>
        public event Action<Unit> UnitRegistered;

        /// <summary>单位离开注册表后触发。</summary>
        public event Action<Unit> UnitUnregistered;

        /// <summary>战斗状态。</summary>
        public BattleState State { get; private set; } = BattleState.NotStarted;

        /// <summary>当前轮数，从 1 开始；未开始时为 0。</summary>
        public int RoundNumber { get; private set; }

        /// <summary>当前行动的阵营；未开始时为顺序中的第一个阵营。</summary>
        public Faction CurrentFaction => factionOrder[factionIndex];

        /// <summary>已注册的全部单位。</summary>
        public IReadOnlyList<Unit> Units => units;

        /// <summary>设置阵营行动顺序，只能在战斗开始前调用。</summary>
        /// <exception cref="ArgumentException">顺序为空或包含重复阵营。</exception>
        /// <exception cref="InvalidOperationException">战斗已经开始。</exception>
        public void SetFactionOrder(IEnumerable<Faction> order)
        {
            if (State != BattleState.NotStarted)
                throw new InvalidOperationException("战斗开始后不能修改阵营顺序。");
            var list = new List<Faction>(order ?? throw new ArgumentNullException(nameof(order)));
            if (list.Count == 0)
                throw new ArgumentException("阵营顺序不能为空。", nameof(order));
            if (new HashSet<Faction>(list).Count != list.Count)
                throw new ArgumentException("阵营顺序不能包含重复阵营。", nameof(order));
            factionOrder = list;
            factionIndex = 0;
        }

        /// <summary>注册单位；重复注册或空引用会被忽略。战斗中注册的单位从其阵营的下个回合开始行动。</summary>
        /// <returns>是否新增注册。</returns>
        public bool RegisterUnit(Unit unit)
        {
            if (unit == null || units.Contains(unit))
                return false;
            units.Add(unit);
            UnitRegistered?.Invoke(unit);
            return true;
        }

        /// <summary>注销单位，不会调用其 EndTurn。</summary>
        /// <returns>单位原本是否已注册。</returns>
        public bool UnregisterUnit(Unit unit)
        {
            if (!units.Remove(unit)) return false;
            UnitUnregistered?.Invoke(unit);
            return true;
        }

        /// <summary>获取指定阵营的已注册单位。</summary>
        public List<Unit> GetUnits(Faction faction)
        {
            var result = new List<Unit>();
            foreach (Unit unit in units)
                if (unit != null && unit.Faction == faction)
                    result.Add(unit);
            return result;
        }

        /// <summary>开始战斗：进入第 1 轮，并开始第一个阵营的回合。</summary>
        /// <exception cref="InvalidOperationException">战斗已经开始，或阵营顺序为空。</exception>
        public void StartBattle()
        {
            if (State != BattleState.NotStarted)
                throw new InvalidOperationException("战斗已经开始。");
            if (factionOrder == null || factionOrder.Count == 0)
                throw new InvalidOperationException("阵营顺序不能为空。");

            State = BattleState.Running;
            RoundNumber = 1;
            factionIndex = 0;
            BeginFactionTurn();
        }

        /// <summary>结束当前阵营回合并开始下一个阵营的回合；最后一个阵营结束后进入下一轮。</summary>
        /// <exception cref="InvalidOperationException">战斗不在进行中。</exception>
        public void EndCurrentTurn()
        {
            if (State != BattleState.Running)
                throw new InvalidOperationException("战斗不在进行中。");

            EndFactionTurn();
            factionIndex++;
            if (factionIndex >= factionOrder.Count)
            {
                factionIndex = 0;
                RoundNumber++;
            }
            BeginFactionTurn();
        }

        /// <summary>结束战斗：结束当前阵营回合，之后不再轮转。</summary>
        public void EndBattle()
        {
            if (State != BattleState.Running)
                return;
            EndFactionTurn();
            State = BattleState.Ended;
        }

        private void BeginFactionTurn()
        {
            Faction faction = CurrentFaction;
            foreach (Unit unit in GetUnits(faction))
                unit.BeginTurn();
            FactionTurnStarted?.Invoke(faction);
        }

        private void EndFactionTurn()
        {
            Faction faction = CurrentFaction;
            // 回合内死亡的单位 IsTurnActive 仍为 true，EndTurn 会正常结算它的 Buff。
            foreach (Unit unit in GetUnits(faction))
                unit.EndTurn();
            FactionTurnEnded?.Invoke(faction);
        }
    }
}
