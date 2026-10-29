namespace Wargame.Units
{
    /// <summary>
    /// 单位身上的状态标记。按用途分段并显式赋值，Inspector 序列化保存的是数值，
    /// 新增标记请追加到对应分段末尾，不要修改已有数值。
    /// </summary>
    public enum GameplayTag
    {
        /// <summary>无效标记，不会被记录。</summary>
        None = 0,

        // ---- 生命状态 1~99 ----
        /// <summary>已死亡。</summary>
        Dead = 1,
        /// <summary>失去意识，不能行动但未死亡。</summary>
        Unconscious = 2,

        // ---- 控制状态 100~199 ----
        /// <summary>眩晕，不能行动。</summary>
        Stunned = 100,
        /// <summary>恐慌，行动不受玩家控制。</summary>
        Panicked = 101,
        /// <summary>被压制，射击命中降低且无法警戒。</summary>
        Suppressed = 102,
        /// <summary>被束缚，不能移动。</summary>
        Bound = 103,
        /// <summary>被心灵控制。</summary>
        MindControlled = 104,

        // ---- 行动姿态 200~299 ----
        /// <summary>警戒中，敌方移动时触发反应射击。</summary>
        Overwatch = 200,
        /// <summary>蹲伏防御，提高防御和闪避。</summary>
        HunkerDown = 201,
        /// <summary>正在移动。</summary>
        Moving = 202,
        /// <summary>处于掩体后。</summary>
        InCover = 203,

        // ---- 持续伤害 300~399 ----
        /// <summary>燃烧。</summary>
        Burning = 300,
        /// <summary>中毒。</summary>
        Poisoned = 301,
        /// <summary>流血。</summary>
        Bleeding = 302,
        /// <summary>酸蚀，降低护甲。</summary>
        Acid = 303,

        // ---- 隐蔽与侦测 400~499 ----
        /// <summary>小队隐蔽状态。</summary>
        Concealed = 400,
        /// <summary>被标记暴露。</summary>
        Revealed = 401,

        // ---- 免疫 500~599 ----
        /// <summary>免疫燃烧。</summary>
        ImmuneFire = 500,
        /// <summary>免疫中毒。</summary>
        ImmunePoison = 501,
        /// <summary>免疫心灵类效果。</summary>
        ImmuneMental = 502,
        /// <summary>免疫眩晕。</summary>
        ImmuneStun = 503,

        // ---- 单位特性 600~699 ----
        /// <summary>机械单位。</summary>
        Robotic = 600,
        /// <summary>生物单位。</summary>
        Organic = 601
    }
}
