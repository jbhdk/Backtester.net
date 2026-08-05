using System;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Stops;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Stops.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TrailingStopManager"/>: the re-anchoring of both protective legs onto the
    /// entry fill price the first bar the position is open, and the single R-based management rule — once
    /// profit reaches <c>triggerR x R</c> the stop moves to break-even and the trail arms, thereafter
    /// ratcheting a stop whose distance (in R) tightens as price approaches the <c>trailTightenR x R</c>
    /// reference, with break-even as a floor.
    /// </summary>
    public sealed class TrailingStopManagerTests
    {
        // Default trail distances in R: from 2 R (far from the tightening reference) down to 1.25 R (at it).
        private const decimal TrailDistance = 2.0m;
        private const decimal TrailMinDistance = 1.25m;

        /// <summary>
        /// Builds the handle of a bracket whose entry has filled and whose legs rest — the state in which the
        /// manager has a stop to manage. The state is what the manager reads; the ids are what it moves.
        /// </summary>
        private static BracketHandle ArmedHandle(string stopOrderId = "stop-1", string targetOrderId = null)
        {
            return new BracketHandle
            {
                State = BracketState.Armed,
                StopOrderId = stopOrderId,
                TargetOrderId = targetOrderId
            };
        }

        /// <summary>Builds a manager with the supplied break-even inputs and default trail parameters.</summary>
        private static TrailingStopManager CreateManager(
            BracketHandle handle,
            decimal initialStopPrice,
            PositionDirection direction,
            decimal triggerR,
            decimal stopDistance = 10m,
            decimal targetDistance = 0m,
            decimal trailTightenR = 10m,
            decimal trailDistanceR = TrailDistance,
            decimal trailMinDistanceR = TrailMinDistance,
            bool enableManagement = true)
        {
            return new TrailingStopManager(
                handle,
                initialStopPrice,
                direction,
                triggerR,
                stopDistance,
                targetDistance,
                trailTightenR,
                trailDistanceR,
                trailMinDistanceR,
                enableManagement);
        }

        /// <summary>
        /// Drives the entry-fill bar so the manager re-anchors both protective legs onto the fill price, then
        /// clears the recorded re-anchor calls so a test can assert only the break-even/trail behaviour that
        /// follows on later bars.
        /// </summary>
        private static void Reanchor(TrailingStopManager manager, IBroker broker, decimal fillPrice)
        {
            manager.OnBar(close: fillPrice, averagePrice: fillPrice, broker);
            Fake.ClearRecordedCalls(broker);
        }

        /// <summary>The first bar the bracket is armed re-anchors a long's stop and target to the fill price plus/minus the frozen distances.</summary>
        [Fact]
        public void OnBar_LongFirstArmedBar_ReanchorsBothLegsToFillPrice()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle(targetOrderId: "target-1");
            // Submission levels (trigger-close anchored) differ from the fill; re-anchor uses fill 100 +/- distances.
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 95m, PositionDirection.Long, triggerR: 1.0m, stopDistance: 10m, targetDistance: 30m);

            manager.OnBar(close: 100m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify("stop-1", 90m)).MustHaveHappenedOnceExactly();
            A.CallTo(() => broker.Modify("target-1", 130m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>The first bar the bracket is armed re-anchors a short's stop and target to the fill price (mirrored).</summary>
        [Fact]
        public void OnBar_ShortFirstArmedBar_ReanchorsBothLegsToFillPrice()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle(targetOrderId: "target-1");
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 105m, PositionDirection.Short, triggerR: 1.0m, stopDistance: 10m, targetDistance: 30m);

            manager.OnBar(close: 100m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify("stop-1", 110m)).MustHaveHappenedOnceExactly();
            A.CallTo(() => broker.Modify("target-1", 70m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>The re-anchor happens exactly once: a later armed bar does not re-anchor the legs again.</summary>
        [Fact]
        public void OnBar_AlreadyReanchored_DoesNotReanchorAgain()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle(targetOrderId: "target-1");
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 95m, PositionDirection.Long, triggerR: 1.0m, stopDistance: 10m, targetDistance: 30m);

            manager.OnBar(close: 100m, averagePrice: 100m, broker); // re-anchors to 90 / 130
            manager.OnBar(close: 100m, averagePrice: 100m, broker); // no profit yet; nothing to do

            A.CallTo(() => broker.Modify("stop-1", 90m)).MustHaveHappenedOnceExactly();
            A.CallTo(() => broker.Modify("target-1", 130m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>While the bracket is still pending its entry has not filled, so neither the re-anchor nor any move fires.</summary>
        [Fact]
        public void OnBar_BracketStillPending_DoesNotModifyAnyLeg()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = new() { State = BracketState.Pending };
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m);

            manager.OnBar(close: 1000m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify(A<string>._, A<decimal>._)).MustNotHaveHappened();
        }

        /// <summary>A manager over a bracket that has retired is finished: its trade is over and the owner should drop it.</summary>
        [Fact]
        public void IsFinished_BracketRetired_IsTrue()
        {
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m);

            // The bracket retires when its stop or target fills, or when a signal exit cancels what rested.
            handle.State = BracketState.Retired;

            Assert.True(manager.IsFinished);
        }

        /// <summary>A manager over a bracket whose legs still rest is not finished.</summary>
        [Fact]
        public void IsFinished_BracketStillArmed_IsFalse()
        {
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m);

            Assert.False(manager.IsFinished);
        }

        /// <summary>Once the bracket has retired there is no leg left to move, so a further bar modifies nothing.</summary>
        [Fact]
        public void OnBar_BracketRetired_DoesNotModifyAnyLeg()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m);
            Reanchor(manager, broker, fillPrice: 100m);

            handle.State = BracketState.Retired;
            manager.OnBar(close: 130m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify(A<string>._, A<decimal>._)).MustNotHaveHappened();
        }

        /// <summary>
        /// A target-only bracket arms no stop leg, so a stop manager over it has nothing to move: it neither
        /// re-anchors nor trails, however far the trade runs in profit.
        /// </summary>
        [Fact]
        public void OnBar_TargetOnlyBracketArmed_DoesNotModifyAnyLeg()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle(stopOrderId: null, targetOrderId: "target-1");
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m);

            manager.OnBar(close: 100m, averagePrice: 100m, broker);
            manager.OnBar(close: 130m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify(A<string>._, A<decimal>._)).MustNotHaveHappened();
        }

        /// <summary>
        /// A long whose close reaches exactly the configured profit in R moves the stop to the entry price:
        /// break-even fires, and the freshly armed trail (still far below entry) cannot better it.
        /// </summary>
        [Fact]
        public void OnBar_LongReachesTriggerExactly_MovesStopToEntryOnce()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            // Fill 100, stopDistance 10 => re-anchored stop 90 => R = 10; triggerR = 1 => break-even at +10 (close 110).
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 110m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify("stop-1", 100m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>A long still short of the trigger leaves the stop untouched.</summary>
        [Fact]
        public void OnBar_LongBelowTrigger_DoesNotMoveStop()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m);
            Reanchor(manager, broker, fillPrice: 100m);

            // Profit of 9 against R = 10 has not reached the +10 trigger.
            manager.OnBar(close: 109m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify(A<string>._, A<decimal>._)).MustNotHaveHappened();
        }

        /// <summary>A short whose close falls the configured profit in R moves the stop to the entry price.</summary>
        [Fact]
        public void OnBar_ShortReachesTrigger_MovesStopToEntry()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            // Fill 100, stopDistance 10 => re-anchored stop 110 => R = 10; a fall to 90 is +10 profit for a short.
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 110m, PositionDirection.Short, triggerR: 1.0m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 90m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify("stop-1", 100m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>
        /// R is the re-anchored initial risk (entry fill to the re-anchored stop), which equals the frozen stop
        /// distance. With a fill at 104 and a stop distance of 14 the re-anchored stop is 90, so R is 14: a
        /// profit of 10 must not trigger break-even, 14 must.
        /// </summary>
        [Fact]
        public void OnBar_BreakEvenUsesReanchoredRiskFromFillPrice()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m, stopDistance: 14m);
            Reanchor(manager, broker, fillPrice: 104m);

            manager.OnBar(close: 114m, averagePrice: 104m, broker); // profit 10 < R (14)
            A.CallTo(() => broker.Modify(A<string>._, A<decimal>._)).MustNotHaveHappened();

            manager.OnBar(close: 118m, averagePrice: 104m, broker); // profit 14 == R
            A.CallTo(() => broker.Modify("stop-1", 104m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>
        /// Once armed, the long trail sets the stop the (far-from-reference) trail distance below the close.
        /// Entry 100, R 10, trailTightenR 10 => reference span 100: at close 120 progress is 0.2,
        /// distance = 2 - 0.2 x (2 - 1.25) = 1.85 R => stop 120 - 18.5 = 101.5.
        /// </summary>
        [Fact]
        public void OnBar_LongAfterTrigger_TrailsStopBelowCloseByInterpolatedDistance()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m, trailTightenR: 10m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 110m, averagePrice: 100m, broker); // break-even to 100
            manager.OnBar(close: 120m, averagePrice: 100m, broker); // trail

            A.CallTo(() => broker.Modify("stop-1", 101.5m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>The long trail only ever ratchets up: a lower close after a higher one does not loosen the stop.</summary>
        [Fact]
        public void OnBar_LongTrail_NeverLoosens()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m, trailTightenR: 10m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 110m, averagePrice: 100m, broker); // break-even to 100
            manager.OnBar(close: 130m, averagePrice: 100m, broker); // trail up
            manager.OnBar(close: 120m, averagePrice: 100m, broker); // would be lower; ignored

            // The lower close would imply a stop of 101.5, below the prior trailed stop, so no further modify.
            A.CallTo(() => broker.Modify("stop-1", 101.5m)).MustNotHaveHappened();
        }

        /// <summary>
        /// The trail tightens as price nears the reference: at the reference (progress 1) the distance is the
        /// minimum multiple. Entry 100, R 10, trailTightenR 2 => reference span 20, so close 120 is at
        /// the reference => stop 120 - 1.25 x 10 = 107.5.
        /// </summary>
        [Fact]
        public void OnBar_LongAtTightenReference_UsesMinimumTrailDistance()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m, trailTightenR: 2m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 110m, averagePrice: 100m, broker); // break-even to 100
            manager.OnBar(close: 120m, averagePrice: 100m, broker); // at reference

            A.CallTo(() => broker.Modify("stop-1", 107.5m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>
        /// The short trail mirrors the long: once armed it sets the stop the interpolated distance above
        /// the close and only ever ratchets down. Entry 100, R 10, trailTightenR 10 => reference span
        /// 100: at close 80 progress is 0.2, distance = 1.85 R => stop 80 + 18.5 = 98.5.
        /// </summary>
        [Fact]
        public void OnBar_ShortAfterTrigger_TrailsStopAboveCloseByInterpolatedDistance()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 110m, PositionDirection.Short, triggerR: 1.0m, trailTightenR: 10m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 90m, averagePrice: 100m, broker); // break-even to 100
            manager.OnBar(close: 80m, averagePrice: 100m, broker); // trail

            A.CallTo(() => broker.Modify("stop-1", 98.5m)).MustHaveHappenedOnceExactly();
        }

        /// <summary>
        /// On the arming bar itself the trail already competes with break-even: a thrust bar whose trail stop
        /// sits above entry gets that trail stop, not break-even, in a single modify. Entry 100, R 10, a
        /// constant 0.5 R trail: close 110 arms and trails to 110 - 5 = 105 > entry 100.
        /// </summary>
        [Fact]
        public void OnBar_LongTrailBeatsBreakEvenOnArmingBar_UsesTrailStop()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m, trailTightenR: 10m, trailDistanceR: 0.5m, trailMinDistanceR: 0.5m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 110m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify("stop-1", 105m)).MustHaveHappenedOnceExactly();
            A.CallTo(() => broker.Modify(A<string>._, A<decimal>.That.Not.IsEqualTo(105m))).MustNotHaveHappened();
        }

        /// <summary>
        /// The arming-bar floor mirrors for a short: a downward thrust whose trail stop sits below entry gets
        /// that trail stop. Entry 100, R 10, constant 0.5 R trail: close 90 arms and trails to 90 + 5 = 95.
        /// </summary>
        [Fact]
        public void OnBar_ShortTrailBeatsBreakEvenOnArmingBar_UsesTrailStop()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 110m, PositionDirection.Short, triggerR: 1.0m, trailTightenR: 10m, trailDistanceR: 0.5m, trailMinDistanceR: 0.5m);
            Reanchor(manager, broker, fillPrice: 100m);

            manager.OnBar(close: 90m, averagePrice: 100m, broker);

            A.CallTo(() => broker.Modify("stop-1", 95m)).MustHaveHappenedOnceExactly();
            A.CallTo(() => broker.Modify(A<string>._, A<decimal>.That.Not.IsEqualTo(95m))).MustNotHaveHappened();
        }

        /// <summary>
        /// An inverted trail configuration — the maximum distance below the minimum — would make the trail
        /// widen as profit grows instead of tightening, so the constructor rejects it outright.
        /// </summary>
        [Fact]
        public void Constructor_TrailDistanceBelowMinDistance_Throws()
        {
            BracketHandle handle = ArmedHandle();

            ArgumentOutOfRangeException rejection = Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m, trailDistanceR: 1.0m, trailMinDistanceR: 1.5m));

            Assert.Equal("trailDistanceR", rejection.ParamName);
        }

        /// <summary>Equal trail distances are a legal constant-distance trail, not an inversion.</summary>
        [Fact]
        public void Constructor_TrailDistanceEqualToMinDistance_IsAccepted()
        {
            IBroker broker = A.Fake<IBroker>();
            BracketHandle handle = ArmedHandle();
            TrailingStopManager manager = CreateManager(handle, initialStopPrice: 90m, PositionDirection.Long, triggerR: 1.0m, trailTightenR: 10m, trailDistanceR: 1.5m, trailMinDistanceR: 1.5m);
            Reanchor(manager, broker, fillPrice: 100m);

            // Entry 100, R 10: break-even at 110, then the trail holds a constant 1.5 R = 15 behind the close.
            manager.OnBar(close: 110m, averagePrice: 100m, broker); // break-even to 100
            manager.OnBar(close: 120m, averagePrice: 100m, broker); // trail: 120 - 15

            A.CallTo(() => broker.Modify("stop-1", 105m)).MustHaveHappenedOnceExactly();
        }
    }
}
