using LandmarkEmulator.Internal.Social;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.Internal.Login
{
    /// <summary>
    /// A link to the login service (the auth server's internal port, LOGIN_INTERNAL_ADDRESS; gateway-service.md §3.7,
    /// <see cref="LoginMessages"/>): ticket checks and relog sessions, for the gateway and the world channel. Requests
    /// wait at most <see cref="Timeout"/>; an unreachable login service fails them (null).
    /// </summary>
    public sealed class LoginClient : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly InternalDialer dialer;
        private readonly Action<string> log;
        private readonly object sync = new();
        private InternalLink link;
        private int nextRequest;
        private readonly Dictionary<uint, TaskCompletionSource<ISocialMessage>> waiting = new();

        /// <param name="log">Where to write the client's few log lines (connected, lost, unanswered).</param>
        public LoginClient(string address, Action<string> log)
        {
            this.log = log ?? (_ => { });
            dialer = new InternalDialer(address.Trim());
            dialer.Connected += newLink =>
            {
                newLink.Received += (_, message) => OnReceived(message);
                newLink.Closed += (l, error) =>
                {
                    lock (sync)
                        if (link == l)
                            link = null;
                    this.log($"Login service link lost{(error != null ? ": " + error.Message : "")}; reconnecting.");
                };
                lock (sync)
                    link = newLink;
                this.log("Login service connected.");
            };
            dialer.Start();
        }

        public void Dispose() => dialer.Dispose();

        private void OnReceived(IInternalMessage message)
        {
            if (message is not ServicePacket packet || LoginMessages.Parse(packet) is not ISocialMessage reply)
                return;
            uint id = reply switch
            {
                TicketConsumeReply r => r.RequestId,
                RelogSessionReply r => r.RequestId,
                _ => 0
            };
            TaskCompletionSource<ISocialMessage> request;
            lock (sync)
                waiting.Remove(id, out request);
            request?.TrySetResult(reply);
        }

        private async Task<T> Ask<T>(Func<uint, ISocialMessage> build) where T : class
        {
            var request = new TaskCompletionSource<ISocialMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            uint id;
            bool sent;
            lock (sync)
            {
                id = (uint)Interlocked.Increment(ref nextRequest);
                waiting[id] = request;
                sent = link != null && link.Send(build(id).ToPacket());
            }
            if (sent && await Task.WhenAny(request.Task, Task.Delay(Timeout)) == request.Task)
                return request.Task.Result as T;
            lock (sync)
                waiting.Remove(id);
            log(sent ? "Login service did not answer in time." : "Login service unreachable.");
            return null;
        }

        /// <summary>Consumes a server ticket; null when refused or the service can't be reached.</summary>
        public async Task<TicketConsumeReply> Consume(string ticket, ulong characterGuid, uint worldId = 0)
        {
            TicketConsumeReply reply = await Ask<TicketConsumeReply>(id => new TicketConsume
            {
                RequestId = id, Ticket = ticket ?? "", CharacterGuid = characterGuid, WorldId = worldId
            });
            return reply is { Ok: true } ? reply : null;
        }

        /// <summary>
        /// A relog session: for <paramref name="accountId"/>, or (0) the account of <paramref name="characterGuid"/>; with
        /// <paramref name="key"/> or one the service makes. Null when none could be made.
        /// </summary>
        public async Task<RelogSessionReply> Relog(ulong accountId, ulong characterGuid = 0, string key = "")
        {
            RelogSessionReply reply = await Ask<RelogSessionReply>(id => new RelogSession
            {
                RequestId = id, AccountId = accountId, CharacterGuid = characterGuid, Key = key ?? ""
            });
            return string.IsNullOrEmpty(reply?.SessionKey) ? null : reply;
        }
    }
}
