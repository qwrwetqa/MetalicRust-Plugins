using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("MetalicRust", "MetalicRust", "1.0.1")]
    [Description("Защита базы от рейдов через шкаф")]
    public class MetalicRust : RustPlugin
    {
        private const string DataFileName = "MetalicRustData";

        private Dictionary<ulong, ProtectionData> protections = new Dictionary<ulong, ProtectionData>();
        private Dictionary<ulong, Timer> activationTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, Timer> expirationTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, Timer> activationStart = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, Timer> protectionEnd = new Dictionary<ulong, Timer>();
        private HashSet<ulong> protectedCupboards = new HashSet<ulong>();
        private HashSet<ulong> disabledTurrets = new HashSet<ulong>();
        private HashSet<ulong> activeUIs = new HashSet<ulong>();

        private Configuration config;

        #region Time

        private double CurrentTimestamp()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        #endregion

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Стоимость 1 часа")]
            public int CostPerHour = 100;

            [JsonProperty("Максимальное количество часов")]
            public int MaxHours = 48;

            [JsonProperty("Защита при онлайне (%)")]
            public float OnlineProtection = 50f;

            [JsonProperty("Защита при офлайне (%)")]
            public float OfflineProtection = 100f;

            [JsonProperty("Валюта")]
            public string Currency = "scrap";
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
            }
            catch
            {
                PrintWarning("Ошибка чтения конфигурации. Создаётся новая.");
                LoadDefaultConfig();
            }

            if (config == null)
                LoadDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        private class ProtectionData
        {
            public ulong TCId;
            public int Balance;
            public int TotalHours;
            public bool IsPaused;
            public List<ulong> Owners = new List<ulong>();
            public double ProtectedUntil;
            public bool IsActive;
            public bool NotifiedExpired;
            public bool MenuOpen;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, protections);
        }

        private void LoadData()
        {
            try
            {
                protections = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, ProtectionData>>(DataFileName);
            }
            catch
            {
                protections = new Dictionary<ulong, ProtectionData>();
            }

            if (protections == null)
                protections = new Dictionary<ulong, ProtectionData>();
        }

        #endregion

        #region Initialization

        private void Init()
        {
            LoadData();

            timer.Every(5f, UpdateProtections);
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyProtectionButton(player);
                CloseProtectionMenu(player);
            }

            foreach (var t in activationTimers.Values)
                t?.Destroy();

            foreach (var t in expirationTimers.Values)
                t?.Destroy();

            foreach (var t in activationStart.Values)
                t?.Destroy();

            foreach (var t in protectionEnd.Values)
                t?.Destroy();

            SaveData();
        }

        #endregion

        #region Protection

        private void UpdateProtections()
        {
            double now = CurrentTimestamp();

            foreach (var pair in protections.ToList())
            {
                ProtectionData data = pair.Value;

                if (!data.IsActive)
                    continue;

                if (data.IsPaused)
                    continue;

                if (data.ProtectedUntil <= 0)
                    continue;

                if (now >= data.ProtectedUntil)
                {
                    data.IsActive = false;
                    data.TotalHours = 0;
                    data.ProtectedUntil = 0;
                    data.NotifiedExpired = false;

                    protectedCupboards.Remove(data.TCId);

                    SaveData();
                }
            }
        }

        private bool IsProtected(BuildingPrivlidge tc)
        {
            if (tc == null)
                return false;

            if (!protections.TryGetValue(tc.net.ID.Value, out ProtectionData data))
                return false;

            if (!data.IsActive || data.IsPaused)
                return false;

            if (data.ProtectedUntil <= CurrentTimestamp())
                return false;

            return true;
        }

        #endregion

        #region Damage

        private object OnStructureDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
                return null;

            BuildingBlock block = entity as BuildingBlock;

            if (block == null)
                return null;

            BuildingPrivlidge tc = block.GetBuildingPrivilege();

            if (tc == null)
                return null;

            if (!IsProtected(tc))
                return null;

            ProtectionData data;

            if (!protections.TryGetValue(tc.net.ID.Value, out data))
                return null;

            bool ownerOnline = false;

            foreach (ulong ownerId in data.Owners)
            {
                BasePlayer owner = BasePlayer.FindByID(ownerId);

                if (owner != null && owner.IsConnected)
                {
                    ownerOnline = true;
                    break;
                }
            }

            float protection = ownerOnline
                ? config.OnlineProtection
                : config.OfflineProtection;

            if (protection <= 0f)
                return null;

            info.damageTypes.ScaleAll(1f - protection / 100f);

            return null;
        }

        #endregion

        #region TC Authorization

        private void OnPlayerAuthorize(BasePlayer player, BuildingPrivlidge tc)
        {
            if (player == null || tc == null)
                return;

            ulong tcId = tc.net.ID.Value;

            ProtectionData data;

            if (!protections.TryGetValue(tcId, out data))
            {
                data = new ProtectionData
                {
                    TCId = tcId
                };

                protections[tcId] = data;
            }

            if (!data.Owners.Contains(player.userID))
                data.Owners.Add(player.userID);

            SaveData();
        }

        #endregion

        #region Loot TC

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null)
                return;

            BuildingPrivlidge tc = entity as BuildingPrivlidge;

            if (tc == null)
                return;

            ProtectionData data;

            if (!protections.TryGetValue(tc.net.ID.Value, out data))
            {
                data = new ProtectionData
                {
                    TCId = tc.net.ID.Value
                };

                foreach (var auth in tc.authorizedPlayers)
                {
                    if (!data.Owners.Contains(auth))
                        data.Owners.Add(auth);
                }

                protections[tc.net.ID.Value] = data;
                SaveData();
            }

            timer.Once(0.1f, () =>
            {
                if (player == null || !player.IsConnected)
                    return;

                CuiElementContainer container = new CuiElementContainer();

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"metalic_open {data.TCId}",
                        Color = "0.10 0.10 0.10 0.95"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.824 0.870",
                        AnchorMax = "0.902 0.912"
                    },
                    Text =
                    {
                        Text = "ЗАЩИТА",
                        FontSize = 13,
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1"
                    }
                }, "Overlay", "MetalicBtn");

                CuiHelper.AddUi(player, container);

                activeUIs.Add(player.userID);
            });
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null)
                return;

            DestroyProtectionButton(player);
            CloseProtectionMenu(player);
        }

        private void DestroyProtectionButton(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, "MetalicBtn");
            activeUIs.Remove(player.userID);
        }

        #endregion

        #region Protection Menu

        [ConsoleCommand("metalic_open")]
        private void CmdOpen(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null)
                return;

            if (arg.Args == null || arg.Args.Length == 0)
                return;

            ulong tcId;

            if (!ulong.TryParse(arg.Args[0], out tcId))
                return;

            ProtectionData data;

            if (!protections.TryGetValue(tcId, out data))
                return;

            OpenProtectionMenu(player, data);
        }

        private void OpenProtectionMenu(BasePlayer player, ProtectionData data)
        {
            if (player == null || data == null)
                return;

            CloseProtectionMenu(player);

            data.MenuOpen = true;

            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image =
                {
                    Color = "0.04 0.04 0.04 0.98"
                },
                RectTransform =
                {
                    AnchorMin = "0.30 0.25",
                    AnchorMax = "0.70 0.75"
                },
                CursorEnabled = true
            }, "Overlay", "MetalicProtectionMenu");

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = "ЗАЩИТА БАЗЫ ОФФЛАЙН 60%",
                    FontSize = 24,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.05 0.84",
                    AnchorMax = "0.95 0.96"
                }
            }, "MetalicProtectionMenu");

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"Баланс: {data.Balance} {config.Currency}",
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.8 0.8 0.8 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.05 0.73",
                    AnchorMax = "0.95 0.82"
                }
            }, "MetalicProtectionMenu");

            string status;

            if (data.IsActive)
            {
                if (data.IsPaused)
                {
                    status = "ЗАЩИТА ПРИОСТАНОВЛЕНА";
                }
                else
                {
                    TimeSpan remaining = TimeSpan.FromSeconds(
                        Math.Max(0, data.ProtectedUntil - CurrentTimestamp())
                    );

                    status = $"ЗАЩИТА АКТИВНА\nОсталось: {remaining.Days}д {remaining.Hours}ч {remaining.Minutes}м";
                }
            }
            else
            {
                status = "ЗАЩИТА НЕ АКТИВНА";
            }

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = status,
                    FontSize = 17,
                    Align = TextAnchor.MiddleCenter,
                    Color = data.IsActive ? "0.3 1 0.3 1" : "1 0.4 0.4 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.05 0.57",
                    AnchorMax = "0.95 0.70"
                }
            }, "MetalicProtectionMenu");

            AddMenuButton(
                container,
                "1 ЧАС",
                $"metalic_buy1 {data.TCId}",
                "0.10 0.43",
                "0.90 0.52"
            );

            AddMenuButton(
                container,
                "24 ЧАСА",
                $"metalic_buy24 {data.TCId}",
                "0.10 0.31",
                "0.90 0.40"
            );

            AddMenuButton(
                container,
                $"МАКСИМУМ ({config.MaxHours} Ч)",
                $"metalic_buymax {data.TCId}",
                "0.10 0.19",
                "0.90 0.28"
            );

            if (data.IsActive)
            {
                AddMenuButton(
                    container,
                    data.IsPaused ? "ПРОДОЛЖИТЬ" : "ПАУЗА",
                    $"metalic_pause {data.TCId}",
                    "0.10 0.07",
                    "0.45 0.15"
                );
            }

            AddMenuButton(
                container,
                "ЗАКРЫТЬ",
                "metalic_close",
                "0.55 0.07",
                "0.90 0.15"
            );

            CuiHelper.AddUi(player, container);
        }

        private void AddMenuButton(
            CuiElementContainer container,
            string text,
            string command,
            string anchorMin,
            string anchorMax)
        {
            container.Add(new CuiButton
            {
                Button =
                {
                    Command = command,
                    Color = "0.12 0.12 0.12 1"
                },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax
                },
                Text =
                {
                    Text = text,
                    FontSize = 15,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, "MetalicProtectionMenu");
        }

        private void CloseProtectionMenu(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, "MetalicProtectionMenu");
        }

        [ConsoleCommand("metalic_close")]
        private void CmdClose(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null)
                return;

            CloseProtectionMenu(player);
        }

        #endregion

        #region Purchase

        [ConsoleCommand("metalic_buy1")]
        private void CmdBuy1(ConsoleSystem.Arg arg)
        {
            BuyProtection(arg, 1);
        }

        [ConsoleCommand("metalic_buy24")]
        private void CmdBuy24(ConsoleSystem.Arg arg)
        {
            BuyProtection(arg, 24);
        }

        [ConsoleCommand("metalic_buymax")]
        private void CmdBuyMax(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null)
                return;

            if (arg.Args == null || arg.Args.Length == 0)
                return;

            ulong tcId;

            if (!ulong.TryParse(arg.Args[0], out tcId))
                return;

            ProtectionData data;

            if (!protections.TryGetValue(tcId, out data))
                return;

            int availableHours = config.MaxHours - data.TotalHours;

            if (availableHours <= 0)
            {
                player.ChatMessage("Достигнуто максимальное время защиты.");
                return;
            }

            BuyProtection(player, data, availableHours);
        }

        private void BuyProtection(ConsoleSystem.Arg arg, int hours)
        {
            BasePlayer player = arg.Player();

            if (player == null)
                return;

            if (arg.Args == null || arg.Args.Length == 0)
                return;

            ulong tcId;

            if (!ulong.TryParse(arg.Args[0], out tcId))
                return;

            ProtectionData data;

            if (!protections.TryGetValue(tcId, out data))
                return;

            BuyProtection(player, data, hours);
        }

        private void BuyProtection(BasePlayer player, ProtectionData data, int hours)
        {
            if (player == null || data == null)
                return;

            if (hours <= 0)
                return;

            if (data.TotalHours + hours > config.MaxHours)
                hours = config.MaxHours - data.TotalHours;

            if (hours <= 0)
            {
                player.ChatMessage("Достигнуто максимальное время защиты.");
                return;
            }

            int cost = hours * config.CostPerHour;

            if (config.Currency.ToLower() == "scrap")
            {
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition("scrap");

                if (itemDefinition == null)
                {
                    player.ChatMessage("Ошибка валюты.");
                    return;
                }

                int amount = player.inventory.GetAmount(itemDefinition.itemid);

                if (amount < cost)
                {
                    player.ChatMessage($"Недостаточно металлолома. Нужно: {cost}.");
                    return;
                }

                player.inventory.Take(null, itemDefinition.itemid, cost);
            }

            data.TotalHours += hours;

            double seconds = hours * 3600d;

            if (data.IsActive && data.ProtectedUntil > CurrentTimestamp())
            {
                data.ProtectedUntil += seconds;
            }
            else
            {
                data.ProtectedUntil = CurrentTimestamp() + seconds;
                data.IsActive = true;
                data.IsPaused = false;
            }

            data.NotifiedExpired = false;

            protectedCupboards.Add(data.TCId);

            SaveData();

            player.ChatMessage(
                $"Защита продлена на {hours} ч. Стоимость: {cost} {config.Currency}."
            );

            OpenProtectionMenu(player, data);
        }

        #endregion

        #region Pause

        [ConsoleCommand("metalic_pause")]
        private void CmdPause(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();

            if (player == null)
                return;

            if (arg.Args == null || arg.Args.Length == 0)
                return;

            ulong tcId;

            if (!ulong.TryParse(arg.Args[0], out tcId))
                return;

            ProtectionData data;

            if (!protections.TryGetValue(tcId, out data))
                return;

            if (!data.IsActive)
                return;

            if (!data.IsPaused)
            {
                data.IsPaused = true;
                player.ChatMessage("Защита приостановлена.");
            }
            else
            {
                data.IsPaused = false;

                if (data.ProtectedUntil <= CurrentTimestamp())
                {
                    data.ProtectedUntil =
                        CurrentTimestamp() + data.TotalHours * 3600d;
                }

                player.ChatMessage("Защита продолжена.");
            }

            SaveData();

            OpenProtectionMenu(player, data);
        }

        #endregion
    }
}
