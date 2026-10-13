using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    /// <summary>The avatar the server assigned to each seat (GDD 15.6) reaches the seat list and survives presence changes.</summary>
    public sealed class SeatAvatarTests
    {
        [Test]
        public void TheTableStateCarriesEachSeatsAvatar()
        {
            var model = new TableModel(Messages.Me);
            model.Apply(Messages.TableState(1, avatars: new List<string> { "a03", "a01", "a02", "a04" }));
            Assert.AreEqual("a03", model.Seats[0].Avatar);
            Assert.AreEqual("a01", model.Seats[1].Avatar);
            Assert.AreEqual("a04", model.Seats[3].Avatar);
        }

        [Test]
        public void ADisconnectOrAutoPlayKeepsTheSeatsAvatar()
        {
            var model = new TableModel(Messages.Me);
            model.Apply(Messages.TableState(1, avatars: new List<string> { "a03", "a01", "a02", "a04" }));
            model.Apply(new ServerMessage(MessageTypes.Presence, 2, new Presence { Seat = 1, PlayerId = "user-1", Status = Status.Disconnected }));
            Assert.AreEqual("a01", model.Seats[1].Avatar);
            Assert.IsFalse(model.Seats[1].Connected);
            model.Apply(new ServerMessage(MessageTypes.Presence, 3, new Presence { Seat = 1, PlayerId = "user-1", Status = Status.AutoPlay }));
            Assert.AreEqual("a01", model.Seats[1].Avatar);
            model.Apply(new ServerMessage(MessageTypes.Presence, 4, new Presence { Seat = 1, PlayerId = "user-1", Status = Status.Reconnected }));
            Assert.AreEqual("a01", model.Seats[1].Avatar, "the same player back in the seat keeps it");
        }

        [Test]
        public void ANewOccupantWaitsForTheTableStateForTheirs()
        {
            var model = new TableModel(Messages.Me);
            model.Apply(Messages.TableState(1, avatars: new List<string> { "a03", "a01", "a02", "a04" }));
            model.Apply(new ServerMessage(MessageTypes.Presence, 2, new Presence { Seat = 1, PlayerId = "user-1", Status = Status.Left }));
            Assert.IsNull(model.Seats[1].Avatar);
            model.Apply(new ServerMessage(MessageTypes.Presence, 3, new Presence { Seat = 1, PlayerId = "user-9", DisplayName = "Noor", Status = Status.Joined }));
            Assert.IsNull(model.Seats[1].Avatar, "no avatar is guessed for a player the server has not described yet");
            Assert.AreEqual("Noor", model.Seats[1].DisplayName);
        }
    }
}
