using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MetalicRustStreamerRewards", "MetalicRust", "2.3.0")]
    [Description("Реферальная система стримеров, автоматический учет времени, выплаты, TOP-1 и автоматический префикс стримера.")]
    public class MetalicRustStreamerRewards : RustPlugin
    {
        #region Plugins

        [PluginReference]
        private Plugin ServerRewards;

        [PluginReference]
        private Plugin Economics;

        [PluginReference]
        private Plugin BetterChat;

        #endregion

        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Награда игроку за активацию кода")]
            public PlayerRewardSettings PlayerReward = new PlayerRewardSettings();

            [JsonProperty("Почасовая награда стримеру")]
            public StreamerHourlyRewardSettings StreamerHourlyReward = new StreamerHourlyRewardSettings();

            [JsonProperty("Бонусы за количество приглашенных игроков")]
            public ReferralBonusSettings ReferralBonuses = new ReferralBonusSettings();

            [JsonProperty("Бонусы за онлайн во время стрима")]
            public OnlineBonusSettings OnlineBonuses = new OnlineBonusSettings();

            [JsonProperty("Награда TOP-1 стримеру")]
            public TopStreamerSettings TopStreamer = new TopStreamerSettings();

            [JsonProperty("Настройки активации")]
            public ActivationSettings Activation = new ActivationSettings();

            [JsonProperty("Префикс стримера")]
            public StreamerPrefixSettings StreamerPrefix = new StreamerPrefixSettings();

            [JsonProperty("Глобальный промокод")]
            public GlobalPromoSettings GlobalPromo = new GlobalPromoSettings();

            [JsonProperty("Сообщения")]
            public MessageSettings Messages = new MessageSettings();

            [JsonProperty("Настройки вайпа")]
            public WipeSettings Wipe = new WipeSettings();
        }

        private class PlayerRewardSettings
        {
            [JsonProperty("Economics")]
            public double Economics = 10000;

            [JsonProperty("RP")]
            public int RP = 1000;

            [JsonProperty("Минимальное время на сервере для активации, минут")]
            public int MinimumPlaytimeMinutes = 10;
        }

        private class StreamerHourlyRewardSettings
        {
            [JsonProperty("RP за полный час")]
            public int RPPerHour = 500;

            [JsonProperty("Economics за полный час")]
            public double EconomicsPerHour = 5000;
        }

        private class ReferralBonusSettings
        {
            [JsonProperty("5 игроков")]
            public int Five = 2500;

            [JsonProperty("10 игроков")]
            public int Ten = 5000;

            [JsonProperty("20 игроков")]
            public int Twenty = 15000;

            [JsonProperty("30 игроков")]
            public int Thirty = 25000;

            [JsonProperty("50 игроков")]
            public int Fifty = 50000;
        }

        private class OnlineBonusSettings
        {
            [JsonProperty("10 игроков онлайн")]
            public int Ten = 2500;

            [JsonProperty("20 игроков онлайн")]
            public int Twenty = 5000;

            [JsonProperty("30 игроков онлайн")]
            public int Thirty = 10000;

            [JsonProperty("40 игроков онлайн")]
            public int Forty = 20000;
        }

        private class TopStreamerSettings
        {
            [JsonProperty("RP")]
            public int RP = 50000;

            [JsonProperty("Economics")]
            public double Economics = 100000;

            [JsonProperty("Выдавать permission")]
            public bool GivePermission = true;

            [JsonProperty("Permission")]
            public string Permission = "vip.use";

            [JsonProperty("Снимать permission с прошлого TOP-1")]
            public bool RevokePreviousPermission = true;
        }

        private class ActivationSettings
        {
            [JsonProperty("Запрещать стримеру активировать собственный код")]
            public bool BlockStreamerSelfActivation = true;

            [JsonProperty("Один код на SteamID за вайп")]
            public bool OneCodePerWipe = true;
        }

        private class StreamerPrefixSettings
        {
            [JsonProperty("Включить автоматический префикс")]
            public bool Enabled = true;

            [JsonProperty("Группа Better Chat")]
            public string GroupName = "streamer";

            [JsonProperty("Префикс")]
            public string Prefix = "〔🎥〕";

            [JsonProperty("Цвет префикса")]
            public string TitleColor = "#FFD700";

            [JsonProperty("Цвет ника")]
            public string NameColor = "#FFFFFF";

            [JsonProperty("Размер префикса")]
            public int TitleSize = 15;

            [JsonProperty("Размер ника")]
            public int NameSize = 15;

            [JsonProperty("Приоритет группы")]
            public int Priority = 2;
        }

        private class GlobalPromoSettings
        {
            [JsonProperty("Включен")]
            public bool Enabled = true;

            [JsonProperty("Код")]
            public string Code = "STT";

            [JsonProperty("Economics")]
            public double Economics = 3000;

            [JsonProperty("RP")]
            public int RP = 100;

            [JsonProperty("Один раз за вайп")]
            public bool OneUsePerWipe = true;
        }

        private class MessageSettings
        {
            [JsonProperty("Показывать сообщение о награде игроку")]
            public bool ShowRewardMessage = true;

            [JsonProperty("Показывать имя стримера")]
            public bool ShowStreamerName = true;

            [JsonProperty("Сообщения стримеру")]
            public bool ShowStreamerMessages = true;

            [JsonProperty("Сообщения о выплатах")]
            public bool ShowPaymentMessages = true;

            [JsonProperty("Сообщения о вайпе")]
            public bool ShowWipeMessages = true;
        }

        private class WipeSettings
        {
            [JsonProperty("Дата первого вайпа")]
            public string AnchorDate = "2026-09-17";

            [JsonProperty("Время вайпа")]
            public string Time = "17:00";

            [JsonProperty("День недели")]
            public string DayOfWeek = "Thursday";

            [JsonProperty("Цикл вайпа, дней")]
            public int CycleDays = 14;

            [JsonProperty("UTC offset")]
            public int UtcOffsetHours = 3;

            [JsonProperty("Проверка вайпа, секунд")]
            public int CheckIntervalSeconds = 30;

            [JsonProperty("Обновление выплат, секунд")]
            public int UpdateIntervalSeconds = 60;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();

                if (config == null)
                    throw new Exception("Config returned null.");
            }
            catch
            {
                PrintError("Ошибка чтения конфигурации. Создаю новую конфигурацию.");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        private StoredData data;

        private class StoredData
        {
            [JsonProperty("Стримеры")]
            public Dictionary<string, StreamerData> Streamers =
                new Dictionary<string, StreamerData>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Рефералы игроков")]
            public Dictionary<ulong, PlayerReferralData> PlayerReferrals =
                new Dictionary<ulong, PlayerReferralData>();

            [JsonProperty("Глобальные промокоды игроков")]
            public Dictionary<ulong, GlobalPromoActivationData> GlobalPromoActivations =
                new Dictionary<ulong, GlobalPromoActivationData>();

            [JsonProperty("Последний обработанный вайп UTC")]
            public string LastProcessedWipeUtc;

            [JsonProperty("Текущий ID вайпа")]
            public string CurrentWipeId;

            [JsonProperty("Следующий вайп UTC")]
            public string NextWipeUtc;

            [JsonProperty("Предыдущий TOP-1 SteamID")]
            public ulong PreviousTopStreamerSteamID;
        }

        private class StreamerData
        {
            [JsonProperty("Код")]
            public string Code;

            [JsonProperty("SteamID")]
            public ulong SteamID;

            [JsonProperty("Имя")]
            public string Name;

            [JsonProperty("Игроки")]
            public HashSet<ulong> Players = new HashSet<ulong>();

            [JsonProperty("Количество рефералов")]
            public int Referrals;

            [JsonProperty("Активации")]
            public int Activations;

            [JsonProperty("Минут сыграно")]
            public double PlayedMinutes;

            [JsonProperty("Оплачено часов")]
            public int PaidHours;

            [JsonProperty("Выплачено RP")]
            public int PaidRP;

            [JsonProperty("Выплачено Economics")]
            public double PaidEconomics;

            [JsonProperty("Бонус 5")]
            public bool ReferralBonus5;

            [JsonProperty("Бонус 10")]
            public bool ReferralBonus10;

            [JsonProperty("Бонус 20")]
            public bool ReferralBonus20;

            [JsonProperty("Бонус 30")]
            public bool ReferralBonus30;

            [JsonProperty("Бонус 50")]
            public bool ReferralBonus50;

            [JsonProperty("Онлайн бонус 10")]
            public bool OnlineBonus10;

            [JsonProperty("Онлайн бонус 20")]
            public bool OnlineBonus20;

            [JsonProperty("Онлайн бонус 30")]
            public bool OnlineBonus30;

            [JsonProperty("Онлайн бонус 40")]
            public bool OnlineBonus40;

            [JsonProperty("TOP-1 награда получена")]
            public bool TopStreamerRewardPaid;
        }

        private class PlayerReferralData
        {
            [JsonProperty("SteamID")]
            public ulong SteamID;

            [JsonProperty("Код стримера")]
            public string StreamerCode;

            [JsonProperty("SteamID стримера")]
            public ulong StreamerSteamID;

            [JsonProperty("Имя стримера")]
            public string StreamerName;

            [JsonProperty("Время активации UTC")]
            public string ActivatedUtc;

            [JsonProperty("ID вайпа")]
            public string WipeId;
        }

        private class GlobalPromoActivationData
        {
            [JsonProperty("SteamID")]
            public ulong SteamID;

            [JsonProperty("Код")]
            public string Code;

            [JsonProperty("Время UTC")]
            public string ActivatedUtc;

            [JsonProperty("ID вайпа")]
            public string WipeId;
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(
                    "MetalicRustStreamerRewards"
                );
            }
            catch
            {
                data = new StoredData();
            }

            if (data == null)
                data = new StoredData();

            if (data.Streamers == null)
                data.Streamers = new Dictionary<string, StreamerData>(StringComparer.OrdinalIgnoreCase);

            if (data.PlayerReferrals == null)
                data.PlayerReferrals = new Dictionary<ulong, PlayerReferralData>();

            if (data.GlobalPromoActivations == null)
                data.GlobalPromoActivations = new Dictionary<ulong, GlobalPromoActivationData>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(
                "MetalicRustStreamerRewards",
                data
            );
        }

        #endregion

        #region Runtime

        private Dictionary<ulong, DateTime> streamerJoinTimes =
            new Dictionary<ulong, DateTime>();

        private Timer wipeTimer;
        private Timer updateTimer;

        #endregion

        #region Initialization

        private void Init()
        {
            permission.RegisterPermission(
                "metalicrust.streamer.admin",
                this
            );

            LoadData();
            InitializeWipeSchedule();
        }

        private void OnServerInitialized()
        {
            SetupBetterChatGroup();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                StreamerData streamer = FindStreamerBySteamID(player.userID);

                if (streamer != null)
                {
                    streamerJoinTimes[player.userID] = DateTime.UtcNow;
                    AddStreamerPrefix(player);
                }
            }

            StartTimers();

            SaveData();
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (streamerJoinTimes.ContainsKey(player.userID))
                    ProcessStreamerTime(player.userID);
            }

            if (wipeTimer != null)
                wipeTimer.Destroy();

            if (updateTimer != null)
                updateTimer.Destroy();

            SaveData();
        }

        #endregion

        #region Timers

        private void StartTimers()
        {
            if (wipeTimer != null)
                wipeTimer.Destroy();

            if (updateTimer != null)
                updateTimer.Destroy();

            wipeTimer = timer.Every(
                Mathf.Max(10, config.Wipe.CheckIntervalSeconds),
                CheckWipe
            );

            updateTimer = timer.Every(
                Mathf.Max(30, config.Wipe.UpdateIntervalSeconds),
                UpdateStreamerPayments
            );
        }

        private void UpdateStreamerPayments()
        {
            CheckWipe();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                StreamerData streamer = FindStreamerBySteamID(player.userID);

                if (streamer == null)
                    continue;

                if (!streamerJoinTimes.ContainsKey(player.userID))
                    streamerJoinTimes[player.userID] = DateTime.UtcNow;

                ProcessStreamerTime(player.userID);

                streamerJoinTimes[player.userID] = DateTime.UtcNow;
            }

            SaveData();
        }

        #endregion

        #region Player Hooks

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null)
                return;

            StreamerData streamer = FindStreamerBySteamID(player.userID);

            if (streamer == null)
                return;

            streamerJoinTimes[player.userID] = DateTime.UtcNow;

            AddStreamerPrefix(player);

            if (config.Messages.ShowStreamerMessages)
            {
                SendReply(
                    player,
                    $"<color=#FFD700>🎥 Ты вошёл как зарегистрированный стример MetalicRust.</color>\n" +
                    $"Код: <color=#00FFFF>{streamer.Code}</color>"
                );
            }

            SaveData();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            if (!streamerJoinTimes.ContainsKey(player.userID))
                return;

            ProcessStreamerTime(player.userID);

            streamerJoinTimes.Remove(player.userID);

            SaveData();
        }

        #endregion

        #region Streamer Time

        private void ProcessStreamerTime(ulong steamId)
        {
            if (!streamerJoinTimes.TryGetValue(steamId, out DateTime joinTime))
                return;

            StreamerData streamer = FindStreamerBySteamID(steamId);

            if (streamer == null)
                return;

            TimeSpan elapsed = DateTime.UtcNow - joinTime;

            if (elapsed.TotalSeconds <= 0)
                return;

            streamer.PlayedMinutes += elapsed.TotalMinutes;

            int availableHours =
                Mathf.FloorToInt((float)(streamer.PlayedMinutes / 60.0));

            int hoursToPay =
                availableHours - streamer.PaidHours;

            if (hoursToPay > 0)
            {
                PayStreamerHours(streamer, hoursToPay);
            }

            streamerJoinTimes[steamId] = DateTime.UtcNow;
        }

        private void PayStreamerHours(StreamerData streamer, int hours)
        {
            if (streamer == null || hours <= 0)
                return;

            int rp = config.StreamerHourlyReward.RPPerHour * hours;
            double economics =
                config.StreamerHourlyReward.EconomicsPerHour * hours;

            bool rpPaid = GiveRP(streamer.SteamID, rp);
            bool economicsPaid = GiveEconomics(streamer.SteamID, economics);

            if (rpPaid)
                streamer.PaidRP += rp;

            if (economicsPaid)
                streamer.PaidEconomics += economics;

            if (rpPaid && economicsPaid)
            {
                streamer.PaidHours += hours;

                BasePlayer player = BasePlayer.FindByID(streamer.SteamID);

                if (player != null && config.Messages.ShowPaymentMessages)
                {
                    SendReply(
                        player,
                        $"<color=#00FF00>🎥 Выплата за стрим:</color> " +
                        $"<color=#FFD700>{hours} ч.</color>\n" +
                        $"RP: <color=#00FFFF>+{rp}</color>\n" +
                        $"Economics: <color=#00FF00>+{FormatMoney(economics)}</color>"
                    );
                }
            }
        }

        #endregion

        #region Better Chat Prefix

        private void SetupBetterChatGroup()
        {
            if (!config.StreamerPrefix.Enabled)
                return;

            if (BetterChat == null)
            {
                PrintWarning(
                    "Better Chat не найден. Автоматический префикс стримера отключен до загрузки Better Chat."
                );

                return;
            }

            try
            {
                object result = BetterChat.Call(
                    "API_GroupExists",
                    config.StreamerPrefix.GroupName
                );

                bool exists = result is bool && (bool)result;

                if (!exists)
                {
                    BetterChat.Call(
                        "API_AddGroup",
                        config.StreamerPrefix.GroupName
                    );
                }

                BetterChat.Call(
                    "API_SetGroupField",
                    config.StreamerPrefix.GroupName,
                    "Title",
                    config.StreamerPrefix.Prefix
                );

                BetterChat.Call(
                    "API_SetGroupField",
                    config.StreamerPrefix.GroupName,
                    "TitleColor",
                    config.StreamerPrefix.TitleColor
                );

                BetterChat.Call(
                    "API_SetGroupField",
                    config.StreamerPrefix.GroupName,
                    "TitleSize",
                    config.StreamerPrefix.TitleSize.ToString()
                );

                BetterChat.Call(
                    "API_SetGroupField",
                    config.StreamerPrefix.GroupName,
                    "NameColor",
                    config.StreamerPrefix.NameColor
                );

                BetterChat.Call(
                    "API_SetGroupField",
                    config.StreamerPrefix.GroupName,
                    "NameSize",
                    config.StreamerPrefix.NameSize.ToString()
                );

                BetterChat.Call(
                    "API_SetGroupField",
                    config.StreamerPrefix.GroupName,
                    "Priority",
                    config.StreamerPrefix.Priority.ToString()
                );

                Puts(
                    $"Better Chat: группа '{config.StreamerPrefix.GroupName}' настроена. Префикс: {config.StreamerPrefix.Prefix}"
                );
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка настройки префикса Better Chat: {ex.Message}"
                );
            }
        }

        private void AddStreamerPrefix(BasePlayer player)
        {
            if (!config.StreamerPrefix.Enabled)
                return;

            if (player == null)
                return;

            if (BetterChat == null)
                return;

            try
            {
                if (!permission.GroupExists(config.StreamerPrefix.GroupName))
                {
                    permission.CreateGroup(
                        config.StreamerPrefix.GroupName,
                        config.StreamerPrefix.GroupName,
                        config.StreamerPrefix.Priority
                    );
                }

                if (!permission.UserHasGroup(
                    player.UserIDString,
                    config.StreamerPrefix.GroupName))
                {
                    permission.AddUserGroup(
                        player.UserIDString,
                        config.StreamerPrefix.GroupName
                    );
                }
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка выдачи префикса стримеру {player.userID}: {ex.Message}"
                );
            }
        }

        private void RemoveStreamerPrefix(ulong steamId)
        {
            if (!config.StreamerPrefix.Enabled)
                return;

            try
            {
                if (!permission.GroupExists(config.StreamerPrefix.GroupName))
                    return;

                permission.RemoveUserGroup(
                    steamId.ToString(),
                    config.StreamerPrefix.GroupName
                );
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка снятия префикса со SteamID {steamId}: {ex.Message}"
                );
            }
        }

        #endregion

        #region Chat Command /stream

        [ChatCommand("stream")]
        private void CmdStream(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (args == null || args.Length != 1)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Использование:</color> /stream КОД"
                );

                return;
            }

            string code = args[0].Trim();

            if (string.IsNullOrEmpty(code))
                return;

            if (config.GlobalPromo.Enabled &&
                code.Equals(
                    config.GlobalPromo.Code,
                    StringComparison.OrdinalIgnoreCase))
            {
                ActivateGlobalPromo(player);
                return;
            }

            StreamerData streamer = FindStreamerByCode(code);

            if (streamer == null)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Такого кода стримера не существует.</color>"
                );

                return;
            }

            if (config.Activation.BlockStreamerSelfActivation &&
                streamer.SteamID == player.userID)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Стример не может активировать собственный код.</color>"
                );

                return;
            }

            string currentWipeId = data.CurrentWipeId;

            if (config.Activation.OneCodePerWipe &&
                data.PlayerReferrals.TryGetValue(
                    player.userID,
                    out PlayerReferralData existingReferral))
            {
                if (existingReferral.WipeId == currentWipeId)
                {
                    SendReply(
                        player,
                        $"<color=#FF5555>Ты уже активировал код стримера за этот вайп:</color> " +
                        $"<color=#FFD700>{existingReferral.StreamerCode}</color>"
                    );

                    return;
                }
            }

            double currentPlaytimeMinutes = GetCurrentSessionMinutes(player.userID);

            if (currentPlaytimeMinutes <
                config.PlayerReward.MinimumPlaytimeMinutes)
            {
                double remaining =
                    config.PlayerReward.MinimumPlaytimeMinutes -
                    currentPlaytimeMinutes;

                SendReply(
                    player,
                    $"<color=#FF5555>Для активации кода нужно провести на сервере минимум " +
                    $"{config.PlayerReward.MinimumPlaytimeMinutes} минут.</color>\n" +
                    $"Осталось: <color=#FFD700>{Math.Ceiling(remaining)} мин.</color>"
                );

                return;
            }

            bool rpPaid = GiveRP(
                player.userID,
                config.PlayerReward.RP
            );

            bool economicsPaid = GiveEconomics(
                player.userID,
                config.PlayerReward.Economics
            );

            if (!rpPaid && !economicsPaid)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Не удалось выдать награду. Обратись к администрации.</color>"
                );

                return;
            }

            PlayerReferralData referral = new PlayerReferralData
            {
                SteamID = player.userID,
                StreamerCode = streamer.Code,
                StreamerSteamID = streamer.SteamID,
                StreamerName = streamer.Name,
                ActivatedUtc = DateTime.UtcNow.ToString("O"),
                WipeId = currentWipeId
            };

            data.PlayerReferrals[player.userID] = referral;

            streamer.Players.Add(player.userID);
            streamer.Referrals++;
            streamer.Activations++;

            ProcessReferralBonuses(streamer);

            SaveData();

            string rewardText =
                $"RP: <color=#00FFFF>+{config.PlayerReward.RP}</color>\n" +
                $"Economics: <color=#00FF00>+{FormatMoney(config.PlayerReward.Economics)}</color>";

            if (config.Messages.ShowRewardMessage)
            {
                SendReply(
                    player,
                    $"<color=#00FF00>✓ Код стримера активирован!</color>\n" +
                    $"Стример: <color=#FFD700>{streamer.Name}</color>\n" +
                    rewardText
                );
            }

            BasePlayer streamerPlayer =
                BasePlayer.FindByID(streamer.SteamID);

            if (streamerPlayer != null &&
                config.Messages.ShowStreamerMessages)
            {
                SendReply(
                    streamerPlayer,
                    $"<color=#00FF00>🎥 Новый реферал!</color>\n" +
                    $"Игрок: <color=#FFD700>{player.displayName}</color>\n" +
                    $"Всего рефералов: <color=#00FFFF>{streamer.Referrals}</color>"
                );
            }
        }

        #endregion

        #region Global Promo STT

        private void ActivateGlobalPromo(BasePlayer player)
        {
            if (!config.GlobalPromo.Enabled)
                return;

            string wipeId = data.CurrentWipeId;

            if (config.GlobalPromo.OneUsePerWipe &&
                data.GlobalPromoActivations.TryGetValue(
                    player.userID,
                    out GlobalPromoActivationData previous))
            {
                if (previous.WipeId == wipeId)
                {
                    SendReply(
                        player,
                        $"<color=#FF5555>Ты уже активировал промокод " +
                        $"<color=#FFD700>{config.GlobalPromo.Code}</color> за этот вайп.</color>"
                    );

                    return;
                }
            }

            bool rpPaid = GiveRP(
                player.userID,
                config.GlobalPromo.RP
            );

            bool economicsPaid = GiveEconomics(
                player.userID,
                config.GlobalPromo.Economics
            );

            if (!rpPaid && !economicsPaid)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Не удалось выдать награду.</color>"
                );

                return;
            }

            data.GlobalPromoActivations[player.userID] =
                new GlobalPromoActivationData
                {
                    SteamID = player.userID,
                    Code = config.GlobalPromo.Code,
                    ActivatedUtc = DateTime.UtcNow.ToString("O"),
                    WipeId = wipeId
                };

            SaveData();

            SendReply(
                player,
                $"<color=#00FF00>✓ Промокод {config.GlobalPromo.Code} активирован!</color>\n" +
                $"Economics: <color=#00FF00>+{FormatMoney(config.GlobalPromo.Economics)}</color>\n" +
                $"RP: <color=#00FFFF>+{config.GlobalPromo.RP}</color>"
            );
        }

        #endregion

        #region Referral Bonuses

        private void ProcessReferralBonuses(StreamerData streamer)
        {
            if (streamer == null)
                return;

            BasePlayer player =
                BasePlayer.FindByID(streamer.SteamID);

            if (streamer.Referrals >= 5 &&
                !streamer.ReferralBonus5)
            {
                streamer.ReferralBonus5 = true;
                GiveRP(streamer.SteamID, config.ReferralBonuses.Five);

                NotifyStreamer(
                    player,
                    $"🎯 Бонус за 5 рефералов: +{config.ReferralBonuses.Five} RP"
                );
            }

            if (streamer.Referrals >= 10 &&
                !streamer.ReferralBonus10)
            {
                streamer.ReferralBonus10 = true;
                GiveRP(streamer.SteamID, config.ReferralBonuses.Ten);

                NotifyStreamer(
                    player,
                    $"🎯 Бонус за 10 рефералов: +{config.ReferralBonuses.Ten} RP"
                );
            }

            if (streamer.Referrals >= 20 &&
                !streamer.ReferralBonus20)
            {
                streamer.ReferralBonus20 = true;
                GiveRP(streamer.SteamID, config.ReferralBonuses.Twenty);

                NotifyStreamer(
                    player,
                    $"🎯 Бонус за 20 рефералов: +{config.ReferralBonuses.Twenty} RP"
                );
            }

            if (streamer.Referrals >= 30 &&
                !streamer.ReferralBonus30)
            {
                streamer.ReferralBonus30 = true;
                GiveRP(streamer.SteamID, config.ReferralBonuses.Thirty);

                NotifyStreamer(
                    player,
                    $"🎯 Бонус за 30 рефералов: +{config.ReferralBonuses.Thirty} RP"
                );
            }

            if (streamer.Referrals >= 50 &&
                !streamer.ReferralBonus50)
            {
                streamer.ReferralBonus50 = true;
                GiveRP(streamer.SteamID, config.ReferralBonuses.Fifty);

                NotifyStreamer(
                    player,
                    $"🎯 Бонус за 50 рефералов: +{config.ReferralBonuses.Fifty} RP"
                );
            }
        }

        #endregion

        #region Online Bonuses

        private void ProcessOnlineBonuses(StreamerData streamer)
        {
            if (streamer == null)
                return;

            int online = BasePlayer.activePlayerList.Count;

            BasePlayer player =
                BasePlayer.FindByID(streamer.SteamID);

            if (online >= 10 && !streamer.OnlineBonus10)
            {
                streamer.OnlineBonus10 = true;

                GiveRP(
                    streamer.SteamID,
                    config.OnlineBonuses.Ten
                );

                NotifyStreamer(
                    player,
                    $"🔥 Онлайн {online}: +{config.OnlineBonuses.Ten} RP"
                );
            }

            if (online >= 20 && !streamer.OnlineBonus20)
            {
                streamer.OnlineBonus20 = true;

                GiveRP(
                    streamer.SteamID,
                    config.OnlineBonuses.Twenty
                );

                NotifyStreamer(
                    player,
                    $"🔥 Онлайн {online}: +{config.OnlineBonuses.Twenty} RP"
                );
            }

            if (online >= 30 && !streamer.OnlineBonus30)
            {
                streamer.OnlineBonus30 = true;

                GiveRP(
                    streamer.SteamID,
                    config.OnlineBonuses.Thirty
                );

                NotifyStreamer(
                    player,
                    $"🔥 Онлайн {online}: +{config.OnlineBonuses.Thirty} RP"
                );
            }

            if (online >= 40 && !streamer.OnlineBonus40)
            {
                streamer.OnlineBonus40 = true;

                GiveRP(
                    streamer.SteamID,
                    config.OnlineBonuses.Forty
                );

                NotifyStreamer(
                    player,
                    $"🔥 Онлайн {online}: +{config.OnlineBonuses.Forty} RP"
                );
            }
        }

        #endregion

        #region Admin Commands

        [ChatCommand("streamadmin")]
        private void CmdStreamAdmin(
            BasePlayer player,
            string command,
            string[] args)
        {
            if (!IsAdmin(player))
            {
                SendReply(
                    player,
                    "<color=#FF5555>Нет доступа.</color>"
                );

                return;
            }

            if (args == null || args.Length == 0)
            {
                ShowAdminHelp(player);
                return;
            }

            string sub = args[0].ToLower();

            switch (sub)
            {
                case "add":
                    AdminAddStreamer(player, args);
                    break;

                case "remove":
                    AdminRemoveStreamer(player, args);
                    break;

                case "list":
                    AdminListStreamers(player);
                    break;

                case "stats":
                    AdminStats(player, args);
                    break;

                case "players":
                    AdminPlayers(player, args);
                    break;

                case "pay":
                    AdminPay(player, args);
                    break;

                case "top":
                    AdminTop(player);
                    break;

                case "nextwipe":
                    AdminNextWipe(player);
                    break;

                case "reset":
                    AdminReset(player);
                    break;

                default:
                    ShowAdminHelp(player);
                    break;
            }
        }

        private void ShowAdminHelp(BasePlayer player)
        {
            SendReply(
                player,
                "<color=#FFD700>MetalicRust StreamerRewards</color>\n" +
                "/streamadmin add КОД STEAMID ИМЯ\n" +
                "/streamadmin remove КОД\n" +
                "/streamadmin list\n" +
                "/streamadmin stats КОД\n" +
                "/streamadmin players КОД\n" +
                "/streamadmin pay КОД ЧАСЫ\n" +
                "/streamadmin top\n" +
                "/streamadmin nextwipe\n" +
                "/streamadmin reset"
            );
        }

        private void AdminAddStreamer(
            BasePlayer player,
            string[] args)
        {
            if (args.Length < 4)
            {
                SendReply(
                    player,
                    "/streamadmin add КОД STEAMID ИМЯ"
                );

                return;
            }

            string code = args[1].Trim();

            if (!ulong.TryParse(args[2], out ulong steamId))
            {
                SendReply(
                    player,
                    "<color=#FF5555>Неверный SteamID.</color>"
                );

                return;
            }

            string name = string.Join(
                " ",
                args.Skip(3).ToArray()
            );

            if (data.Streamers.ContainsKey(code))
            {
                SendReply(
                    player,
                    "<color=#FF5555>Такой код уже зарегистрирован.</color>"
                );

                return;
            }

            if (FindStreamerBySteamID(steamId) != null)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Этот SteamID уже зарегистрирован как стример.</color>"
                );

                return;
            }

            StreamerData streamer = new StreamerData
            {
                Code = code,
                SteamID = steamId,
                Name = name
            };

            data.Streamers[code] = streamer;

            SaveData();

            BasePlayer target = BasePlayer.FindByID(steamId);

            if (target != null)
                AddStreamerPrefix(target);

            SendReply(
                player,
                $"<color=#00FF00>✓ Стример зарегистрирован.</color>\n" +
                $"Код: <color=#00FFFF>{code}</color>\n" +
                $"SteamID: <color=#FFD700>{steamId}</color>\n" +
                $"Имя: <color=#FFFFFF>{name}</color>"
            );
        }

        private void AdminRemoveStreamer(
            BasePlayer player,
            string[] args)
        {
            if (args.Length < 2)
            {
                SendReply(
                    player,
                    "/streamadmin remove КОД"
                );

                return;
            }

            string code = args[1];

            StreamerData streamer = FindStreamerByCode(code);

            if (streamer == null)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Стример не найден.</color>"
                );

                return;
            }

            ulong steamId = streamer.SteamID;

            data.Streamers.Remove(streamer.Code);

            RemoveStreamerPrefix(steamId);

            SaveData();

            SendReply(
                player,
                $"<color=#00FF00>✓ Стример {streamer.Name} удалён.</color>\n" +
                $"Префикс <color=#FFD700>〔🎥〕</color> снят."
            );
        }

        private void AdminListStreamers(BasePlayer player)
        {
            if (data.Streamers.Count == 0)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Стримеров пока нет.</color>"
                );

                return;
            }

            List<string> lines = new List<string>();

            foreach (StreamerData streamer in data.Streamers.Values)
            {
                bool online =
                    BasePlayer.FindByID(streamer.SteamID) != null;

                lines.Add(
                    $"<color=#FFD700>{streamer.Code}</color> — " +
                    $"{streamer.Name} — " +
                    $"рефералов: {streamer.Referrals} — " +
                    $"часов: {streamer.PaidHours} — " +
                    $"{(online ? "<color=#00FF00>ONLINE</color>" : "<color=#777777>OFFLINE</color>")}"
                );
            }

            SendReply(
                player,
                "<color=#FFD700>Стримеры:</color>\n" +
                string.Join("\n", lines)
            );
        }

        private void AdminStats(
            BasePlayer player,
            string[] args)
        {
            if (args.Length < 2)
            {
                SendReply(
                    player,
                    "/streamadmin stats КОД"
                );

                return;
            }

            StreamerData streamer =
                FindStreamerByCode(args[1]);

            if (streamer == null)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Стример не найден.</color>"
                );

                return;
            }

            SendReply(
                player,
                $"<color=#FFD700>🎥 {streamer.Name}</color>\n" +
                $"Код: <color=#00FFFF>{streamer.Code}</color>\n" +
                $"SteamID: {streamer.SteamID}\n" +
                $"Рефералов: <color=#00FFFF>{streamer.Referrals}</color>\n" +
                $"Минут: <color=#00FFFF>{streamer.PlayedMinutes:F0}</color>\n" +
                $"Оплачено часов: <color=#00FFFF>{streamer.PaidHours}</color>\n" +
                $"Получено RP: <color=#00FFFF>{streamer.PaidRP}</color>\n" +
                $"Получено Economics: <color=#00FF00>{FormatMoney(streamer.PaidEconomics)}</color>"
            );
        }

        private void AdminPlayers(
            BasePlayer player,
            string[] args)
        {
            if (args.Length < 2)
            {
                SendReply(
                    player,
                    "/streamadmin players КОД"
                );

                return;
            }

            StreamerData streamer =
                FindStreamerByCode(args[1]);

            if (streamer == null)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Стример не найден.</color>"
                );

                return;
            }

            if (streamer.Players.Count == 0)
            {
                SendReply(
                    player,
                    "У стримера пока нет рефералов."
                );

                return;
            }

            List<string> names = new List<string>();

            foreach (ulong steamId in streamer.Players)
            {
                BasePlayer target =
                    BasePlayer.FindByID(steamId);

                names.Add(
                    target != null
                        ? $"{target.displayName} ({steamId})"
                        : steamId.ToString()
                );
            }

            SendReply(
                player,
                $"<color=#FFD700>Рефералы {streamer.Name}:</color>\n" +
                string.Join("\n", names)
            );
        }

        private void AdminPay(
            BasePlayer player,
            string[] args)
        {
            if (args.Length < 3)
            {
                SendReply(
                    player,
                    "/streamadmin pay КОД ЧАСЫ"
                );

                return;
            }

            StreamerData streamer =
                FindStreamerByCode(args[1]);

            if (streamer == null)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Стример не найден.</color>"
                );

                return;
            }

            if (!int.TryParse(args[2], out int hours) ||
                hours <= 0)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Неверное количество часов.</color>"
                );

                return;
            }

            PayStreamerHours(streamer, hours);

            SaveData();

            SendReply(
                player,
                $"<color=#00FF00>✓ Выплачено {hours} ч. стримеру {streamer.Name}.</color>"
            );
        }

        private void AdminTop(BasePlayer player)
        {
            StreamerData top = GetTopStreamer();

            if (top == null)
            {
                SendReply(
                    player,
                    "<color=#FF5555>Нет данных для определения TOP-1.</color>"
                );

                return;
            }

            SendReply(
                player,
                $"<color=#FFD700>🏆 TOP-1 стример:</color>\n" +
                $"<color=#00FFFF>{top.Name}</color>\n" +
                $"Код: {top.Code}\n" +
                $"Рефералов: {top.Referrals}\n" +
                $"Оплачено часов: {top.PaidHours}\n" +
                $"Минут: {top.PlayedMinutes:F0}"
            );
        }

        private void AdminNextWipe(BasePlayer player)
        {
            SendReply(
                player,
                $"<color=#FFD700>Следующий вайп:</color>\n" +
                $"{GetNextWipeUtc().ToLocalTime():dd.MM.yyyy HH:mm:ss}"
            );
        }

        private void AdminReset(BasePlayer player)
        {
            ResetWipeStatistics();

            SaveData();

            SendReply(
                player,
                "<color=#00FF00>✓ Статистика стримеров текущего вайпа сброшена.</color>"
            );
        }

        #endregion

        #region Wipe

        private void InitializeWipeSchedule()
        {
            DateTime next = CalculateNextWipeUtc();

            if (string.IsNullOrEmpty(data.CurrentWipeId))
            {
                DateTime current =
                    next.AddDays(-config.Wipe.CycleDays);

                data.CurrentWipeId =
                    current.ToString("O");
            }

            if (string.IsNullOrEmpty(data.NextWipeUtc))
            {
                data.NextWipeUtc =
                    next.ToString("O");
            }

            SaveData();
        }

        private DateTime CalculateNextWipeUtc()
        {
            if (!DateTime.TryParseExact(
                config.Wipe.AnchorDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime anchorDate))
            {
                anchorDate = new DateTime(
                    2026,
                    9,
                    17
                );
            }

            if (!TimeSpan.TryParse(
                config.Wipe.Time,
                out TimeSpan wipeTime))
            {
                wipeTime = new TimeSpan(17, 0, 0);
            }

            DateTime localAnchor =
                new DateTime(
                    anchorDate.Year,
                    anchorDate.Month,
                    anchorDate.Day,
                    wipeTime.Hours,
                    wipeTime.Minutes,
                    wipeTime.Seconds,
                    DateTimeKind.Unspecified
                );

            DateTime anchorUtc =
                localAnchor.AddHours(-config.Wipe.UtcOffsetHours);

            DateTime now = DateTime.UtcNow;

            if (now <= anchorUtc)
                return anchorUtc;

            TimeSpan elapsed =
                now - anchorUtc;

            int cycles =
                Mathf.FloorToInt(
                    (float)(elapsed.TotalDays /
                            config.Wipe.CycleDays)
                ) + 1;

            return anchorUtc.AddDays(
                cycles * config.Wipe.CycleDays
            );
        }

        private DateTime GetNextWipeUtc()
        {
            if (DateTime.TryParse(
                data.NextWipeUtc,
                null,
                DateTimeStyles.RoundtripKind,
                out DateTime result))
            {
                return result.ToUniversalTime();
            }

            DateTime next = CalculateNextWipeUtc();

            data.NextWipeUtc = next.ToString("O");

            return next;
        }

        private void CheckWipe()
        {
            DateTime nextWipe = GetNextWipeUtc();

            if (DateTime.UtcNow < nextWipe)
                return;

            FinishCurrentWipe(nextWipe);
        }

        private void FinishCurrentWipe(DateTime wipeTime)
        {
            string wipeKey = wipeTime.ToString("O");

            if (data.LastProcessedWipeUtc == wipeKey)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                StreamerData streamer =
                    FindStreamerBySteamID(player.userID);

                if (streamer != null)
                    ProcessStreamerTime(player.userID);
            }

            StreamerData top = GetTopStreamer();

            if (top != null)
                GiveTopStreamerReward(top);

            BroadcastWipeResult(top);

            ResetWipeStatistics();

            DateTime next =
                wipeTime.AddDays(config.Wipe.CycleDays);

            data.LastProcessedWipeUtc = wipeKey;
            data.CurrentWipeId = wipeTime.ToString("O");
            data.NextWipeUtc = next.ToString("O");

            SaveData();
        }

        private void ResetWipeStatistics()
        {
            data.PlayerReferrals.Clear();
            data.GlobalPromoActivations.Clear();

            foreach (StreamerData streamer in data.Streamers.Values)
            {
                streamer.Players.Clear();
                streamer.Referrals = 0;
                streamer.Activations = 0;
                streamer.PlayedMinutes = 0;
                streamer.PaidHours = 0;
                streamer.PaidRP = 0;
                streamer.PaidEconomics = 0;

                streamer.ReferralBonus5 = false;
                streamer.ReferralBonus10 = false;
                streamer.ReferralBonus20 = false;
                streamer.ReferralBonus30 = false;
                streamer.ReferralBonus50 = false;

                streamer.OnlineBonus10 = false;
                streamer.OnlineBonus20 = false;
                streamer.OnlineBonus30 = false;
                streamer.OnlineBonus40 = false;

                streamer.TopStreamerRewardPaid = false;
            }

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                StreamerData streamer =
                    FindStreamerBySteamID(player.userID);

                if (streamer != null)
                    streamerJoinTimes[player.userID] = DateTime.UtcNow;
            }
        }

        #endregion

        #region TOP-1

        private StreamerData GetTopStreamer()
        {
            if (data.Streamers == null ||
                data.Streamers.Count == 0)
                return null;

            return data.Streamers.Values
                .OrderByDescending(x => x.Referrals)
                .ThenByDescending(x => x.PaidHours)
                .ThenByDescending(x => x.PlayedMinutes)
                .FirstOrDefault();
        }

        private void GiveTopStreamerReward(StreamerData streamer)
        {
            if (streamer == null)
                return;

            if (streamer.TopStreamerRewardPaid)
                return;

            if (config.TopStreamer.RevokePreviousPermission &&
                data.PreviousTopStreamerSteamID != 0 &&
                data.PreviousTopStreamerSteamID != streamer.SteamID)
            {
                permission.RevokeUserPermission(
                    data.PreviousTopStreamerSteamID.ToString(),
                    config.TopStreamer.Permission
                );
            }

            bool rpPaid =
                GiveRP(
                    streamer.SteamID,
                    config.TopStreamer.RP
                );

            bool economicsPaid =
                GiveEconomics(
                    streamer.SteamID,
                    config.TopStreamer.Economics
                );

            if (config.TopStreamer.GivePermission)
            {
                permission.GrantUserPermission(
                    streamer.SteamID.ToString(),
                    config.TopStreamer.Permission,
                    this
                );
            }

            streamer.TopStreamerRewardPaid = true;

            data.PreviousTopStreamerSteamID =
                streamer.SteamID;

            BasePlayer player =
                BasePlayer.FindByID(streamer.SteamID);

            if (player != null)
            {
                SendReply(
                    player,
                    $"<color=#FFD700>🏆 TOP-1 СТРИМЕР ВАЙПА!</color>\n" +
                    $"RP: <color=#00FFFF>+{config.TopStreamer.RP}</color>\n" +
                    $"Economics: <color=#00FF00>+{FormatMoney(config.TopStreamer.Economics)}</color>\n" +
                    $"Permission: <color=#FFD700>{config.TopStreamer.Permission}</color>"
                );
            }
        }

        private void BroadcastWipeResult(StreamerData top)
        {
            if (!config.Messages.ShowWipeMessages)
                return;

            if (top == null)
            {
                PrintToChat(
                    "<color=#FFD700>🏆 MetalicRust</color>\n" +
                    "Вайп завершён. TOP-1 стример не определён."
                );

                return;
            }

            PrintToChat(
                $"<color=#FFD700>🏆 TOP-1 СТРИМЕР ВАЙПА</color>\n" +
                $"<color=#00FFFF>{top.Name}</color>\n" +
                $"Рефералов: <color=#FFD700>{top.Referrals}</color>\n" +
                $"Награда: <color=#00FFFF>+{config.TopStreamer.RP} RP</color> " +
                $"и <color=#00FF00>+{FormatMoney(config.TopStreamer.Economics)}</color>"
            );
        }

        #endregion

        #region Helpers

        private StreamerData FindStreamerByCode(string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;

            foreach (StreamerData streamer in data.Streamers.Values)
            {
                if (streamer.Code.Equals(
                    code,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return streamer;
                }
            }

            return null;
        }

        private StreamerData FindStreamerBySteamID(ulong steamId)
        {
            foreach (StreamerData streamer in data.Streamers.Values)
            {
                if (streamer.SteamID == steamId)
                    return streamer;
            }

            return null;
        }

        private double GetCurrentSessionMinutes(ulong steamId)
        {
            if (!streamerJoinTimes.TryGetValue(
                steamId,
                out DateTime joinTime))
            {
                return 0;
            }

            return Math.Max(
                0,
                (DateTime.UtcNow - joinTime).TotalMinutes
            );
        }

        private void NotifyStreamer(
            BasePlayer player,
            string message)
        {
            if (player == null)
                return;

            if (!config.Messages.ShowStreamerMessages)
                return;

            SendReply(
                player,
                $"<color=#FFD700>🎥 STREAMER</color>\n{message}"
            );
        }

        private bool IsAdmin(BasePlayer player)
        {
            if (player == null)
                return false;

            return player.IsAdmin ||
                   permission.UserHasPermission(
                       player.UserIDString,
                       "metalicrust.streamer.admin"
                   );
        }

        private bool GiveRP(
            ulong steamId,
            int amount)
        {
            if (amount <= 0)
                return true;

            if (ServerRewards == null)
            {
                PrintWarning(
                    "ServerRewards не загружен. RP не выданы."
                );

                return false;
            }

            try
            {
                object result =
                    ServerRewards.Call(
                        "AddPoints",
                        steamId,
                        amount
                    );

                if (result == null)
                    return true;

                if (result is bool)
                    return (bool)result;

                return true;
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка выдачи RP {steamId}: {ex.Message}"
                );

                return false;
            }
        }

        private bool GiveEconomics(
            ulong steamId,
            double amount)
        {
            if (amount <= 0)
                return true;

            if (Economics == null)
            {
                PrintWarning(
                    "Economics не загружен. Деньги не выданы."
                );

                return false;
            }

            try
            {
                object result =
                    Economics.Call(
                        "Deposit",
                        steamId,
                        amount
                    );

                if (result == null)
                    return true;

                if (result is bool)
                    return (bool)result;

                return true;
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка выдачи Economics {steamId}: {ex.Message}"
                );

                return false;
            }
        }

        private string FormatMoney(double amount)
        {
            return amount.ToString(
                "#,##0",
                CultureInfo.InvariantCulture
            ).Replace(",", " ");
        }

        #endregion
    }
}
