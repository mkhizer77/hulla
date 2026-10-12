using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The first screen: sign in with a name, then create a private table or join one by code.</summary>
    public sealed class HomeViewModel
    {
        private readonly ISessionService _session;
        private readonly ITableGateway _tables;

        public HomeViewModel(ISessionService session, ITableGateway tables)
        {
            _session = session;
            _tables = tables;
        }

        public event Action? Changed;

        public bool Busy { get; private set; }

        public string? ErrorCode { get; private set; }

        public string LastName { get; set; } = "";

        public Awaitable<Result<TableTicket>> CreateAsync(string name, CancellationToken cancellation) =>
            RunAsync(name, null, cancellation);

        public Awaitable<Result<TableTicket>> JoinAsync(string name, string code, CancellationToken cancellation) =>
            RunAsync(name, code, cancellation);

        /// <summary>Signs in (again only when the name changed) without creating or joining a table.</summary>
        public async Awaitable<Result<PlayerSession>> SignInAsync(string name, CancellationToken cancellation)
        {
            if (_session.Current != null && name == LastName)
            {
                return Result<PlayerSession>.Ok(_session.Current);
            }

            var signedIn = await _session.SignInAsync(name, cancellation);
            if (signedIn.IsOk)
            {
                LastName = name;
            }
            else
            {
                ErrorCode = signedIn.Error.Code;
                Changed?.Invoke();
            }

            return signedIn;
        }

        private async Awaitable<Result<TableTicket>> RunAsync(string name, string? code, CancellationToken cancellation)
        {
            if (Busy)
            {
                return Result<TableTicket>.Fail("home.busy");
            }

            Busy = true;
            ErrorCode = null;
            Changed?.Invoke();
            try
            {
                if (_session.Current == null || name != LastName)
                {
                    var signedIn = await _session.SignInAsync(name, cancellation);
                    if (!signedIn.IsOk)
                    {
                        return Fail(signedIn.Error);
                    }

                    LastName = name;
                }

                var ticket = code == null
                    ? await _tables.CreateTableAsync(cancellation)
                    : await _tables.JoinByCodeAsync(code, cancellation);
                return ticket.IsOk ? ticket : Fail(ticket.Error);
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }

        private Result<TableTicket> Fail(RuleErrorCode error)
        {
            ErrorCode = error.Code;
            return Result<TableTicket>.Fail(error);
        }
    }
}
