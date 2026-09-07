using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MetalicRustAutoCodeLock", "MetalicRust", "1.0.6")]
    [Description("Единый код Code Lock для каждого Tool Cupboard с автоматическим доступом авторизованных игроков.")]
    public class MetalicRustAutoCodeLock : RustPlugin
    {
        #region Configuration

        private PluginConfig config;

        private class PluginConfig
        {
            [JsonProperty("Минимальная длина кода")]
            public int MinCodeLength = 4;

            [JsonProperty("Максимальная длина кода")]
            public int MaxCodeLength = 8;

            [JsonProperty("Автоматически менять существующие замки")]
            public bool UpdateExistingLocks = true;

            [JsonProperty("Автоматически устанавливать код на новые замки")]
            public bool UpdateNewLocks = true;

            [JsonProperty("Автоматически открывать замки авторизованным в TC")]
            public bool AutoOpenAuthorizedPlayers = true;

            [JsonProperty("Радиус резервного поиска TC")]
            public float SearchRadius = 50f;

            [JsonProperty("Показывать сообщения игроку")]
            public bool ShowMessages = true;
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<PluginConfig>();

                if (config == null)
                    throw new Exception("Config is null");
            }
            catch
            {
                PrintWarning(
                    "Ошибка конфигурации. Создаётся новая конфигурация."
                );

                LoadDefaultConfig();
            }

            if (config.MinCodeLength < 1)
                config.MinCodeLength = 4;

            if (config.MaxCodeLength < config.MinCodeLength)
                config.MaxCodeLength = config.MinCodeLength;

            if (config.SearchRadius < 1f)
                config.SearchRadius = 50f;

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        private StoredData storedData;

        private class StoredData
        {
            public Dictionary<ulong, CupboardData> Cupboards =
                new Dictionary<ulong, CupboardData>();
        }

        private class CupboardData
        {
            public ulong CupboardId;
            public string Code;
            public bool Enabled = true;
        }

        private void LoadData()
        {
            try
            {
                storedData =
                    Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                storedData = new StoredData();
            }

            if (storedData == null)
                storedData = new StoredData();

            if (storedData.Cupboards == null)
            {
                storedData.Cupboards =
                    new Dictionary<ulong, CupboardData>();
            }
        }

        private void SaveData()
        {
            if (storedData == null)
                storedData = new StoredData();

            Interface.Oxide.DataFileSystem.WriteObject(
                Name,
                storedData
            );
        }

        #endregion

        #region Reflection

        private FieldInfo codeField;
        private FieldInfo hasCodeField;

        #endregion

        #region Initialization

        private void Init()
        {
            LoadData();

            codeField = typeof(CodeLock).GetField(
                "code",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic
            );

            hasCodeField = typeof(CodeLock).GetField(
                "hasCode",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic
            );

            if (codeField == null)
            {
                PrintError(
                    "Не удалось найти поле CodeLock.code. Плагин отключён."
                );

                return;
            }

            permission.RegisterPermission(
                "metalicrust.autocodelock.admin",
                this
            );
        }

        private void OnServerInitialized()
        {
            if (codeField == null)
                return;

            timer.Once(5f, () =>
            {
                UpdateAllKnownCupboards();
            });
        }

        #endregion

        #region Chat Command

        [ChatCommand("tccode")]
        private void CommandTcCode(
            BasePlayer player,
            string command,
            string[] args)
        {
            if (player == null)
                return;

            BuildingPrivlidge cupboard =
                GetPlayerCupboard(player);

            if (cupboard == null)
            {
                SendReply(
                    player,
                    "<color=#ff4444>✘</color> Вы должны находиться в зоне своего Tool Cupboard."
                );

                return;
            }

            bool admin =
                permission.UserHasPermission(
                    player.UserIDString,
                    "metalicrust.autocodelock.admin"
                );

            if (!IsAuthorized(cupboard, player) && !admin)
            {
                SendReply(
                    player,
                    "<color=#ff4444>✘</color> Вы не авторизованы в этом шкафу."
                );

                return;
            }

            ulong cupboardId =
                GetCupboardId(cupboard);

            CupboardData data =
                GetCupboardData(cupboardId);

            if (args == null || args.Length == 0)
            {
                if (data == null ||
                    !data.Enabled ||
                    string.IsNullOrEmpty(data.Code))
                {
                    SendReply(
                        player,
                        "<color=#ffaa00>ℹ</color> Для этого TC код ещё не установлен.\n" +
                        "Используйте: <color=#55ff55>/tccode 1234</color>"
                    );

                    return;
                }

                SendReply(
                    player,
                    $"<color=#55ff55>🔐 Код этого TC:</color> " +
                    $"<color=#ffffff>{data.Code}</color>"
                );

                return;
            }

            string code = args[0];

            if (code.Equals(
                "off",
                StringComparison.OrdinalIgnoreCase))
            {
                if (data == null)
                {
                    data = new CupboardData
                    {
                        CupboardId = cupboardId
                    };

                    storedData.Cupboards[cupboardId] = data;
                }

                data.Enabled = false;
                data.Code = null;

                SaveData();

                ClearAutoWhitelistForCupboard(cupboard);

                SendReply(
                    player,
                    "<color=#ffaa00>🔓 AutoCodeLock отключён для этого TC.</color>"
                );

                return;
            }

            if (!IsValidCode(code))
            {
                SendReply(
                    player,
                    $"<color=#ff4444>✘</color> Код должен состоять только из цифр " +
                    $"и содержать от {config.MinCodeLength} до " +
                    $"{config.MaxCodeLength} цифр."
                );

                return;
            }

            if (data == null)
            {
                data = new CupboardData
                {
                    CupboardId = cupboardId
                };

                storedData.Cupboards[cupboardId] = data;
            }

            data.Code = code;
            data.Enabled = true;

            SaveData();

            int found;
            int changed;

            UpdateCupboardLocks(
                cupboard,
                code,
                out found,
                out changed
            );

            if (config.ShowMessages)
            {
                SendReply(
                    player,
                    $"<color=#55ff55>🔐 Код TC установлен:</color> " +
                    $"<color=#ffffff>{code}</color>\n" +
                    $"<color=#aaaaaa>Найдено замков:</color> {found}\n" +
                    $"<color=#aaaaaa>Изменено замков:</color> {changed}"
                );
            }

            Puts(
                $"TC {cupboardId}: игрок {player.displayName} " +
                $"({player.userID}) установил код {code}. " +
                $"Найдено замков: {found}. Изменено: {changed}."
            );
        }

        #endregion

        #region Automatic Access

        /*
         * В Rust CodeLock при попытке открыть дверь
         * вызывает CanUseLockedEntity(player, this).
         *
         * Если hook возвращает true — Rust сразу разрешает
         * использование и НЕ требует ввода кода.
         *
         * Если возвращаем null — работает стандартная
         * проверка whitelist/code.
         */
        private object CanUseLockedEntity(
            BasePlayer player,
            BaseLock baseLock)
        {
            if (!config.AutoOpenAuthorizedPlayers)
                return null;

            if (player == null ||
                baseLock == null)
                return null;

            CodeLock codeLock =
                baseLock as CodeLock;

            if (codeLock == null)
                return null;

            BuildingPrivlidge cupboard =
                GetCupboardForEntity(codeLock);

            if (cupboard == null)
                return null;

            CupboardData data =
                GetCupboardData(
                    GetCupboardId(cupboard)
                );

            /*
             * Если для этого TC нет активного кода,
             * не вмешиваемся в стандартный Rust.
             */
            if (data == null ||
                !data.Enabled ||
                string.IsNullOrEmpty(data.Code))
                return null;

            bool admin =
                permission.UserHasPermission(
                    player.UserIDString,
                    "metalicrust.autocodelock.admin"
                );

            /*
             * Администратор имеет полный доступ.
             */
            if (admin)
                return true;

            /*
             * Самое главное:
             * игрок авторизован в TC → дверь открывается
             * БЕЗ ВВОДА КОДА.
             */
            if (IsAuthorized(cupboard, player))
                return true;

            /*
             * Посторонний игрок:
             * стандартная система CodeLock,
             * требуется код.
             */
            return null;
        }

        #endregion

        #region Code Lock Hooks

        private void OnEntitySpawned(CodeLock codeLock)
        {
            if (codeLock == null ||
                !config.UpdateNewLocks)
                return;

            timer.Once(0.5f, () =>
            {
                ApplyCodeToLock(codeLock);
            });
        }

        private object CanChangeCode(
            BasePlayer player,
            CodeLock codeLock,
            string newCode,
            bool isGuestCode)
        {
            if (player == null ||
                codeLock == null ||
                isGuestCode)
                return null;

            BuildingPrivlidge cupboard =
                GetCupboardForEntity(codeLock);

            if (cupboard == null)
                return null;

            CupboardData data =
                GetCupboardData(
                    GetCupboardId(cupboard)
                );

            if (data == null ||
                !data.Enabled ||
                string.IsNullOrEmpty(data.Code))
                return null;

            bool admin =
                permission.UserHasPermission(
                    player.UserIDString,
                    "metalicrust.autocodelock.admin"
                );

            if (!IsAuthorized(cupboard, player) && !admin)
                return null;

            if (newCode == data.Code)
                return null;

            NextTick(() =>
            {
                if (codeLock == null ||
                    codeLock.IsDestroyed)
                    return;

                SetLockCode(
                    codeLock,
                    data.Code
                );

                SyncLockWhitelist(
                    codeLock,
                    cupboard
                );
            });

            if (config.ShowMessages)
            {
                SendReply(
                    player,
                    $"<color=#ffaa00>🔐 Код замка управляется через TC.</color>\n" +
                    $"Текущий код: <color=#ffffff>{data.Code}</color>"
                );
            }

            return false;
        }

        #endregion

        #region Cupboard Authorization

        /*
         * Вызывается при авторизации игрока в TC.
         */
        private void OnCupboardAuthorize(
            BuildingPrivlidge privilege,
            BasePlayer player)
        {
            if (privilege == null ||
                player == null)
                return;

            if (!config.AutoOpenAuthorizedPlayers)
                return;

            timer.Once(0.15f, () =>
            {
                if (privilege == null ||
                    privilege.IsDestroyed)
                    return;

                SyncCupboardLockWhitelist(privilege);
            });
        }

        /*
         * Вызывается при удалении игрока из TC.
         */
        private void OnCupboardDeauthorize(
            BuildingPrivlidge privilege,
            BasePlayer player)
        {
            if (privilege == null)
                return;

            if (!config.AutoOpenAuthorizedPlayers)
                return;

            timer.Once(0.15f, () =>
            {
                if (privilege == null ||
                    privilege.IsDestroyed)
                    return;

                SyncCupboardLockWhitelist(privilege);
            });
        }

        /*
         * Очистка списка авторизованных TC.
         */
        private void OnCupboardClearList(
            BuildingPrivlidge privilege,
            BasePlayer player)
        {
            if (privilege == null)
                return;

            if (!config.AutoOpenAuthorizedPlayers)
                return;

            timer.Once(0.15f, () =>
            {
                if (privilege == null ||
                    privilege.IsDestroyed)
                    return;

                SyncCupboardLockWhitelist(privilege);
            });
        }

        #endregion

        #region Cupboard Detection

        private BuildingPrivlidge GetPlayerCupboard(
            BasePlayer player)
        {
            if (player == null)
                return null;

            BuildingPrivlidge cupboard =
                player.GetBuildingPrivilege();

            if (cupboard == null)
                return null;

            bool admin =
                permission.UserHasPermission(
                    player.UserIDString,
                    "metalicrust.autocodelock.admin"
                );

            if (!IsAuthorized(cupboard, player) && !admin)
                return null;

            return cupboard;
        }

        private BuildingPrivlidge GetCupboardForEntity(
            BaseEntity entity)
        {
            if (entity == null)
                return null;

            /*
             * Первый вариант:
             * непосредственно BuildingPrivilege объекта.
             */
            try
            {
                BuildingPrivlidge privilege =
                    entity.GetBuildingPrivilege();

                if (privilege != null)
                    return privilege;
            }
            catch
            {
            }

            /*
             * Второй вариант:
             * поднимаемся по parent chain.
             *
             * Для CodeLock родителем обычно является
             * дверь / контейнер.
             */
            BaseEntity current = entity;

            for (int i = 0; i < 8; i++)
            {
                if (current == null)
                    break;

                try
                {
                    BuildingPrivlidge privilege =
                        current.GetBuildingPrivilege();

                    if (privilege != null)
                        return privilege;
                }
                catch
                {
                }

                try
                {
                    current =
                        current.GetParentEntity();
                }
                catch
                {
                    break;
                }
            }

            /*
             * Последний резервный вариант —
             * поиск ближайшего TC.
             */
            return FindNearestCupboard(entity);
        }

        private BuildingPrivlidge FindNearestCupboard(
            BaseEntity entity)
        {
            if (entity == null)
                return null;

            BuildingPrivlidge closest = null;

            float closestDistance =
                float.MaxValue;

            BuildingPrivlidge[] cupboards =
                UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>();

            foreach (BuildingPrivlidge cupboard in cupboards)
            {
                if (cupboard == null ||
                    cupboard.IsDestroyed)
                    continue;

                float distance =
                    Vector3.Distance(
                        cupboard.transform.position,
                        entity.transform.position
                    );

                if (distance > config.SearchRadius)
                    continue;

                if (distance < closestDistance)
                {
                    closest =
                        cupboard;

                    closestDistance =
                        distance;
                }
            }

            return closest;
        }

        private bool IsAuthorized(
            BuildingPrivlidge cupboard,
            BasePlayer player)
        {
            if (cupboard == null ||
                player == null)
                return false;

            try
            {
                if (cupboard.authorizedPlayers == null)
                    return false;

                return cupboard.authorizedPlayers.Any(
                    userid => userid == player.userID
                );
            }
            catch
            {
                return false;
            }
        }

        private ulong GetCupboardId(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null)
                return 0;

            if (cupboard.net != null)
                return cupboard.net.ID.Value;

            return (ulong)cupboard.GetInstanceID();
        }

        #endregion

        #region Data Helpers

        private CupboardData GetCupboardData(
            ulong cupboardId)
        {
            if (cupboardId == 0 ||
                storedData == null ||
                storedData.Cupboards == null)
                return null;

            CupboardData data;

            if (!storedData.Cupboards.TryGetValue(
                cupboardId,
                out data))
            {
                return null;
            }

            return data;
        }

        #endregion

        #region Lock Management

        private void ApplyCodeToLock(
            CodeLock codeLock)
        {
            if (codeLock == null ||
                codeLock.IsDestroyed)
                return;

            BuildingPrivlidge cupboard =
                GetCupboardForEntity(codeLock);

            if (cupboard == null)
                return;

            CupboardData data =
                GetCupboardData(
                    GetCupboardId(cupboard)
                );

            if (data == null ||
                !data.Enabled ||
                string.IsNullOrEmpty(data.Code))
                return;

            SetLockCode(
                codeLock,
                data.Code
            );

            SyncLockWhitelist(
                codeLock,
                cupboard
            );
        }

        private void UpdateCupboardLocks(
            BuildingPrivlidge cupboard,
            string code,
            out int found,
            out int changed)
        {
            found = 0;
            changed = 0;

            if (cupboard == null ||
                string.IsNullOrEmpty(code))
                return;

            try
            {
                CodeLock[] locks =
                    UnityEngine.Object.FindObjectsOfType<CodeLock>();

                foreach (CodeLock codeLock in locks)
                {
                    if (codeLock == null ||
                        codeLock.IsDestroyed)
                        continue;

                    BuildingPrivlidge lockCupboard =
                        GetCupboardForEntity(codeLock);

                    if (lockCupboard == null)
                        continue;

                    if (lockCupboard != cupboard)
                        continue;

                    found++;

                    if (SetLockCode(
                        codeLock,
                        code))
                    {
                        changed++;
                    }

                    SyncLockWhitelist(
                        codeLock,
                        cupboard
                    );
                }
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка обновления замков TC " +
                    $"{GetCupboardId(cupboard)}: {ex}"
                );
            }
        }

        private void UpdateAllKnownCupboards()
        {
            if (storedData == null ||
                storedData.Cupboards == null)
                return;

            foreach (CupboardData data in
                     storedData.Cupboards.Values.ToList())
            {
                if (data == null ||
                    !data.Enabled ||
                    string.IsNullOrEmpty(data.Code))
                    continue;

                BuildingPrivlidge cupboard =
                    FindCupboard(data.CupboardId);

                if (cupboard == null)
                    continue;

                int found;
                int changed;

                UpdateCupboardLocks(
                    cupboard,
                    data.Code,
                    out found,
                    out changed
                );
            }
        }

        private BuildingPrivlidge FindCupboard(
            ulong id)
        {
            if (id == 0)
                return null;

            BuildingPrivlidge[] cupboards =
                UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>();

            foreach (BuildingPrivlidge cupboard in cupboards)
            {
                if (cupboard == null ||
                    cupboard.IsDestroyed ||
                    cupboard.net == null)
                    continue;

                if (cupboard.net.ID.Value == id)
                    return cupboard;
            }

            return null;
        }

        #endregion

        #region Whitelist

        /*
         * Синхронизация авторизованных TC игроков
         * с whitelist CodeLock.
         */
        private void SyncLockWhitelist(
            CodeLock codeLock,
            BuildingPrivlidge cupboard)
        {
            if (codeLock == null ||
                codeLock.IsDestroyed ||
                cupboard == null)
                return;

            try
            {
                if (codeLock.whitelistPlayers == null)
                {
                    codeLock.whitelistPlayers =
                        new List<ulong>();
                }

                HashSet<ulong> authorizedIds =
                    new HashSet<ulong>();

                if (cupboard.authorizedPlayers != null)
                {
                    foreach (ulong userId in cupboard.authorizedPlayers)
                    {
                        if (userId != 0)
                            authorizedIds.Add(userId);
                    }
                }

                /*
                 * Удаляем игроков, которых больше нет
                 * в авторизации TC.
                 */
                for (int i =
                    codeLock.whitelistPlayers.Count - 1;
                    i >= 0;
                    i--)
                {
                    ulong userId =
                        codeLock.whitelistPlayers[i];

                    if (!authorizedIds.Contains(userId))
                    {
                        codeLock.whitelistPlayers.RemoveAt(i);
                    }
                }

                /*
                 * Добавляем всех авторизованных TC игроков.
                 */
                foreach (ulong userId in authorizedIds)
                {
                    if (!codeLock.whitelistPlayers.Contains(userId))
                    {
                        codeLock.whitelistPlayers.Add(userId);
                    }
                }

                codeLock.SendNetworkUpdateImmediate();
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка синхронизации whitelist CodeLock: {ex.Message}"
                );
            }
        }

        private void SyncCupboardLockWhitelist(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null ||
                cupboard.IsDestroyed)
                return;

            try
            {
                CodeLock[] locks =
                    UnityEngine.Object.FindObjectsOfType<CodeLock>();

                foreach (CodeLock codeLock in locks)
                {
                    if (codeLock == null ||
                        codeLock.IsDestroyed)
                        continue;

                    BuildingPrivlidge lockCupboard =
                        GetCupboardForEntity(codeLock);

                    if (lockCupboard != cupboard)
                        continue;

                    CupboardData data =
                        GetCupboardData(
                            GetCupboardId(cupboard)
                        );

                    if (data == null ||
                        !data.Enabled ||
                        string.IsNullOrEmpty(data.Code))
                        continue;

                    SyncLockWhitelist(
                        codeLock,
                        cupboard
                    );
                }
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка синхронизации замков TC: {ex.Message}"
                );
            }
        }

        private void EnsurePlayerWhitelisted(
            CodeLock codeLock,
            ulong userId)
        {
            if (codeLock == null ||
                codeLock.IsDestroyed ||
                userId == 0)
                return;

            try
            {
                if (codeLock.whitelistPlayers == null)
                {
                    codeLock.whitelistPlayers =
                        new List<ulong>();
                }

                if (!codeLock.whitelistPlayers.Contains(userId))
                {
                    codeLock.whitelistPlayers.Add(userId);

                    codeLock.SendNetworkUpdateImmediate();
                }
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка добавления игрока {userId} в whitelist CodeLock: {ex.Message}"
                );
            }
        }

        private void ClearAutoWhitelistForCupboard(
            BuildingPrivlidge cupboard)
        {
            if (cupboard == null ||
                cupboard.IsDestroyed)
                return;

            try
            {
                CodeLock[] locks =
                    UnityEngine.Object.FindObjectsOfType<CodeLock>();

                foreach (CodeLock codeLock in locks)
                {
                    if (codeLock == null ||
                        codeLock.IsDestroyed)
                        continue;

                    BuildingPrivlidge lockCupboard =
                        GetCupboardForEntity(codeLock);

                    if (lockCupboard != cupboard)
                        continue;

                    if (codeLock.whitelistPlayers != null)
                    {
                        codeLock.whitelistPlayers.Clear();

                        codeLock.SendNetworkUpdateImmediate();
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка очистки whitelist TC: {ex.Message}"
                );
            }
        }

        #endregion

        #region CodeLock

        private bool SetLockCode(
            CodeLock codeLock,
            string code)
        {
            if (codeLock == null ||
                codeField == null ||
                string.IsNullOrEmpty(code))
                return false;

            try
            {
                string oldCode =
                    GetLockCode(codeLock);

                bool codeChanged =
                    oldCode != code;

                codeField.SetValue(
                    codeLock,
                    code
                );

                if (hasCodeField != null)
                {
                    hasCodeField.SetValue(
                        codeLock,
                        true
                    );
                }

                codeLock.SendNetworkUpdateImmediate();

                return codeChanged;
            }
            catch (Exception ex)
            {
                PrintError(
                    $"Ошибка установки кода CodeLock: {ex.Message}"
                );

                return false;
            }
        }

        private string GetLockCode(
            CodeLock codeLock)
        {
            if (codeLock == null ||
                codeField == null)
                return null;

            try
            {
                object value =
                    codeField.GetValue(codeLock);

                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Validation

        private bool IsValidCode(
            string code)
        {
            if (string.IsNullOrEmpty(code))
                return false;

            if (code.Length < config.MinCodeLength ||
                code.Length > config.MaxCodeLength)
                return false;

            for (int i = 0; i < code.Length; i++)
            {
                if (!char.IsDigit(code[i]))
                    return false;
            }

            return true;
        }

        #endregion

        #region Cleanup

        private void Unload()
        {
            SaveData();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        #endregion
    }
}
