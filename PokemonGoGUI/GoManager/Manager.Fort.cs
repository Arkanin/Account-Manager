﻿using Google.Protobuf;
using POGOProtos.Map.Fort;
using POGOProtos.Networking.Requests;
using POGOProtos.Networking.Requests.Messages;
using POGOProtos.Networking.Responses;
using PokemonGoGUI.Extensions;
using PokemonGoGUI.GoManager.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PokemonGoGUI.GoManager
{
    public partial class Manager
    {
        private async Task<MethodResult> SearchPokestop(FortData pokestop)
        {
            if (pokestop == null)
                return new MethodResult();

            FortSearchResponse fortResponse = null;
            const int maxFortAttempts = 5;

            string fort = pokestop.Type == FortType.Checkpoint ? "Fort" : "Gym";

            for (int i = 0; i < maxFortAttempts; i++)
            {
                var response = await _client.ClientSession.RpcClient.SendRemoteProcedureCallAsync(new Request
                {
                    RequestType = RequestType.FortSearch,
                    RequestMessage = new FortSearchMessage
                    {
                        FortId = pokestop.Id,
                        FortLatitude = pokestop.Latitude,
                        FortLongitude = pokestop.Longitude,
                        PlayerLatitude = _client.ClientSession.Player.Latitude,
                        PlayerLongitude = _client.ClientSession.Player.Longitude
                    }.ToByteString()
                });

                if (response == null)
                    return new MethodResult();

                fortResponse = FortSearchResponse.Parser.ParseFrom(response);

                switch (fortResponse.Result)
                {
                    case FortSearchResponse.Types.Result.ExceededDailyLimit:
                        LogCaller(new LoggerEventArgs(String.Format("Failed to search {0}. Response: {1}", fort, fortResponse.Result), LoggerTypes.Warning));
                        return new MethodResult
                        {
                            Message = "Failed to search fort"
                        };
                    case FortSearchResponse.Types.Result.InCooldownPeriod:
                        LogCaller(new LoggerEventArgs(String.Format("Failed to search {0}. Response: {1}", fort, fortResponse.Result), LoggerTypes.Warning));
                        return new MethodResult
                        {
                            Message = "Failed to search fort"
                        };
                    case FortSearchResponse.Types.Result.InventoryFull:
                        LogCaller(new LoggerEventArgs(String.Format("Failed to search {0}. Response: {1}", fort, fortResponse.Result), LoggerTypes.Warning));
                        return new MethodResult
                        {
                            Message = "Failed to search fort"
                        };
                    case FortSearchResponse.Types.Result.NoResultSet:
                        LogCaller(new LoggerEventArgs(String.Format("Failed to search {0}. Response: {1}", fort, fortResponse.Result), LoggerTypes.Warning));
                        return new MethodResult
                        {
                            Message = "Failed to search fort"
                        };
                    case FortSearchResponse.Types.Result.OutOfRange:
                        if (_potentialPokeStopBan)
                        {
                            if (AccountState != Enums.AccountState.SoftBan)
                            {
                                LogCaller(new LoggerEventArgs("Pokestop ban detected. Marking state", LoggerTypes.Warning));
                            }

                            AccountState = Enums.AccountState.SoftBan;

                            if (fortResponse.ExperienceAwarded != 0)
                            {
                                if (!_potentialPokemonBan && _fleeingPokemonResponses >= _fleeingPokemonUntilBan)
                                {
                                    LogCaller(new LoggerEventArgs("Potential pokemon ban detected. Setting flee count to 0 avoid false positives", LoggerTypes.Warning));

                                    _potentialPokemonBan = true;
                                    _fleeingPokemonResponses = 0;
                                }
                                else if (_fleeingPokemonResponses >= _fleeingPokemonUntilBan)
                                {
                                    //Already pokestop banned
                                    if (AccountState == Enums.AccountState.SoftBan)
                                    {
                                        _potentialPokemonBan = false;
                                        _potentialPokemonBan = false;
                                    }

                                    if (AccountState != Enums.AccountState.SoftBan)
                                    {
                                        //Only occurs when out of range is found
                                        if (fortResponse.ExperienceAwarded == 0)
                                        {
                                            LogCaller(new LoggerEventArgs("Pokemon fleeing and failing to grab stops. Potential pokemon & pokestop ban.", LoggerTypes.Warning));
                                        }
                                        else
                                        {
                                            LogCaller(new LoggerEventArgs("Pokemon fleeing, yet grabbing stops. Potential pokemon ban.", LoggerTypes.Warning));
                                        }
                                    }

                                    if (UserSettings.StopAtMinAccountState == Enums.AccountState.SoftBan)
                                    {
                                        LogCaller(new LoggerEventArgs("Auto stopping bot ...", LoggerTypes.Info));

                                        Stop();
                                    }

                                    return new MethodResult
                                    {
                                        Message = "Bans detected",
                                    };
                                }
                            }
                        }
                        else //This error should never happen normally, so assume temp ban
                        {
                            _potentialPokeStopBan = true;
                            _proxyIssue = true;
                            //Display error only on first notice
                            LogCaller(new LoggerEventArgs("Pokestop out of range. Potential temp pokestop ban or IP ban", LoggerTypes.Warning));
                        }

                        //Let it continue down
                        continue;
                    case FortSearchResponse.Types.Result.PoiInaccessible:
                        LogCaller(new LoggerEventArgs(String.Format("Failed to search {0}. Response: {1}", fort, fortResponse.Result), LoggerTypes.Warning));
                        return new MethodResult
                        {
                            Message = "Failed to search fort"
                        };
                    case FortSearchResponse.Types.Result.Success:
                        string message = String.Format("Searched {0}. Exp: {1}. Items: {2}.", // Badge: {3}. BonusLoot: {4}. Gems: {5}. Loot: {6}, Eggs: {7:0.0}. RaidTickets: {8}. TeamBonusLoot: {9}",
                            fort,
                            fortResponse.ExperienceAwarded,
                            StringUtil.GetSummedFriendlyNameOfItemAwardList(fortResponse.ItemsAwarded.ToList())
                            /*,
                            fortResponse.AwardedGymBadge.ToString(),
                            fortResponse.BonusLoot.LootItem.ToString(),
                            fortResponse.GemsAwarded.ToString(),
                            fortResponse.Loot.LootItem.ToString(),
                            fortResponse.PokemonDataEgg.EggKmWalkedStart,
                            fortResponse.RaidTickets.ToString(),
                            fortResponse.TeamBonusLoot.LootItem.ToString()*/);

                        //Successfully grabbed stop
                        if (AccountState == Enums.AccountState.SoftBan)// || AccountState == Enums.AccountState.HashIssues)
                        {
                            AccountState = Enums.AccountState.Good;

                            LogCaller(new LoggerEventArgs("Soft ban was removed", LoggerTypes.Info));
                        }

                        ExpIncrease(fortResponse.ExperienceAwarded);
                        TotalPokeStopExp += fortResponse.ExperienceAwarded;

                        Tracker.AddValues(0, 1);

                        if (fortResponse.ExperienceAwarded == 0)
                        {
                            //Softban on the fleeing pokemon. Reset.
                            _fleeingPokemonResponses = 0;
                            _potentialPokemonBan = false;

                            ++_totalZeroExpStops;
                            message += String.Format(" No exp gained. Attempt {0} of {1}", i + 1, maxFortAttempts);
                            continue;
                        }

                        LogCaller(new LoggerEventArgs(message, LoggerTypes.Success));

                        await Task.Delay(CalculateDelay(UserSettings.DelayBetweenPlayerActions, UserSettings.PlayerActionDelayRandom));

                        return new MethodResult
                        {
                            Success = true,
                            Message = "Success"
                        };
                }
            }

            return new MethodResult
            {
                Success = true,
                Message = "Success"
            };
        }

        private async Task<MethodResult<FortDetailsResponse>> FortDetails(FortData pokestop)
        {
            var response = await _client.ClientSession.RpcClient.SendRemoteProcedureCallAsync(new Request
            {
                RequestType = RequestType.FortDetails,
                RequestMessage = new FortDetailsMessage
                {
                    FortId = pokestop.Id,
                    Latitude = pokestop.Latitude,
                    Longitude = pokestop.Longitude,
                }.ToByteString()
            });

            if (response == null)
                return new MethodResult<FortDetailsResponse>();

            var fortDetailsResponse = FortDetailsResponse.Parser.ParseFrom(response);

            if (fortDetailsResponse != null)
                return new MethodResult<FortDetailsResponse>
                {
                    Data = fortDetailsResponse,
                    Success = true
                };
            else
                return new MethodResult<FortDetailsResponse>();
        }

        private async Task<MethodResult<GymGetInfoResponse>> GymGetInfo(FortData pokestop)
        {
            var response = await _client.ClientSession.RpcClient.SendRemoteProcedureCallAsync(new Request
            {
                RequestType = RequestType.GymGetInfo,
                RequestMessage = new GymGetInfoMessage
                {
                    GymId = pokestop.Id,
                    GymLatDegrees = pokestop.Latitude,
                    GymLngDegrees = pokestop.Longitude,
                    PlayerLatDegrees = _client.ClientSession.Player.Latitude,
                    PlayerLngDegrees = _client.ClientSession.Player.Longitude
                }.ToByteString()
            });

            if (response == null)
                return new MethodResult<GymGetInfoResponse>();

            var gymGetInfoResponse = GymGetInfoResponse.Parser.ParseFrom(response);

            switch (gymGetInfoResponse.Result)
            {
                case GymGetInfoResponse.Types.Result.ErrorGymDisabled:
                    return new MethodResult<GymGetInfoResponse>();
                case GymGetInfoResponse.Types.Result.ErrorNotInRange:
                    return new MethodResult<GymGetInfoResponse>();
                case GymGetInfoResponse.Types.Result.Success:
                    return new MethodResult<GymGetInfoResponse>
                    {
                        Data = gymGetInfoResponse,
                        Message = "Succes",
                        Success = true
                    };
                case GymGetInfoResponse.Types.Result.Unset:
                    return new MethodResult<GymGetInfoResponse>();
            }
            return new MethodResult<GymGetInfoResponse>();
        }
    }
}
