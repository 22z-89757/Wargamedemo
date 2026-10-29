namespace Wargame.Units
{
    /// <summary>单位数值属性。序列化保存数值，新增属性请追加，不要修改已有数值。</summary>
    public enum AttributeType
    {
        /// <summary>当前生命，限制在 [0, MaxHealth]。</summary>
        Health = 0,
        /// <summary>生命上限。</summary>
        MaxHealth = 1,
        /// <summary>本回合剩余行动点，限制在 [0, MaxActionPoints]。</summary>
        ActionPoints = 2,
        /// <summary>每回合恢复的行动点上限。</summary>
        MaxActionPoints = 3,
        /// <summary>每个行动点可支付的移动代价。</summary>
        Mobility = 4,
        /// <summary>命中。</summary>
        Aim = 5,
        /// <summary>防御，降低被命中率。</summary>
        Defense = 6,
        /// <summary>闪避，命中后转为擦伤的概率。</summary>
        Dodge = 7,
        /// <summary>护甲，每次受击减免的伤害。</summary>
        Armor = 8,
        /// <summary>暴击率。</summary>
        CritChance = 9,
        /// <summary>意志，影响恐慌和心灵抗性。</summary>
        Will = 10,
        /// <summary>视野范围（格）。</summary>
        SightRange = 11
    }
}
