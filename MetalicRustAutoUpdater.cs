using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MetalicRustAutoUpdater", "MetalicRust", "2.1.0")]
    [Description("Автоматическая проверка и безопасное обновление плагинов MetalicRust через uMod и GitHub.")]
    public class MetalicRustAutoUpdater : RustPlugin
    {
        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Автоматически обновлять плагины")]
            public bool AutoUpdate = true;

            [JsonProperty("Проверять обновления каждые N минут")]
            public float CheckIntervalMinutes = 60f;

            [JsonProperty("Задержка первой проверки")]
            public float InitialCheckDelay = 30f;

            [JsonProperty("Задержка перед обновлением")]
            public float UpdateDelay = 5f;

            [JsonProperty("Количество сетевых повторов")]
            public int NetworkRetries = 4;

            [JsonProperty("Задержка между сетевыми повторами")]
            public float NetworkRetryDelay = 5f;

            [JsonProperty("Создавать резервную копию .bak")]
            public bool CreateBackup = true;

            [JsonProperty("Откатывать при ошибке")]
            public bool RollbackOnError = true;

            [JsonProperty("Проверять плагин после reload")]
            public bool VerifyAfterReload = true;

            [JsonProperty("Задержка проверки после reload")]
            public float VerifyDelay = 5f;

            [JsonProperty("Уведомлять администраторов")]
            public bool NotifyAdmins = true;

            [JsonProperty("Показывать предупреждения жёлтым")]
            public bool YellowNotFound = true;

            [JsonProperty("GitHub Base URL")]
            public string GitHubBaseUrl =
                "https://raw.githubusercontent.com/qwrwetqa/MetalicRust-Plugins/main/";

            [JsonProperty("GitHub versions.json")]
            public string GitHubVersionsUrl =
                "https://raw.githubusercontent.com/qwrwetqa/MetalicRust-Plugins/main/versions.json";

            [JsonProperty("Плагины uMod")]
            public Dictionary<string, string> UModPlugins =
                new Dictionary<string, string>
                {
                    { "GUIShop", "guishop" },
                    { "GatherManager", "gather-manager" },
                    { "GatherRewards", "gather-rewards" },
                    { "ImageLibrary", "image-library" },
                    { "KillRewards", "kill-rewards" },
                    { "NTeleportation", "nteleportation" },
                    { "NoGiveNotices", "no-give-notices" },
                    { "PlayerAdministration", "player-administration" },
                    { "PlayerRankings", "player-rankings" },
                    { "PlaytimeTracker", "playtime-tracker" },
                    { "PortableVehicles", "portable-vehicles" },
                    { "Quests", "quests" },
                    { "ServerRewards", "server-rewards" },
                    { "StackSizeController", "stack-size-controller" }
                };

            [JsonProperty("Защищенные плагины")]
            public List<string> ProtectedPlugins =
                new List<string>
                {
                    "MetalicRustAutoUpdater",
                    "TopPlugin",
                    "GameStores",
                    "MicroPanel",
                    "Building Upgrade",
                    "BuildingUpgrade"
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
            catch (Exception ex)
            {
                PrintWarning(
                    "[Config] Ошибка конфигурации: " +
                    ex.Message
                );

                LoadDefaultConfig();
            }

            if (config.UModPlugins == null)
                config.UModPlugins = new Dictionary<string, string>();

            if (config.ProtectedPlugins == null)
                config.ProtectedPlugins = new List<string>();

            SaveConfig();
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

        private readonly HashSet<string> updatingPlugins =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        private class PluginStatus
        {
            public string Name;
            public string Source;

            public string CurrentVersion;
            public string ManifestVersion;
            public string RemoteVersion;

            public VersionState State;

            public string ErrorMessage;
        }

        private enum VersionState
        {
            Unknown,
            UpToDate,
            UpdateAvailable,
            ManifestNewer,
            FileNewer,
            Mismatch,
            NetworkError,
            NotLoaded,
            Protected,
            InvalidSource,
            Updated,
            Rollback
        }

        private class GitHubManifest
        {
            public Dictionary<string, string> Versions =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private class UpdateOperation
        {
            public string PluginName;
            public string FilePath;
            public string BackupPath;
            public string ExpectedVersion;
            public string DownloadedVersion;
            public string Source;
            public string SourceType;
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
                "MetalicRust AutoUpdater 2.1.0"
            );

            PrintWarning(
                "Автообновление: " +
                (
                    config.AutoUpdate
                        ? "ВКЛЮЧЕНО"
                        : "ВЫКЛЮЧЕНО"
                )
            );

            PrintWarning(
                "Проверка каждые: " +
                config.CheckIntervalMinutes +
                " минут"
            );

            PrintWarning(
                "GitHub: " +
                config.GitHubVersionsUrl
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
                Mathf.Max(
                    5f,
                    config.InitialCheckDelay
                ),
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

        #region Console Commands

        [ConsoleCommand("mrupdate")]
        private void ConsoleCommand(
            ConsoleSystem.Arg arg
        )
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
                        "[AutoUpdater] Запущена проверка."
                    );

                    CheckAllPlugins(false);

                    break;

                case "update":

                    Puts(
                        "[AutoUpdater] Запущена проверка с автообновлением."
                    );

                    CheckAllPlugins(true);

                    break;

                case "status":

                    PrintStatus();

                    break;

                case "local":

                    PrintLocalPlugins();

                    break;

                case "protected":

                    PrintProtected();

                    break;

                case "on":

                    config.AutoUpdate = true;
                    SaveConfig();

                    Puts(
                        "[AutoUpdater] Автообновление ВКЛЮЧЕНО."
                    );

                    break;

                case "off":

                    config.AutoUpdate = false;
                    SaveConfig();

                    Puts(
                        "[AutoUpdater] Автообновление ВЫКЛЮЧЕНО."
                    );

                    break;

                default:

                    Puts(
                        "========== MetalicRust AutoUpdater =========="
                    );

                    Puts(
                        "mrupdate check      - проверить"
                    );

                    Puts(
                        "mrupdate update     - проверить и обновить"
                    );

                    Puts(
                        "mrupdate status     - статус"
                    );

                    Puts(
                        "mrupdate local      - локальные плагины"
                    );

                    Puts(
                        "mrupdate protected  - защищённые"
                    );

                    Puts(
                        "mrupdate on         - автообновление ВКЛ"
                    );

                    Puts(
                        "mrupdate off        - автообновление ВЫКЛ"
                    );

                    Puts(
                        "============================================"
                    );

                    break;
            }
        }

        #endregion

        #region Main Check

        private void CheckAllPlugins(bool update)
        {
            Puts(
                "[AutoUpdater] =========================================="
            );

            Puts(
                "[AutoUpdater] Начинаю проверку плагинов..."
            );

            CheckUModPlugins(update);

            DownloadGitHubManifest(
                update
            );
        }

        #endregion

        #region uMod

        private void CheckUModPlugins(bool update)
        {
            foreach (
                KeyValuePair<string, string> entry
                in config.UModPlugins
            )
            {
                string configuredName =
                    entry.Key;

                string slug =
                    entry.Value;

                if (IsProtected(configuredName))
                {
                    SetStatus(
                        configuredName,
                        "uMod",
                        null,
                        null,
                        null,
                        VersionState.Protected,
                        "Защищён"
                    );

                    continue;
                }

                Plugin plugin =
                    plugins.Find(
                        configuredName
                    );

                if (plugin == null)
                {
                    SetStatus(
                        configuredName,
                        "uMod",
                        null,
                        null,
                        null,
                        VersionState.NotLoaded,
                        "Плагин не загружен"
                    );

                    Warn(
                        "[uMod] " +
                        configuredName +
                        " | Плагин не загружен."
                    );

                    continue;
                }

                CheckUModPlugin(
                    plugin,
                    slug,
                    update
                );
            }
        }

        private void CheckUModPlugin(
            Plugin plugin,
            string slug,
            bool update
        )
        {
            if (plugin == null)
                return;

            string pluginName =
                plugin.Name;

            string currentVersion =
                GetPluginVersion(plugin);

            string url =
                "https://umod.org/plugins/" +
                slug +
                "/latest.json";

            Puts(
                "[uMod] Проверка: " +
                pluginName +
                " " +
                currentVersion
            );

            EnqueueGetWithRetry(
                url,
                config.NetworkRetries,
                delegate(
                    int code,
                    string response
                )
                {
                    if (
                        code != 200 ||
                        string.IsNullOrEmpty(response)
                    )
                    {
                        SetStatus(
                            pluginName,
                            "uMod",
                            currentVersion,
                            null,
                            null,
                            VersionState.NetworkError,
                            "HTTP " + code
                        );

                        Warn(
                            "[uMod] " +
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
                            JsonConvert.DeserializeObject<LatestPluginInfo>(
                                response
                            );
                    }
                    catch (Exception ex)
                    {
                        SetStatus(
                            pluginName,
                            "uMod",
                            currentVersion,
                            null,
                            null,
                            VersionState.NetworkError,
                            ex.Message
                        );

                        Warn(
                            "[uMod] " +
                            pluginName +
                            " | JSON ошибка: " +
                            ex.Message
                        );

                        return;
                    }

                    if (
                        latest == null ||
                        string.IsNullOrEmpty(latest.version)
                    )
                    {
                        SetStatus(
                            pluginName,
                            "uMod",
                            currentVersion,
                            null,
                            null,
                            VersionState.Unknown,
                            "Версия отсутствует"
                        );

                        Warn(
                            "[uMod] " +
                            pluginName +
                            " | Версия не найдена."
                        );

                        return;
                    }

                    string latestVersion =
                        latest.version;

                    int comparison =
                        CompareVersions(
                            currentVersion,
                            latestVersion
                        );

                    if (comparison == 0)
                    {
                        SetStatus(
                            pluginName,
                            "uMod",
                            currentVersion,
                            latestVersion,
                            latestVersion,
                            VersionState.UpToDate,
                            null
                        );

                        Puts(
                            "[uMod] " +
                            pluginName +
                            " | " +
                            currentVersion +
                            " = " +
                            latestVersion +
                            " | OK"
                        );

                        return;
                    }

                    if (comparison > 0)
                    {
                        SetStatus(
                            pluginName,
                            "uMod",
                            currentVersion,
                            latestVersion,
                            latestVersion,
                            VersionState.FileNewer,
                            "Локальная версия новее uMod"
                        );

                        Warn(
                            "[uMod] " +
                            pluginName +
                            " | ЛОКАЛЬНАЯ ВЕРСИЯ НОВЕЕ: " +
                            currentVersion +
                            " > " +
                            latestVersion +
                            " | Обновление не требуется."
                        );

                        return;
                    }

                    SetStatus(
                        pluginName,
                        "uMod",
                        currentVersion,
                        latestVersion,
                        latestVersion,
                        VersionState.UpdateAvailable,
                        null
                    );

                    Warn(
                        "[uMod] ОБНОВЛЕНИЕ: " +
                        pluginName +
                        " | " +
                        currentVersion +
                        " -> " +
                        latestVersion
                    );

                    NotifyAdmin(
                        pluginName +
                        " — uMod обновление " +
                        currentVersion +
                        " → " +
                        latestVersion
                    );

                    if (!update)
                        return;

                    string downloadUrl =
                        GetDownloadUrl(
                            latest
                        );

                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        Warn(
                            "[uMod] " +
                            pluginName +
                            " | URL скачивания отсутствует."
                        );

                        return;
                    }

                    timer.Once(
                        Mathf.Max(
                            1f,
                            config.UpdateDelay
                        ),
                        delegate
                        {
                            DownloadUModPlugin(
                                plugin,
                                downloadUrl,
                                latestVersion
                            );
                        }
                    );
                }
            );
        }

        private void DownloadUModPlugin(
            Plugin plugin,
            string url,
            string expectedVersion
        )
        {
            if (plugin == null)
                return;

            string pluginName =
                plugin.Name;

            if (
                updatingPlugins.Contains(
                    pluginName
                )
            )
                return;

            updatingPlugins.Add(
                pluginName
            );

            Puts(
                "[uMod] Скачивание: " +
                url
            );

            EnqueueGetWithRetry(
                url,
                config.NetworkRetries,
                delegate(
                    int code,
                    string source
                )
                {
                    if (
                        code != 200 ||
                        string.IsNullOrEmpty(source)
                    )
                    {
                        updatingPlugins.Remove(
                            pluginName
                        );

                        Warn(
                            "[uMod] " +
                            pluginName +
                            " | Ошибка скачивания HTTP " +
                            code
                        );

                        return;
                    }

                    string downloadedVersion =
                        ExtractPluginVersion(
                            source
                        );

                    Puts(
                        "[uMod] " +
                        pluginName +
                        " | версия в скачанном файле: " +
                        downloadedVersion
                    );

                    VersionCheckResult result =
                        ValidateDownloadedVersion(
                            pluginName,
                            expectedVersion,
                            downloadedVersion
                        );

                    if (
                        result != VersionCheckResult.Ok
                    )
                    {
                        updatingPlugins.Remove(
                            pluginName
                        );

                        HandleVersionMismatch(
                            pluginName,
                            "uMod",
                            expectedVersion,
                            downloadedVersion,
                            result
                        );

                        return;
                    }

                    UpdateOperation operation =
                        PrepareOperation(
                            pluginName,
                            source,
                            expectedVersion,
                            downloadedVersion,
                            "uMod"
                        );

                    InstallOperation(
                        operation,
                        plugin
                    );
                }
            );
        }

        #endregion

        #region GitHub

        private void DownloadGitHubManifest(
            bool update
        )
        {
            Puts(
                "[GitHub] Загрузка versions.json..."
            );

            EnqueueGetWithRetry(
                config.GitHubVersionsUrl,
                config.NetworkRetries,
                delegate(
                    int code,
                    string response
                )
                {
                    if (
                        code != 200 ||
                        string.IsNullOrEmpty(response)
                    )
                    {
                        Warn(
                            "[GitHub] versions.json | HTTP " +
                            code
                        );

                        return;
                    }

                    Dictionary<string, string> versions;

                    try
                    {
                        versions =
                            JsonConvert.DeserializeObject<Dictionary<string, string>>(
                                response
                            );
                    }
                    catch (Exception ex)
                    {
                        Warn(
                            "[GitHub] Ошибка versions.json: " +
                            ex.Message
                        );

                        return;
                    }

                    if (
                        versions == null ||
                        versions.Count == 0
                    )
                    {
                        Warn(
                            "[GitHub] versions.json пустой."
                        );

                        return;
                    }

                    foreach (
                        KeyValuePair<string, string> entry
                        in versions
                    )
                    {
                        CheckGitHubPlugin(
                            entry.Key,
                            entry.Value,
                            update
                        );
                    }
                }
            );
        }

        private void CheckGitHubPlugin(
            string pluginName,
            string manifestVersion,
            bool update
        )
        {
            if (string.IsNullOrEmpty(pluginName))
                return;

            if (IsProtected(pluginName))
            {
                SetStatus(
                    pluginName,
                    "GitHub",
                    null,
                    manifestVersion,
                    null,
                    VersionState.Protected,
                    "Защищён"
                );

                Puts(
                    "[GitHub] Защищён: " +
                    pluginName
                );

                return;
            }

            string url =
                config.GitHubBaseUrl.TrimEnd('/') +
                "/" +
                pluginName +
                ".cs";

            Puts(
                "[GitHub] Проверка: " +
                pluginName +
                " | manifest=" +
                manifestVersion
            );

            string localPath =
                FindPluginFile(
                    pluginName
                );

            Plugin localPlugin =
                plugins.Find(
                    pluginName
                );

            string currentVersion =
                localPlugin != null
                    ? GetPluginVersion(localPlugin)
                    : ExtractPluginVersionFromFile(
                        localPath
                    );

            EnqueueGetWithRetry(
                url,
                config.NetworkRetries,
                delegate(
                    int code,
                    string source
                )
                {
                    if (
                        code != 200 ||
                        string.IsNullOrEmpty(source)
                    )
                    {
                        SetStatus(
                            pluginName,
                            "GitHub",
                            currentVersion,
                            manifestVersion,
                            null,
                            VersionState.NetworkError,
                            "HTTP " + code
                        );

                        Warn(
                            "[GitHub] " +
                            pluginName +
                            " | HTTP " +
                            code
                        );

                        return;
                    }

                    string fileVersion =
                        ExtractPluginVersion(
                            source
                        );

                    Puts(
                        "[GitHub] " +
                        pluginName +
                        " | versions.json=" +
                        manifestVersion +
                        " | файл=" +
                        fileVersion +
                        " | локально=" +
                        currentVersion
                    );

                    int manifestVsFile =
                        CompareVersions(
                            fileVersion,
                            manifestVersion
                        );

                    /*
                     * Файл GitHub старее, чем versions.json.
                     *
                     * Например:
                     *
                     * versions.json = 2.1.1
                     * InfoMenu.cs   = 2.1.0
                     *
                     * Это НЕ обновление.
                     */
                    if (manifestVsFile < 0)
                    {
                        SetStatus(
                            pluginName,
                            "GitHub",
                            currentVersion,
                            manifestVersion,
                            fileVersion,
                            VersionState.ManifestNewer,
                            "versions.json новее самого файла"
                        );

                        Warn(
                            "[GitHub] " +
                            pluginName +
                            " | НЕСООТВЕТСТВИЕ ИСТОЧНИКА: " +
                            "versions.json=" +
                            manifestVersion +
                            ", файл=" +
                            fileVersion +
                            " | Файл НЕ устанавливается."
                        );

                        NotifyAdmin(
                            pluginName +
                            " — ошибка GitHub: versions.json=" +
                            manifestVersion +
                            ", файл=" +
                            fileVersion
                        );

                        return;
                    }

                    /*
                     * Сам файл новее, чем versions.json.
                     *
                     * Это значит, что versions.json забыли обновить.
                     */
                    if (manifestVsFile > 0)
                    {
                        SetStatus(
                            pluginName,
                            "GitHub",
                            currentVersion,
                            manifestVersion,
                            fileVersion,
                            VersionState.FileNewer,
                            "Файл новее versions.json"
                        );

                        Warn(
                            "[GitHub] " +
                            pluginName +
                            " | ВНИМАНИЕ: файл " +
                            fileVersion +
                            " новее versions.json " +
                            manifestVersion +
                            " | Обновление отменено."
                        );

                        return;
                    }

                    /*
                     * Теперь:
                     *
                     * файл == versions.json
                     *
                     * Источник считается корректным.
                     */

                    if (string.IsNullOrEmpty(currentVersion))
                    {
                        currentVersion = "0.0.0";
                    }

                    int localVsManifest =
                        CompareVersions(
                            currentVersion,
                            manifestVersion
                        );

                    if (localVsManifest == 0)
                    {
                        SetStatus(
                            pluginName,
                            "GitHub",
                            currentVersion,
                            manifestVersion,
                            fileVersion,
                            VersionState.UpToDate,
                            null
                        );

                        Puts(
                            "[GitHub] " +
                            pluginName +
                            " | " +
                            currentVersion +
                            " = " +
                            manifestVersion +
                            " | OK"
                        );

                        return;
                    }

                    if (localVsManifest > 0)
                    {
                        SetStatus(
                            pluginName,
                            "GitHub",
                            currentVersion,
                            manifestVersion,
                            fileVersion,
                            VersionState.FileNewer,
                            "Локальная версия новее manifest"
                        );

                        Warn(
                            "[GitHub] " +
                            pluginName +
                            " | локальная версия " +
                            currentVersion +
                            " новее manifest " +
                            manifestVersion +
                            " | Откат не выполняется."
                        );

                        return;
                    }

                    SetStatus(
                        pluginName,
                        "GitHub",
                        currentVersion,
                        manifestVersion,
                        fileVersion,
                        VersionState.UpdateAvailable,
                        null
                    );

                    Warn(
                        "[GitHub] ОБНОВЛЕНИЕ: " +
                        pluginName +
                        " | " +
                        currentVersion +
                        " -> " +
                        manifestVersion
                    );

                    NotifyAdmin(
                        pluginName +
                        " — GitHub обновление " +
                        currentVersion +
                        " → " +
                        manifestVersion
                    );

                    if (!update)
                        return;

                    if (localPlugin == null)
                    {
                        Warn(
                            "[GitHub] " +
                            pluginName +
                            " | локальный плагин не загружен. Автоустановка отменена."
                        );

                        return;
                    }

                    if (
                        updatingPlugins.Contains(
                            pluginName
                        )
                    )
                        return;

                    updatingPlugins.Add(
                        pluginName
                    );

                    timer.Once(
                        Mathf.Max(
                            1f,
                            config.UpdateDelay
                        ),
                        delegate
                        {
                            UpdateOperation operation =
                                PrepareOperation(
                                    pluginName,
                                    source,
                                    manifestVersion,
                                    fileVersion,
                                    "GitHub"
                                );

                            InstallOperation(
                                operation,
                                localPlugin
                            );
                        }
                    );
                }
            );
        }

        #endregion

        #region Version Validation

        private enum VersionCheckResult
        {
            Ok,
            DownloadedOlder,
            DownloadedNewer,
            Unknown
        }

        private VersionCheckResult ValidateDownloadedVersion(
            string pluginName,
            string expectedVersion,
            string downloadedVersion
        )
        {
            if (
                string.IsNullOrEmpty(
                    downloadedVersion
                )
            )
            {
                return VersionCheckResult.Unknown;
            }

            int comparison =
                CompareVersions(
                    downloadedVersion,
                    expectedVersion
                );

            if (comparison == 0)
                return VersionCheckResult.Ok;

            if (comparison < 0)
                return VersionCheckResult.DownloadedOlder;

            return VersionCheckResult.DownloadedNewer;
        }

        private void HandleVersionMismatch(
            string pluginName,
            string sourceType,
            string expectedVersion,
            string downloadedVersion,
            VersionCheckResult result
        )
        {
            if (
                result ==
                VersionCheckResult.DownloadedOlder
            )
            {
                Warn(
                    "[" +
                    sourceType +
                    "] " +
                    pluginName +
                    " | СКАЧАННЫЙ ФАЙЛ СТАРШЕ/СТАРАЯ ВЕРСИЯ: " +
                    "ожидалась " +
                    expectedVersion +
                    ", получена " +
                    downloadedVersion +
                    " | Обновление отменено."
                );

                return;
            }

            if (
                result ==
                VersionCheckResult.DownloadedNewer
            )
            {
                Warn(
                    "[" +
                    sourceType +
                    "] " +
                    pluginName +
                    " | СКАЧАННЫЙ ФАЙЛ НОВЕЕ ЗАЯВЛЕННОЙ ВЕРСИИ: " +
                    "ожидалась " +
                    expectedVersion +
                    ", получена " +
                    downloadedVersion +
                    " | Обновление отменено."
                );

                return;
            }

            Warn(
                "[" +
                sourceType +
                "] " +
                pluginName +
                " | НЕ УДАЛОСЬ ОПРЕДЕЛИТЬ ВЕРСИЮ ФАЙЛА | Обновление отменено."
            );
        }

        private string ExtractPluginVersion(
            string source
        )
        {
            if (string.IsNullOrEmpty(source))
                return null;

            Match match =
                Regex.Match(
                    source,
                    @"\[Info\s*\(\s*""[^""]+""\s*,\s*""[^""]+""\s*,\s*""([^""]+)""\s*\)\]",
                    RegexOptions.IgnoreCase
                );

            if (
                match.Success &&
                match.Groups.Count > 1
            )
            {
                return match.Groups[1].Value.Trim();
            }

            return null;
        }

        private string ExtractPluginVersionFromFile(
            string path
        )
        {
            if (
                string.IsNullOrEmpty(path) ||
                !File.Exists(path)
            )
                return null;

            try
            {
                string source =
                    File.ReadAllText(path);

                return ExtractPluginVersion(
                    source
                );
            }
            catch
            {
                return null;
            }
        }

        private int CompareVersions(
            string left,
            string right
        )
        {
            int[] leftParts =
                ParseVersion(left);

            int[] rightParts =
                ParseVersion(right);

            int length =
                Math.Max(
                    leftParts.Length,
                    rightParts.Length
                );

            for (int i = 0; i < length; i++)
            {
                int l =
                    i < leftParts.Length
                        ? leftParts[i]
                        : 0;

                int r =
                    i < rightParts.Length
                        ? rightParts[i]
                        : 0;

                if (l > r)
                    return 1;

                if (l < r)
                    return -1;
            }

            return 0;
        }

        private int[] ParseVersion(
            string version
        )
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

                if (
                    !int.TryParse(
                        digits,
                        out number
                    )
                )
                {
                    number = 0;
                }

                numbers.Add(number);
            }

            while (numbers.Count < 3)
                numbers.Add(0);

            return numbers.ToArray();
        }

        #endregion

        #region Install / Backup / Rollback

        private UpdateOperation PrepareOperation(
            string pluginName,
            string source,
            string expectedVersion,
            string downloadedVersion,
            string sourceType
        )
        {
            string filePath =
                FindPluginFile(
                    pluginName
                );

            if (string.IsNullOrEmpty(filePath))
            {
                Warn(
                    "[" +
                    sourceType +
                    "] " +
                    pluginName +
                    " | Файл .cs не найден."
                );

                updatingPlugins.Remove(
                    pluginName
                );

                return null;
            }

            return new UpdateOperation
            {
                PluginName = pluginName,
                FilePath = filePath,
                BackupPath = filePath + ".bak",
                ExpectedVersion = expectedVersion,
                DownloadedVersion = downloadedVersion,
                Source = source,
                SourceType = sourceType
            };
        }

        private void InstallOperation(
            UpdateOperation operation,
            Plugin plugin
        )
        {
            if (operation == null)
                return;

            if (
                string.IsNullOrEmpty(
                    operation.Source
                )
            )
            {
                updatingPlugins.Remove(
                    operation.PluginName
                );

                return;
            }

            if (
                !operation.Source.Contains(
                    "namespace Oxide.Plugins"
                )
            )
            {
                Warn(
                    "[" +
                    operation.SourceType +
                    "] " +
                    operation.PluginName +
                    " | Файл не похож на Oxide-плагин. Отмена."
                );

                updatingPlugins.Remove(
                    operation.PluginName
                );

                return;
            }

            try
            {
                if (config.CreateBackup)
                {
                    File.Copy(
                        operation.FilePath,
                        operation.BackupPath,
                        true
                    );

                    Puts(
                        "[" +
                        operation.SourceType +
                        "] " +
                        operation.PluginName +
                        " | Создан backup: " +
                        Path.GetFileName(
                            operation.BackupPath
                        )
                    );
                }

                File.WriteAllText(
                    operation.FilePath,
                    operation.Source
                );

                Puts(
                    "[" +
                    operation.SourceType +
                    "] " +
                    operation.PluginName +
                    " | Файл записан: версия " +
                    operation.DownloadedVersion
                );

                if (plugin != null)
                {
                    ReloadPlugin(
                        operation.PluginName
                    );

                    if (
                        config.VerifyAfterReload
                    )
                    {
                        timer.Once(
                            Mathf.Max(
                                1f,
                                config.VerifyDelay
                            ),
                            delegate
                            {
                                VerifyUpdate(
                                    operation
                                );
                            }
                        );
                    }
                    else
                    {
                        CompleteUpdate(
                            operation
                        );
                    }
                }
                else
                {
                    CompleteUpdate(
                        operation
                    );
                }
            }
            catch (Exception ex)
            {
                PrintError(
                    "[" +
                    operation.SourceType +
                    "] " +
                    operation.PluginName +
                    " | Ошибка установки: " +
                    ex.Message
                );

                if (
                    config.RollbackOnError
                )
                {
                    RollbackOperation(
                        operation
                    );
                }
                else
                {
                    updatingPlugins.Remove(
                        operation.PluginName
                    );
                }
            }
        }

        private void VerifyUpdate(
            UpdateOperation operation
        )
        {
            Plugin plugin =
                plugins.Find(
                    operation.PluginName
                );

            if (plugin == null)
            {
                PrintError(
                    "[VERIFY] " +
                    operation.PluginName +
                    " | Плагин не загрузился после обновления."
                );

                if (
                    config.RollbackOnError
                )
                {
                    RollbackOperation(
                        operation
                    );
                }
                else
                {
                    updatingPlugins.Remove(
                        operation.PluginName
                    );
                }

                return;
            }

            string loadedVersion =
                GetPluginVersion(
                    plugin
                );

            int comparison =
                CompareVersions(
                    loadedVersion,
                    operation.ExpectedVersion
                );

            if (comparison != 0)
            {
                PrintError(
                    "[VERIFY] " +
                    operation.PluginName +
                    " | ОЖИДАЛОСЬ " +
                    operation.ExpectedVersion +
                    ", ЗАГРУЖЕНО " +
                    loadedVersion
                );

                if (
                    config.RollbackOnError
                )
                {
                    RollbackOperation(
                        operation
                    );
                }
                else
                {
                    updatingPlugins.Remove(
                        operation.PluginName
                    );
                }

                return;
            }

            CompleteUpdate(
                operation
            );
        }

        private void CompleteUpdate(
            UpdateOperation operation
        )
        {
            updatingPlugins.Remove(
                operation.PluginName
            );

            SetStatus(
                operation.PluginName,
                operation.SourceType,
                operation.ExpectedVersion,
                operation.ExpectedVersion,
                operation.DownloadedVersion,
                VersionState.Updated,
                null
            );

            Puts(
                "[" +
                operation.SourceType +
                "] " +
                operation.PluginName +
                " | УСПЕШНО ОБНОВЛЁН до " +
                operation.ExpectedVersion
            );

            NotifyAdmin(
                operation.PluginName +
                " успешно обновлён до " +
                operation.ExpectedVersion
            );
        }

        private void RollbackOperation(
            UpdateOperation operation
        )
        {
            try
            {
                Plugin current =
                    plugins.Find(
                        operation.PluginName
                    );

                if (current != null)
                {
                    try
                    {
                        ConsoleSystem.Run(
                            ConsoleSystem.Option.Server.Quiet(),
                            "oxide.unload \"" +
                            operation.PluginName +
                            "\""
                        );
                    }
                    catch
                    {
                    }
                }

                if (
                    !File.Exists(
                        operation.BackupPath
                    )
                )
                {
                    PrintError(
                        "[ROLLBACK] " +
                        operation.PluginName +
                        " | .bak файл отсутствует!"
                    );

                    updatingPlugins.Remove(
                        operation.PluginName
                    );

                    return;
                }

                File.Copy(
                    operation.BackupPath,
                    operation.FilePath,
                    true
                );

                Puts(
                    "[ROLLBACK] " +
                    operation.PluginName +
                    " | Восстановлен .bak"
                );

                ConsoleSystem.Run(
                    ConsoleSystem.Option.Server.Quiet(),
                    "oxide.load \"" +
                    operation.PluginName +
                    "\""
                );

                SetStatus(
                    operation.PluginName,
                    operation.SourceType,
                    operation.ExpectedVersion,
                    operation.ExpectedVersion,
                    operation.DownloadedVersion,
                    VersionState.Rollback,
                    "Выполнен автоматический откат"
                );

                NotifyAdmin(
                    operation.PluginName +
                    " — ошибка обновления, выполнен автоматический откат."
                );
            }
            catch (Exception ex)
            {
                PrintError(
                    "[ROLLBACK] " +
                    operation.PluginName +
                    " | КРИТИЧЕСКАЯ ОШИБКА: " +
                    ex.Message
                );
            }

            updatingPlugins.Remove(
                operation.PluginName
            );
        }

        private void ReloadPlugin(
            string pluginName
        )
        {
            Puts(
                "[RELOAD] Перезагрузка: " +
                pluginName
            );

            ConsoleSystem.Run(
                ConsoleSystem.Option.Server.Quiet(),
                "oxide.reload \"" +
                pluginName +
                "\""
            );
        }

        private string FindPluginFile(
            string pluginName
        )
        {
            if (
                string.IsNullOrEmpty(
                    pluginName
                )
            )
                return null;

            string directory =
                Interface.Oxide.PluginDirectory;

            string exact =
                Path.Combine(
                    directory,
                    pluginName + ".cs"
                );

            if (File.Exists(exact))
                return exact;

            string found =
                Directory
                .GetFiles(
                    directory,
                    "*.cs"
                )
                .FirstOrDefault(
                    x =>
                        Path.GetFileNameWithoutExtension(
                            x
                        ).Equals(
                            pluginName,
                            StringComparison.OrdinalIgnoreCase
                        )
                );

            return found;
        }

        #endregion

        #region Network

        private void EnqueueGetWithRetry(
            string url,
            int retries,
            Action<int, string> callback
        )
        {
            if (string.IsNullOrEmpty(url))
            {
                callback(
                    0,
                    null
                );

                return;
            }

            int attempts =
                Mathf.Max(
                    1,
                    retries
                );

            EnqueueGetAttempt(
                url,
                attempts,
                callback
            );
        }

        private void EnqueueGetAttempt(
            string url,
            int remaining,
            Action<int, string> callback
        )
        {
            webrequest.EnqueueGet(
                url,
                delegate(
                    int code,
                    string response
                )
                {
                    bool success =
                        code == 200 &&
                        !string.IsNullOrEmpty(
                            response
                        );

                    if (success)
                    {
                        callback(
                            code,
                            response
                        );

                        return;
                    }

                    if (remaining <= 1)
                    {
                        callback(
                            code,
                            response
                        );

                        return;
                    }

                    Warn(
                        "[Network] Ошибка HTTP " +
                        code +
                        " | Повтор через " +
                        config.NetworkRetryDelay +
                        " сек. | Осталось попыток: " +
                        (remaining - 1)
                    );

                    timer.Once(
                        Mathf.Max(
                            1f,
                            config.NetworkRetryDelay
                        ),
                        delegate
                        {
                            EnqueueGetAttempt(
                                url,
                                remaining - 1,
                                callback
                            );
                        }
                    );
                },
                this
            );
        }

        #endregion

        #region Status

        private void SetStatus(
            string name,
            string source,
            string current,
            string manifest,
            string remote,
            VersionState state,
            string error
        )
        {
            PluginStatus status;

            if (
                !statuses.TryGetValue(
                    name,
                    out status
                )
            )
            {
                status =
                    new PluginStatus();

                statuses[name] =
                    status;
            }

            status.Name =
                name;

            status.Source =
                source;

            status.CurrentVersion =
                current;

            status.ManifestVersion =
                manifest;

            status.RemoteVersion =
                remote;

            status.State =
                state;

            status.ErrorMessage =
                error;
        }

        private void PrintStatus()
        {
            Puts(
                "========== METALICRUST AUTOUPDATER =========="
            );

            Puts(
                "Версия AutoUpdater: 2.1.0"
            );

            Puts(
                "Автообновление: " +
                (
                    config.AutoUpdate
                        ? "ВКЛ"
                        : "ВЫКЛ"
                )
            );

            foreach (
                KeyValuePair<string, PluginStatus> entry
                in statuses
            )
            {
                PluginStatus s =
                    entry.Value;

                string current =
                    string.IsNullOrEmpty(
                        s.CurrentVersion
                    )
                        ? "?"
                        : s.CurrentVersion;

                string manifest =
                    string.IsNullOrEmpty(
                        s.ManifestVersion
                    )
                        ? "-"
                        : s.ManifestVersion;

                string remote =
                    string.IsNullOrEmpty(
                        s.RemoteVersion
                    )
                        ? "-"
                        : s.RemoteVersion;

                Puts(
                    s.Name +
                    " | " +
                    s.Source +
                    " | local=" +
                    current +
                    " | manifest=" +
                    manifest +
                    " | file=" +
                    remote +
                    " | " +
                    GetStateText(
                        s.State
                    )
                );

                if (
                    !string.IsNullOrEmpty(
                        s.ErrorMessage
                    )
                )
                {
                    Puts(
                        "    -> " +
                        s.ErrorMessage
                    );
                }
            }

            Puts(
                "============================================="
            );
        }

        private string GetStateText(
            VersionState state
        )
        {
            switch (state)
            {
                case VersionState.UpToDate:
                    return "OK";

                case VersionState.UpdateAvailable:
                    return "UPDATE";

                case VersionState.ManifestNewer:
                    return "MANIFEST NEWER";

                case VersionState.FileNewer:
                    return "FILE NEWER";

                case VersionState.Mismatch:
                    return "MISMATCH";

                case VersionState.NetworkError:
                    return "NETWORK ERROR";

                case VersionState.NotLoaded:
                    return "NOT LOADED";

                case VersionState.Protected:
                    return "PROTECTED";

                case VersionState.InvalidSource:
                    return "INVALID SOURCE";

                case VersionState.Updated:
                    return "UPDATED";

                case VersionState.Rollback:
                    return "ROLLBACK";

                default:
                    return "UNKNOWN";
            }
        }

        #endregion

        #region Local

        private void PrintLocalPlugins()
        {
            Puts(
                "========== ЛОКАЛЬНЫЕ ПЛАГИНЫ =========="
            );

            foreach (
                Plugin plugin
                in plugins.GetAll()
            )
            {
                if (plugin == null)
                    continue;

                Puts(
                    plugin.Name +
                    " | " +
                    GetPluginVersion(
                        plugin
                    )
                );
            }

            Puts(
                "======================================="
            );
        }

        #endregion

        #region Protected

        private bool IsProtected(
            string pluginName
        )
        {
            if (string.IsNullOrEmpty(pluginName))
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
                "========== ЗАЩИЩЁННЫЕ ПЛАГИНЫ =========="
            );

            foreach (
                string plugin
                in config.ProtectedPlugins
            )
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

        #region Helpers

        private string GetPluginVersion(
            Plugin plugin
        )
        {
            if (plugin == null)
                return "0.0.0";

            return plugin.Version != null
                ? plugin.Version.ToString()
                : "0.0.0";
        }

        private string GetDownloadUrl(
            LatestPluginInfo info
        )
        {
            if (
                info == null
            )
                return null;

            if (
                !string.IsNullOrEmpty(
                    info.download_url
                )
            )
            {
                return info.download_url;
            }

            if (
                !string.IsNullOrEmpty(
                    info.download
                )
            )
            {
                return info.download;
            }

            return null;
        }

        private void Warn(
            string message
        )
        {
            PrintWarning(
                message
            );
        }

        private void NotifyAdmin(
            string message
        )
        {
            if (!config.NotifyAdmins)
                return;

            foreach (
                BasePlayer player
                in BasePlayer.activePlayerList
            )
            {
                if (player == null)
                    continue;

                if (!player.IsAdmin)
                    continue;

                SendReply(
                    player,
                    "<color=#00ff88>[MetalicRust]</color> " +
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
