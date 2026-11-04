using System;
using System.Collections.Generic;
using UnityEngine;
using Wargame.Events;
using Wargame.Battle.AI;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Battle
{
    [Serializable]
    public sealed class UnitSpawnEntry
    {
        public Unit Prefab;
        public Vector2Int Coordinates;
        public float Yaw;
        public UnitSpawnEntry() { }
        public UnitSpawnEntry(Unit prefab, Vector2Int coordinates, float yaw = 0)
        { Prefab = prefab; Coordinates = coordinates; Yaw = yaw; }
    }

    /// <summary>局内装配入口。统一生成单位、授予能力并接入玩家与敌方回合控制。</summary>
    public sealed class BattleSceneManager : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private GameSettings gameSettings;
        [SerializeField] private BattleTurnManager turns;
        [SerializeField] private EnemyTurnController enemyTurnController;
        [SerializeField] private BattlePlayerController playerController;
        [SerializeField] private BattleMovementPreview preview;
        [SerializeField] private BattleHUD hud;
        [SerializeField] private BattleCameraRig cameraRig;
        [SerializeField] private BattleUnitHealthBars healthBars;
        [SerializeField] private BattleDamagePopupManager damagePopups;
        [SerializeField] private bool startAutomatically = true;
        [SerializeField] private List<UnitSpawnEntry> playerSpawns = new List<UnitSpawnEntry>();
        [SerializeField] private List<UnitSpawnEntry> enemySpawns = new List<UnitSpawnEntry>();
        private readonly List<Unit> spawned = new List<Unit>();
        private BattleCameraDirector cameraDirector;
        private ICoverBuffFactory coverBuffFactory;
        private bool initializing, cleaning, outcomePublished;
        public event Action Changed;
        public bool IsInitialized { get; private set; }
        public GridManager Grid => grid;
        public BattleTurnManager Turns => turns;
        public EnemyTurnController EnemyTurns => enemyTurnController;
        public BattleCameraRig CameraRig => cameraRig;
        public BattleCameraDirector CameraDirector => cameraDirector;
        public BattleUnitHealthBars HealthBars => healthBars;
        public BattleDamagePopupManager DamagePopups => damagePopups;
        public bool HasActiveGesture => playerController != null && playerController.HasActiveGesture;
        public IReadOnlyList<Unit> SpawnedUnits => spawned;
        public bool HasActiveAction
        {
            get
            {
                foreach (Unit unit in spawned)
                    if (unit != null && unit.Abilities.ActiveExclusiveAbility != null) return true;
                return false;
            }
        }
        public bool CanIssueOrders => IsInitialized && turns.State == BattleState.Running &&
            turns.CurrentFaction == Faction.Player && !HasActiveAction;
        public bool CanEndPlayerTurn => CanIssueOrders && !HasActiveGesture && (cameraRig == null || !cameraRig.IsRotating);
        /// <summary>当前玩家阵营是否仍有任一存活单位保留行动点。</summary>
        public bool HasUnspentPlayerActionPoints
        {
            get
            {
                if (turns == null || turns.State != BattleState.Running || turns.CurrentFaction != Faction.Player)
                    return false;
                return HasUnspentActionPoints(turns.GetUnits(Faction.Player), Faction.Player);
            }
        }

        /// <summary>查询指定阵营存活单位是否仍保留行动点；只读且不改变单位状态。</summary>
        public static bool HasUnspentActionPoints(IEnumerable<Unit> units, Faction faction)
        {
            if (units == null) return false;
            foreach (Unit unit in units)
            {
                if (unit == null || unit.Faction != faction || !unit.IsAlive ||
                    !unit.Attributes.TryGetValue(AttributeType.ActionPoints, out float ap))
                    continue;
                if (ap > 0f) return true;
            }
            return false;
        }

        private void Start()
        {
            if (startAutomatically && !TryInitialize(out string error)) Debug.LogError("战斗初始化失败：" + error, this);
        }

        public bool TryInitialize(out string error)
        {
            error = null;
            if (IsInitialized) return true;
            if (initializing) { error = "初始化正在进行。"; return false; }
            if (grid == null || !grid.IsInitialized || turns == null || playerController == null || preview == null || hud == null)
            { error = "需要已初始化棋盘、回合管理器、玩家控制器、范围预览和 HUD。"; return false; }
            if (enemyTurnController == null || !enemyTurnController.isActiveAndEnabled)
            { error = "需要激活且启用的 EnemyTurnController。"; return false; }
            if (gameSettings == null)
            { error = "需要显式引用 GameSettings 射击配置资产。"; return false; }
            if (!gameSettings.TryValidate(out error)) return false;
            if (turns.State != BattleState.NotStarted || turns.Units.Count != 0)
            { error = "回合管理器必须尚未开始且没有已注册单位。"; return false; }
            var occupied = new HashSet<Vector2Int>();
            if (playerSpawns == null || playerSpawns.Count == 0)
            { error = "至少配置一个玩家出生点。"; return false; }
            if (!Validate(playerSpawns, Faction.Player, occupied, out error) ||
                !Validate(enemySpawns, Faction.Enemy, occupied, out error) ||
                !playerController.ValidateConfiguration(out error)) return false;
            if (healthBars == null) { error = "需要独立 Canvas 上的 BattleUnitHealthBars 血条层。"; return false; }
            if (damagePopups == null || !damagePopups.ValidateConfiguration(out error))
            { error = "需要有效的 BattleDamagePopupManager。" + (string.IsNullOrEmpty(error) ? string.Empty : " " + error); return false; }
            if (cameraRig == null) { error = "需要 BattleCameraRig。"; return false; }
            if (!cameraRig.ValidateConfiguration(out error)) return false;
            initializing = true;
            try
            {
                coverBuffFactory = new ConfiguredCoverBuffFactory(
                    gameSettings.CoverEffectSettings.CreateSnapshot());
                Spawn(playerSpawns, Faction.Player);
                Spawn(enemySpawns, Faction.Enemy);
                cameraRig.Initialize(grid, grid.GridToWorld(playerSpawns[0].Coordinates), playerController);
                cameraDirector = new BattleCameraDirector(cameraRig, spawned);
                damagePopups.Initialize(cameraRig.OutputCamera);
                healthBars.Initialize(this, cameraRig.OutputCamera);
                preview.Initialize(grid);
                hud.Initialize(this);
                turns.SetFactionOrder(new[] { Faction.Player, Faction.Enemy });
                turns.FactionTurnStarted += OnFactionStarted;
                turns.FactionTurnEnded += OnFactionEnded;
                IsInitialized = true;
                playerController.Initialize(this);
                enemyTurnController.Initialize(turns, grid);
                turns.StartBattle();
                if (turns.State != BattleState.Running)
                    throw new InvalidOperationException("回合管理器未能进入运行状态。");
                EventCenter center = EventCenter.Instance;
                GameFlowController flow = center.GetComponent<GameFlowController>();
                flow?.TryConsumePendingGameStart();
                Changed?.Invoke();
                CheckForGameOutcome();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Cleanup();
                return false;
            }
            finally { initializing = false; }
        }

        private bool Validate(List<UnitSpawnEntry> entries, Faction faction,
            HashSet<Vector2Int> occupied, out string error)
        {
            error = null;
            if (entries == null) { error = "出生列表不能为 null。"; return false; }
            foreach (var entry in entries)
            {
                UnitShootVisualController shootVisual = entry?.Prefab != null
                    ? entry.Prefab.GetComponent<UnitShootVisualController>() : null;
                UnitBattleCameraSet cameraSet = entry?.Prefab != null
                    ? entry.Prefab.GetComponent<UnitBattleCameraSet>() : null;
                UnitCoverController coverController = entry?.Prefab != null
                    ? entry.Prefab.GetComponent<UnitCoverController>() : null;
                string visualError = null;
                if (entry == null || entry.Prefab == null || !entry.Prefab.gameObject.activeSelf ||
                    entry.Prefab.GetComponent<UnitMovementVisual>() == null ||
                    !entry.Prefab.GetComponent<UnitMovementVisual>().enabled ||
                    coverController == null || !coverController.enabled ||
                    shootVisual == null || !shootVisual.enabled || !shootVisual.ValidateConfiguration(out visualError) ||
                    entry.Prefab.GetComponentInChildren<Collider>() == null)
                {
                    error = "每项需要激活的 Unit Prefab、UnitMovementVisual、UnitCoverController、有效 UnitShootVisualController 和拾取 Collider。" +
                        (string.IsNullOrEmpty(visualError) ? string.Empty : " " + visualError);
                    return false;
                }
                if (cameraSet == null || !cameraSet.enabled || !cameraSet.ValidateConfiguration(faction, out error))
                {
                    error = $"{faction} Unit Prefab 的战斗相机装配无效。" +
                        (string.IsNullOrEmpty(error) ? string.Empty : " " + error);
                    return false;
                }
                if (float.IsNaN(entry.Yaw) || float.IsInfinity(entry.Yaw) ||
                    !grid.TryGetCell(entry.Coordinates, out GridCell cell) || cell.BlockState != BlockState.Unblocked ||
                    cell.Occupant != null || !occupied.Add(entry.Coordinates))
                { error = $"出生格 {entry.Coordinates} 越界、重复、被占用或不是 Unblocked。"; return false; }
            }
            return true;
        }

        private void Spawn(List<UnitSpawnEntry> entries, Faction faction)
        {
            foreach (var entry in entries)
            {
                Unit unit = Instantiate(entry.Prefab, grid.GridToWorld(entry.Coordinates), Quaternion.Euler(0, entry.Yaw, 0));
                unit.name = entry.Prefab.name;
                unit.Faction = faction;
                spawned.Add(unit);
                if (!grid.TryPlaceUnit(unit, entry.Coordinates)) throw new InvalidOperationException("出生格登记失败。");
                unit.GetComponent<UnitCoverController>().Initialize(grid, coverBuffFactory);
                turns.RegisterUnit(unit);
                unit.Abilities.Grant(new MoveAbility(grid, unit.GetComponent<UnitMovementVisual>()));
                unit.Abilities.Grant(new ShootAbility(grid, gameSettings.ShootSettings,
                    unit.GetComponent<UnitShootVisualController>()));
                unit.Died += HandleUnitDied;
                unit.Destroyed += ReleaseUnit;
                unit.Attributes.AttributeChanged += OnUnitAttributeChanged;
                unit.Tags.TagChanged += OnUnitTagChanged;
                unit.Abilities.AbilityActivated += OnAbilityActivated;
                unit.Abilities.AbilityEnded += OnAbilityEnded;
            }
        }

        public bool TryEndPlayerTurn()
        {
            if (!CanEndPlayerTurn) return false;
            turns.EndCurrentTurn();
            return true;
        }

        private void OnFactionStarted(Faction faction)
        {
            Changed?.Invoke();
        }
        private void OnFactionEnded(Faction faction) => Changed?.Invoke();
        private void OnAbilityActivated(AbilityBase ability, AbilityTarget target) => Changed?.Invoke();
        private void OnAbilityEnded(AbilityBase ability, bool cancelled) => Changed?.Invoke();
        private void OnUnitAttributeChanged(AttributeType type, float previous, float value) => Changed?.Invoke();
        private void OnUnitTagChanged(GameplayTag tag, bool added) => Changed?.Invoke();

        private void HandleUnitDied(Unit unit)
        {
            if (cleaning || unit == null) return;
            ReleaseUnit(unit);
            CheckForGameOutcome();
            if (Application.isPlaying) Destroy(unit.gameObject); else DestroyImmediate(unit.gameObject);
        }

        private void ReleaseUnit(Unit unit)
        {
            if (cleaning) return;
            bool shotOwnerUnavailable = cameraDirector != null && cameraDirector.Shooter == unit;
            cameraDirector?.UnitUnavailable(unit);
            if (shotOwnerUnavailable) damagePopups?.Clear();
            unit.Abilities.CancelAll();
            unit.EndTurn();
            grid.TryRemoveUnit(unit);
            turns.UnregisterUnit(unit);
            unit.Died -= HandleUnitDied;
            unit.Destroyed -= ReleaseUnit;
            unit.Attributes.AttributeChanged -= OnUnitAttributeChanged;
            unit.Tags.TagChanged -= OnUnitTagChanged;
            unit.Abilities.AbilityActivated -= OnAbilityActivated;
            unit.Abilities.AbilityEnded -= OnAbilityEnded;
            Changed?.Invoke();
        }

        private void CheckForGameOutcome()
        {
            if (cleaning || outcomePublished || !IsInitialized || turns == null ||
                turns.State != BattleState.Running)
                return;

            int livingPlayers = CountLiving(Faction.Player);
            int livingEnemies = CountLiving(Faction.Enemy);
            if (livingPlayers > 0 && livingEnemies > 0)
                return;

            GameOutcome outcome = livingPlayers == 0 && livingEnemies == 0
                ? GameOutcome.Draw
                : livingEnemies == 0 ? GameOutcome.Victory : GameOutcome.Defeat;
            outcomePublished = true;
            turns.EndBattle();
            if (turns.State == BattleState.Ended)
                EventCenter.Instance.Publish(new GameEndedEvent(outcome));
        }

        private int CountLiving(Faction faction)
        {
            int count = 0;
            foreach (Unit unit in spawned)
                if (unit != null && unit.Faction == faction && unit.IsAlive)
                    count++;
            return count;
        }

        private void Cleanup()
        {
            cleaning = true;
            if (enemyTurnController != null) enemyTurnController.Shutdown();
            if (playerController != null) playerController.Shutdown();
            if (damagePopups != null) damagePopups.Shutdown();
            cameraDirector?.Dispose();
            cameraDirector = null;
            if (hud != null) hud.Shutdown();
            if (healthBars != null) healthBars.Shutdown();
            if (cameraRig != null) cameraRig.Shutdown();
            if (turns != null)
            {
                turns.FactionTurnStarted -= OnFactionStarted;
                turns.FactionTurnEnded -= OnFactionEnded;
                turns.EndBattle();
            }
            foreach (Unit unit in spawned)
            {
                if (unit == null) continue;
                unit.Abilities.CancelAll();
                unit.Died -= HandleUnitDied;
                unit.Destroyed -= ReleaseUnit;
                unit.Attributes.AttributeChanged -= OnUnitAttributeChanged;
                unit.Tags.TagChanged -= OnUnitTagChanged;
                unit.Abilities.AbilityActivated -= OnAbilityActivated;
                unit.Abilities.AbilityEnded -= OnAbilityEnded;
                if (grid != null) grid.TryRemoveUnit(unit);
                if (turns != null) turns.UnregisterUnit(unit);
                if (Application.isPlaying) Destroy(unit.gameObject); else DestroyImmediate(unit.gameObject);
            }
            spawned.Clear();
            coverBuffFactory = null;
            IsInitialized = false;
            cleaning = false;
        }
        private void OnDestroy() => Cleanup();
    }
}
