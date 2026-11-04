namespace Wargame.Events
{
    /// <summary>战斗产生正常胜负结算后发布的事件。</summary>
    public readonly struct GameEndedEvent
    {
        public GameEndedEvent(GameOutcome outcome)
        {
            Outcome = outcome;
        }

        /// <summary>玩家视角的结算结果。</summary>
        public GameOutcome Outcome { get; }
    }
}
