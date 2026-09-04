using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MetalicRust TopTime", "MetalicRust", "3.0.0")]
    [Description("ТОП игроков по активному времени текущего вайпа. Отдельно учитывает Active и AFK время.")]
    public class MetalicRustTopTime : RustPlugin
    {
        private const string DataFileName = "MetalicRustTopTime_Data";
        private const string UiName = "MetalicRustTopTime_UI";

        private ConfigData config;
        private StoredData data;

        private readonly Dictionary<ulong, DateTime> sessions =
            new Dictionary<ulong, DateTime>();

        private readonly Dictionary<ulong, DateTime> lastActivity =
            new Dictionary<ulong, DateTime>();

        private readonly Dictionary<ulong, Vector3> lastPosition =
            new Dictionary<ulong, Vector3>();

        private Timer saveTimer;
        private Timer wipeTimer;
        private Timer activityTimer;

        #region Config

        private class ConfigData
        {
            [JsonProperty("Плагин включен")]
            public bool Enabled = true;

            [JsonProperty("Длительность вайпа в днях")]
            public int WipeDays = 14;

            [JsonProperty("Первый вайп МСК")]
            public string FirstWipe = "2026-08-27 17:00";

            [JsonProperty("VIP permission для ТОП-1")]
            public string VipPermission = "metalicrust.vip";

            [JsonProperty("Название валюты")]
            public string CurrencyName = "Economics";

            [JsonProperty("Через сколько минут без активности считать AFK")]
            public int AfkMinutes = 5;

            [JsonProperty("Не учитывать администраторов")]
            public bool ExcludeAdmins = true;

            [JsonProperty("Награды")]
            public RewardSettings Rewards = new RewardSettings();
        }

        private class RewardSettings
        {
            [JsonProperty("ТОП-1 Economics")]
            public double Top1Money = 20000;

            [JsonProperty("ТОП-1 VIP дней")]
            public int Top1VipDays = 14;

            [JsonProperty("ТОП-2 Scrap")]
            public int Top2Scrap = 1000;

            [JsonProperty("ТОП-2 Economics")]
            public double Top2Money = 5000;

            [JsonProperty("ТОП-3 Scrap")]
            public int Top3Scrap = 500;

            [JsonProperty("ТОП-3 Economics")]
            public double Top3Money = 1000;
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();

                if (config == null)
                    throw new Exception();
            }
            catch
            {
                PrintWarning("Ошибка конфигурации. Создаю новый конфиг.");
                LoadDefaultConfig();
            }

            if (config.AfkMinutes < 1)
                config.AfkMinutes = 5;
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        private class StoredData
        {
            public string WipeStart = "";

            public Dictionary<ulong, PlayerData> Players =
                new Dictionary<ulong, PlayerData>();

            public List<ulong> RewardedPlayers =
                new List<ulong>();
        }

        private class PlayerData
        {
            public ulong UserId;
            public string Name;

            // Общее время на сервере
            public long TotalSeconds;

            // Реально активное игровое время
            public long ActiveSeconds;

            // AFK время
            public long AfkSeconds;
        }

        private void LoadData()
        {
            try
            {
                data =
                    Interface.Oxide.DataFileSystem.ReadObject<StoredData>(
                        DataFileName
                    );
            }
            catch
            {
                data = new StoredData();
            }

            if (data == null)
                data = new StoredData();

            if (data.Players == null)
                data.Players =
                    new Dictionary<ulong, PlayerData>();

            if (data.RewardedPlayers == null)
                data.RewardedPlayers =
                    new List<ulong>();
        }

        private void SaveData()
        {
            if (data == null)
                return;

            Interface.Oxide.DataFileSystem.WriteObject(
                DataFileName,
                data
            );
        }

        #endregion

        #region Initialization

        private void Init()
        {
            LoadData();

            permission.RegisterPermission(
                config.VipPermission,
                this
            );
        }

        private void OnServerInitialized()
        {
            if (!config.Enabled)
                return;

            CheckWipe();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                StartTracking(player);
            }

            saveTimer = timer.Every(
                60f,
                SaveOnlineTime
            );

            wipeTimer = timer.Every(
                60f,
                CheckWipe
            );

            activityTimer = timer.Every(
                10f,
                CheckPlayerActivity
            );
        }

        private void Unload()
        {
            SaveOnlineTime();

            if (saveTimer != null)
                saveTimer.Destroy();

            if (wipeTimer != null)
                wipeTimer.Destroy();

            if (activityTimer != null)
                activityTimer.Destroy();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null)
                    CuiHelper.DestroyUi(
                        player,
                        UiName
                    );
            }

            SaveData();

            sessions.Clear();
            lastActivity.Clear();
            lastPosition.Clear();
        }

        #endregion

        #region Wipe

        private DateTime MoscowNow()
        {
            return DateTime.UtcNow.AddHours(3);
        }

        private DateTime ParseMoscowDate(string value)
        {
            DateTime result;

            if (DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result))
            {
                return DateTime.SpecifyKind(
                    result,
                    DateTimeKind.Unspecified
                );
            }

            return DateTime.SpecifyKind(
                DateTime.Now,
                DateTimeKind.Unspecified
            );
        }

        private DateTime GetCurrentWipe()
        {
            DateTime first =
                ParseMoscowDate(config.FirstWipe);

            DateTime now =
                MoscowNow();

            if (now < first)
                return first;

            double days =
                (now - first).TotalDays;

            int periods =
                (int)Math.Floor(
                    days / config.WipeDays
                );

            return first.AddDays(
                periods * config.WipeDays
            );
        }

        private void CheckWipe()
        {
            if (!config.Enabled)
                return;

            DateTime current =
                GetCurrentWipe();

            if (string.IsNullOrEmpty(data.WipeStart))
            {
                data.WipeStart =
                    current.ToString(
                        "yyyy-MM-dd HH:mm"
                    );

                SaveData();
                return;
            }

            DateTime saved =
                ParseMoscowDate(
                    data.WipeStart
                );

            if (current <= saved)
                return;

            FinishWipe();

            data.Players.Clear();
            data.RewardedPlayers.Clear();

            sessions.Clear();
            lastActivity.Clear();
            lastPosition.Clear();

            data.WipeStart =
                current.ToString(
                    "yyyy-MM-dd HH:mm"
                );

            SaveData();

            PrintToChat(
                "<color=#FFD700>METALICRUST</color>\n" +
                "<color=#FFFFFF>Новый вайп начался!</color>\n" +
                "<color=#AAAAAA>Активный ТОП времени обнулён.</color>"
            );
        }

        private void FinishWipe()
        {
            SaveOnlineTime();

            List<PlayerData> top =
                GetSortedPlayers();

            if (top.Count >= 1)
                GiveTop1Reward(top[0]);

            if (top.Count >= 2)
                GiveTop2Reward(top[1]);

            if (top.Count >= 3)
                GiveTop3Reward(top[2]);
        }

        #endregion

        #region Tracking

        private void OnPlayerInit(BasePlayer player)
        {
            if (player == null)
                return;

            timer.Once(
                5f,
                () =>
                {
                    if (player == null)
                        return;

                    if (!player.IsConnected)
                        return;

                    StartTracking(player);
                }
            );
        }

        private void OnPlayerDisconnected(
            BasePlayer player,
            string reason)
        {
            StopTracking(player);
        }

        private void StartTracking(BasePlayer player)
        {
            if (player == null)
                return;

            if (!player.IsConnected)
                return;

            if (config.ExcludeAdmins &&
                player.IsAdmin)
                return;

            if (sessions.ContainsKey(player.userID))
                return;

            sessions[player.userID] =
                DateTime.UtcNow;

            lastActivity[player.userID] =
                DateTime.UtcNow;

            lastPosition[player.userID] =
                player.transform.position;

            PlayerData pd;

            if (!data.Players.TryGetValue(
                player.userID,
                out pd))
            {
                pd = new PlayerData
                {
                    UserId = player.userID,
                    Name = player.displayName,
                    TotalSeconds = 0,
                    ActiveSeconds = 0,
                    AfkSeconds = 0
                };

                data.Players.Add(
                    player.userID,
                    pd
                );
            }
            else
            {
                pd.Name =
                    player.displayName;
            }
        }

        private void StopTracking(BasePlayer player)
        {
            if (player == null)
                return;

            DateTime start;

            if (!sessions.TryGetValue(
                player.userID,
                out start))
                return;

            AddSessionTime(
                player.userID,
                start
            );

            sessions.Remove(
                player.userID
            );

            lastActivity.Remove(
                player.userID
            );

            lastPosition.Remove(
                player.userID
            );

            SaveData();
        }

        #endregion

        #region Activity / AFK

        private void CheckPlayerActivity()
        {
            if (!config.Enabled)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null)
                    continue;

                if (!player.IsConnected)
                    continue;

                if (config.ExcludeAdmins &&
                    player.IsAdmin)
                    continue;

                if (!sessions.ContainsKey(player.userID))
                    StartTracking(player);

                DetectActivity(player);
            }
        }

        private void DetectActivity(BasePlayer player)
        {
            if (player == null)
                return;

            bool active = false;

            Vector3 currentPosition =
                player.transform.position;

            Vector3 oldPosition;

            if (lastPosition.TryGetValue(
                player.userID,
                out oldPosition))
            {
                float distance =
                    Vector3.Distance(
                        currentPosition,
                        oldPosition
                    );

                // Движение игрока
                if (distance >= 0.5f)
                    active = true;
            }

            lastPosition[player.userID] =
                currentPosition;

            // Игрок не спит
            if (!player.IsSleeping())
            {
                // Если игрок двигается/смотрит/находится в активном состоянии
                // дополнительная проверка положения камеры
                if (player.transform.hasChanged)
                    active = true;
            }

            if (active)
            {
                lastActivity[player.userID] =
                    DateTime.UtcNow;

                player.transform.hasChanged =
                    false;
            }
        }

        private bool IsPlayerAfk(ulong userId)
        {
            DateTime activity;

            if (!lastActivity.TryGetValue(
                userId,
                out activity))
                return true;

            return
                (DateTime.UtcNow - activity).TotalMinutes
                >= config.AfkMinutes;
        }

        #endregion

        #region Time Saving

        private void SaveOnlineTime()
        {
            if (!config.Enabled)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null)
                    continue;

                if (config.ExcludeAdmins &&
                    player.IsAdmin)
                    continue;

                DateTime start;

                if (!sessions.TryGetValue(
                    player.userID,
                    out start))
                    continue;

                AddSessionTime(
                    player.userID,
                    start
                );

                sessions[player.userID] =
                    DateTime.UtcNow;
            }

            SaveData();
        }

        private void AddSessionTime(
            ulong userId,
            DateTime start)
        {
            double seconds =
                (DateTime.UtcNow - start).TotalSeconds;

            if (seconds <= 0)
                return;

            PlayerData pd;

            if (!data.Players.TryGetValue(
                userId,
                out pd))
            {
                pd = new PlayerData
                {
                    UserId = userId,
                    Name = "",
                    TotalSeconds = 0,
                    ActiveSeconds = 0,
                    AfkSeconds = 0
                };

                data.Players.Add(
                    userId,
                    pd
                );
            }

            long elapsed =
                (long)seconds;

            pd.TotalSeconds += elapsed;

            if (IsPlayerAfk(userId))
            {
                pd.AfkSeconds += elapsed;
            }
            else
            {
                pd.ActiveSeconds += elapsed;
            }

            BasePlayer player =
                BasePlayer.FindByID(userId);

            if (player != null)
                pd.Name =
                    player.displayName;
        }

        #endregion

        #region Ranking

        private List<PlayerData> GetSortedPlayers()
        {
            return data.Players.Values
                .Where(x => x != null)
                .OrderByDescending(
                    x => x.ActiveSeconds
                )
                .ToList();
        }

        private int GetPosition(ulong userId)
        {
            List<PlayerData> list =
                GetSortedPlayers();

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].UserId == userId)
                    return i + 1;
            }

            return 0;
        }

        private PlayerData GetPlayer(ulong userId)
        {
            PlayerData pd;

            if (data.Players.TryGetValue(
                userId,
                out pd))
                return pd;

            return null;
        }

        #endregion

        #region Rewards

        private void GiveTop1Reward(PlayerData player)
        {
            if (player == null)
                return;

            if (data.RewardedPlayers.Contains(
                player.UserId))
                return;

            BasePlayer online =
                BasePlayer.FindByID(
                    player.UserId
                );

            if (!string.IsNullOrEmpty(
                config.VipPermission))
            {
                permission.GrantUserPermission(
                    player.UserId.ToString(),
                    config.VipPermission,
                    this
                );
            }

            if (config.Rewards.Top1Money > 0)
            {
                EconomicsCallGive(
                    player.UserId,
                    config.Rewards.Top1Money
                );
            }

            data.RewardedPlayers.Add(
                player.UserId
            );

            if (online != null)
            {
                online.ChatMessage(
                    "<color=#FFD700>METALICRUST</color>\n" +
                    "<color=#FFFFFF>Ты занял TOP-1 по АКТИВНОМУ времени!</color>\n\n" +
                    "<color=#FFD700>Награды:</color>\n" +
                    "<color=#FFFFFF>VIP на следующий вайп</color>\n" +
                    "<color=#00FFAA>20 000 игровой валюты</color>"
                );
            }

            PrintWarning(
                "TOP-1 ACTIVE: " +
                player.Name +
                " (" +
                player.UserId +
                ")"
            );
        }

        private void GiveTop2Reward(PlayerData player)
        {
            if (player == null)
                return;

            if (data.RewardedPlayers.Contains(
                player.UserId))
                return;

            GiveItem(
                player.UserId,
                "scrap",
                config.Rewards.Top2Scrap
            );

            EconomicsCallGive(
                player.UserId,
                config.Rewards.Top2Money
            );

            data.RewardedPlayers.Add(
                player.UserId
            );

            BasePlayer online =
                BasePlayer.FindByID(
                    player.UserId
                );

            if (online != null)
            {
                online.ChatMessage(
                    "<color=#FFD700>METALICRUST</color>\n" +
                    "<color=#FFFFFF>Ты занял TOP-2 по АКТИВНОМУ времени!</color>\n\n" +
                    "<color=#FFD700>Награды:</color>\n" +
                    "<color=#FFFFFF>1000 Scrap</color>\n" +
                    "<color=#00FFAA>5000 игровой валюты</color>"
                );
            }
        }

        private void GiveTop3Reward(PlayerData player)
        {
            if (player == null)
                return;

            if (data.RewardedPlayers.Contains(
                player.UserId))
                return;

            GiveItem(
                player.UserId,
                "scrap",
                config.Rewards.Top3Scrap
            );

            EconomicsCallGive(
                player.UserId,
                config.Rewards.Top3Money
            );

            data.RewardedPlayers.Add(
                player.UserId
            );

            BasePlayer online =
                BasePlayer.FindByID(
                    player.UserId
                );

            if (online != null)
            {
                online.ChatMessage(
                    "<color=#FFD700>METALICRUST</color>\n" +
                    "<color=#FFFFFF>Ты занял TOP-3 по АКТИВНОМУ времени!</color>\n\n" +
                    "<color=#FFD700>Награды:</color>\n" +
                    "<color=#FFFFFF>500 Scrap</color>\n" +
                    "<color=#00FFAA>1000 игровой валюты</color>"
                );
            }
        }

        private void GiveItem(
            ulong userId,
            string shortname,
            int amount)
        {
            if (amount <= 0)
                return;

            BasePlayer player =
                BasePlayer.FindByID(userId);

            if (player == null)
                return;

            Item item =
                ItemManager.CreateByName(
                    shortname,
                    amount
                );

            if (item == null)
                return;

            player.GiveItem(item);
        }

        private void EconomicsCallGive(
            ulong userId,
            double amount)
        {
            if (amount <= 0)
                return;

            string command =
                "economics.deposit " +
                userId +
                " " +
                amount.ToString(
                    CultureInfo.InvariantCulture
                );

            ConsoleSystem.Run(
                ConsoleSystem.Option.Server,
                command
            );
        }

        #endregion

        #region UI

        [ChatCommand("toptime")]
        private void TopTimeCommand(
            BasePlayer player,
            string command,
            string[] args)
        {
            if (player == null)
                return;

            CheckWipe();
            SaveOnlineTime();

            OpenMenu(player);
        }

        private void OpenMenu(BasePlayer player)
        {
            CuiHelper.DestroyUi(
                player,
                UiName
            );

            CuiElementContainer container =
                new CuiElementContainer();

            container.Add(
                new CuiPanel
                {
                    Image =
                    {
                        Color = "0.03 0.04 0.05 0.98"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0.5",
                        AnchorMax = "0.5 0.5",
                        OffsetMin = "-500 -300",
                        OffsetMax = "500 300"
                    },
                    CursorEnabled = true
                },
                "Overlay",
                UiName
            );

            container.Add(
                new CuiPanel
                {
                    Image =
                    {
                        Color = "0.10 0.11 0.13 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0.86",
                        AnchorMax = "1 1"
                    }
                },
                UiName
            );

            AddLabel(
                container,
                UiName,
                "METALICRUST",
                "0.05 0.92",
                "0.95 0.98",
                28,
                "1 0.82 0.1 1",
                TextAnchor.MiddleCenter
            );

            AddLabel(
                container,
                UiName,
                "ТОП ПО АКТИВНОМУ ВРЕМЕНИ • ТЕКУЩИЙ ВАЙП",
                "0.05 0.85",
                "0.95 0.91",
                13,
                "0.75 0.78 0.82 1",
                TextAnchor.MiddleCenter
            );

            AddButton(
                container,
                UiName,
                "X",
                "0.94 0.93",
                "0.985 0.985",
                "metalicrust.toptime.close"
            );

            List<PlayerData> top =
                GetSortedPlayers();

            for (int i = 0; i < 3; i++)
            {
                float yMax =
                    0.76f - i * 0.19f;

                float yMin =
                    yMax - 0.15f;

                string name =
                    i < top.Count
                        ? top[i].Name
                        : "Свободно";

                string active =
                    i < top.Count
                        ? FormatTime(
                            top[i].ActiveSeconds
                        )
                        : "--";

                string total =
                    i < top.Count
                        ? FormatTime(
                            top[i].TotalSeconds
                        )
                        : "--";

                string afk =
                    i < top.Count
                        ? FormatTime(
                            top[i].AfkSeconds
                        )
                        : "--";

                string place =
                    (i + 1).ToString();

                string reward =
                    GetRewardText(i + 1);

                string titleColor;

                if (i == 0)
                    titleColor = "1 0.75 0.05 1";
                else if (i == 1)
                    titleColor = "0.75 0.78 0.82 1";
                else
                    titleColor = "0.85 0.5 0.2 1";

                string timeText =
                    "ACTIVE: " +
                    active +
                    "   |   AFK: " +
                    afk +
                    "   |   TOTAL: " +
                    total;

                container.Add(
                    new CuiPanel
                    {
                        Image =
                        {
                            Color = "0.08 0.09 0.11 1"
                        },
                        RectTransform =
                        {
                            AnchorMin =
                                $"0.08 {yMin}",
                            AnchorMax =
                                $"0.92 {yMax}"
                        }
                    },
                    UiName,
                    UiName + "_place_" + i
                );

                AddLabel(
                    container,
                    UiName + "_place_" + i,
                    "#" + place,
                    "0.02 0.15",
                    "0.12 0.85",
                    28,
                    titleColor,
                    TextAnchor.MiddleCenter
                );

                AddLabel(
                    container,
                    UiName + "_place_" + i,
                    name,
                    "0.14 0.55",
                    "0.60 0.88",
                    17,
                    "1 1 1 1",
                    TextAnchor.MiddleLeft
                );

                AddLabel(
                    container,
                    UiName + "_place_" + i,
                    timeText,
                    "0.14 0.12",
                    "0.70 0.52",
                    11,
                    "0.45 0.85 1 1",
                    TextAnchor.MiddleLeft
                );

                AddLabel(
                    container,
                    UiName + "_place_" + i,
                    reward,
                    "0.70 0.15",
                    "0.98 0.85",
                    11,
                    "0.75 0.75 0.75 1",
                    TextAnchor.MiddleCenter
                );
            }

            PlayerData me =
                GetPlayer(player.userID);

            int myPosition =
                GetPosition(player.userID);

            long activeSeconds =
                GetCurrentActiveSeconds(player);

            long totalSeconds =
                GetCurrentTotalSeconds(player);

            long afkSeconds =
                GetCurrentAfkSeconds(player);

            string myText;

            if (me == null)
            {
                myText =
                    "Твоя статистика появится после начала игры.";
            }
            else
            {
                myText =
                    "ТВОЁ МЕСТО: #" +
                    myPosition +
                    "    •    ACTIVE: " +
                    FormatTime(activeSeconds);
            }

            container.Add(
                new CuiPanel
                {
                    Image =
                    {
                        Color = "0.12 0.13 0.16 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.08 0.09",
                        AnchorMax = "0.92 0.20"
                    }
                },
                UiName,
                UiName + "_my"
            );

            AddLabel(
                container,
                UiName + "_my",
                myText,
                "0.02 0.58",
                "0.98 0.95",
                15,
                "0.2 0.85 1 1",
                TextAnchor.MiddleCenter
            );

            AddLabel(
                container,
                UiName + "_my",
                "ACTIVE: " +
                FormatTime(activeSeconds) +
                "   |   AFK: " +
                FormatTime(afkSeconds) +
                "   |   TOTAL: " +
                FormatTime(totalSeconds),
                "0.02 0.10",
                "0.98 0.55",
                11,
                "0.75 0.75 0.75 1",
                TextAnchor.MiddleCenter
            );

            CuiHelper.AddUi(
                player,
                container
            );
        }

        private string GetRewardText(int place)
        {
            if (place == 1)
                return "VIP + 20 000";

            if (place == 2)
                return "1000 Scrap + 5 000";

            if (place == 3)
                return "500 Scrap + 1 000";

            return "";
        }

        private long GetCurrentTotalSeconds(
            BasePlayer player)
        {
            PlayerData pd =
                GetPlayer(player.userID);

            long seconds =
                pd != null
                    ? pd.TotalSeconds
                    : 0;

            DateTime start;

            if (sessions.TryGetValue(
                player.userID,
                out start))
            {
                seconds +=
                    (long)(
                        DateTime.UtcNow -
                        start
                    ).TotalSeconds;
            }

            return seconds;
        }

        private long GetCurrentActiveSeconds(
            BasePlayer player)
        {
            PlayerData pd =
                GetPlayer(player.userID);

            long seconds =
                pd != null
                    ? pd.ActiveSeconds
                    : 0;

            DateTime start;

            if (sessions.TryGetValue(
                player.userID,
                out start))
            {
                double elapsed =
                    (DateTime.UtcNow - start)
                    .TotalSeconds;

                if (!IsPlayerAfk(player.userID))
                {
                    seconds +=
                        (long)elapsed;
                }
            }

            return seconds;
        }

        private long GetCurrentAfkSeconds(
            BasePlayer player)
        {
            PlayerData pd =
                GetPlayer(player.userID);

            long seconds =
                pd != null
                    ? pd.AfkSeconds
                    : 0;

            DateTime start;

            if (sessions.TryGetValue(
                player.userID,
                out start))
            {
                double elapsed =
                    (DateTime.UtcNow - start)
                    .TotalSeconds;

                if (IsPlayerAfk(player.userID))
                {
                    seconds +=
                        (long)elapsed;
                }
            }

            return seconds;
        }

        private void AddLabel(
            CuiElementContainer container,
            string parent,
            string text,
            string min,
            string max,
            int fontSize,
            string color,
            TextAnchor align)
        {
            container.Add(
                new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = min,
                        AnchorMax = max
                    },
                    Text =
                    {
                        Text = text,
                        FontSize = fontSize,
                        Color = color,
                        Align = align,
                        Font =
                            "robotocondensed-regular.ttf"
                    }
                },
                parent
            );
        }

        private void AddButton(
            CuiElementContainer container,
            string parent,
            string text,
            string min,
            string max,
            string command)
        {
            container.Add(
                new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = min,
                        AnchorMax = max
                    },
                    Button =
                    {
                        Color = "0.8 0.1 0.1 0.9",
                        Command = command
                    },
                    Text =
                    {
                        Text = text,
                        FontSize = 16,
                        Color = "1 1 1 1",
                        Align =
                            TextAnchor.MiddleCenter
                    }
                },
                parent
            );
        }

        [ConsoleCommand("metalicrust.toptime.close")]
        private void CloseMenu(
            ConsoleSystem.Arg arg)
        {
            BasePlayer player =
                arg.Player();

            if (player == null)
                return;

            CuiHelper.DestroyUi(
                player,
                UiName
            );
        }

        #endregion

        #region Chat

        private void PrintToChat(string message)
        {
            foreach (
                BasePlayer player
                in BasePlayer.activePlayerList)
            {
                if (player != null)
                    player.ChatMessage(message);
            }
        }

        #endregion

        #region Helpers

        private string FormatTime(long seconds)
        {
            if (seconds < 60)
                return seconds + " сек.";

            long minutes =
                seconds / 60;

            long hours =
                minutes / 60;

            long days =
                hours / 24;

            hours %= 24;
            minutes %= 60;

            if (days > 0)
                return
                    days + "д " +
                    hours + "ч " +
                    minutes + "м";

            if (hours > 0)
                return
                    hours + "ч " +
                    minutes + "м";

            return
                minutes + "м";
        }

        #endregion
    }
}