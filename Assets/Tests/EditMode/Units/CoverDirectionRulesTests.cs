using NUnit.Framework;
using UnityEngine;
using Wargame.GridSystem;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class CoverDirectionRulesTests
    {
        private static readonly Vector2Int Target = new Vector2Int(5, 5);

        [TestCase(5, 7, GridDirection.North)]
        [TestCase(7, 5, GridDirection.East)]
        [TestCase(5, 3, GridDirection.South)]
        [TestCase(3, 5, GridDirection.West)]
        public void OrthogonalAttackReadsMatchingBoundary(int attackerX, int attackerY,
            GridDirection expected)
        {
            IncomingDirectionMask mask = CoverDirectionRules.GetIncomingDirections(
                new Vector2Int(attackerX, attackerY), Target);

            for (GridDirection direction = GridDirection.North;
                 direction <= GridDirection.West; direction++)
                Assert.That(CoverDirectionRules.Includes(mask, direction),
                    Is.EqualTo(direction == expected));
        }

        [Test]
        public void DiagonalAttackUsesStrongestOfTwoFacingBoundaries()
        {
            var cell = new MapCellData(Target, 1, BlockState.Unblocked,
                northCover: CoverType.HalfCover, eastCover: CoverType.FullCover,
                southCover: CoverType.FullCover, westCover: CoverType.NoCover);

            Assert.That(CoverDirectionRules.GetStrongestCover(cell, Target + new Vector2Int(2, 2)),
                Is.EqualTo(CoverType.FullCover));
            Assert.That(CoverDirectionRules.GetStrongestCover(cell, Target + new Vector2Int(-2, -2)),
                Is.EqualTo(CoverType.FullCover));
        }

        [Test]
        public void UnrelatedBoundaryAndSameCellProvideNoCover()
        {
            var eastOnly = new MapCellData(Target, 1, BlockState.Unblocked,
                eastCover: CoverType.FullCover);

            Assert.That(CoverDirectionRules.GetStrongestCover(eastOnly, Target + Vector2Int.left * 3),
                Is.EqualTo(CoverType.NoCover));
            Assert.That(CoverDirectionRules.GetStrongestCover(eastOnly, Target),
                Is.EqualTo(CoverType.NoCover));
        }
    }
}
