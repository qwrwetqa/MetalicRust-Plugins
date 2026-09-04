using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("MetalicRust OfflineProtection", "MetalicRust", "1.5.5")]
    [Description("Offline защита TC: 10 минут ожидания, 24 часа защиты, отключение AutoTurret и сохранение состояния после рестарта.")]
    public class MetalicRustOfflineProtection : RustPlugin
    {
        private const string Permission = "metalicrust.offlineprotection";
        private const string DataFileName = "MetalicRustOfflineProtection_Data";

        private Configuration config;

        private StoredData storedData;

        private readonly Dictionary<string, Timer> activationTimers =
            new Dictionary<string, Timer>();

        private readonly Dictionary<string, Timer> expirationTimers =
            new Dictionary<string, Timer>();

        private readonly Dictionary<string, DateTime> activationStart =
            new Dictionary<string, DateTime>();

        private readonly Dictionary<string, DateTime> protectionEnd =
            new Dictionary<string, DateTime>();

        private readonly HashSet<string> protectedCupboards =
            new HashSet<string>();

        // Турели, которые плагин отключил.
        private readonly HashSet<ulong> disabledTurrets =
            new HashSet<ulong>();

        #region Configuration

        private class Configuration
        {
            public bool Enabled = true;

            public float ProtectionDelayMinutes = 10f;

            public float ProtectionDurationHours = 24f;

            public bool ProtectBuildingBlocks = true;

            public bool ProtectDoors = true;

            public bool ProtectDeployables = true;

            public bool ProtectToolCupboard = true;

            public bool DisableAutoTurrets = true;

            public bool IgnoreAdmins = true;

            public bool ShowMessage = true;

            public string ProtectionMessage =
                "🛡️ Эта база защищена от рейда.";

            public string ProtectionEnabledMessage =
                "🛡️ OfflineProtection: защита базы включена на 24 часа.";

            public string ProtectionDisabledMessage =
                "🔓 OfflineProtection: игрок вошёл. Защита базы отключена.";

            public string ProtectionExpiredMessage =
                "⏰ OfflineProtection: 24 часа защиты закончились.";
        }

        #endregion

        #region Stored Data

        private class StoredData
        {
            public Dictionary<string, StoredCupboard> Cupboards =
                new Dictionary<string, StoredCupboard>();
        }

        private class StoredCupboard
        {
            public string CupboardId;

            public long ActivationStartTicks;

            public long ProtectionEndTicks;

            public bool ProtectionActive;
        }

        #endregion

        #region Configuration Loading

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
                    throw new Exception();

                SaveConfig();
            }
            catch
            {
                PrintWarning(
                    "Ошибка конфигурации. Создаю новый конфиг."
                );

                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        private void LoadData()
        {
            try
            {
                storedData =
                    Interface.Oxide.DataFileSystem.ReadObject<StoredData>(
                        DataFileName
                    );

                if (storedData == null)
                    storedData = new StoredData();
            }
            catch
            {
                PrintWarning(
                    "Не удалось загрузить файл данных OfflineProtection. Создаю новый."
                );

                storedData = new StoredData();
            }
        }

        private void SaveData()
        {
            if (storedData == null)
                storedData = new StoredData();

            Interface.Oxide.DataFileSystem.WriteObject(
                DataFileName,
                storedData
            );
        }

        private void SaveCupboardState(
            string cupboardId,
            DateTime activation,
            DateTime protectionEndTime,
            bool protectionActive)
        {
            if (string.IsNullOrEmpty(cupboardId))
                return;

            if (storedData == null)
                storedData = new StoredData();

            storedData.Cupboards[cupboardId] =
                new StoredCupboard
                {
                    CupboardId = cupboardId,
                    ActivationStartTicks = activation.Ticks,
                    ProtectionEndTicks = protectionEndTime.Ticks,
                    ProtectionActive = protectionActive
                };

            SaveData();
        }

        private void RemoveCupboardState(
            string cupboardId)
        {
            if (string.IsNullOrEmpty(cupboardId))
                return;

            if (storedData == null)
                return;

            if (storedData.Cupboards.Remove(cupboardId))
                SaveData();
        }

        #endregion

        #region Init

        private void Init()
        {
            permission.RegisterPermission(
                Permission,
                this
            );

            LoadData();
        }

        private void OnServerInitialized()
        {
            timer.Once(
                5f,
                RestoreSavedProtection
            );
        }

        #endregion

        #region ID

        private string GetCupboardId(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return null;

            if (cupboard.net == null)
                return null;

            return cupboard.net.ID.ToString();
        }

        #endregion

        #region Restore

        private void RestoreSavedProtection()
        {
            if (!config.Enabled)
                return;

            if (storedData == null ||
                storedData.Cupboards == null)
                return;

            List<string> invalid =
                new List<string>();

            foreach (
                KeyValuePair<string, StoredCupboard> entry
                in storedData.Cupboards)
            {
                StoredCupboard saved =
                    entry.Value;

                if (saved == null)
                {
                    invalid.Add(entry.Key);
                    continue;
                }

                BuildingPrivlidge cupboard =
                    FindCupboardById(entry.Key);

                if (cupboard == null)
                {
                    invalid.Add(entry.Key);
                    continue;
                }

                if (cupboard.IsDestroyed)
                {
                    invalid.Add(entry.Key);
                    continue;
                }

                DateTime activation =
                    new DateTime(
                        saved.ActivationStartTicks,
                        DateTimeKind.Utc
                    );

                DateTime end =
                    new DateTime(
                        saved.ProtectionEndTicks,
                        DateTimeKind.Utc
                    );

                if (saved.ProtectionActive)
                {
                    if (end <= DateTime.UtcNow)
                    {
                        invalid.Add(entry.Key);

                        EnableTurretsForCupboard(
                            cupboard
                        );

                        continue;
                    }

                    protectedCupboards.Add(
                        entry.Key
                    );

                    protectionEnd[entry.Key] =
                        end;

                    Puts(
                        "TC " +
                        entry.Key +
                        ": OfflineProtection восстановлена после рестарта."
                    );

                    DisableTurretsForCupboard(
                        cupboard
                    );

                    StartExpirationTimer(
                        cupboard,
                        end
                    );

                    continue;
                }

                if (activation <= DateTime.UtcNow)
                {
                    double delaySeconds =
                        config.ProtectionDelayMinutes * 60.0;

                    DateTime activationEnd =
                        activation.AddSeconds(
                            delaySeconds
                        );

                    if (activationEnd <= DateTime.UtcNow)
                    {
                        if (!HasOnlineAuthorizedPlayer(
                            cupboard))
                        {
                            EnableProtection(
                                cupboard
                            );
                        }
                        else
                        {
                            invalid.Add(entry.Key);
                        }

                        continue;
                    }

                    activationStart[entry.Key] =
                        activation;

                    StartActivationTimer(
                        cupboard,
                        activation
                    );
                }
            }

            foreach (string id in invalid)
            {
                storedData.Cupboards.Remove(id);
            }

            if (invalid.Count > 0)
                SaveData();
        }

        private BuildingPrivlidge FindCupboardById(
            string cupboardId)
        {
            if (string.IsNullOrEmpty(cupboardId))
                return null;

            foreach (
                BaseNetworkable networkable
                in BaseNetworkable.serverEntities)
            {
                BuildingPrivlidge cupboard =
                    networkable as BuildingPrivlidge;

                if (cupboard == null)
                    continue;

                if (cupboard.IsDestroyed)
                    continue;

                if (GetCupboardId(cupboard) ==
                    cupboardId)
                {
                    return cupboard;
                }
            }

            return null;
        }

        #endregion

        #region Server

        private void CheckAllCupboards()
        {
            if (!config.Enabled)
                return;

            foreach (
                BaseNetworkable networkable
                in BaseNetworkable.serverEntities)
            {
                BuildingPrivlidge cupboard =
                    networkable as BuildingPrivlidge;

                if (cupboard == null)
                    continue;

                if (cupboard.IsDestroyed)
                    continue;

                UpdateCupboard(cupboard);
            }
        }

        #endregion

        #region Player Events

        private void OnPlayerInit(
            BasePlayer player)
        {
            if (player == null)
                return;

            SchedulePlayerOnlineCheck(player);
        }

        private void OnPlayerConnected(
            BasePlayer player)
        {
            if (player == null)
                return;

            SchedulePlayerOnlineCheck(player);
        }

        private void SchedulePlayerOnlineCheck(
            BasePlayer player)
        {
            timer.Once(
                2f,
                () =>
                {
                    if (player == null ||
                        !player.IsConnected)
                        return;

                    ProcessPlayerLogin(player);
                }
            );

            timer.Once(
                5f,
                () =>
                {
                    if (player == null ||
                        !player.IsConnected)
                        return;

                    ProcessPlayerLogin(player);
                }
            );

            timer.Once(
                10f,
                () =>
                {
                    if (player == null ||
                        !player.IsConnected)
                        return;

                    ProcessPlayerLogin(player);
                }
            );
        }

        private void ProcessPlayerLogin(
            BasePlayer player)
        {
            if (player == null ||
                !player.IsConnected)
                return;

            UpdateCupboardsForPlayer(player);

            EnableTurretsForOnlinePlayer(
                player
            );
        }

        private void OnPlayerDisconnected(
            BasePlayer player,
            string reason)
        {
            if (player == null)
                return;

            timer.Once(
                2f,
                () =>
                {
                    if (player == null)
                        return;

                    UpdateCupboardsForPlayer(
                        player
                    );
                }
            );
        }

        #endregion

        #region Cupboard

        private void UpdateCupboardsForPlayer(
            BasePlayer player)
        {
            if (player == null)
                return;

            foreach (
                BaseNetworkable networkable
                in BaseNetworkable.serverEntities)
            {
                BuildingPrivlidge cupboard =
                    networkable as BuildingPrivlidge;

                if (cupboard == null)
                    continue;

                if (cupboard.IsDestroyed)
                    continue;

                if (!IsPlayerAuthorized(
                    cupboard,
                    player.userID))
                    continue;

                UpdateCupboard(cupboard);
            }
        }

        private void UpdateCupboard(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return;

            string cupboardId =
                GetCupboardId(cupboard);

            if (string.IsNullOrEmpty(cupboardId))
                return;

            if (!config.Enabled)
            {
                CancelActivationTimer(
                    cupboardId
                );

                CancelExpirationTimer(
                    cupboardId
                );

                protectedCupboards.Remove(
                    cupboardId
                );

                protectionEnd.Remove(
                    cupboardId
                );

                activationStart.Remove(
                    cupboardId
                );

                RemoveCupboardState(
                    cupboardId
                );

                EnableTurretsForCupboard(
                    cupboard
                );

                return;
            }

            bool online =
                HasOnlineAuthorizedPlayer(
                    cupboard
                );

            if (online)
            {
                bool wasProtected =
                    protectedCupboards.Remove(
                        cupboardId
                    );

                CancelActivationTimer(
                    cupboardId
                );

                CancelExpirationTimer(
                    cupboardId
                );

                protectionEnd.Remove(
                    cupboardId
                );

                activationStart.Remove(
                    cupboardId
                );

                RemoveCupboardState(
                    cupboardId
                );

                EnableTurretsForCupboard(
                    cupboard
                );

                if (wasProtected)
                {
                    SendMessageToAuthorizedPlayers(
                        cupboard,
                        config.ProtectionDisabledMessage
                    );
                }

                return;
            }

            if (protectedCupboards.Contains(
                cupboardId))
            {
                DisableTurretsForCupboard(
                    cupboard
                );

                return;
            }

            if (activationTimers.ContainsKey(
                cupboardId))
                return;

            StartActivationTimer(
                cupboard
            );
        }

        #endregion

        #region Activation

        private void StartActivationTimer(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return;

            string cupboardId =
                GetCupboardId(cupboard);

            if (string.IsNullOrEmpty(cupboardId))
                return;

            DateTime start =
                DateTime.UtcNow;

            StartActivationTimer(
                cupboard,
                start
            );
        }

        private void StartActivationTimer(
            BuildingPrivlidge cupboard,
            DateTime start)
        {
            if (cupboard == null)
                return;

            string cupboardId =
                GetCupboardId(cupboard);

            if (string.IsNullOrEmpty(cupboardId))
                return;

            if (activationTimers.ContainsKey(
                cupboardId))
                return;

            DateTime activationEnd =
                start.AddMinutes(
                    config.ProtectionDelayMinutes
                );

            activationStart[cupboardId] =
                start;

            SaveCupboardState(
                cupboardId,
                start,
                DateTime.MinValue,
                false
            );

            double seconds =
                (
                    activationEnd -
                    DateTime.UtcNow
                ).TotalSeconds;

            if (seconds <= 0)
            {
                if (!HasOnlineAuthorizedPlayer(
                    cupboard))
                {
                    EnableProtection(
                        cupboard
                    );
                }

                return;
            }

            Puts(
                "TC " +
                cupboardId +
                ": защита включится через " +
                FormatTime(
                    TimeSpan.FromSeconds(
                        seconds
                    )
                ) +
                "."
            );

            activationTimers[cupboardId] =
                timer.Once(
                    (float)seconds,
                    () =>
                    {
                        activationTimers.Remove(
                            cupboardId
                        );

                        activationStart.Remove(
                            cupboardId
                        );

                        if (cupboard == null ||
                            cupboard.IsDestroyed)
                            return;

                        if (!config.Enabled)
                            return;

                        if (HasOnlineAuthorizedPlayer(
                            cupboard))
                        {
                            RemoveCupboardState(
                                cupboardId
                            );

                            return;
                        }

                        EnableProtection(
                            cupboard
                        );
                    }
                );
        }

        private void EnableProtection(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return;

            if (cupboard.IsDestroyed)
                return;

            if (HasOnlineAuthorizedPlayer(
                cupboard))
                return;

            string cupboardId =
                GetCupboardId(cupboard);

            if (string.IsNullOrEmpty(cupboardId))
                return;

            protectedCupboards.Add(
                cupboardId
            );

            activationStart.Remove(
                cupboardId
            );

            DateTime end =
                DateTime.UtcNow.AddHours(
                    config.ProtectionDurationHours
                );

            protectionEnd[cupboardId] =
                end;

            SaveCupboardState(
                cupboardId,
                DateTime.MinValue,
                end,
                true
            );

            Puts(
                "TC " +
                cupboardId +
                ": OfflineProtection включена на " +
                config.ProtectionDurationHours +
                " часов."
            );

            DisableTurretsForCupboard(
                cupboard
            );

            SendMessageToAuthorizedPlayers(
                cupboard,
                config.ProtectionEnabledMessage
            );

            StartExpirationTimer(
                cupboard,
                end
            );
        }

        #endregion

        #region Expiration

        private void StartExpirationTimer(
            BuildingPrivlidge cupboard,
            DateTime endTime)
        {
            if (cupboard == null)
                return;

            string cupboardId =
                GetCupboardId(cupboard);

            if (string.IsNullOrEmpty(cupboardId))
                return;

            CancelExpirationTimer(
                cupboardId
            );

            double seconds =
                (
                    endTime -
                    DateTime.UtcNow
                ).TotalSeconds;

            if (seconds <= 0)
            {
                DisableProtectionByExpiration(
                    cupboard
                );

                return;
            }

            expirationTimers[cupboardId] =
                timer.Once(
                    (float)seconds,
                    () =>
                    {
                        expirationTimers.Remove(
                            cupboardId
                        );

                        if (cupboard == null ||
                            cupboard.IsDestroyed)
                            return;

                        DisableProtectionByExpiration(
                            cupboard
                        );
                    }
                );
        }

        private void DisableProtectionByExpiration(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return;

            string cupboardId =
                GetCupboardId(cupboard);

            if (string.IsNullOrEmpty(cupboardId))
                return;

            protectedCupboards.Remove(
                cupboardId
            );

            protectionEnd.Remove(
                cupboardId
            );

            activationStart.Remove(
                cupboardId
            );

            RemoveCupboardState(
                cupboardId
            );

            EnableTurretsForCupboard(
                cupboard
            );

            Puts(
                "TC " +
                cupboardId +
                ": 24 часа защиты закончились."
            );

            SendMessageToAuthorizedPlayers(
                cupboard,
                config.ProtectionExpiredMessage
            );

            if (!HasOnlineAuthorizedPlayer(
                cupboard))
            {
                StartActivationTimer(
                    cupboard
                );
            }
        }

        #endregion

        #region Turrets

        private void DisableTurretsForCupboard(
            BuildingPrivlidge cupboard)
        {
            if (!config.DisableAutoTurrets)
                return;

            if (cupboard == null)
                return;

            foreach (
                BaseNetworkable networkable
                in BaseNetworkable.serverEntities)
            {
                AutoTurret turret =
                    networkable as AutoTurret;

                if (turret == null)
                    continue;

                if (turret.IsDestroyed)
                    continue;

                if (turret.net == null)
                    continue;

                BuildingPrivlidge turretCupboard =
                    turret.GetBuildingPrivilege();

                if (turretCupboard == null)
                    continue;

                if (turretCupboard != cupboard)
                    continue;

                ulong turretId =
                    turret.net.ID.Value;

                if (turret.IsOnline())
                {
                    turret.SetIsOnline(false);
                    turret.SendNetworkUpdateImmediate();

                    disabledTurrets.Add(
                        turretId
                    );

                    Puts(
                        "AutoTurret " +
                        turretId +
                        " отключена из-за OfflineProtection."
                    );
                }
            }
        }

        private void EnableTurretsForCupboard(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return;

            foreach (
                BaseNetworkable networkable
                in BaseNetworkable.serverEntities)
            {
                AutoTurret turret =
                    networkable as AutoTurret;

                if (turret == null)
                    continue;

                if (turret.IsDestroyed)
                    continue;

                if (turret.net == null)
                    continue;

                BuildingPrivlidge turretCupboard =
                    turret.GetBuildingPrivilege();

                if (turretCupboard == null)
                    continue;

                if (turretCupboard != cupboard)
                    continue;

                ulong turretId =
                    turret.net.ID.Value;

                // Включаем турель, если её отключил плагин.
                // Также это гарантирует включение турелей после входа.
                if (disabledTurrets.Contains(turretId) ||
                    !turret.IsOnline())
                {
                    turret.SetIsOnline(true);
                    turret.SendNetworkUpdateImmediate();

                    Puts(
                        "AutoTurret " +
                        turretId +
                        " снова включена."
                    );
                }

                disabledTurrets.Remove(
                    turretId
                );
            }
        }

        private void EnableTurretsForOnlinePlayer(
            BasePlayer player)
        {
            if (player == null ||
                !player.IsConnected)
                return;

            foreach (
                BaseNetworkable networkable
                in BaseNetworkable.serverEntities)
            {
                BuildingPrivlidge cupboard =
                    networkable as BuildingPrivlidge;

                if (cupboard == null)
                    continue;

                if (cupboard.IsDestroyed)
                    continue;

                if (!IsPlayerAuthorized(
                    cupboard,
                    player.userID))
                    continue;

                string cupboardId =
                    GetCupboardId(cupboard);

                if (string.IsNullOrEmpty(cupboardId))
                    continue;

                // Игрок вошёл.
                // Снимаем OfflineProtection.
                protectedCupboards.Remove(
                    cupboardId
                );

                CancelActivationTimer(
                    cupboardId
                );

                CancelExpirationTimer(
                    cupboardId
                );

                protectionEnd.Remove(
                    cupboardId
                );

                activationStart.Remove(
                    cupboardId
                );

                RemoveCupboardState(
                    cupboardId
                );

                // Гарантированно включаем турели.
                EnableTurretsForCupboard(
                    cupboard
                );
            }
        }

        #endregion

        #region Timers

        private void CancelActivationTimer(
            string cupboardId)
        {
            if (string.IsNullOrEmpty(cupboardId))
                return;

            Timer existing;

            if (activationTimers.TryGetValue(
                cupboardId,
                out existing))
            {
                if (existing != null)
                    existing.Destroy();

                activationTimers.Remove(
                    cupboardId
                );
            }

            activationStart.Remove(
                cupboardId
            );
        }

        private void CancelExpirationTimer(
            string cupboardId)
        {
            if (string.IsNullOrEmpty(cupboardId))
                return;

            Timer existing;

            if (expirationTimers.TryGetValue(
                cupboardId,
                out existing))
            {
                if (existing != null)
                    existing.Destroy();

                expirationTimers.Remove(
                    cupboardId
                );
            }
        }

        #endregion

        #region Players

        private bool HasOnlineAuthorizedPlayer(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return false;

            if (cupboard.authorizedPlayers == null)
                return false;

            foreach (
                ulong userId
                in cupboard.authorizedPlayers)
            {
                if (userId == 0UL)
                    continue;

                BasePlayer player =
                    BasePlayer.FindByID(userId);

                if (player == null)
                    continue;

                if (!player.IsConnected)
                    continue;

                if (config.IgnoreAdmins &&
                    player.IsAdmin)
                    continue;

                return true;
            }

            return false;
        }

        private bool IsPlayerAuthorized(
            BuildingPrivlidge cupboard,
            ulong userId)
        {
            if (cupboard == null)
                return false;

            if (cupboard.authorizedPlayers == null)
                return false;

            return cupboard.authorizedPlayers.Contains(
                userId
            );
        }

        #endregion

        #region Damage

        private object OnEntityTakeDamage(
            BaseCombatEntity entity,
            HitInfo info)
        {
            if (!config.Enabled)
                return null;

            if (entity == null)
                return null;

            if (info == null)
                return null;

            if (!IsProtectedEntity(entity))
                return null;

            BuildingPrivlidge cupboard =
                entity.GetBuildingPrivilege();

            if (cupboard == null)
                return null;

            string cupboardId =
                GetCupboardId(cupboard);

            if (string.IsNullOrEmpty(cupboardId))
                return null;

            if (!protectedCupboards.Contains(
                cupboardId))
                return null;

            if (HasOnlineAuthorizedPlayer(
                cupboard))
            {
                protectedCupboards.Remove(
                    cupboardId
                );

                CancelExpirationTimer(
                    cupboardId
                );

                CancelActivationTimer(
                    cupboardId
                );

                protectionEnd.Remove(
                    cupboardId
                );

                RemoveCupboardState(
                    cupboardId
                );

                EnableTurretsForCupboard(
                    cupboard
                );

                return null;
            }

            BasePlayer attacker =
                info.InitiatorPlayer;

            if (attacker != null &&
                config.IgnoreAdmins &&
                attacker.IsAdmin)
            {
                return null;
            }

            if (attacker != null &&
                config.ShowMessage)
            {
                attacker.ChatMessage(
                    config.ProtectionMessage
                );
            }

            if (info.damageTypes != null)
            {
                info.damageTypes.ScaleAll(
                    0f
                );
            }

            return true;
        }

        private bool IsProtectedEntity(
            BaseCombatEntity entity)
        {
            if (entity == null)
                return false;

            if (entity is AutoTurret)
                return false;

            if (entity is BuildingBlock)
                return config.ProtectBuildingBlocks;

            if (entity is Door)
                return config.ProtectDoors;

            if (entity is BuildingPrivlidge)
                return config.ProtectToolCupboard;

            return config.ProtectDeployables;
        }

        #endregion

        #region Messages

        private void SendMessageToAuthorizedPlayers(
            BuildingPrivlidge cupboard,
            string message)
        {
            if (!config.ShowMessage)
                return;

            if (cupboard == null)
                return;

            if (cupboard.authorizedPlayers == null)
                return;

            foreach (
                ulong userId
                in cupboard.authorizedPlayers)
            {
                BasePlayer player =
                    BasePlayer.FindByID(userId);

                if (player == null)
                    continue;

                if (!player.IsConnected)
                    continue;

                player.ChatMessage(
                    message
                );
            }
        }

        #endregion

        #region Command

        [ChatCommand("offlineprotect")]
        private void OfflineProtectCommand(
            BasePlayer player,
            string command,
            string[] args)
        {
            if (player == null)
                return;

            if (!player.IsAdmin)
            {
                player.ChatMessage(
                    "У тебя нет прав для этой команды."
                );

                return;
            }

            if (args == null ||
                args.Length == 0)
            {
                player.ChatMessage(
                    "Использование: /offlineprotect status"
                );

                player.ChatMessage(
                    "/offlineprotect on"
                );

                player.ChatMessage(
                    "/offlineprotect off"
                );

                return;
            }

            string option =
                args[0].ToLower();

            if (option == "on")
            {
                config.Enabled = true;

                SaveConfig();

                CheckAllCupboards();

                player.ChatMessage(
                    "🟢 OfflineProtection включена."
                );

                return;
            }

            if (option == "off")
            {
                config.Enabled = false;

                SaveConfig();

                foreach (
                    BaseNetworkable networkable
                    in BaseNetworkable.serverEntities)
                {
                    BuildingPrivlidge cupboard =
                        networkable as BuildingPrivlidge;

                    if (cupboard == null)
                        continue;

                    string id =
                        GetCupboardId(cupboard);

                    CancelActivationTimer(id);
                    CancelExpirationTimer(id);

                    protectedCupboards.Remove(id);
                    protectionEnd.Remove(id);
                    activationStart.Remove(id);

                    RemoveCupboardState(id);

                    EnableTurretsForCupboard(
                        cupboard
                    );
                }

                player.ChatMessage(
                    "🔴 OfflineProtection выключена."
                );

                return;
            }

            if (option == "status")
            {
                BuildingPrivlidge cupboard =
                    player.GetBuildingPrivilege();

                if (cupboard == null)
                {
                    player.ChatMessage(
                        "⚠️ Ты должен находиться возле своего TC."
                    );

                    return;
                }

                if (!config.Enabled)
                {
                    player.ChatMessage(
                        "🔴 OfflineProtection: ВЫКЛЮЧЕНА."
                    );

                    return;
                }

                string cupboardId =
                    GetCupboardId(cupboard);

                if (protectedCupboards.Contains(
                    cupboardId))
                {
                    DateTime end;

                    if (protectionEnd.TryGetValue(
                        cupboardId,
                        out end))
                    {
                        TimeSpan remaining =
                            end - DateTime.UtcNow;

                        if (remaining.TotalSeconds < 0)
                        {
                            remaining =
                                TimeSpan.Zero;
                        }

                        player.ChatMessage(
                            "🛡️ БАЗА ЗАЩИЩЕНА"
                        );

                        player.ChatMessage(
                            "До окончания защиты: " +
                            FormatTime(
                                remaining
                            )
                        );

                        if (config.DisableAutoTurrets)
                        {
                            player.ChatMessage(
                                "🔫 AutoTurret: ОТКЛЮЧЕНЫ"
                            );
                        }
                    }
                    else
                    {
                        player.ChatMessage(
                            "🛡️ БАЗА ЗАЩИЩЕНА"
                        );
                    }

                    return;
                }

                if (HasOnlineAuthorizedPlayer(
                    cupboard))
                {
                    player.ChatMessage(
                        "🔓 БАЗА НЕ ЗАЩИЩЕНА"
                    );

                    player.ChatMessage(
                        "Авторизованный игрок сейчас онлайн."
                    );

                    return;
                }

                DateTime start;

                if (activationStart.TryGetValue(
                    cupboardId,
                    out start))
                {
                    double elapsed =
                        (
                            DateTime.UtcNow -
                            start
                        ).TotalSeconds;

                    double total =
                        config.ProtectionDelayMinutes *
                        60.0;

                    double remaining =
                        Math.Max(
                            0,
                            total - elapsed
                        );

                    player.ChatMessage(
                        "⏱️ ЗАЩИТА ЕЩЁ НЕ ВКЛЮЧЕНА"
                    );

                    player.ChatMessage(
                        "До включения: " +
                        FormatTime(
                            TimeSpan.FromSeconds(
                                remaining
                            )
                        )
                    );

                    return;
                }

                player.ChatMessage(
                    "⏱️ Ожидание защиты ещё не запущено."
                );

                return;
            }

            player.ChatMessage(
                "Использование: /offlineprotect status"
            );

            player.ChatMessage(
                "/offlineprotect on"
            );

            player.ChatMessage(
                "/offlineprotect off"
            );
        }

        #endregion

        #region Helpers

        private string FormatTime(
            TimeSpan time)
        {
            if (time.TotalSeconds <= 0)
                return "0 сек.";

            if (time.TotalHours >= 1)
            {
                return string.Format(
                    "{0} ч. {1} мин.",
                    (int)time.TotalHours,
                    time.Minutes
                );
            }

            if (time.TotalMinutes >= 1)
            {
                return string.Format(
                    "{0} мин. {1} сек.",
                    time.Minutes,
                    time.Seconds
                );
            }

            return string.Format(
                "{0} сек.",
                time.Seconds
            );
        }

        #endregion

        #region Unload

        private void Unload()
        {
            // Включаем только те турели,
            // которые плагин отключил.
            foreach (
                BaseNetworkable networkable
                in BaseNetworkable.serverEntities)
            {
                AutoTurret turret =
                    networkable as AutoTurret;

                if (turret == null)
                    continue;

                if (turret.IsDestroyed)
                    continue;

                if (turret.net == null)
                    continue;

                ulong turretId =
                    turret.net.ID.Value;

                if (!disabledTurrets.Contains(
                    turretId))
                    continue;

                turret.SetIsOnline(true);
                turret.SendNetworkUpdateImmediate();
            }

            foreach (
                Timer timerObject
                in activationTimers.Values)
            {
                if (timerObject != null)
                    timerObject.Destroy();
            }

            foreach (
                Timer timerObject
                in expirationTimers.Values)
            {
                if (timerObject != null)
                    timerObject.Destroy();
            }

            activationTimers.Clear();
            expirationTimers.Clear();
            activationStart.Clear();
            protectionEnd.Clear();
            protectedCupboards.Clear();
            disabledTurrets.Clear();
        }

        #endregion
    }
}