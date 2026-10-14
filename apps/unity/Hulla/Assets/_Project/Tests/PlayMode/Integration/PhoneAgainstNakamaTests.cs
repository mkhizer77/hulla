using System;
using System.Collections;
using Hulla.Application.ViewModels;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>
    /// Phone sign-in against a running Nakama on a local box (GDD 10.2, #99): the local provider's code
    /// is the number's last six digits. One phone links the number; a second phone confirms it, switches
    /// to that account with its progress, and its device id then opens that account. Runs only when
    /// HULLA_NAKAMA_HOST is set.
    /// </summary>
    public sealed class PhoneAgainstNakamaTests
    {
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ANumberLinksOnOnePhoneAndSignsInOnAnother()
        {
            var host = Environment.GetEnvironmentVariable("HULLA_NAKAMA_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Ignore("HULLA_NAKAMA_HOST is not set");
            }

            var port = int.TryParse(Environment.GetEnvironmentVariable("HULLA_NAKAMA_PORT"), out var p) ? p : 7350;
            var awaiter = RunAsync(host!, port).GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            awaiter.GetResult();
        }

        private static async Awaitable RunAsync(string host, int port)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var phone = "+92300" + stamp.Substring(stamp.Length - 7);
            var code = phone.Substring(phone.Length - 6);

            var firstConnection = NakamaConnection.Create("http", host, port, "local-only-server-key");
            var firstSessions = new NakamaSessionService(firstConnection, "hulla-phone-a-" + stamp);
            var first = await firstSessions.SignInAsync("Ayesha", default);
            Assert.IsTrue(first.IsOk, "first phone signs in");
            var firstPhone = new PhoneViewModel(new NakamaPhoneService(firstConnection, firstSessions));
            await firstPhone.SendAsync(phone, default);
            Assert.AreEqual(PhoneViewModel.Stage.CodeSent, firstPhone.Current, firstPhone.ErrorCode);
            await firstPhone.VerifyAsync(code, default);
            Assert.AreEqual(PhoneViewModel.Stage.Linked, firstPhone.Current, firstPhone.ErrorCode);

            // The same number waits a minute before another code.
            await Awaitable.WaitForSecondsAsync(61f);
            var secondDevice = "hulla-phone-b-" + stamp;
            var secondConnection = NakamaConnection.Create("http", host, port, "local-only-server-key");
            var secondSessions = new NakamaSessionService(secondConnection, secondDevice);
            Assert.IsTrue((await secondSessions.SignInAsync("Guest", default)).IsOk, "second phone signs in as a guest");
            var secondPhone = new PhoneViewModel(new NakamaPhoneService(secondConnection, secondSessions));
            await secondPhone.SendAsync(phone, default);
            await secondPhone.VerifyAsync(code, default);
            Assert.AreEqual(PhoneViewModel.Stage.Offer, secondPhone.Current, secondPhone.ErrorCode);
            await secondPhone.SwitchAsync(default);
            Assert.AreEqual(PhoneViewModel.Stage.Switched, secondPhone.Current, secondPhone.ErrorCode);
            Assert.AreEqual(first.Value.UserId, secondSessions.Current!.UserId);
            Assert.AreEqual("Ayesha", secondSessions.Current.DisplayName, "the account keeps its own name");

            // The next start of the second phone signs in by device id and lands on the same account.
            var restart = new NakamaSessionService(NakamaConnection.Create("http", host, port, "local-only-server-key"), secondDevice);
            var again = await restart.SignInAsync("", default);
            Assert.IsTrue(again.IsOk);
            Assert.AreEqual(first.Value.UserId, again.Value.UserId);
            firstConnection.Close();
            secondConnection.Close();
        }
    }
}
