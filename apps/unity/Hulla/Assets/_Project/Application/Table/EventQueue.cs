using System.Collections.Generic;
using Hulla.Application.Services;

namespace Hulla.Application.Table
{
    /// <summary>Applied messages waiting for the animation scheduler, in the order the model applied them.</summary>
    public sealed class EventQueue
    {
        private readonly Queue<ServerMessage> _queue = new Queue<ServerMessage>();

        public int Count => _queue.Count;

        public void Enqueue(ServerMessage message) => _queue.Enqueue(message);

        public bool TryDequeue(out ServerMessage message) => _queue.TryDequeue(out message);

        public void Clear() => _queue.Clear();
    }
}
