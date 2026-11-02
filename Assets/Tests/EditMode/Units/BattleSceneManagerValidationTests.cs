using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
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
        private BattleUnitHealthBars healthBars;
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
            var hudRoot = new GameObject("TestHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(BattleHUD));
            Track(hudRoot);
            hudRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var hudScaler = hudRoot.GetComponent<CanvasScaler>();
            hudScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            hudScaler.referenceResolution = new Vector2(1280, 720);
            hudScaler.matchWidthOrHeight = .5f;
            Reference(battle, "hud", hudRoot.GetComponent<BattleHUD>());
            var barRoot = new GameObject("TestHealthBars", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(BattleUnitHealthBars));
            Track(barRoot);
            barRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            barRoot.GetComponent<Canvas>().sortingOrder = -1;
            var barScaler = barRoot.GetComponent<CanvasScaler>();
            barScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            barScaler.referenceResolution = hudScaler.referenceResolution;
            barScaler.matchWidthOrHeight = hudScaler.matchWidthOrHeight;
            healthBars = barRoot.GetComponent<BattleUnitHealthBars>();
            Reference(healthBars, "healthCanvas", barRoot.GetComponent<Canvas>());
            Reference(healthBars, "viewPrefab", AssetDatabase.LoadAssetAtPath<UnitHealthBarView>("Assets/Prefabs/UI/UnitHealthBar.prefab"));
            Reference(battle, "healthBars", healthBars);
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

        [Test]
        public void Map1HasIndependentNonInteractiveHealthCanvas()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Map1.unity");
            try
            {
                BattleSceneManager manager = null;
                BattleHUD hud = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.TryGetComponent(out BattleSceneManager candidate)) manager = candidate;
                    if (hud == null) hud = root.GetComponentInChildren<BattleHUD>(true);
                }
                Assert.That(manager, Is.Not.Null);
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.gameObject.name, Is.EqualTo("canvas"));
                Assert.That(hud.transform.parent, Is.Null);
                Assert.That(hud.AbilityDic.name, Is.EqualTo("AbilityDic"));
                Assert.That(hud.AbilityDic.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
                Assert.That(hud.AbilityDic.IsChildOf(hud.InteractionCanvas.transform), Is.True);
                Assert.That(hud.AbilityDic.GetComponentsInChildren<Button>(true), Has.Length.EqualTo(1));
                Assert.That(hud.AbilityDic.GetComponentInChildren<Button>(true).name, Is.EqualTo("AbilityButtonTemplate"));
                Assert.That(hud.ConfirmDialog.name, Is.EqualTo("ActionConfirmDialog"));
                Assert.That(hud.InteractionCanvas.GetComponentsInChildren<Transform>(true).Any(t => t.name == "FireButton"), Is.False);
                Assert.That(manager.HealthBars, Is.Not.Null);
                var canvas = hud.InteractionCanvas;
                Assert.That(canvas, Is.Not.Null);
                Transform canvasRoot = manager.HealthBars.transform.parent;
                Assert.That(canvasRoot, Is.Not.Null);
                Assert.That(canvasRoot.name, Is.EqualTo("canvas"));
                Assert.That(canvasRoot.GetComponent<Canvas>(), Is.Null);
                Assert.That(hud.InfoCanvas.transform.parent, Is.SameAs(canvasRoot));
                Assert.That(canvas.transform.parent, Is.SameAs(canvasRoot));
                Assert.That(manager.HealthBars.HealthCanvas, Is.Not.SameAs(canvas));
                Assert.That(manager.HealthBars.GetComponentsInChildren<GraphicRaycaster>(true), Is.Empty);
                Assert.That(hud.InfoCanvas, Is.Not.Null);
                Assert.That(hud.InfoCanvas, Is.Not.SameAs(canvas));
                Assert.That(hud.InfoCanvas.GetComponent<GraphicRaycaster>(), Is.Null);
                Assert.That(hud.InfoCanvas.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
