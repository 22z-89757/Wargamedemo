using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Wargame.Battle;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class BattleUiModelTests : UnitTestBase
    {
        [Test]
        public void StateSnapshotCopiesCollectionsAndDoesNotExposeMutableLists()
        {
            var abilities = new List<AbilityUiState>
            {
                new AbilityUiState(null, "移动", 1, true, AbilityFailReason.None, "可用")
            };
            var selected = new SelectedUnitUiState(null, 0, 0, abilities);
            var targets = new List<ShootPreview>();
            var state = new BattleUiState(2, Faction.Player, true, false, "提示", selected, targets, null, false);

            abilities.Clear();
            targets.Add(default);

            Assert.That(state.SelectedUnit.Abilities, Has.Count.EqualTo(1));
            Assert.That(state.ShootTargets, Has.Count.EqualTo(0));
            Assert.Throws<NotSupportedException>(() => ((IList<ShootPreview>)state.ShootTargets).Add(default));
            Assert.Throws<NotSupportedException>(() => ((IList<AbilityUiState>)state.SelectedUnit.Abilities).Clear());
        }

        [Test]
        public void ReplacingSelectedUnitDoesNotRetainPreviousAbilitiesOrTargets()
        {
            var oldAbility = new AbilityUiState(null, "旧操作", 1, true, AbilityFailReason.None, "可用");
            var oldUnit = new SelectedUnitUiState(null, 2, 2, new[] { oldAbility });
            var nextUnit = new SelectedUnitUiState(null, 1, 2, Array.Empty<AbilityUiState>());

            Assert.That(oldUnit.Abilities, Has.Count.EqualTo(1));
            Assert.That(nextUnit.Abilities, Is.Empty);
            Assert.That(nextUnit.DisplayName, Is.Empty);
        }

        [Test]
        public void ChineseTextProvidesFallbacksForUnknownAbilityAndFailureReasons()
        {
            var unknown = new TestAbility("UnknownAbility");

            Assert.That(BattleUiText.AbilityName(unknown), Is.EqualTo("未命名操作"));
            Assert.That(BattleUiText.AbilityStatus(false, AbilityFailReason.NotEnoughActionPoints), Is.EqualTo("行动点不足"));
            Assert.That(BattleUiText.AbilityStatus(false, AbilityFailReason.Custom), Is.EqualTo("当前条件不满足"));
            Assert.That(BattleUiText.EndTurnWarning, Does.Contain("行动力").And.Contain("结束回合"));
        }

        [Test]
        public void AbilityAdapterReadsLatestStateWithoutSpendingActionPoints()
        {
            Unit unit = CreateUnit("中文单位");
            unit.BeginTurn();
            var ability = new TestAbility("Scan").Cost(1).Cooldown(2).Charges(2);
            Assert.That(unit.Abilities.Grant(ability), Is.True);

            float before = unit.Attributes.GetValue(AttributeType.ActionPoints);
            AbilityUiState first = BattleUiAbilityAdapter.Read(unit, ability);
            Assert.That(first.TargetType, Is.EqualTo(AbilityTargetType.None));
            Assert.That(first.ActionPointCost, Is.EqualTo(1));
            Assert.That(first.RemainingCooldown, Is.EqualTo(0));
            Assert.That(first.RemainingCharges, Is.EqualTo(2));
            Assert.That(first.CanActivate, Is.True);
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(before));

            Assert.That(unit.Abilities.TryActivate(ability, AbilityTarget.None, out _), Is.True);
            AbilityUiState afterActivation = BattleUiAbilityAdapter.Read(unit, ability);
            Assert.That(afterActivation.RemainingCooldown, Is.EqualTo(2));
            Assert.That(afterActivation.RemainingCharges, Is.EqualTo(1));
            Assert.That(afterActivation.CanActivate, Is.False);
            Assert.That(afterActivation.StatusText, Is.EqualTo("冷却中"));
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(before - 1));

            Assert.That(unit.Abilities.Remove(ability), Is.True);
            Assert.That(BattleUiAbilityAdapter.ReadAll(unit), Is.Empty);
        }

        [Test]
        public void PreparingTargetChecksConditionsWithoutRequiringTargetOrSpendingCosts()
        {
            Unit unit = CreateUnit("准备射击");
            Unit enemy = CreateUnit("目标", Faction.Enemy);
            var ability = new TestAbility("Targeted", AbilityTargetType.Unit).Cost(1).Cooldown(2).Charges(1);
            unit.Abilities.Grant(ability);
            Assert.That(unit.Abilities.CanPrepareTarget(ability, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotYourTurn));
            unit.BeginTurn();
            Assert.That(unit.Abilities.CanActivate(ability, AbilityTarget.None, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.InvalidTarget));
            Assert.That(unit.Abilities.CanPrepareTarget(ability, out reason), Is.True);
            Assert.That(ability.ActivateCount, Is.Zero);
            Assert.That(ability.RemainingCharges, Is.EqualTo(1));
            Assert.That(unit.Attributes.GetValue(AttributeType.ActionPoints), Is.EqualTo(2));
            unit.Attributes.SetBaseValue(AttributeType.ActionPoints, 0);
            Assert.That(unit.Abilities.CanPrepareTarget(ability, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotEnoughActionPoints));
            unit.Attributes.SetBaseValue(AttributeType.ActionPoints, 2);
            unit.Tags.AddTag(GameplayTag.Stunned);
            Assert.That(unit.Abilities.CanPrepareTarget(ability, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.BlockedByTag));
            unit.Tags.RemoveTag(GameplayTag.Stunned);
            Assert.That(unit.Abilities.TryActivate(ability, AbilityTarget.ForUnit(enemy), out _), Is.True);
            Assert.That(unit.Abilities.CanPrepareTarget(ability, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.OnCooldown));
            unit.EndTurn(); unit.BeginTurn(); unit.EndTurn(); unit.BeginTurn();
            Assert.That(unit.Abilities.CanPrepareTarget(ability, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NoCharges));
            unit.Abilities.Remove(ability);
            Assert.That(unit.Abilities.CanPrepareTarget(ability, out reason), Is.False);
            Assert.That(reason, Is.EqualTo(AbilityFailReason.NotGranted));
        }

        [Test]
        public void TargetPanelVisibilityIsExplicitInSnapshot()
        {
            var hidden = new BattleUiState(1, Faction.Player, true, true, "", null, null, null, false);
            var visible = new BattleUiState(1, Faction.Player, true, true, "", null, null, null, false, true);
            Assert.That(hidden.IsTargetPanelVisible, Is.False);
            Assert.That(visible.IsTargetPanelVisible, Is.True);
        }

        [Test]
        public void UnspentActionPointQueryIgnoresDeadUnitsAndOtherFactions()
        {
            Unit playerWithAp = CreateUnit("有行动点", Faction.Player);
            Unit playerWithoutAp = CreateUnit("无行动点", Faction.Player);
            Unit enemyWithAp = CreateUnit("敌方单位", Faction.Enemy);
            playerWithAp.Attributes.SetBaseValue(AttributeType.ActionPoints, 1);
            playerWithoutAp.Attributes.SetBaseValue(AttributeType.ActionPoints, 0);
            enemyWithAp.Attributes.SetBaseValue(AttributeType.ActionPoints, 2);

            Assert.That(BattleSceneManager.HasUnspentActionPoints(new[] { playerWithAp, playerWithoutAp, enemyWithAp }, Faction.Player), Is.True);
            playerWithAp.Kill();
            Assert.That(BattleSceneManager.HasUnspentActionPoints(new[] { playerWithAp, playerWithoutAp, enemyWithAp }, Faction.Player), Is.False);
            Assert.That(BattleSceneManager.HasUnspentActionPoints(new[] { playerWithAp, playerWithoutAp, enemyWithAp }, Faction.Enemy), Is.True);
        }
    }
}
