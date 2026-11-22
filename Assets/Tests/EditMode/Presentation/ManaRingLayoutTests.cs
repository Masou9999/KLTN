using System;
using NUnit.Framework;

namespace KLTN.Game.Presentation.Tests
{
    public sealed class ManaRingLayoutTests
    {
        private const float Tolerance = 1e-4f;

        private static ManaRingLayoutSettings Defaults() => new ManaRingLayoutSettings();

        private static ManaSlotGeometry[] Upper() =>
            ManaRingLayout.Build(Defaults(), ManaArcSide.Upper);

        private static ManaSlotGeometry[] Lower() =>
            ManaRingLayout.Build(Defaults(), ManaArcSide.Lower);

        [Test]
        public void EachArcHasTenSlots()
        {
            Assert.That(Upper().Length, Is.EqualTo(10));
            Assert.That(Lower().Length, Is.EqualTo(10));
        }

        [Test]
        public void AllTwentySlotsShareTheSameSize()
        {
            ManaSlotGeometry reference = Upper()[0];

            foreach (ManaArcSide side in new[] { ManaArcSide.Upper, ManaArcSide.Lower })
            {
                foreach (ManaSlotGeometry slot in ManaRingLayout.Build(Defaults(), side))
                {
                    Assert.That(slot.HalfAngle,
                        Is.EqualTo(reference.HalfAngle).Within(Tolerance));
                    Assert.That(slot.InnerRadius,
                        Is.EqualTo(reference.InnerRadius).Within(Tolerance));
                    Assert.That(slot.OuterRadius,
                        Is.EqualTo(reference.OuterRadius).Within(Tolerance));
                }
            }
        }

        [TestCase(ManaArcSide.Upper)]
        [TestCase(ManaArcSide.Lower)]
        public void AdjacentSlotsTouchWithoutGaps(ManaArcSide side)
        {
            ManaSlotGeometry[] slots = ManaRingLayout.Build(Defaults(), side);

            for (int i = 0; i < slots.Length - 1; i++)
            {
                float forward = Math.Abs(slots[i].EndAngle - slots[i + 1].StartAngle);
                float backward = Math.Abs(slots[i].StartAngle - slots[i + 1].EndAngle);
                Assert.That(Math.Min(forward, backward), Is.LessThan(Tolerance),
                    $"gap after slot {i}");
            }
        }

        [Test]
        public void ArcsAreSeparatedByTheConfiguredGapAroundNineOClock()
        {
            ManaSlotGeometry[] upper = Upper();
            ManaSlotGeometry[] lower = Lower();

            // Default fill starts at the ornament, so the last slot borders the gap.
            float upperGapEdge = upper[9].EndAngle;
            float lowerGapEdge = lower[9].StartAngle;

            Assert.That(upperGapEdge, Is.EqualTo(175f).Within(Tolerance));
            Assert.That(lowerGapEdge, Is.EqualTo(185f).Within(Tolerance));
        }

        [Test]
        public void ArcsMirrorEachOtherAroundTheGap()
        {
            ManaSlotGeometry[] upper = Upper();
            ManaSlotGeometry[] lower = Lower();

            for (int i = 0; i < upper.Length; i++)
            {
                Assert.That(180f - upper[i].CenterAngle,
                    Is.EqualTo(lower[i].CenterAngle - 180f).Within(Tolerance));
            }
        }

        [Test]
        public void FirstManaStartsAtTheOrnamentAndMovesTowardTheGap()
        {
            ManaSlotGeometry[] upper = Upper();
            ManaSlotGeometry[] lower = Lower();

            Assert.That(upper[0].StartAngle, Is.EqualTo(104f).Within(Tolerance));
            Assert.That(lower[0].EndAngle, Is.EqualTo(256f).Within(Tolerance));
            Assert.That(upper[1].CenterAngle, Is.GreaterThan(upper[0].CenterAngle));
            Assert.That(lower[1].CenterAngle, Is.LessThan(lower[0].CenterAngle));
        }

        [Test]
        public void FromGapReversesTheFillOrder()
        {
            ManaRingLayoutSettings settings = Defaults();
            settings.fillDirection = ManaFillDirection.FromGap;

            ManaSlotGeometry[] upper = ManaRingLayout.Build(settings, ManaArcSide.Upper);

            Assert.That(upper[0].EndAngle, Is.EqualTo(175f).Within(Tolerance));
            Assert.That(upper[9].StartAngle, Is.EqualTo(104f).Within(Tolerance));
        }

        [Test]
        public void RotationOffsetMovesBothArcsTogether()
        {
            ManaRingLayoutSettings settings = Defaults();
            settings.rotationOffset = 5f;

            Assert.That(ManaRingLayout.Build(settings, ManaArcSide.Upper)[0].StartAngle,
                Is.EqualTo(109f).Within(Tolerance));
            Assert.That(ManaRingLayout.Build(settings, ManaArcSide.Lower)[0].EndAngle,
                Is.EqualTo(261f).Within(Tolerance));
        }
    }
}
