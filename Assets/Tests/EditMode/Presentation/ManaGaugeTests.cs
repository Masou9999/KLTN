using NUnit.Framework;

namespace KLTN.Game.Presentation.Tests
{
    public sealed class ManaGaugeTests
    {
        [TestCase(0, 0)]
        [TestCase(5, 5)]
        [TestCase(10, 10)]
        [TestCase(15, 10)]
        [TestCase(-3, 0)]
        public void SetManaClampsAndFillsSequentially(int value, int expectedFilled)
        {
            var gauge = new ManaGauge(10);
            gauge.SetMana(value);

            for (int i = 0; i < 10; i++)
            {
                ManaSlotState expected = i < expectedFilled
                    ? ManaSlotState.Filled
                    : ManaSlotState.Empty;
                Assert.That(gauge.StateOf(i), Is.EqualTo(expected), $"slot {i}");
            }
        }

        [Test]
        public void ReservedManaIsTheTailOfTheFilledSlots()
        {
            var gauge = new ManaGauge(10);
            gauge.SetMana(8);
            gauge.SetReserved(2);

            for (int i = 0; i < 10; i++)
            {
                ManaSlotState expected = i < 6 ? ManaSlotState.Filled
                    : i < 8 ? ManaSlotState.Reserved
                    : ManaSlotState.Empty;
                Assert.That(gauge.StateOf(i), Is.EqualTo(expected), $"slot {i}");
            }
        }

        [Test]
        public void ReservedNeverExceedsCurrentMana()
        {
            var gauge = new ManaGauge(10);
            gauge.SetMana(3);
            gauge.SetReserved(7);

            Assert.That(gauge.ReservedMana, Is.EqualTo(3));
            Assert.That(gauge.AvailableMana, Is.EqualTo(0));
        }

        [Test]
        public void AddAndConsumeUpdateCurrentMana()
        {
            var gauge = new ManaGauge(10);
            gauge.AddMana(4);
            gauge.AddMana(9);

            Assert.That(gauge.CurrentMana, Is.EqualTo(10));
            Assert.That(gauge.ConsumeMana(3), Is.True);
            Assert.That(gauge.CurrentMana, Is.EqualTo(7));
            Assert.That(gauge.ConsumeMana(8), Is.False);
            Assert.That(gauge.CurrentMana, Is.EqualTo(7));
        }

        [Test]
        public void ChangedFiresOnlyForVisibleChanges()
        {
            var gauge = new ManaGauge(10);
            int notifications = 0;
            gauge.Changed += _ => notifications++;

            gauge.SetMana(4);
            gauge.SetMana(4);
            gauge.SetReserved(0);
            gauge.SetReserved(1);

            Assert.That(notifications, Is.EqualTo(2));
        }

        [Test]
        public void LoweringMaxManaClampsCurrentMana()
        {
            var gauge = new ManaGauge(10);
            gauge.SetMana(9);
            gauge.SetMaxMana(5);

            Assert.That(gauge.CurrentMana, Is.EqualTo(5));
        }
    }
}
