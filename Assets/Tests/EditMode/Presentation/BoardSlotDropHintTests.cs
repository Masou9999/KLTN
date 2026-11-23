using NUnit.Framework;

namespace KLTN.Game.Presentation.Tests
{
    public sealed class BoardSlotDropHintTests
    {
        private const float Tolerance = 1e-4f;
        private const int Slots = 3;

        #region Drop Hint Rules

        [Test]
        public void AttackLightsEveryEmptySlot()
        {
            bool[] result = BoardDropHintRules.ComputeEligibleSlots(
                BoardDropHintMode.Attack, Slots, new bool[Slots], null);

            Assert.That(result, Is.EqualTo(new[] { true, true, true }));
        }

        [Test]
        public void AttackSkipsOccupiedSlots()
        {
            bool[] result = BoardDropHintRules.ComputeEligibleSlots(
                BoardDropHintMode.Attack, Slots, new[] { false, true, false }, null);

            Assert.That(result, Is.EqualTo(new[] { true, false, true }));
        }

        [Test]
        public void BlockLightsOnlyDefendableSlots()
        {
            bool[] result = BoardDropHintRules.ComputeEligibleSlots(
                BoardDropHintMode.Block, Slots, new bool[Slots], new[] { false, true, true });

            Assert.That(result, Is.EqualTo(new[] { false, true, true }));
        }

        [Test]
        public void BlockSkipsDefendableSlotThatAlreadyHasBlocker()
        {
            bool[] result = BoardDropHintRules.ComputeEligibleSlots(
                BoardDropHintMode.Block, Slots, new[] { false, true, false }, new[] { false, true, true });

            Assert.That(result, Is.EqualTo(new[] { false, false, true }));
        }

        [Test]
        public void NoneLightsNothing()
        {
            bool[] result = BoardDropHintRules.ComputeEligibleSlots(
                BoardDropHintMode.None, Slots, new bool[Slots], new[] { true, true, true });

            Assert.That(result, Is.EqualTo(new[] { false, false, false }));
        }

        #endregion

        #region Perimeter

        [Test]
        public void PerimeterStartsTopLeftAndRunsClockwise()
        {
            AssertPoint(0f, -125f, 180f);
            AssertPoint(250f, 125f, 180f);
            AssertPoint(250f + 360f, 125f, -180f);
            AssertPoint(500f + 360f, -125f, -180f);
            AssertPoint(125f, 0f, 180f);
            AssertPoint(250f + 180f, 125f, 0f);
        }

        [Test]
        public void PerimeterWrapsBothDirections()
        {
            float length = BoardSlotPerimeter.Length(250f, 360f);

            Assert.That(length, Is.EqualTo(1220f).Within(Tolerance));
            AssertPoint(length + 10f, -115f, 180f);
            AssertPoint(-10f, -125f, 170f);
        }

        private static void AssertPoint(float distance, float expectedX, float expectedY)
        {
            BoardSlotPerimeter.Evaluate(250f, 360f, distance, out float x, out float y);

            Assert.That(x, Is.EqualTo(expectedX).Within(Tolerance));
            Assert.That(y, Is.EqualTo(expectedY).Within(Tolerance));
        }

        #endregion
    }
}
