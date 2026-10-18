using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Login;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Database;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace LandmarkEmulator.AuthServer
{
    /// <summary>
    /// The login service's internal port (LOGIN_INTERNAL_ADDRESS, default auth:20210, never published;
    /// trace/architecture/gateway-service.md §3.7): gateways in their own process consume the clients' single-use server
    /// tickets here instead of reading the auth database, and ask for relog sessions (Internal/Login/LoginMessages.cs).
    /// Each request is answered on the link it came on; the database work runs off the link's reading task.
    /// </summary>
    public static class LoginInternalService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static InternalListener listener;

        public static void Start(IPAddress address, int port)
        {
            listener = new InternalListener(address, port);
            listener.Accepted += link =>
            {
                link.Received += (l, message) => OnReceived(l, message);
                link.Closed += (l, error) => log.Info($"Gateway link from {l.Remote} closed{(error != null ? ": " + error.Message : ".")}");
                link.Start();
                log.Info($"Gateway connected from {link.Remote}.");
            };
            listener.Start();
            log.Info($"Login service internal port {listener.Port}.");
        }

        public static void Stop() => listener?.Dispose();

        private static void OnReceived(InternalLink link, IInternalMessage message)
        {
            if (message is not ServicePacket packet || LoginMessages.Parse(packet) is not ISocialMessage request)
            {
                log.Warn($"Login service: internal message {message.Opcode} not handled.");
                return;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    ISocialMessage reply = request switch
                    {
                        TicketConsume consume => await Consume(consume),
                        RelogSession relog => await Relog(relog),
                        _ => null
                    };
                    if (reply != null)
                        link.Send(reply.ToPacket());
                }
                catch (Exception e)
                {
                    log.Error(e, "Login service request failed.");
                }
            });
        }

        /// <summary>The ticket is consumed (single use); the character must be one of the account's.</summary>
        private static async Task<ISocialMessage> Consume(TicketConsume request)
        {
            AccountModel account = await DatabaseManager.Instance.AuthDatabase.ConsumeServerTicketAsync(request.Ticket, request.WorldId);
            CharacterModel character = null;
            if (account != null)
            {
                List<CharacterModel> characters = await DatabaseManager.Instance.CharacterDatabase.GetCharacters(account.Id);
                character = characters.SingleOrDefault(c => c.Id == request.CharacterGuid);
            }
            if (account == null || character == null)
            {
                log.Warn($"Ticket for character {request.CharacterGuid} refused ({(account == null ? "unknown or used ticket" : "not the account's character")}).");
                return new TicketConsumeReply { RequestId = request.RequestId, Ok = false, CharacterGuid = request.CharacterGuid };
            }
            return new TicketConsumeReply
            {
                RequestId = request.RequestId, Ok = true, AccountId = account.Id, AccountName = account.Username ?? "",
                CharacterGuid = character.Id, CharacterName = character.Name ?? "", ZoneId = character.ZoneId
            };
        }

        /// <summary>
        /// A session key, saved as the account's (as a logout does), for the client's relogin: the requester's key (a
        /// zone's relog tag in it lets the relogin keep the client's item cache) or a fresh one. The account is the
        /// request's, or the character's when the request names only the character (the world channel).
        /// </summary>
        private static async Task<ISocialMessage> Relog(RelogSession request)
        {
            ulong accountId = request.AccountId;
            if (accountId == 0 && request.CharacterGuid != 0)
                accountId = (await DatabaseManager.Instance.CharacterDatabase.GetCharacterById(request.CharacterGuid))?.AccountId ?? 0;
            AccountModel account = accountId != 0 ? await DatabaseManager.Instance.AuthDatabase.GetAccountByIdAsync(accountId) : null;
            if (account == null)
                return new RelogSessionReply { RequestId = request.RequestId };
            string key = !string.IsNullOrEmpty(request.Key) && request.Key.Length <= 64 ? request.Key
                : Convert.ToHexString(Shared.Network.Cryptography.RandomProvider.GetBytes(16u));
            await DatabaseManager.Instance.AuthDatabase.UpdateAccountSessionKey(account, key);
            log.Info($"Relog session for account {account.Id}.");
            return new RelogSessionReply { RequestId = request.RequestId, SessionKey = key, AccountId = account.Id };
        }
    }
}
