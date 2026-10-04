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
    [Info("MetalicRustAutoUpdater", "MetalicRust", "3.1.1")]
    [Description("MetalicRust: безопасное обновление плагинов только из uMod и GitHub.")]
    public class MetalicRustAutoUpdater : RustPlugin
    {
        private Configuration config;
        private Timer updateTimer;
        private bool checkRunning;
        private readonly HashSet<string> updating = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PluginStatus> statuses = new Dictionary<string, PluginStatus>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> sourceCooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private bool initializedOnce;

        #region Configuration

        private class PluginEntry
        {
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Источник")] public string Source;
            [JsonProperty("uMod slug")] public string Slug;
            [JsonProperty("GitHub файл")] public string GitHubFile;
            [JsonProperty("Локальный файл")] public string FileName;
            [JsonProperty("Автообновление")] public bool AutoUpdate = true;
            [JsonProperty("Защищён")] public bool Protected;

            public PluginEntry() { }

            public PluginEntry(string name, string source, string slug, string githubFile, string fileName, bool autoUpdate, bool protectedPlugin)
            {
                Name = name;
                Source = source;
                Slug = slug;
                GitHubFile = githubFile;
                FileName = fileName;
                AutoUpdate = autoUpdate;
                Protected = protectedPlugin;
            }
        }

        private class Configuration
        {
            [JsonProperty("Автоматически обновлять плагины")]
            public bool AutoUpdate = true;

            [JsonProperty("Проверять обновления каждые N минут")]
            public float CheckIntervalMinutes = 60f;

            [JsonProperty("Задержка первой проверки")]
            public float InitialCheckDelay = 30f;

            [JsonProperty("Задержка перед установкой")]
            public float UpdateDelay = 5f;

            [JsonProperty("Задержка между сетевыми запросами")]
            public float RequestDelay = 0.75f;

            [JsonProperty("Количество сетевых повторов")]
            public int NetworkRetries = 2;

            [JsonProperty("Задержка между повторами")]
            public float NetworkRetryDelay = 10f;

            [JsonProperty("Задержка после HTTP 429")]
            public float RateLimitRetryDelay = 120f;

            [JsonProperty("Пауза после HTTP 404, минут")]
            public float NotFoundCooldownMinutes = 360f;

            [JsonProperty("Создавать .bak")]
            public bool CreateBackup = true;

            [JsonProperty("Откатывать при ошибке reload")]
            public bool RollbackOnError = true;

            [JsonProperty("Проверять после reload")]
            public bool VerifyAfterReload = true;

            [JsonProperty("Задержка проверки после reload")]
            public float VerifyDelay = 6f;

            [JsonProperty("Уведомлять администраторов")]
            public bool NotifyAdmins = true;

            [JsonProperty("GitHub Base URL")]
            public string GitHubBaseUrl = "https://raw.githubusercontent.com/qwrwetqa/MetalicRust-Plugins/main/";

            [JsonProperty("Плагины для обновления")]
            public List<PluginEntry> Plugins = DefaultPlugins();

            public static List<PluginEntry> DefaultPlugins()
            {
                var list = new List<PluginEntry>();

                // uMod. Только плагины, для которых источник действительно uMod.
                AddUmod(list, "Advert Messages", "advert-messages", "AdvertMessages.cs");
                AddUmod(list, "Better Chat", "better-chat", "BetterChat.cs");
                AddUmod(list, "Building Skins", "building-skins", "BuildingSkins.cs");
                AddUmod(list, "Building Workbench", "building-workbench", "BuildingWorkbench.cs");
                AddUmod(list, "Coloured Chat", "coloured-chat", "ColouredChat.cs");
                AddUmod(list, "Connection DB", "connection-db", "ConnectionDB.cs");
                AddUmod(list, "Copy Paste", "copy-paste", "CopyPaste.cs");
                AddUmod(list, "Custom Genetics", "custom-genetics", "CustomGenetics.cs");
                AddUmod(list, "Friends", "friends", "Friends.cs");
                AddUmod(list, "GUIAnnouncements", "gui-announcements", "GUIAnnouncements.cs");
                AddUmod(list, "Gather Manager", "gather-manager", "GatherManager.cs");
                AddUmod(list, "Gather Rewards", "gather-rewards", "GatherRewards.cs");
                AddUmod(list, "Image Library", "image-library", "ImageLibrary.cs");
                AddUmod(list, "Inventory Viewer", "inventory-viewer", "InventoryViewer.cs");
                AddUmod(list, "Kill Rewards", "kill-rewards", "KillRewards.cs");
                AddUmod(list, "NTeleportation", "nteleportation", "NTeleportation.cs");
                AddUmod(list, "No Give Notices", "no-give-notices", "NoGiveNotices.cs");
                AddUmod(list, "PlayerAdministration", "player-administration", "PlayerAdministration.cs");
                AddUmod(list, "Player Rankings", "player-rankings", "PlayerRankings.cs");
                AddUmod(list, "Playtime Tracker", "playtime-tracker", "PlaytimeTracker.cs");
                AddUmod(list, "Portable Vehicles", "portable-vehicles", "PortableVehicles.cs");
                AddUmod(list, "Quests", "quests", "Quests.cs");
                AddUmod(list, "Remover Tool", "remover-tool", "RemoverTool.cs");
                AddUmod(list, "Server Rewards", "server-rewards", "ServerRewards.cs");
                AddUmod(list, "Stack Size Controller", "stack-size-controller", "StackSizeController.cs");
                AddUmod(list, "TimeOfDay", "time-of-day", "TimeOfDay.cs");
                AddUmod(list, "Timed Permissions", "timed-permissions", "TimedPermissions.cs");
                AddUmod(list, "Vanish", "vanish", "Vanish.cs");
                AddUmod(list, "Welcomer", "welcomer", "Welcomer.cs");
                AddUmod(list, "Backpacks", "backpacks", "Backpacks.cs");

                // GitHub: MetalicRust repository.
                // Эти три файла сейчас отсутствуют в репозитории qwrwetqa/MetalicRust-Plugins.
                // Не делаем бессмысленные HTTP 404 на каждом цикле.
                AddGitHub(list, "InfoMenu", "InfoMenu.cs", "InfoMenu.cs", true);
                AddGitHub(list, "MetalicRust", "MetalicRust.cs", "MetalicRust.cs", false);
                AddGitHub(list, "MetalicRustRemoveStarterItems", "MetalicRustRemoveStarterItems.cs", "MetalicRustRemoveStarterItems.cs", true);
                AddGitHub(list, "MetalicRustReportSystem", "MetalicRustReportSystem.cs", "MetalicRustReportSystem.cs", true);
                AddGitHub(list, "MetalicRustStreamerRewards", "MetalicRustStreamerRewards.cs", "MetalicRustStreamerRewards.cs", false);
                AddGitHub(list, "MetalicRust TopTime", "MetalicRustTopTime.cs", "MetalicRustTopTime.cs", false);
                AddGitHub(list, "Quick Smelt", "QuickSmelt.cs", "QuickSmelt.cs", false);
                AddGitHub(list, "MetalicRustAutoUpdater", "MetalicRustAutoUpdater.cs", "MetalicRustAutoUpdater.cs", true);

                // Raidable Bases намеренно не обновляется автоматически:
                // автор плагина прямо запрещает auto-updaters для этого плагина.
                // Он может быть добавлен вручную в конфиг как Protected=true, но здесь
                // специально отсутствует, чтобы updater никогда не трогал его.

                return list;
            }

            private static void AddUmod(List<PluginEntry> list, string name, string slug, string fileName)
            {
                list.Add(new PluginEntry(name, "uMod", slug, null, fileName, true, false));
            }

            private static void AddGitHub(List<PluginEntry> list, string name, string fileName, string githubFile, bool protectedPlugin)
            {
                list.Add(new PluginEntry(name, "GitHub", null, githubFile, fileName, !protectedPlugin, protectedPlugin));
            }
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
                PrintWarning("[AutoUpdater] Ошибка config: " + ex.Message);
                LoadDefaultConfig();
                return;
            }

            // Канонизируем список: старые конфиги могли содержать 76/141 устаревших записей.
            // Сохраняем настройки существующих записей, но оставляем только актуальный список 37 плагинов.
            List<PluginEntry> defaults = Configuration.DefaultPlugins();
            Dictionary<string, PluginEntry> oldEntries = (config.Plugins ?? new List<PluginEntry>())
                .Where(x => x != null && !string.IsNullOrEmpty(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (PluginEntry entry in defaults)
            {
                PluginEntry old;
                if (oldEntries.TryGetValue(entry.Name, out old))
                {
                    entry.AutoUpdate = old.AutoUpdate;
                    entry.Protected = entry.Protected || old.Protected;
                }
            }

            config.Plugins = defaults;
            SaveConfig();
        }

        private bool IsAllowedEntry(PluginEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Name))
                return false;

            if (!string.Equals(e.Source, "uMod", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(e.Source, "GitHub", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(e.Name, "RaidableBases", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            LoadConfig();

            PrintWarning("================================================");
            PrintWarning("MetalicRust AutoUpdater 3.0.1");
            PrintWarning("Разрешённые источники: ТОЛЬКО uMod + GitHub");
            PrintWarning("Плагинов в автообновлении: " + config.Plugins.Count);
            PrintWarning("Автообновление: " + (config.AutoUpdate ? "ВКЛ" : "ВЫКЛ"));
            PrintWarning("================================================");
        }

        private void OnServerInitialized()
        {
            if (initializedOnce)
                return;

            initializedOnce = true;

            if (updateTimer != null)
                updateTimer.Destroy();

            updateTimer = timer.Every(Mathf.Max(5f, config.CheckIntervalMinutes) * 60f, delegate
            {
                CheckAll(config.AutoUpdate);
            });

            timer.Once(Mathf.Max(5f, config.InitialCheckDelay), delegate
            {
                if (!checkRunning)
                    CheckAll(config.AutoUpdate);
            });
        }

        private void Unload()
        {
            if (updateTimer != null)
                updateTimer.Destroy();

            updateTimer = null;
            checkRunning = false;
            initializedOnce = false;
            updating.Clear();
            sourceCooldowns.Clear();
        }

        #endregion

        #region Console

        [ConsoleCommand("mrupdate")]
        private void CmdMrUpdate(ConsoleSystem.Arg arg)
        {
            string command = arg == null ? "status" : arg.GetString(0, "status").ToLowerInvariant();

            switch (command)
            {
                case "check":
                    CheckAll(config.AutoUpdate);
                    break;

                case "update":
                    CheckAll(true);
                    break;

                case "scan":
                    CheckAll(false);
                    break;

                case "status":
                    PrintStatus();
                    break;

                case "local":
                    PrintLocal();
                    break;

                case "on":
                    config.AutoUpdate = true;
                    SaveConfig();
                    Puts("[AutoUpdater] Автообновление ВКЛ.");
                    break;

                case "off":
                    config.AutoUpdate = false;
                    SaveConfig();
                    Puts("[AutoUpdater] Автообновление ВЫКЛ.");
                    break;

                default:
                    Puts("mrupdate check   - проверка + обновление, если автообновление ВКЛ");
                    Puts("mrupdate update  - принудительная проверка + обновление");
                    Puts("mrupdate scan    - только проверка без установки");
                    Puts("mrupdate status  - статус");
                    Puts("mrupdate local   - локальные версии");
                    Puts("mrupdate on      - автообновление ВКЛ");
                    Puts("mrupdate off     - автообновление ВЫКЛ");
                    break;
            }
        }

        #endregion

        #region Check queue

        private void CheckAll(bool doUpdate)
        {
            if (checkRunning)
                return;

            checkRunning = true;
            statuses.Clear();

            List<PluginEntry> entries = config.Plugins
                .Where(x => x != null && IsAllowedEntry(x))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            Puts("[AutoUpdater] ===============================================");
            Puts("[AutoUpdater] Проверка " + entries.Count + " плагинов.");
            Puts("[AutoUpdater] Источники: только uMod / GitHub.");
            Puts("[AutoUpdater] Очередь запросов: 1 за раз, защита от 404/429.");
            Puts("[AutoUpdater] ===============================================");

            ProcessEntry(entries, 0, doUpdate);
        }

        private void ProcessEntry(List<PluginEntry> entries, int index, bool doUpdate)
        {
            if (index >= entries.Count)
            {
                FinishCheck();
                return;
            }

            PluginEntry entry = entries[index];

            if (entry.Protected)
            {
                string localProtected = GetLocalVersion(entry);
                SetStatus(entry.Name, entry.Source, localProtected, null, null, VersionState.Protected, "Защищён");
                ScheduleNext(entries, index + 1, doUpdate);
                return;
            }

            if (!entry.AutoUpdate && doUpdate)
            {
                SetStatus(entry.Name, entry.Source, GetLocalVersion(entry), null, null, VersionState.Protected, "Автообновление отключено");
                ScheduleNext(entries, index + 1, doUpdate);
                return;
            }

            if (string.Equals(entry.Source, "uMod", StringComparison.OrdinalIgnoreCase))
                CheckUmod(entry, entries, index, doUpdate);
            else
                CheckGitHub(entry, entries, index, doUpdate);
        }

        private void ScheduleNext(List<PluginEntry> entries, int nextIndex, bool doUpdate)
        {
            timer.Once(Mathf.Max(0.1f, config.RequestDelay), delegate
            {
                ProcessEntry(entries, nextIndex, doUpdate);
            });
        }

        private void FinishCheck()
        {
            checkRunning = false;

            int updates = statuses.Values.Count(x => x.State == VersionState.UpdateAvailable);
            int updated = statuses.Values.Count(x => x.State == VersionState.Updated);

            Puts("[AutoUpdater] ===============================================");
            Puts("[AutoUpdater] ПРОВЕРКА ЗАВЕРШЕНА");
            Puts("[AutoUpdater] Доступно обновлений: " + updates);
            Puts("[AutoUpdater] Обновлено сейчас: " + updated);
            Puts("[AutoUpdater] ===============================================");
        }

        #endregion

        #region uMod

        private void CheckUmod(PluginEntry entry, List<PluginEntry> entries, int index, bool doUpdate)
        {
            string localVersion = GetLocalVersion(entry);
            string url = "https://umod.org/plugins/" + entry.Slug + "/latest.json";

            Puts("[uMod] Проверка: " + entry.Name + " | local=" + localVersion);

            if (IsSourceCoolingDown(entry.Name))
            {
                SetStatus(entry.Name, "uMod", localVersion, null, null, VersionState.Cooldown, "Пропуск после предыдущего HTTP 404/429");
                ScheduleNext(entries, index + 1, doUpdate);
                return;
            }

            GetWithRetry(url, config.NetworkRetries, delegate(int code, string response)
            {
                if (code != 200 || string.IsNullOrEmpty(response))
                {
                    VersionState state = code == 404 ? VersionState.NotFound : (code == 429 ? VersionState.RateLimited : VersionState.NetworkError);
                    string message = "HTTP " + code;
                    SetStatus(entry.Name, "uMod", localVersion, null, null, state, message);
                    Warn("[uMod] " + entry.Name + " | " + message);
                    if (code == 404 || code == 429)
                        SetSourceCooldown(entry.Name, code == 404 ? config.NotFoundCooldownMinutes : config.RateLimitRetryDelay / 60f);
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                LatestPluginInfo latest;
                try
                {
                    latest = JsonConvert.DeserializeObject<LatestPluginInfo>(response);
                }
                catch (Exception ex)
                {
                    SetStatus(entry.Name, "uMod", localVersion, null, null, VersionState.NetworkError, "JSON: " + ex.Message);
                    Warn("[uMod] " + entry.Name + " | JSON ошибка.");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                if (latest == null || string.IsNullOrEmpty(latest.version))
                {
                    SetStatus(entry.Name, "uMod", localVersion, null, null, VersionState.NetworkError, "Версия отсутствует");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                string remoteVersion = latest.version;
                int cmp = CompareVersions(localVersion, remoteVersion);

                if (cmp == 0)
                {
                    SetStatus(entry.Name, "uMod", localVersion, remoteVersion, remoteVersion, VersionState.UpToDate, null);
                    Puts("[uMod] " + entry.Name + " | " + localVersion + " = " + remoteVersion + " | OK");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                if (cmp > 0)
                {
                    SetStatus(entry.Name, "uMod", localVersion, remoteVersion, remoteVersion, VersionState.FileNewer, "Локальная версия новее");
                    Warn("[uMod] " + entry.Name + " | local=" + localVersion + " > remote=" + remoteVersion + " | DOWNGRADE запрещён");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                SetStatus(entry.Name, "uMod", localVersion, remoteVersion, remoteVersion, VersionState.UpdateAvailable, null);
                Puts("[uMod] " + entry.Name + " | UPDATE: " + localVersion + " -> " + remoteVersion);

                if (!doUpdate)
                {
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                string downloadUrl = latest.download_url;
                if (string.IsNullOrEmpty(downloadUrl))
                    downloadUrl = latest.download;

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    SetStatus(entry.Name, "uMod", localVersion, remoteVersion, remoteVersion, VersionState.NetworkError, "download_url отсутствует");
                    Warn("[uMod] " + entry.Name + " | download_url отсутствует");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                timer.Once(Mathf.Max(1f, config.UpdateDelay), delegate
                {
                    DownloadAndInstall(entry, downloadUrl, remoteVersion, "uMod", null, delegate
                    {
                        ScheduleNext(entries, index + 1, doUpdate);
                    });
                });
            });
        }

        #endregion

        #region GitHub

        private void CheckGitHub(PluginEntry entry, List<PluginEntry> entries, int index, bool doUpdate)
        {
            string localVersion = GetLocalVersion(entry);
            string fileName = string.IsNullOrEmpty(entry.GitHubFile) ? entry.FileName : entry.GitHubFile;
            string url = config.GitHubBaseUrl.TrimEnd('/') + "/" + fileName;

            Puts("[GitHub] Проверка: " + entry.Name + " | local=" + localVersion + " | file=" + fileName);

            if (IsSourceCoolingDown(entry.Name))
            {
                SetStatus(entry.Name, "GitHub", localVersion, null, null, VersionState.Cooldown, "Пропуск после предыдущего HTTP 404/429");
                ScheduleNext(entries, index + 1, doUpdate);
                return;
            }

            // versions.json здесь НЕ используется.
            // Версия берётся прямо из [Info(...)] удалённого .cs,
            // поэтому старый manifest никогда не сможет откатить InfoMenu.
            GetWithRetry(url, config.NetworkRetries, delegate(int code, string source)
            {
                if (code != 200 || string.IsNullOrEmpty(source))
                {
                    VersionState state = code == 404 ? VersionState.NotFound : (code == 429 ? VersionState.RateLimited : VersionState.NetworkError);
                    string message = "HTTP " + code;
                    SetStatus(entry.Name, "GitHub", localVersion, null, null, state, message);
                    Warn("[GitHub] " + entry.Name + " | " + message);
                    if (code == 404 || code == 429)
                        SetSourceCooldown(entry.Name, code == 404 ? config.NotFoundCooldownMinutes : config.RateLimitRetryDelay / 60f);
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                string remoteVersion = ExtractInfoVersion(source);

                if (string.IsNullOrEmpty(remoteVersion))
                {
                    SetStatus(entry.Name, "GitHub", localVersion, null, null, VersionState.NetworkError, "В .cs нет [Info(...)]");
                    Warn("[GitHub] " + entry.Name + " | версия в .cs не найдена");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                int cmp = CompareVersions(localVersion, remoteVersion);

                if (cmp == 0)
                {
                    SetStatus(entry.Name, "GitHub", localVersion, remoteVersion, remoteVersion, VersionState.UpToDate, null);
                    Puts("[GitHub] " + entry.Name + " | " + localVersion + " = " + remoteVersion + " | OK");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                if (cmp > 0)
                {
                    SetStatus(entry.Name, "GitHub", localVersion, remoteVersion, remoteVersion, VersionState.FileNewer, "Локальная версия новее");
                    Warn("[GitHub] " + entry.Name + " | local=" + localVersion + " > remote=" + remoteVersion + " | DOWNGRADE запрещён");
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                SetStatus(entry.Name, "GitHub", localVersion, remoteVersion, remoteVersion, VersionState.UpdateAvailable, null);
                Puts("[GitHub] " + entry.Name + " | UPDATE: " + localVersion + " -> " + remoteVersion);

                if (!doUpdate)
                {
                    ScheduleNext(entries, index + 1, doUpdate);
                    return;
                }

                timer.Once(Mathf.Max(1f, config.UpdateDelay), delegate
                {
                    DownloadAndInstall(entry, url, remoteVersion, "GitHub", source, delegate
                    {
                        ScheduleNext(entries, index + 1, doUpdate);
                    });
                });
            });
        }

        #endregion

        #region Install

        private void DownloadAndInstall(PluginEntry entry, string url, string expectedVersion, string sourceType, string alreadyDownloaded, Action finished)
        {
            if (updating.Contains(entry.Name))
            {
                finished();
                return;
            }

            updating.Add(entry.Name);

            Action<string> install = delegate(string source)
            {
                try
                {
                    string filePath = FindPluginFile(entry);

                    if (string.IsNullOrEmpty(filePath))
                    {
                        SetStatus(entry.Name, sourceType, "?", expectedVersion, expectedVersion, VersionState.NotLoaded, "Локальный .cs файл не найден");
                        PrintError("[" + sourceType + "] " + entry.Name + " | .cs не найден: " + entry.FileName);
                        updating.Remove(entry.Name);
                        finished();
                        return;
                    }

                    string downloadedVersion = ExtractInfoVersion(source);

                    if (string.IsNullOrEmpty(downloadedVersion))
                    {
                        PrintError("[" + sourceType + "] " + entry.Name + " | версия в скачанном файле не найдена");
                        updating.Remove(entry.Name);
                        finished();
                        return;
                    }

                    if (CompareVersions(downloadedVersion, expectedVersion) != 0)
                    {
                        PrintError("[" + sourceType + "] " + entry.Name + " | ожидалась " + expectedVersion + ", скачано " + downloadedVersion);
                        updating.Remove(entry.Name);
                        finished();
                        return;
                    }

                    string localVersion = ExtractInfoVersion(File.ReadAllText(filePath));
                    if (string.IsNullOrEmpty(localVersion))
                        localVersion = "0.0.0";

                    if (CompareVersions(downloadedVersion, localVersion) <= 0)
                    {
                        Warn("[" + sourceType + "] " + entry.Name + " | downgrade/повтор запрещён: local=" + localVersion + ", remote=" + downloadedVersion);
                        updating.Remove(entry.Name);
                        finished();
                        return;
                    }

                    string backupPath = filePath + ".bak";
                    if (config.CreateBackup)
                        File.Copy(filePath, backupPath, true);

                    // Всегда unload перед записью, чтобы старый экземпляр не оставался в памяти.
                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "oxide.unload \"" + entry.Name + "\"");

                    File.WriteAllText(filePath, source);

                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "oxide.load \"" + entry.Name + "\"");

                    UpdateOperation op = new UpdateOperation
                    {
                        PluginName = entry.Name,
                        FilePath = filePath,
                        BackupPath = backupPath,
                        ExpectedVersion = downloadedVersion,
                        SourceType = sourceType
                    };

                    if (config.VerifyAfterReload)
                    {
                        timer.Once(Mathf.Max(1f, config.VerifyDelay), delegate
                        {
                            VerifyUpdate(op, finished);
                        });
                    }
                    else
                    {
                        CompleteUpdate(op);
                        finished();
                    }
                }
                catch (Exception ex)
                {
                    PrintError("[" + sourceType + "] " + entry.Name + " | установка: " + ex.Message);
                    updating.Remove(entry.Name);
                    finished();
                }
            };

            if (!string.IsNullOrEmpty(alreadyDownloaded))
            {
                install(alreadyDownloaded);
                return;
            }

            GetWithRetry(url, config.NetworkRetries, delegate(int code, string response)
            {
                if (code != 200 || string.IsNullOrEmpty(response))
                {
                    if (code == 429)
                    {
                        SetStatus(entry.Name, sourceType, GetLocalVersion(entry), expectedVersion, expectedVersion, VersionState.RateLimited, "HTTP 429 — uMod ограничил скачивание; повтор позже");
                        Warn("[" + sourceType + "] " + entry.Name + " | HTTP 429 — повтор будет в следующем цикле");
                        SetSourceCooldown(entry.Name, config.RateLimitRetryDelay / 60f);
                    }
                    else
                    {
                        PrintError("[" + sourceType + "] " + entry.Name + " | скачивание HTTP " + code);
                    }

                    updating.Remove(entry.Name);
                    finished();
                    return;
                }

                install(response);
            });
        }

        private void VerifyUpdate(UpdateOperation op, Action finished)
        {
            string filePath = op.FilePath;

            try
            {
                if (!File.Exists(filePath))
                    throw new Exception("Файл после обновления отсутствует");

                string source = File.ReadAllText(filePath);
                string loadedVersionFromFile = ExtractInfoVersion(source);

                if (string.IsNullOrEmpty(loadedVersionFromFile) ||
                    CompareVersions(loadedVersionFromFile, op.ExpectedVersion) != 0)
                    throw new Exception("Версия файла не совпадает: " + loadedVersionFromFile);

                Plugin loaded = FindLoadedPlugin(op.PluginName);

                if (loaded == null)
                    throw new Exception("Плагин не загрузился после reload");

                string loadedVersion = GetPluginVersion(loaded);

                if (CompareVersions(loadedVersion, op.ExpectedVersion) != 0)
                    throw new Exception("Загружена версия " + loadedVersion + ", ожидалась " + op.ExpectedVersion);

                CompleteUpdate(op);
                finished();
            }
            catch (Exception ex)
            {
                PrintError("[VERIFY] " + op.PluginName + " | " + ex.Message);

                if (config.RollbackOnError)
                    Rollback(op, finished);
                else
                {
                    updating.Remove(op.PluginName);
                    finished();
                }
            }
        }

        private void CompleteUpdate(UpdateOperation op)
        {
            updating.Remove(op.PluginName);

            SetStatus(op.PluginName, op.SourceType, op.ExpectedVersion, op.ExpectedVersion, op.ExpectedVersion, VersionState.Updated, null);

            Puts("[" + op.SourceType + "] " + op.PluginName + " | УСПЕШНО ОБНОВЛЁН до " + op.ExpectedVersion);
            NotifyAdmin(op.PluginName + " обновлён до " + op.ExpectedVersion);
        }

        private void Rollback(UpdateOperation op, Action finished)
        {
            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "oxide.unload \"" + op.PluginName + "\"");

                if (!File.Exists(op.BackupPath))
                    throw new Exception(".bak не найден");

                File.Copy(op.BackupPath, op.FilePath, true);
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "oxide.load \"" + op.PluginName + "\"");

                SetStatus(op.PluginName, op.SourceType, null, op.ExpectedVersion, op.ExpectedVersion, VersionState.Rollback, "Автоматический откат");

                NotifyAdmin(op.PluginName + " — ошибка обновления, выполнен откат.");
            }
            catch (Exception ex)
            {
                PrintError("[ROLLBACK] " + op.PluginName + " | " + ex.Message);
            }

            updating.Remove(op.PluginName);
            finished();
        }

        #endregion

        #region Network

        private void GetWithRetry(string url, int retries, Action<int, string> callback)
        {
            EnqueueAttempt(url, Mathf.Max(1, retries), callback, 0);
        }

        private void EnqueueAttempt(string url, int remaining, Action<int, string> callback, int attempt)
        {
            webrequest.EnqueueGet(url, delegate(int code, string response)
            {
                if (code == 200 && !string.IsNullOrEmpty(response))
                {
                    callback(code, response);
                    return;
                }

                // 404 — ресурс реально отсутствует. Повторять бессмысленно.
                if (code == 404)
                {
                    callback(code, response);
                    return;
                }

                // 429 — сервер ограничил частоту. Не спамим uMod повторными запросами.
                if (code == 429)
                {
                    if (remaining <= 1)
                    {
                        callback(code, response);
                        return;
                    }

                    float delay429 = Mathf.Max(30f, config.RateLimitRetryDelay);
                    Warn("[Network] HTTP 429 | повтор через " + delay429 + " сек.");
                    timer.Once(delay429, delegate
                    {
                        EnqueueAttempt(url, remaining - 1, callback, attempt + 1);
                    });
                    return;
                }

                // Сетевые/5xx ошибки повторяем с увеличением интервала.
                if (remaining <= 1)
                {
                    callback(code, response);
                    return;
                }

                float delay = Mathf.Min(
                    Mathf.Max(2f, config.NetworkRetryDelay) * Mathf.Pow(2f, attempt),
                    60f
                );

                Warn("[Network] HTTP " + code + " | повтор через " + delay + " сек.");
                timer.Once(delay, delegate
                {
                    EnqueueAttempt(url, remaining - 1, callback, attempt + 1);
                });
            }, this);
        }

        private bool IsSourceCoolingDown(string key)
        {
            DateTime until;
            if (!sourceCooldowns.TryGetValue(key, out until))
                return false;

            if (DateTime.UtcNow >= until)
            {
                sourceCooldowns.Remove(key);
                return false;
            }

            return true;
        }

        private void SetSourceCooldown(string key, float minutes)
        {
            sourceCooldowns[key] = DateTime.UtcNow.AddMinutes(Mathf.Max(1f, minutes));
        }

        #endregion

        #region Local files

        private string FindPluginFile(PluginEntry entry)
        {
            string directory = Interface.Oxide.PluginDirectory;

            if (!string.IsNullOrEmpty(entry.FileName))
            {
                string exact = Path.Combine(directory, entry.FileName);
                if (File.Exists(exact))
                    return exact;

                string caseInsensitive = Directory.GetFiles(directory, "*.cs")
                    .FirstOrDefault(x => Path.GetFileName(x).Equals(entry.FileName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(caseInsensitive))
                    return caseInsensitive;
            }

            string byName = Path.Combine(directory, entry.Name + ".cs");
            if (File.Exists(byName))
                return byName;

            return Directory.GetFiles(directory, "*.cs")
                .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        }

        private string GetLocalVersion(PluginEntry entry)
        {
            string file = FindPluginFile(entry);

            if (!string.IsNullOrEmpty(file))
            {
                try
                {
                    string version = ExtractInfoVersion(File.ReadAllText(file));
                    if (!string.IsNullOrEmpty(version))
                        return version;
                }
                catch { }
            }

            Plugin loaded = FindLoadedPlugin(entry.Name);
            return loaded == null ? "0.0.0" : GetPluginVersion(loaded);
        }

        private Plugin FindLoadedPlugin(string name)
        {
            Plugin plugin = plugins.Find(name);
            if (plugin != null)
                return plugin;

            foreach (Plugin p in plugins.GetAll())
            {
                if (p == null)
                    continue;

                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p;

                if (!string.IsNullOrEmpty(p.Title) && string.Equals(p.Title, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return null;
        }

        private void PrintLocal()
        {
            Puts("========== LOCAL VERSIONS ==========");

            foreach (PluginEntry entry in config.Plugins)
                Puts(entry.Name + " | " + entry.Source + " | " + GetLocalVersion(entry));

            Puts("====================================");
        }

        #endregion

        #region Status

        private class PluginStatus
        {
            public string Name;
            public string Source;
            public string Current;
            public string Remote;
            public VersionState State;
            public string Error;
        }

        private enum VersionState
        {
            Unknown,
            UpToDate,
            UpdateAvailable,
            FileNewer,
            NetworkError,
            RateLimited,
            NotFound,
            Cooldown,
            NotLoaded,
            Protected,
            Updated,
            Rollback
        }

        private void SetStatus(string name, string source, string current, string manifest, string remote, VersionState state, string error)
        {
            PluginStatus s;
            if (!statuses.TryGetValue(name, out s))
            {
                s = new PluginStatus();
                statuses[name] = s;
            }

            s.Name = name;
            s.Source = source;
            s.Current = current;
            s.Remote = remote;
            s.State = state;
            s.Error = error;
        }

        private void PrintStatus()
        {
            Puts("========== METALICRUST AUTOUPDATER ==========");

            foreach (PluginEntry entry in config.Plugins)
            {
                PluginStatus s;
                if (!statuses.TryGetValue(entry.Name, out s))
                {
                    Puts(entry.Name + " | НЕ ПРОВЕРЕН");
                    continue;
                }

                string current = string.IsNullOrEmpty(s.Current) ? "?" : s.Current;
                string remote = string.IsNullOrEmpty(s.Remote) ? "-" : s.Remote;

                Puts(entry.Name + " | " + s.Source + " | " + current + " -> " + remote + " | " + StateText(s.State));

                if (!string.IsNullOrEmpty(s.Error))
                    Puts("    " + s.Error);
            }

            Puts("==============================================");
        }

        private string StateText(VersionState state)
        {
            switch (state)
            {
                case VersionState.UpToDate: return "OK";
                case VersionState.UpdateAvailable: return "UPDATE";
                case VersionState.FileNewer: return "LOCAL NEWER";
                case VersionState.NetworkError: return "NETWORK ERROR";
                case VersionState.RateLimited: return "RATE LIMITED";
                case VersionState.NotFound: return "NOT FOUND";
                case VersionState.Cooldown: return "COOLDOWN";
                case VersionState.NotLoaded: return "NOT LOADED";
                case VersionState.Protected: return "PROTECTED";
                case VersionState.Updated: return "UPDATED";
                case VersionState.Rollback: return "ROLLBACK";
                default: return "UNKNOWN";
            }
        }

        #endregion

        #region Helpers

        private string ExtractInfoVersion(string source)
        {
            if (string.IsNullOrEmpty(source))
                return null;

            Match m = Regex.Match(
                source,
                @"\[Info\s*\(\s*""[^""]*""\s*,\s*""[^""]*""\s*,\s*""([^""]+)""\s*\)\]",
                RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private string GetPluginVersion(Plugin plugin)
        {
            if (plugin == null || plugin.Version == null)
                return "0.0.0";

            return plugin.Version.ToString();
        }

        private int CompareVersions(string left, string right)
        {
            Version a = ParseVersion(left);
            Version b = ParseVersion(right);
            return a.CompareTo(b);
        }

        private Version ParseVersion(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new Version(0, 0, 0, 0);

            string clean = Regex.Replace(value.Trim(), @"[^0-9\.]", "");
            string[] parts = clean.Split('.');

            int[] p = new int[4];

            for (int i = 0; i < p.Length && i < parts.Length; i++)
            {
                int n;
                if (int.TryParse(parts[i], out n))
                    p[i] = n;
            }

            return new Version(p[0], p[1], p[2], p[3]);
        }

        private void Warn(string message)
        {
            PrintWarning(message);
        }

        private void NotifyAdmin(string message)
        {
            if (!config.NotifyAdmins)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsAdmin)
                    continue;

                SendReply(player, "<color=#00ff88>[MetalicRust]</color> " + message);
            }
        }

        #endregion

        #region DTO

        private class LatestPluginInfo
        {
            public string version;
            public string download_url;
            public string download;
            public string title;
            public string name;
            public string author;
        }

        private class UpdateOperation
        {
            public string PluginName;
            public string FilePath;
            public string BackupPath;
            public string ExpectedVersion;
            public string SourceType;
        }

        #endregion
    }
}
