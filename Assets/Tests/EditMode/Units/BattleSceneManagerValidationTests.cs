using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Wargame.Battle;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class BattleSceneManagerValidationTests : UnitTestBase
    {
        private GridManager grid;
        private BattleTurnManager turns;
        private BattleSceneManager battle;
        private Unit prefab;
        private static void Reference(Object target, string field, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [SetUp]
        public void ConfigureScene()
        {
            grid = CreateComponent<GridManager>(); grid.Initialize(20,20);
            turns = CreateComponent<BattleTurnManager>();
            battle = CreateComponent<BattleSceneManager>();
            Reference(battle, "grid", grid); Reference(battle, "turns", turns);
            Reference(battle, "gameSettings", AssetDatabase.LoadAssetAtPath<GameSettings>("Assets/Data/Game/GameSettings.asset"));
            Reference(battle, "playerController", CreateComponent<BattlePlayerController>());
            Reference(battle, "preview", CreateComponent<BattleMovementPreview>());
            Reference(battle, "hud", CreateComponent<BattleHUD>());
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/Player.prefab").GetComponent<Unit>();
        }

        private void SetSpawns(params Vector2Int[] positions)
        {
            var data = new SerializedObject(battle);
            var list = data.FindProperty("playerSpawns");
            list.arraySize = positions.Length;
            for (int i = 0; i < positions.Length; i++)
            {
                var item = list.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
                item.FindPropertyRelative("Coordinates").vector2IntValue = positions[i];
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [TestCase(BlockState.HalfBlocked)]
        [TestCase(BlockState.FullBlocked)]
        public void InvalidSpawnTerrainCreatesNoUnits(BlockState state)
        {
            SetSpawns(Vector2Int.one);
            grid.TrySetBlockState(Vector2Int.one, state);
            Assert.That(battle.TryInitialize(out var error), Is.False);
            Assert.That(error, Does.Contain("出生格"));
            Assert.That(battle.SpawnedUnits, Is.Empty);
            Assert.That(turns.Units, Is.Empty);
            Assert.That(turns.State, Is.EqualTo(BattleState.NotStarted));
        }

        [Test]
        public void DuplicateAndOutOfBoundsSpawnsAreRejectedBeforeInstantiating()
        {
            SetSpawns(Vector2Int.one, Vector2Int.one);
            Assert.That(battle.TryInitialize(out _), Is.False);
            Assert.That(battle.SpawnedUnits, Is.Empty);
            SetSpawns(new Vector2Int(-1,0));
            Assert.That(battle.TryInitialize(out _), Is.False);
            Assert.That(turns.Units, Is.Empty);
        }

        [Test]
        public void MissingOrInvalidGameSettingsIsRejectedBeforeSpawning()
        {
            Reference(battle, "gameSettings", null);
            Assert.That(battle.TryInitialize(out string error), Is.False);
            Assert.That(error, Does.Contain("GameSettings"));
            var settings = ScriptableObject.CreateInstance<GameSettings>();
            try
            {
                settings.ShootSettings = null;
                Reference(battle, "gameSettings", settings);
                Assert.That(battle.TryInitialize(out error), Is.False);
                Assert.That(error, Does.Contain("ShootSettings"));
                Assert.That(battle.SpawnedUnits, Is.Empty);
                Assert.That(turns.State, Is.EqualTo(BattleState.NotStarted));
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}
