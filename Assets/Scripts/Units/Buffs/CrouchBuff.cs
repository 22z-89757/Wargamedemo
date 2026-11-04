namespace Wargame.Units
{
    /// <summary>蹲伏姿态提供额外闪避和防御；移动离开姿态时由 MoveAbility 移除。</summary>
    public sealed class CrouchBuff : BuffBase
    {
        public const string Id = "Defend.Crouch";

        public CrouchBuff() : base(Id, Permanent, BuffStackPolicy.Unique)
        {
            DisplayName = "蹲伏";
            AddGrantedTag(GameplayTag.HunkerDown);
            AddModifier(AttributeType.Dodge, ModifierOperation.Add, 20f);
            AddModifier(AttributeType.Defense, ModifierOperation.Add, 10f);
        }
    }
}
