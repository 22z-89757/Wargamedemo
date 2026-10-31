using System;
using System.Collections.Generic;
using UnityEngine;
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

    /// <summary>局内装配入口。生成和占用统一由此管理，敌人行为暂留空。</summary>
    public sealed class BattleSceneManager : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private GameSettings gameSettings;
        [SerializeField] private BattleTurnManager turns;
        [SerializeField] private BattlePlayerController playerController;
        [SerializeField] private BattleMovementPreview preview;
        [SerializeField] private BattleHUD hud;
        [SerializeField] private BattleCameraRig cameraRig;
        [SerializeField] private bool startAutomatically = true;
        [SerializeField] private List<UnitSpawnEntry> playerSpawns = new List<UnitSpawnEntry>();
        [SerializeField] private List<UnitSpawnEntry> enemySpawns = new List<UnitSpawnEntry>();
        private readonly List<Unit> spawned = new List<Unit>();
        private bool initializing, cleaning;
        private int skipEnemyAfterFrame = -1;
        public event Action Changed;
        public bool IsInitialized { get; private set; }
        public GridManager Grid => grid;
        public BattleTurnManager Turns => turns;
        public BattleCameraRig CameraRig => cameraRig;
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
            if (gameSettings == null)
            { error = "需要显式引用 GameSettings 射击配置资产。"; return false; }
            if (!gameSettings.TryValidate(out error)) return false;
            if (turns.State != BattleState.NotStarted || turns.Units.Count != 0)
            { error = "回合管理器必须尚未开始且没有已注册单位。"; return false; }
            var occupied = new HashSet<Vector2Int>();
            if (playerSpawns == null || playerSpawns.Count == 0)
            { error = "至少配置一个玩家出生点。"; return false; }
            if (!Validate(playerSpawns, occupied, out error) || !Validate(enemySpawns, occupied, out error) ||
                !playerController.ValidateConfiguration(out error)) return false;
            if (cameraRig == null) { error = "需要 BattleCameraRig。"; return false; }
            if (!cameraRig.ValidateConfiguration(out error)) return false;
            initializing = true;
            try
            {
                Spawn(playerSpawns, Faction.Player);
                Spawn(enemySpawns, Faction.Enemy);
                cameraRig.Initialize(grid, grid.GridToWorld(playerSpawns[0].Coordinates), playerController);
                preview.Initialize(grid);
                hud.Initialize(this);
                turns.SetFactionOrder(new[] { Faction.Player, Faction.Enemy });
                turns.FactionTurnStarted += OnFactionStarted;
                turns.FactionTurnEnded += OnFactionEnded;
                IsInitialized = true;
                playerController.Initialize(this);
                turns.StartBattle();
                Changed?.Invoke();
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

        private bool Validate(List<UnitSpawnEntry> entries, HashSet<Vector2Int> occupied, out string error)
        {
            error = null;
            if (entries == null) { error = "出生列表不能为 null。"; return false; }
            foreach (var entry in entries)
            {
                if (entry == null || entry.Prefab == null || !entry.Prefab.gameObject.activeSelf ||
                    entry.Prefab.GetComponent<UnitVisualController>() == null ||
                    !entry.Prefab.GetComponent<UnitVisualController>().enabled || entry.Prefab.GetComponentInChildren<Collider>() == null)
                { error = "每项需要激活的 Unit Prefab、UnitVisualController 和拾取 Collider。"; return false; }
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
                turns.RegisterUnit(unit);
                if (faction == Faction.Player)
                {
                    unit.Abilities.Grant(new MoveAbility(grid, unit.GetComponent<UnitVisualController>()));
                    unit.Abilities.Grant(new ShootAbility(grid, gameSettings.ShootSettings));
                }
                unit.Died += ReleaseUnit;
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

        private void Update()
        {
            if (skipEnemyAfterFrame >= 0 && Time.frameCount > skipEnemyAfterFrame)
            {
                skipEnemyAfterFrame = -1;
                if (IsInitialized && turns.State == BattleState.Running && turns.CurrentFaction == Faction.Enemy)
                    turns.EndCurrentTurn();
            }
        }

        private void OnFactionStarted(Faction faction)
        {
            if (faction == Faction.Enemy) skipEnemyAfterFrame = Time.frameCount;
            Changed?.Invoke();
        }
        private void OnFactionEnded(Faction faction) => Changed?.Invoke();
        private void OnAbilityActivated(AbilityBase ability, AbilityTarget target) => Changed?.Invoke();
        private void OnAbilityEnded(AbilityBase ability, bool cancelled) => Changed?.Invoke();
        private void OnUnitAttributeChanged(AttributeType type, float previous, float value) => Changed?.Invoke();
        private void OnUnitTagChanged(GameplayTag tag, bool added) => Changed?.Invoke();

        private void ReleaseUnit(Unit unit)
        {
            if (cleaning) return;
            unit.Abilities.CancelAll();
            unit.EndTurn();
            grid.TryRemoveUnit(unit);
            turns.UnregisterUnit(unit);
            unit.Died -= ReleaseUnit;
            unit.Destroyed -= ReleaseUnit;
            unit.Attributes.AttributeChanged -= OnUnitAttributeChanged;
            unit.Tags.TagChanged -= OnUnitTagChanged;
            unit.Abilities.AbilityActivated -= OnAbilityActivated;
            unit.Abilities.AbilityEnded -= OnAbilityEnded;
            Changed?.Invoke();
        }

        private void Cleanup()
        {
            cleaning = true;
            if (playerController != null) playerController.Shutdown();
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
                unit.Died -= ReleaseUnit;
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
            IsInitialized = false;
            cleaning = false;
        }
        private void OnDestroy() => Cleanup();
    }
}
