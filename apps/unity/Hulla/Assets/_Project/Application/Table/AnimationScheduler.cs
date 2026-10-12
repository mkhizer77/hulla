namespace Hulla.Application.Table
{
    /// <summary>
    /// Plays queued events one after another with the hold each presenter asks for. When the queue
    /// backs up past <see cref="CoalesceAbove"/> the holds are skipped so the table catches up. Input
    /// never waits on the scheduler: commands go straight to the table client.
    /// </summary>
    public sealed class AnimationScheduler
    {
        private readonly EventQueue _queue;
        private readonly IEventPresenter _presenter;
        private float _hold;

        public AnimationScheduler(EventQueue queue, IEventPresenter presenter, int coalesceAbove = 6)
        {
            _queue = queue;
            _presenter = presenter;
            CoalesceAbove = coalesceAbove;
        }

        public int CoalesceAbove { get; }

        public bool Busy => _hold > 0f;

        public void Tick(float deltaSeconds)
        {
            _hold -= deltaSeconds;
            while (_hold <= 0f && _queue.TryDequeue(out var message))
            {
                var hold = _presenter.Present(message);
                _hold = _queue.Count > CoalesceAbove ? 0f : _hold + hold;
            }

            if (_hold < 0f)
            {
                _hold = 0f;
            }
        }
    }
}
