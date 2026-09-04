using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MetalicRustAutoUpdater", "MetalicRust", "1.3.1")]
    [Description("Автоматическая проверка и обновление плагинов MetalicRust через uMod.")]
    public class MetalicRustAutoUpdater : RustPlugin
    {
        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Автоматически обновлять плагины")]
            public bool AutoUpdate = false;

            [JsonProperty("Проверять обновления каждые N минут")]
            public float CheckIntervalMinutes = 60f;

            [JsonProperty("Создавать резервную копию .bak")]
            public bool CreateBackup = true;

            [JsonProperty("Уведомлять администраторов")]
            public bool NotifyAdmins = true;

            [JsonProperty("Плагины uMod")]
            public Dictionary<string, string> Plugins =
                new Dictionary<string, string>
                {
                    { "BetterChat", "better-chat" },
                    { "Clans", "clans" },
                    { "Economics", "economics" },
                    { "Friends", "friends" },
                    { "GUIAnnouncements", "gui-announcements" },
                    { "NTeleportation", "nteleportation" },
                    { "Quests", "quests" },
                    { "ServerRewards", "server-rewards" },
                    { "Vanish", "vanish" },
                    { "SkinBox", "skinbox" },
                    { "RaidableBases", "raidable-bases" },
                    { "CopyPaste", "copy-paste" },
                    { "Backpacks", "backpacks" },
                    { "GatherManager", "gather-manager" }
                };

            [JsonProperty("Защищенные плагины MetalicRust")]
            public List<string> ProtectedPlugins =
                new List<string>
                {
                    "MetalicRustAutoUpdater",
                    "MetalicRust OfflineProtection",
                    "MetalicRust TopTime",
                    "InfoMenu",
                    "QuickSmelt"
                };
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
                    throw new Exception("Configuration is null.");
            }
            catch
            {
                PrintWarning(
                    "[AutoUpdater] Ошибка конфигурации. Создаю новую."
                );

                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Variables

        private Timer updateTimer;

        private readonly Dictionary<string, PluginStatus> statuses =
            new Dictionary<string, PluginStatus>(
                StringComparer.OrdinalIgnoreCase
            );

        private class PluginStatus
        {
            public string Name;
            public string Slug;
            public string CurrentVersion;
            public string LatestVersion;
            public bool UpdateAvailable;
            public bool Error;
            public string ErrorMessage;
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadConfig();

            PrintWarning(
                "=============================================="
            );

            PrintWarning(
                "MetalicRust AutoUpdater 1.3.1"
            );

            PrintWarning(
                "Автообновление: " +
                (config.AutoUpdate
                    ? "ВКЛЮЧЕНО"
                    : "ВЫКЛЮЧЕНО")
            );

            PrintWarning(
                "Проверка каждые: " +
                config.CheckIntervalMinutes +
                " минут"
            );

            PrintWarning(
                "=============================================="
            );
        }

        private void OnServerInitialized()
        {
            float interval =
                Mathf.Max(
                    5f,
                    config.CheckIntervalMinutes
                );

            updateTimer = timer.Every(
                interval * 60f,
                delegate
                {
                    CheckAllPlugins(
                        config.AutoUpdate
                    );
                }
            );

            timer.Once(
                30f,
                delegate
                {
                    CheckAllPlugins(false);
                }
            );
        }

        private void Unload()
        {
            if (updateTimer != null)
                updateTimer.Destroy();
        }

        #endregion

        #region Console

        [ConsoleCommand("mrupdate")]
        private void ConsoleCommand(
            ConsoleSystem.Arg arg)
        {
            if (arg == null)
                return;

            string command =
                arg.GetString(
                    0,
                    "status"
                ).ToLower();

            switch (command)
            {
                case "check":

                    Puts(
                        "[AutoUpdater] Проверяю обновления..."
                    );

                    CheckAllPlugins(false);

                    break;

                case "update":

                    Puts(
                        "[AutoUpdater] Проверяю и устанавливаю обновления..."
                    );

                    CheckAllPlugins(true);

                    break;

                case "status":

                    PrintStatus();

                    break;

                case "protected":

                    PrintProtected();

                    break;

                default:

                    Puts(
                        "========== MetalicRust AutoUpdater =========="
                    );

                    Puts(
                        "mrupdate check      - проверить обновления"
                    );

                    Puts(
                        "mrupdate update     - обновить плагины"
                    );

                    Puts(
                        "mrupdate status     - показать статус"
                    );

                    Puts(
                        "mrupdate protected  - защищенные плагины"
                    );

                    Puts(
                        "============================================"
                    );

                    break;
            }
        }

        #endregion

        #region Check

        private void CheckAllPlugins(
            bool update)
        {
            foreach (
                KeyValuePair<string, string> entry
                in config.Plugins)
            {
                string pluginName =
                    entry.Key;

                string slug =
                    entry.Value;

                if (IsProtected(pluginName))
                {
                    Puts(
                        "[AutoUpdater] Защищен: " +
                        pluginName
                    );

                    continue;
                }

                Plugin plugin =
                    plugins.Find(
                        pluginName
                    );

                if (plugin == null)
                {
                    PrintWarning(
                        "[AutoUpdater] Не загружен: " +
                        pluginName
                    );

                    continue;
                }

                CheckPlugin(
                    plugin,
                    slug,
                    update
                );
            }
        }

        private void CheckPlugin(
            Plugin plugin,
            string slug,
            bool update)
        {
            if (plugin == null)
                return;

            string pluginName =
                plugin.Name;

            string currentVersion =
                plugin.Version != null
                    ? plugin.Version.ToString()
                    : "0.0.0";

            PluginStatus status;

            if (!statuses.TryGetValue(
                pluginName,
                out status))
            {
                status =
                    new PluginStatus();

                statuses[pluginName] =
                    status;
            }

            status.Name =
                pluginName;

            status.Slug =
                slug;

            status.CurrentVersion =
                currentVersion;

            status.Error =
                false;

            status.ErrorMessage =
                null;

            string url =
                "https://umod.org/plugins/" +
                slug +
                "/latest.json";

            webrequest.EnqueueGet(
                url,
                delegate(
                    int code,
                    string response)
                {
                    if (code != 200 ||
                        string.IsNullOrEmpty(response))
                    {
                        status.Error =
                            true;

                        status.ErrorMessage =
                            "HTTP " +
                            code;

                        PrintWarning(
                            "[AutoUpdater] " +
                            pluginName +
                            " | HTTP " +
                            code
                        );

                        return;
                    }

                    LatestPluginInfo latest;

                    try
                    {
                        latest =
                            JsonConvert.DeserializeObject
                            <LatestPluginInfo>(
                                response
                            );
                    }
                    catch (Exception ex)
                    {
                        status.Error =
                            true;

                        status.ErrorMessage =
                            ex.Message;

                        PrintWarning(
                            "[AutoUpdater] JSON ошибка " +
                            pluginName +
                            ": " +
                            ex.Message
                        );

                        return;
                    }

                    if (latest == null)
                    {
                        status.Error =
                            true;

                        status.ErrorMessage =
                            "Пустой ответ.";

                        return;
                    }

                    string latestVersion =
                        latest.version;

                    status.LatestVersion =
                        latestVersion;

                    if (string.IsNullOrEmpty(
                        latestVersion))
                    {
                        status.Error =
                            true;

                        status.ErrorMessage =
                            "Версия не указана.";

                        return;
                    }

                    bool updateAvailable =
                        IsNewerVersion(
                            currentVersion,
                            latestVersion
                        );

                    status.UpdateAvailable =
                        updateAvailable;

                    if (!updateAvailable)
                    {
                        Puts(
                            "[AutoUpdater] " +
                            pluginName +
                            " " +
                            currentVersion +
                            " — актуален."
                        );

                        return;
                    }

                    PrintWarning(
                        "[AutoUpdater] Обновление найдено: " +
                        pluginName +
                        " " +
                        currentVersion +
                        " -> " +
                        latestVersion
                    );

                    if (config.NotifyAdmins)
                    {
                        SendAdminMessage(
                            "<color=#00ff88>[MetalicRust]</color> " +
                            pluginName +
                            " — доступно обновление " +
                            currentVersion +
                            " → " +
                            latestVersion
                        );
                    }

                    if (update)
                    {
                        DownloadPlugin(
                            plugin,
                            latest
                        );
                    }
                },
                this
            );
        }

        #endregion

        #region Download

        private void DownloadPlugin(
            Plugin plugin,
            LatestPluginInfo info)
        {
            if (plugin == null ||
                info == null)
                return;

            string pluginName =
                plugin.Name;

            if (IsProtected(pluginName))
                return;

            string downloadUrl =
                GetDownloadUrl(info);

            if (string.IsNullOrEmpty(
                downloadUrl))
            {
                PrintWarning(
                    "[AutoUpdater] Не найден URL для " +
                    pluginName
                );

                return;
            }

            Puts(
                "[AutoUpdater] Скачиваю " +
                pluginName +
                "..."
            );

            webrequest.EnqueueGet(
                downloadUrl,
                delegate(
                    int code,
                    string response)
                {
                    if (code != 200 ||
                        string.IsNullOrEmpty(response))
                    {
                        PrintWarning(
                            "[AutoUpdater] Ошибка скачивания " +
                            pluginName +
                            " | HTTP " +
                            code
                        );

                        return;
                    }

                    InstallPlugin(
                        plugin,
                        response
                    );
                },
                this
            );
        }

        private string GetDownloadUrl(
            LatestPluginInfo info)
        {
            if (!string.IsNullOrEmpty(
                info.download_url))
            {
                return info.download_url;
            }

            if (!string.IsNullOrEmpty(
                info.download))
            {
                return info.download;
            }

            return null;
        }

        #endregion

        #region Install

        private void InstallPlugin(
            Plugin plugin,
            string source)
        {
            if (plugin == null)
                return;

            string pluginName =
                plugin.Name;

            if (string.IsNullOrEmpty(source))
                return;

            if (!source.Contains(
                "namespace Oxide.Plugins"))
            {
                PrintWarning(
                    "[AutoUpdater] Полученный файл " +
                    pluginName +
                    " не похож на Oxide-плагин. Отмена."
                );

                return;
            }

            string directory =
                Interface.Oxide.PluginDirectory;

            string filePath =
                Path.Combine(
                    directory,
                    pluginName + ".cs"
                );

            if (!File.Exists(filePath))
            {
                string found =
                    Directory.GetFiles(
                        directory,
                        "*.cs"
                    )
                    .FirstOrDefault(
                        x =>
                            Path.GetFileNameWithoutExtension(x)
                            .Equals(
                                pluginName,
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

                if (!string.IsNullOrEmpty(found))
                    filePath =
                        found;
            }

            if (!File.Exists(filePath))
            {
                PrintWarning(
                    "[AutoUpdater] Файл не найден: " +
                    pluginName
                );

                return;
            }

            string backupPath =
                filePath + ".bak";

            try
            {
                if (config.CreateBackup)
                {
                    File.Copy(
                        filePath,
                        backupPath,
                        true
                    );
                }

                File.WriteAllText(
                    filePath,
                    source
                );

                Puts(
                    "[AutoUpdater] " +
                    pluginName +
                    " успешно обновлен."
                );

                Puts(
                    "[AutoUpdater] Для применения новой версии перезапустите сервер."
                );

                if (config.NotifyAdmins)
                {
                    SendAdminMessage(
                        "<color=#00ff88>[MetalicRust]</color> " +
                        pluginName +
                        " обновлен. Перезапустите сервер."
                    );
                }
            }
            catch (Exception ex)
            {
                PrintError(
                    "[AutoUpdater] Ошибка записи " +
                    pluginName +
                    ": " +
                    ex.Message
                );

                RestoreBackup(
                    filePath,
                    backupPath
                );
            }
        }

        #endregion

        #region Backup

        private void RestoreBackup(
            string filePath,
            string backupPath)
        {
            try
            {
                if (!File.Exists(
                    backupPath))
                {
                    PrintWarning(
                        "[AutoUpdater] Backup не найден."
                    );

                    return;
                }

                File.Copy(
                    backupPath,
                    filePath,
                    true
                );

                Puts(
                    "[AutoUpdater] Выполнен откат: " +
                    Path.GetFileName(filePath)
                );
            }
            catch (Exception ex)
            {
                PrintError(
                    "[AutoUpdater] Ошибка отката: " +
                    ex.Message
                );
            }
        }

        #endregion

        #region Version Comparison

        private bool IsNewerVersion(
            string current,
            string latest)
        {
            int[] currentParts =
                ParseVersion(current);

            int[] latestParts =
                ParseVersion(latest);

            int length =
                Math.Max(
                    currentParts.Length,
                    latestParts.Length
                );

            for (int i = 0; i < length; i++)
            {
                int currentValue =
                    i < currentParts.Length
                        ? currentParts[i]
                        : 0;

                int latestValue =
                    i < latestParts.Length
                        ? latestParts[i]
                        : 0;

                if (latestValue > currentValue)
                    return true;

                if (latestValue < currentValue)
                    return false;
            }

            return false;
        }

        private int[] ParseVersion(
            string version)
        {
            if (string.IsNullOrEmpty(version))
                return new[] { 0, 0, 0 };

            string clean =
                version.Trim();

            int separator =
                clean.IndexOfAny(
                    new[]
                    {
                        '-',
                        '+'
                    }
                );

            if (separator >= 0)
            {
                clean =
                    clean.Substring(
                        0,
                        separator
                    );
            }

            string[] parts =
                clean.Split('.');

            List<int> numbers =
                new List<int>();

            foreach (string part in parts)
            {
                string digits = "";

                foreach (char c in part)
                {
                    if (!char.IsDigit(c))
                        break;

                    digits += c;
                }

                int number;

                if (!int.TryParse(
                    digits,
                    out number))
                {
                    number = 0;
                }

                numbers.Add(
                    number
                );
            }

            if (numbers.Count == 0)
                numbers.Add(0);

            return numbers.ToArray();
        }

        #endregion

        #region Status

        private void PrintStatus()
        {
            Puts(
                "========== METALICRUST AUTOUPDATER =========="
            );

            Puts(
                "Автообновление: " +
                (config.AutoUpdate
                    ? "ВКЛ"
                    : "ВЫКЛ")
            );

            Puts(
                "Интервал: " +
                config.CheckIntervalMinutes +
                " минут"
            );

            foreach (
                KeyValuePair<string, string> entry
                in config.Plugins)
            {
                Plugin plugin =
                    plugins.Find(
                        entry.Key
                    );

                if (plugin == null)
                {
                    Puts(
                        entry.Key +
                        " | НЕ ЗАГРУЖЕН"
                    );

                    continue;
                }

                string version =
                    plugin.Version != null
                        ? plugin.Version.ToString()
                        : "unknown";

                PluginStatus status;

                if (statuses.TryGetValue(
                    entry.Key,
                    out status))
                {
                    if (status.Error)
                    {
                        Puts(
                            entry.Key +
                            " | " +
                            version +
                            " | ОШИБКА: " +
                            status.ErrorMessage
                        );
                    }
                    else if (status.UpdateAvailable)
                    {
                        Puts(
                            entry.Key +
                            " | " +
                            version +
                            " -> " +
                            status.LatestVersion +
                            " | ДОСТУПНО"
                        );
                    }
                    else
                    {
                        Puts(
                            entry.Key +
                            " | " +
                            version +
                            " | OK"
                        );
                    }
                }
                else
                {
                    Puts(
                        entry.Key +
                        " | " +
                        version +
                        " | ПРОВЕРКА"
                    );
                }
            }

            Puts(
                "============================================="
            );
        }

        #endregion

        #region Protected

        private bool IsProtected(
            string pluginName)
        {
            if (string.IsNullOrEmpty(
                pluginName))
                return true;

            return config.ProtectedPlugins.Any(
                x =>
                    x.Equals(
                        pluginName,
                        StringComparison.OrdinalIgnoreCase
                    )
            );
        }

        private void PrintProtected()
        {
            Puts(
                "========== ЗАЩИЩЕННЫЕ ПЛАГИНЫ =========="
            );

            foreach (
                string plugin
                in config.ProtectedPlugins)
            {
                Puts(
                    plugin
                );
            }

            Puts(
                "========================================="
            );
        }

        #endregion

        #region Admin

        private void SendAdminMessage(
            string message)
        {
            foreach (
                BasePlayer player
                in BasePlayer.activePlayerList)
            {
                if (player == null)
                    continue;

                if (!player.IsAdmin)
                    continue;

                SendReply(
                    player,
                    message
                );
            }
        }

        #endregion

        #region JSON

        private class LatestPluginInfo
        {
            public string version;
            public string download_url;
            public string download;
            public string title;
            public string name;
            public string author;
        }

        #endregion
    }
}