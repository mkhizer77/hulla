using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The Ranked screen: the player's season card and the season's top places.</summary>
    public sealed class RankedViewModel
    {
        private readonly IRankedService _service;
        private int _request;

        public RankedViewModel(IRankedService service)
        {
            _service = service;
        }

        public event Action? Changed;

        public RankedCard? Card { get; private set; }

        public IReadOnlyList<RankedRow> Top { get; private set; } = Array.Empty<RankedRow>();

        public string? ErrorCode { get; private set; }

        public bool Loading { get; private set; }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            var request = ++_request;
            Loading = true;
            Changed?.Invoke();
            var card = await _service.CardAsync(cancellation);
            IReadOnlyList<RankedRow> top = Array.Empty<RankedRow>();
            if (card.IsOk)
            {
                var board = await _service.BoardAsync(cancellation);
                if (board.IsOk)
                {
                    top = board.Value;
                }
            }

            if (request != _request)
            {
                return;
            }

            Loading = false;
            ErrorCode = card.IsOk ? null : card.Error.Code;
            Card = card.IsOk ? card.Value : null;
            Top = top;
            Changed?.Invoke();
        }
    }
}
