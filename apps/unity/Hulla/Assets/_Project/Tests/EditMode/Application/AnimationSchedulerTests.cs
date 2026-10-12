using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class AnimationSchedulerTests
    {
        private sealed class Recorder : IEventPresenter
        {
            public readonly List<long> Shown = new List<long>();

            public float Present(ServerMessage message)
            {
                Shown.Add(message.Seq);
                return 0.5f;
            }
        }

        [Test]
        public void EventsPlayInOrderWithTheirHolds()
        {
            var queue = new EventQueue();
            var recorder = new Recorder();
            var scheduler = new AnimationScheduler(queue, recorder);
            queue.Enqueue(new ServerMessage("a", 1, new object()));
            queue.Enqueue(new ServerMessage("b", 2, new object()));
            scheduler.Tick(0f);
            CollectionAssert.AreEqual(new[] { 1L }, recorder.Shown);
            scheduler.Tick(0.3f);
            CollectionAssert.AreEqual(new[] { 1L }, recorder.Shown);
            scheduler.Tick(0.3f);
            CollectionAssert.AreEqual(new[] { 1L, 2L }, recorder.Shown);
        }

        [Test]
        public void ABackedUpQueueSkipsTheHoldsToCatchUp()
        {
            var queue = new EventQueue();
            var recorder = new Recorder();
            var scheduler = new AnimationScheduler(queue, recorder, coalesceAbove: 2);
            for (var i = 1; i <= 6; i++)
            {
                queue.Enqueue(new ServerMessage("e", i, new object()));
            }

            scheduler.Tick(0f);
            Assert.AreEqual(4, recorder.Shown.Count);
        }
    }
}
