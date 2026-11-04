using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Wargame.Units;

namespace Wargame.Battle
{
    /// <summary>供战斗界面消费的只读状态快照，不持有任何 Unity UI 控件。</summary>
    public sealed class BattleUiState
    {
        public BattleUiState(int roundNumber, Faction currentFaction, bool canEndTurn,
            bool hasUnspentPlayerActionPoints, string hint, SelectedUnitUiState selectedUnit,
            IReadOnlyList<ShootPreview> shootTargets, ShootPreview? selectedTarget, bool canFire,
            bool isTargetPanelVisible = false, bool canCancelShoot = false)
        {
            RoundNumber = roundNumber;
            CurrentFaction = currentFaction;
            CanEndTurn = canEndTurn;
            HasUnspentPlayerActionPoints = hasUnspentPlayerActionPoints;
            Hint = hint ?? string.Empty;
            SelectedUnit = selectedUnit;
            ShootTargets = new ReadOnlyCollection<ShootPreview>(
                shootTargets == null ? new List<ShootPreview>() : new List<ShootPreview>(shootTargets));
            var targetStates = new List<TargetUiState>();
            foreach (ShootPreview target in ShootTargets)
                targetStates.Add(new TargetUiState(target));
            Targets = new ReadOnlyCollection<TargetUiState>(targetStates);
            SelectedTarget = selectedTarget;
            CanFire = canFire;
            IsTargetPanelVisible = isTargetPanelVisible;
            CanCancelShoot = canCancelShoot;
        }

        public int RoundNumber { get; }
        public Faction CurrentFaction { get; }
        public bool CanEndTurn { get; }
        public bool HasUnspentPlayerActionPoints { get; }
        public string Hint { get; }
        public SelectedUnitUiState SelectedUnit { get; }
        public IReadOnlyList<ShootPreview> ShootTargets { get; }
        public IReadOnlyList<TargetUiState> Targets { get; }
        public ShootPreview? SelectedTarget { get; }
        public bool CanFire { get; }
        public bool IsTargetPanelVisible { get; }
        public bool CanCancelShoot { get; }
    }

    /// <summary>目标列表的只读界面数据，不负责选择或执行射击。</summary>
    public sealed class TargetUiState
    {
        public TargetUiState(ShootPreview preview)
        {
            Target = preview.Target;
            DisplayName = preview.TargetName ?? string.Empty;
            Health = preview.TargetHealth;
            Distance = preview.Distance;
            Breakdown = preview.Breakdown;
            IsAvailable = preview.IsAvailable;
            CanFire = preview.CanFire;
            FailureReason = preview.FailureReason;
        }

        public Unit Target { get; }
        public string DisplayName { get; }
        public float Health { get; }
        public int Distance { get; }
        public HitChanceBreakdown Breakdown { get; }
        public float HitChance => Breakdown?.FinalChance ?? 0f;
        public bool IsAvailable { get; }
        public bool CanFire { get; }
        public AbilityFailReason FailureReason { get; }
    }

    /// <summary>选中单位的界面数据。</summary>
    public sealed class SelectedUnitUiState
    {
        public SelectedUnitUiState(Unit unit, float actionPoints, float maxActionPoints,
            IReadOnlyList<AbilityUiState> abilities)
        {
            Unit = unit;
            DisplayName = unit != null ? unit.DisplayName : string.Empty;
            ActionPoints = actionPoints;
            MaxActionPoints = maxActionPoints;
            Abilities = new ReadOnlyCollection<AbilityUiState>(
                abilities == null ? new List<AbilityUiState>() : new List<AbilityUiState>(abilities));
        }

        public Unit Unit { get; }
        public string DisplayName { get; }
        public float ActionPoints { get; }
        public float MaxActionPoints { get; }
        public IReadOnlyList<AbilityUiState> Abilities { get; }
    }

    /// <summary>单项能力的只读界面数据。</summary>
    public sealed class AbilityUiState
    {
        public AbilityUiState(AbilityBase ability, string displayName, int actionPointCost,
            bool canActivate, AbilityFailReason failReason, string statusText)
        {
            Ability = ability;
            AbilityId = ability != null ? ability.AbilityId : string.Empty;
            DisplayName = displayName ?? string.Empty;
            TargetType = ability != null ? ability.TargetType : AbilityTargetType.None;
            ActionPointCost = actionPointCost;
            CanActivate = canActivate;
            FailReason = failReason;
            StatusText = statusText ?? string.Empty;
            RemainingCooldown = ability != null ? ability.RemainingCooldown : 0;
            RemainingCharges = ability != null ? ability.RemainingCharges : AbilityBase.UnlimitedCharges;
        }

        public AbilityBase Ability { get; }
        public string AbilityId { get; }
        public string DisplayName { get; }
        public AbilityTargetType TargetType { get; }
        public int ActionPointCost { get; }
        public bool CanActivate { get; }
        public AbilityFailReason FailReason { get; }
        public string StatusText { get; }
        public int RemainingCooldown { get; }
        public int RemainingCharges { get; }
    }
}
