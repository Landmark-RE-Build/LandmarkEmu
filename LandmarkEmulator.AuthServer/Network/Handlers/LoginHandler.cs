using LandmarkEmulator.AuthServer.Network.Message;
using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.AuthServer.Network.Message.Model.TunnelData;
using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.AuthServer.Zone;
using LandmarkEmulator.AuthServer.Zone.Static;
using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Character;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Shared.Game.Entity.Static;
using LandmarkEmulator.Shared.Game.Events;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Network.Message;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.AuthServer.Network.Handlers
{
    public static class LoginHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        [AuthMessageHandler(AuthMessageOpcode.LoginRequest, ProtocolVersion.LoginUdp_9)]
        public static void HandleLoginRequest9(AuthSession session, LoginRequest request)
        {
            // prevent packets from being processed until asynchronous account select task is complete
            session.TogglePacketProcessing(false);

            session.EnqueueEvent(new TaskGenericEvent<AccountModel>(DatabaseManager.Instance.AuthDatabase.GetAccountBySessionKeyAsync(request.SessionId),
                account =>
            {
                bool loggedIn = false;
                if (account != null)
                    loggedIn = true;

                session.EnqueueMessage(new LoginReply_9
                {
                    LoggedIn = loggedIn,
                    Status = Convert.ToByte(loggedIn)
                });

                if (account == null)
                {
                    // Unknown session key: answered "not logged in", then closed (no half-open session).
                    session.OnDisconnect();
                    return;
                }
                session.Initialise(account);
                session.TogglePacketProcessing(true);
            }));
        }

        [AuthMessageHandler(AuthMessageOpcode.LoginRequest, ProtocolVersion.LoginUdp_10)]
        public static void HandleLoginRequest(AuthSession session, LoginRequest request)
        {
            // prevent packets from being processed until asynchronous account select task is complete
            session.TogglePacketProcessing(false);

            session.EnqueueEvent(new TaskGenericEvent<AccountModel>(DatabaseManager.Instance.AuthDatabase.GetAccountBySessionKeyAsync(request.SessionId),
                account =>
                {
                    bool loggedIn = false;
                    if (account != null)
                        loggedIn = true;

                    session.EnqueueMessage(new TunnelPacketServerToClient
                    {
                        ServerId = 0,
                        Type = TunnelDataType.ArtData,
                        Data = AuthAssetManager.Instance.ArtData
                    });

                    session.EnqueueMessage(new LoginReply
                    {
                        LoggedIn = loggedIn,
                        Status = Convert.ToByte(loggedIn)
                    });

                    if (account == null)
                    {
                        session.OnDisconnect();
                        return;
                    }
                    session.Initialise(account);
                    session.TogglePacketProcessing(true);
                }));
        }

        [AuthMessageHandler(AuthMessageOpcode.ServerListRequest)]
        public static void HandleServerListRequest(AuthSession session, ServerListRequest request)
        {
            var serverListReply = new ServerListReply();

            foreach (var zoneServer in ZoneServerManager.Instance.ZoneServers)
                serverListReply.Servers.Add(zoneServer.Build());

            session.EnqueueMessage(serverListReply);
        }

        /// <summary>The equipped items' container (WorldServer Game.Inventory.Inventory.EquippedContainer).</summary>
        private const uint EquippedContainer = 2;

        [AuthMessageHandler(AuthMessageOpcode.CharacterSelectInfoRequest, ProtocolVersion.LoginUdp_10)]
        public static void HandleCharacterSelectInfoRequest(AuthSession session, CharacterSelectInfoRequest request)
        {
            uint GetModelForGenderFromProfile(uint profileId, Gender gender)
            {
                var profile = GameTableManager.Instance.ProfileDefinitions.GetEntry(profileId);
                switch (gender)
                {
                    case Gender.Male:
                        return profile.MaleModelId;
                    case Gender.Female:
                        return profile.FemaleModelId;
                    default:
                        return 252u;
                }
            }

            session.EnqueueEvent(new TaskGenericEvent<List<CharacterModel>>(DatabaseManager.Instance.CharacterDatabase.GetCharacters(session.Account.Id),
                characters =>
                {
                    session.Characters.Clear();
                    session.Characters.AddRange(characters);

                    CharacterSelectInfoReply characterSelect = new CharacterSelectInfoReply
                    {
                        Status = 1
                    };

                    foreach (CharacterModel model in characters.Where(c => c.DeleteTime == null))
                    {
                        // Build base payload
                        var characterPayload = new CharacterSelectInfoReply.Character.CharacterPayload
                        {
                            Name = model.Name,
                            Race = model.Race,
                            Gender = model.Gender,
                            ProfileId = model.ProfileTypeId,
                            ModelId = GetModelForGenderFromProfile(model.ProfileTypeId, (Gender)model.Gender)
                        };
                        characterPayload.Model.SkinId = model.SkinTint;
                        characterPayload.Model.Race = model.Race;
                        characterPayload.Model.Gender = model.Gender;

                        // Add customisations
                        foreach (var customisation in model.Customisation)
                        {
                            characterPayload.Model.Customisations.Add(new(customisation.Slot, customisation.Option, customisation.Tint));
                        }

                        // What the character wears: its equipped items with a body model (clothing and armour;
                        // tools and weapons aren't shown here), each in its equipment slot. The client builds the body
                        // (undergarment, head, hair, eyes: slots 13-17) from the customisations and lays these on top,
                        // as the AttachmentProcessor log shows ("Slot=8" for a heavy chest). The tint travels with the
                        // attachment: there is no item here for the client to look it up from.
                        foreach (CharacterItemModel item in model.Items.Where(i => i.Container == EquippedContainer).OrderBy(i => i.Slot))
                            if (ItemAppearances.Get(item.ItemDefinitionId) is { } look && look.Model.StartsWith("Char_", StringComparison.Ordinal))
                                characterPayload.CharacterAttachments.Add(new CharacterSelectInfoReply.Character.CharacterPayload.CharacterAttachment
                                {
                                    ModelName = look.Model,
                                    SemanticTint = look.TintAlias,
                                    Slot = (AttachmentSlot)look.EquipSlot,
                                });

                        // Add character to payload
                        characterSelect.Characters.Add(new CharacterSelectInfoReply.Character
                        {
                            Status = 1,
                            CharacterId = model.Id,
                            LastLogin = DateTime.Now.Subtract(model.LastOnline ?? DateTime.Now).TotalDays * -1d,
                            LastServerId = model.LastServerId,
                            CharacterData = characterPayload
                        });
                    }

                    session.EnqueueMessage(characterSelect);
                }));
        }

        [AuthMessageHandler(AuthMessageOpcode.CharacterSelectInfoRequest, ProtocolVersion.LoginUdp_9)]
        public static void HandleCharacterSelectInfoRequest9(AuthSession session, CharacterSelectInfoRequest request)
        {
            uint GetModelId(Gender gender)
            {
                if (gender == Gender.Male)
                    return 23u;

                // Female
                return 21u;
            }

            session.EnqueueEvent(new TaskGenericEvent<List<CharacterModel>>(DatabaseManager.Instance.CharacterDatabase.GetCharacters(session.Account.Id),
                characters =>
            {
                session.Characters.Clear();
                session.Characters.AddRange(characters);

                CharacterSelectInfoReply9 characterSelect = new CharacterSelectInfoReply9
                {
                    Status = 1
                };

                foreach (CharacterModel model in characters.Where(c => c.DeleteTime == null))
                {
                    // Build base payload
                    var characterPayload = new CharacterSelectInfoReply9.Character.CharacterPayload
                    {
                        Name     = model.Name,
                        Gender   = (Gender)model.Gender,
                        HeadId   = 1,
                        ModelId  = GetModelId((Gender)model.Gender),
                        SkinTint = model.SkinTint
                    };

                    // Add customisations
                    foreach (var customisation in model.Customisation)
                        characterPayload.Customizations.Add(new(customisation.Slot, customisation.Option, customisation.Tint));

                    // TODO: Remove the random chest piece. This is just to keep character select slightly more interesting :P
                    List<string> randomName = new List<string>
                    {
                        "Colony_000",
                        "Trailblazer_000",
                        "Founder_000",
                        "Townsperson_000",
                        "Dapper_000",
                        "Adventurer_000",
                        "Victorian_000",
                        "Wanderer_000",
                        "Qeynos_000_Heavy",
                        "Colony_000_Heavy"
                    };

                    // Add Attachments; Should only ever be a chest item in character select.
                    characterPayload.CharacterAttachments.Add(new CharacterSelectInfoReply9.Character.CharacterPayload.CharacterAttachment
                    {
                        ModelName = $"Char_Biped_{(Race)model.Race}{(Gender)model.Gender}_Entities_{randomName[new Random().Next(randomName.Count)]}_Chest.adr",
                        Slot = AttachmentSlot.Chest,
                    });

                    // Add character to payload
                    characterSelect.Characters.Add(new CharacterSelectInfoReply9.Character
                    {
                        Status        = 1,
                        CharacterId   = model.Id,
                        LastLogin     = DateTime.Now.Subtract(model.LastOnline ?? DateTime.Now).TotalDays * -1d,
                        LastServerId  = model.LastServerId,
                        CharacterData = characterPayload
                    });
                }

                session.EnqueueMessage(characterSelect);
            }));
        }

        [AuthMessageHandler(AuthMessageOpcode.TunnelPacketClientToServer)]
        public static void HandleTunnelPacketClientToServer(AuthSession session, TunnelPacketClientToServer packet)
        {
            switch (packet.Data)
            {
                case NameValidationRequest nameValidationRequest:
                    // We can just respond with the NameValidationResult because nothing is created at this point.
                    session.EnqueueMessage(new TunnelPacketServerToClient
                    {
                        Type = TunnelDataType.NameValidationReply,
                        Data = new NameValidationReply
                        {
                            Result = AuthUtilities.ValidateName(nameValidationRequest.FirstName),
                            FirstName = nameValidationRequest.FirstName
                        }
                    });
                    break;
                case LoginInit loginInit:
                    session.EnqueueMessage(new TunnelPacketServerToClient
                    {
                        ServerId = 0,
                        Type = TunnelDataType.Unknown7,
                        Data = new Unknown7
                        {
                            String = "Welcome to Landmark RE:Build! We're happy to have you!"
                        }
                    });
                    session.EnqueueMessage(new TunnelPacketServerToClient
                    {
                        ServerId = 0,
                        Type = TunnelDataType.ClaimData,
                        Data = new ClaimData()
                    });
                    break;
                default:
                    log.Warn($"Unknown Tunnel Data");
                    break;
            }
        }

        /// <summary>
        /// Returns the zone server a new character should belong to. The 0.589 client sends 0 here, and
        /// a character whose last server isn't in the server list can't be played from character select.
        /// </summary>
        private static ulong GetServerIdForNewCharacter(ulong requestedServerId)
        {
            var servers = ZoneServerManager.Instance.ZoneServers;
            if (servers.Any(s => s.Id == requestedServerId))
                return requestedServerId;

            ZoneServer server = servers.FirstOrDefault(s => (s.Flags & ServerFlags.IsRecommended) != 0) ?? servers.FirstOrDefault();
            return server?.Id ?? requestedServerId;
        }

        [AuthMessageHandler(AuthMessageOpcode.CharacterCreateRequest, ProtocolVersion.LoginUdp_10)]
        public static void HandleCharacterCreateRequest(AuthSession session, CharacterCreateRequest request)
        {
            // TODO: Implement LoginUdp_10 Create
            CharacterCreateResult GetResult()
            {
                if (AuthUtilities.ValidateName(request.Name) != NameValidationResult.Success)
                    return CharacterCreateResult.UnableToCreate;

                return CharacterCreateResult.Success;
            }

            CharacterCreateResult result = GetResult();
            if (result != CharacterCreateResult.Success)
            {
                session.EnqueueMessage(new CharacterCreateReply
                {
                    Result = result
                });
                return;
            }

            // Create CharacterModel for the DB
            CharacterModel character = new CharacterModel
            {
                Id = AuthAssetManager.Instance.NextCharacterId,
                AccountId = session.Account.Id,
                Name = request.Name,
                Gender = (byte)request.Gender,
                Race = (byte)request.Race,
                ProfileTypeId = request.ProfileTypeId,
                SkinTint = request.SkinTint,
                LastServerId = GetServerIdForNewCharacter(request.ServerId)
            };

            // Add customisations
            foreach (var customisation in request.CustomisationOptions)
                character.Customisation.Add(new CharacterCustomisationModel
                {
                    Slot = customisation.Item1,
                    Option = customisation.Item2,
                    Tint = customisation.Item3
                });

            // TODO: Add Outfit

            // Save new Character to DB (asynchronously) and inform the client that it's created and ready to use
            session.EnqueueEvent(new TaskEvent(DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                c.Character.Add(character);
            }),
                    () =>
                    {
                        session.Characters.Add(character);
                        session.EnqueueMessage(new CharacterCreateReply
                        {
                            Result = CharacterCreateResult.Success,
                            CharacterId = character.Id
                        });
                    }));
        }

        [AuthMessageHandler(AuthMessageOpcode.CharacterCreateRequest, ProtocolVersion.LoginUdp_9)]
        public static void HandleCharacterCreateRequest9(AuthSession session, CharacterCreateRequest9 request)
        {
            CharacterCreateResult GetResult()
            {
                if (AuthUtilities.ValidateName(request.Name) != NameValidationResult.Success)
                    return CharacterCreateResult.UnableToCreate;

                return CharacterCreateResult.Success;
            }

            CharacterCreateResult result = GetResult();
            if (result != CharacterCreateResult.Success)
            {
                session.EnqueueMessage(new CharacterCreateReply
                {
                    Result = result
                });
                return;
            }

            // Create CharacterModel for the DB
            CharacterModel character = new CharacterModel
            {
                Id            = AuthAssetManager.Instance.NextCharacterId,
                AccountId     = session.Account.Id,
                Name          = request.Name,
                Gender        = (byte)request.Gender,
                Race          = (byte)Race.Human,
                ProfileTypeId = request.ProfileTypeId,
                SkinTint      = request.SkinTint
            };

            // Add customisations
            foreach (var customisation in request.CustomisationOptions)
                character.Customisation.Add(new CharacterCustomisationModel
                {
                    Slot = customisation.Item1,
                    Option = customisation.Item2,
                    Tint = customisation.Item3
                });

            // Save new Character to DB (asynchronously) and inform the client that it's created and ready to use
            session.EnqueueEvent(new TaskEvent(DatabaseManager.Instance.CharacterDatabase.Save(c =>
                {
                    c.Character.Add(character);
                }),
                    () =>
                {
                    session.Characters.Add(character);
                    session.EnqueueMessage(new CharacterCreateReply
                    {
                        Result = CharacterCreateResult.Success,
                        CharacterId = character.Id
                    });
                }));
        }

        [AuthMessageHandler(AuthMessageOpcode.CharacterDeleteRequest)]
        public static void HandleCharacterDeleteRequest(AuthSession session, CharacterDeleteRequest request)
        {
            CharacterModel characterToDelete = session.Characters.FirstOrDefault(c => c.Id == request.CharacterId);
            if (characterToDelete == null)
            {
                session.EnqueueMessage(new CharacterDeleteReply
                {
                    CharacterId = request.CharacterId,
                    Status = 0
                });
                return;
            }

            session.TogglePacketProcessing(false);

            void Save(CharacterContext context)
            {
                var model = new CharacterModel
                {
                    Id = characterToDelete.Id
                };

                EntityEntry<CharacterModel> entity = context.Attach(model);

                model.DeleteTime = DateTime.UtcNow;
                entity.Property(e => e.DeleteTime).IsModified = true;

                model.OriginalName = characterToDelete.Name;
                entity.Property(e => e.OriginalName).IsModified = true;

                model.Name = null;
                entity.Property(e => e.Name).IsModified = true;
            }

            session.EnqueueEvent(new TaskEvent(DatabaseManager.Instance.CharacterDatabase.Save(Save),
                () =>
            {
                session.TogglePacketProcessing(true);

                // Send CharacterSelectInfoReply, first, with no characters, otherwise it bugs up.
                session.EnqueueMessage(new CharacterSelectInfoReply
                {
                    Status = 1
                });
                session.EnqueueMessage(new CharacterDeleteReply
                {
                    CharacterId = request.CharacterId,
                    Status = 1
                });
            }));
        }

        [AuthMessageHandler(AuthMessageOpcode.CharacterLoginRequest)]
        public static void HandleCharacterLoginRequest(AuthSession session, CharacterLoginRequest request)
        {
            ZoneServer server = ZoneServerManager.Instance.ZoneServers.SingleOrDefault(x => x.Id == request.ServerId);
            CharacterModel character = session.Characters.SingleOrDefault(x => x.Id == request.CharacterId);
            if (server == null || character == null)
            {
                Refuse(session, request, server == null ? CharacterLoginResult.ServerNotFound : CharacterLoginResult.NotAllowed);
                return;
            }

            // The world must be running right now: its row is read fresh at Play (not the list's 3-second view), and
            // a world clears its heartbeat the moment a shutdown starts (WorldServer Game.Shutdown / ServerHeartbeat).
            // Otherwise result 3, which character select shows as "Game Server not found." and stays put - the player
            // never connects to a world that is closing (a client that connects and is dropped quits).
            session.EnqueueEvent(new TaskGenericEvent<ZoneServerModel>(DatabaseManager.Instance.AuthDatabase.GetZoneServerAsync(server.Id),
                row =>
            {
                if (!ZoneServerManager.IsRunning(row))
                {
                    log.Info($"Account {session.Account.Id}: Play refused, world {server.Id} is down or shutting down.");
                    Refuse(session, request, CharacterLoginResult.ServerNotFound);
                    return;
                }

                string serverTicket = RandomProvider.GetBytes(16u).ToHexString();
                session.EnqueueEvent(new TaskEvent(DatabaseManager.Instance.AuthDatabase.UpdateServerTicket(session.Account, serverTicket),
                    () =>
                {
                    CharacterLoginReply.ServerInfo serverInfo = new CharacterLoginReply.ServerInfo
                    {
                        ServerAddress = $"{server.Host}:{server.Port}",
                        AccountName   = session.Account.Username,
                        EncryptionKey = Convert.FromBase64String(session.EncryptionKey),
                        CharacterId   = character.Id,
                        CharacterName = character.Name,
                        // The client logs in to the world server with this as the character id (LoginRequest.CharacterId).
                        Guid          = character.Id,
                        ServerTicket  = serverTicket
                    };
                    session.EnqueueMessage(new CharacterLoginReply
                    {
                        CharacterId = request.CharacterId,
                        ServerId    = request.ServerId,
                        Result      = CharacterLoginResult.Success,
                        Server      = serverInfo
                    });
                }));
            }));
        }

        private static void Refuse(AuthSession session, CharacterLoginRequest request, CharacterLoginResult result) =>
            session.EnqueueMessage(new CharacterLoginReply
            {
                CharacterId = request.CharacterId,
                ServerId    = request.ServerId,
                Result      = result
            });
    }
}
