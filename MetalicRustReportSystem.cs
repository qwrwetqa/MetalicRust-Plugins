using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("MetalicRustReportSystem", "MetalicRust", "1.0.1")]
    [Description("Система F7-репортов для MetalicRust")]
    public class MetalicRustReportSystem : RustPlugin
    {
        private const string PermissionUse = "metalicrustreports.use";
        private const string PermissionAdmin = "metalicrustreports.admin";

        private StoredData data;
        private ConfigData config;

        private class StoredData
        {
            public int NextReportId = 1;
            public List<ReportData> Reports = new List<ReportData>();
        }

        private class ReportData
        {
            public int Id;

            public string ReporterName;
            public ulong ReporterId;

            public string TargetName;
            public ulong TargetId;

            public string Subject;
            public string Message;
            public string Type;

            public string CreatedAt;

            public bool Closed;
            public string ClosedBy;
            public string ClosedAt;
        }

        private class ConfigData
        {
            [JsonProperty("Максимальное количество открытых репортов на одного игрока")]
            public int MaxOpenReportsPerPlayer = 3;

            [JsonProperty("Разрешить повторный репорт одной цели")]
            public bool AllowDuplicateReports = false;

            [JsonProperty("Показывать репортеру сообщение после отправки")]
            public bool NotifyReporter = true;

            [JsonProperty("Уведомлять администраторов в консоли")]
            public bool ConsoleNotifyAdmins = true;

            [JsonProperty("Показывать репорты в чате администраторам")]
            public bool ChatNotifyAdmins = true;

            [JsonProperty("Минимальная длина сообщения репорта")]
            public int MinimumMessageLength = 3;

            [JsonProperty("Максимальная длина сообщения репорта")]
            public int MaximumMessageLength = 300;
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
            }
            catch
            {
                PrintWarning("Ошибка чтения конфига. Создаю новый.");
                LoadDefaultConfig();
            }

            if (config == null)
                LoadDefaultConfig();

            if (config.MaxOpenReportsPerPlayer < 1)
                config.MaxOpenReportsPerPlayer = 1;

            if (config.MinimumMessageLength < 1)
                config.MinimumMessageLength = 1;

            if (config.MaximumMessageLength < config.MinimumMessageLength)
                config.MaximumMessageLength = config.MinimumMessageLength;

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionAdmin, this);

            LoadData();

            AddCovalenceCommand("report", nameof(CommandReport));
            AddCovalenceCommand("reports", nameof(CommandReports));
            AddCovalenceCommand("reportinfo", nameof(CommandReportInfo));
            AddCovalenceCommand("reportclose", nameof(CommandReportClose));
        }

        private void OnServerInitialized()
        {
            Puts("======================================");
            Puts("MetalicRustReportSystem загружен.");
            Puts("F7 reports: ON");
            Puts("Команда игрока: /report");
            Puts("Команды администрации:");
            Puts("/reports");
            Puts("/reportinfo ID");
            Puts("/reportclose ID");
            Puts("======================================");
        }

        private void Unload()
        {
            SaveData();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(
                    "MetalicRustReportSystem"
                );
            }
            catch (Exception ex)
            {
                PrintWarning(
                    "Ошибка загрузки данных репортов: " + ex.Message
                );

                data = new StoredData();
            }

            if (data == null)
                data = new StoredData();

            if (data.Reports == null)
                data.Reports = new List<ReportData>();

            if (data.NextReportId <= 0)
                data.NextReportId = 1;
        }

        private void SaveData()
        {
            if (data == null)
                return;

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(
                    "MetalicRustReportSystem",
                    data
                );
            }
            catch (Exception ex)
            {
                PrintError(
                    "Ошибка сохранения данных репортов: " + ex.Message
                );
            }
        }

        private void OnPlayerReported(
            BasePlayer reporter,
            string targetName,
            string targetId,
            string subject,
            string message,
            string type)
        {
            if (reporter == null)
                return;

            if (!permission.UserHasPermission(
                reporter.UserIDString,
                PermissionUse))
                return;

            ulong targetUserId;

            if (!ulong.TryParse(targetId, out targetUserId))
            {
                Puts(
                    "[REPORT] Не удалось определить SteamID цели. " +
                    "Reporter=" + reporter.displayName +
                    ", Target=" + targetName
                );

                return;
            }

            if (targetUserId == reporter.userID)
            {
                reporter.ChatMessage(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Нельзя отправить жалобу на самого себя."
                );

                return;
            }

            string cleanSubject = CleanText(subject, 100);

            string cleanMessage = CleanText(
                message,
                config.MaximumMessageLength
            );

            string cleanType = CleanText(type, 50);

            if (string.IsNullOrEmpty(cleanMessage))
                cleanMessage = "Без дополнительного сообщения";

            if (cleanMessage.Length < config.MinimumMessageLength)
                cleanMessage = "Без дополнительного сообщения";

            int playerOpenReports = data.Reports.Count(
                x =>
                    !x.Closed &&
                    x.ReporterId == reporter.userID
            );

            if (playerOpenReports >= config.MaxOpenReportsPerPlayer)
            {
                reporter.ChatMessage(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "У вас уже есть " +
                    playerOpenReports +
                    " открытых репортов."
                );

                return;
            }

            if (!config.AllowDuplicateReports)
            {
                bool duplicate = data.Reports.Any(
                    x =>
                        !x.Closed &&
                        x.ReporterId == reporter.userID &&
                        x.TargetId == targetUserId
                );

                if (duplicate)
                {
                    reporter.ChatMessage(
                        "<color=#ff4444>[РЕПОРТ]</color> " +
                        "Вы уже отправляли репорт на этого игрока."
                    );

                    return;
                }
            }

            ReportData report = new ReportData
            {
                Id = data.NextReportId++,

                ReporterName = CleanText(
                    reporter.displayName,
                    80
                ),

                ReporterId = reporter.userID,

                TargetName = CleanText(
                    targetName,
                    80
                ),

                TargetId = targetUserId,

                Subject = cleanSubject,
                Message = cleanMessage,
                Type = cleanType,

                CreatedAt = DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"
                ),

                Closed = false,
                ClosedBy = string.Empty,
                ClosedAt = string.Empty
            };

            data.Reports.Add(report);
            SaveData();

            NotifyReporter(
                reporter,
                report
            );

            NotifyAdmins(report);

            Puts(
                "[REPORT #" + report.Id + "] " +
                report.ReporterName +
                " (" + report.ReporterId + ") -> " +
                report.TargetName +
                " (" + report.TargetId + ") | " +
                report.Subject +
                " | " +
                report.Message
            );
        }

        private void CommandReport(
            IPlayer player,
            string command,
            string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(
                player.Id,
                PermissionUse))
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "У вас нет доступа."
                );

                return;
            }

            ulong reporterId;

            if (!ulong.TryParse(
                player.Id,
                out reporterId))
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Не удалось определить ваш SteamID."
                );

                return;
            }

            if (args == null || args.Length == 0)
            {
                player.Reply(
                    "<color=#55ff22>Использование:</color> " +
                    "/report SteamID причина"
                );

                player.Reply(
                    "<color=#aaaaaa>Пример:</color> " +
                    "/report 76561198000000000 читерство"
                );

                return;
            }

            ulong targetId;

            if (!ulong.TryParse(
                args[0],
                out targetId))
            {
                BasePlayer target = FindPlayer(args[0]);

                if (target == null)
                {
                    player.Reply(
                        "<color=#ff4444>[РЕПОРТ]</color> " +
                        "Игрок не найден."
                    );

                    return;
                }

                targetId = target.userID;
            }

            if (targetId == reporterId)
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Нельзя пожаловаться на самого себя."
                );

                return;
            }

            BasePlayer targetPlayer = BasePlayer.FindByID(targetId);

            string targetName;

            if (targetPlayer != null)
                targetName = targetPlayer.displayName;
            else
                targetName = targetId.ToString();

            string reason = string.Join(
                " ",
                args.Skip(1).ToArray()
            );

            if (string.IsNullOrWhiteSpace(reason))
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Укажите причину."
                );

                return;
            }

            reason = CleanText(
                reason,
                config.MaximumMessageLength
            );

            if (reason.Length < config.MinimumMessageLength)
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Причина должна содержать минимум " +
                    config.MinimumMessageLength +
                    " символа."
                );

                return;
            }

            int openReports = data.Reports.Count(
                x =>
                    !x.Closed &&
                    x.ReporterId == reporterId
            );

            if (openReports >= config.MaxOpenReportsPerPlayer)
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "У вас слишком много открытых репортов."
                );

                return;
            }

            if (!config.AllowDuplicateReports)
            {
                bool duplicate = data.Reports.Any(
                    x =>
                        !x.Closed &&
                        x.ReporterId == reporterId &&
                        x.TargetId == targetId
                );

                if (duplicate)
                {
                    player.Reply(
                        "<color=#ff4444>[РЕПОРТ]</color> " +
                        "Вы уже отправляли репорт на этого игрока."
                    );

                    return;
                }
            }

            ReportData report = new ReportData
            {
                Id = data.NextReportId++,

                ReporterName = CleanText(
                    player.Name,
                    80
                ),

                ReporterId = reporterId,

                TargetName = CleanText(
                    targetName,
                    80
                ),

                TargetId = targetId,

                Subject = "Player Report",
                Message = reason,
                Type = "CHAT",

                CreatedAt = DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"
                ),

                Closed = false,
                ClosedBy = string.Empty,
                ClosedAt = string.Empty
            };

            data.Reports.Add(report);
            SaveData();

            player.Reply(
                "<color=#55ff22>[РЕПОРТ #" +
                report.Id +
                "]</color> " +
                "Жалоба на <color=#ffffff>" +
                targetName +
                "</color> отправлена администрации."
            );

            NotifyAdmins(report);

            Puts(
                "[REPORT #" +
                report.Id +
                "] " +
                player.Name +
                " (" +
                reporterId +
                ") -> " +
                targetName +
                " (" +
                targetId +
                ") | " +
                reason
            );
        }

        private void CommandReports(
            IPlayer player,
            string command,
            string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(
                player.Id,
                PermissionAdmin))
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Нет доступа."
                );

                return;
            }

            List<ReportData> openReports = data.Reports
                .Where(x => !x.Closed)
                .OrderBy(x => x.Id)
                .ToList();

            if (openReports.Count == 0)
            {
                player.Reply(
                    "<color=#55ff22>[РЕПОРТЫ]</color> " +
                    "Открытых репортов нет."
                );

                return;
            }

            player.Reply(
                "<color=#55ff22>" +
                "===== ОТКРЫТЫЕ РЕПОРТЫ: " +
                openReports.Count +
                " =====" +
                "</color>"
            );

            foreach (ReportData report in openReports)
            {
                player.Reply(
                    "<color=#ffd21f>#" +
                    report.Id +
                    "</color> " +
                    "<color=#ffffff>" +
                    report.TargetName +
                    "</color> " +
                    "<color=#aaaaaa>(" +
                    report.TargetId +
                    ")</color>"
                );

                player.Reply(
                    "  Репортер: " +
                    report.ReporterName +
                    " (" +
                    report.ReporterId +
                    ")"
                );

                player.Reply(
                    "  Причина: " +
                    report.Subject
                );

                player.Reply(
                    "  Сообщение: " +
                    report.Message
                );

                player.Reply(
                    "  Время: " +
                    report.CreatedAt
                );
            }

            player.Reply(
                "<color=#aaaaaa>/reportinfo ID</color> " +
                "— подробности"
            );

            player.Reply(
                "<color=#aaaaaa>/reportclose ID</color> " +
                "— закрыть"
            );
        }

        private void CommandReportInfo(
            IPlayer player,
            string command,
            string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(
                player.Id,
                PermissionAdmin))
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Нет доступа."
                );

                return;
            }

            if (args == null || args.Length == 0)
            {
                player.Reply(
                    "Использование: /reportinfo ID"
                );

                return;
            }

            int reportId;

            if (!int.TryParse(
                args[0],
                out reportId))
            {
                player.Reply(
                    "<color=#ff4444>" +
                    "Неверный ID репорта." +
                    "</color>"
                );

                return;
            }

            ReportData report = data.Reports.FirstOrDefault(
                x => x.Id == reportId
            );

            if (report == null)
            {
                player.Reply(
                    "<color=#ff4444>" +
                    "Репорт не найден." +
                    "</color>"
                );

                return;
            }

            player.Reply(
                "<color=#55ff22>" +
                "===== РЕПОРТ #" +
                report.Id +
                " =====" +
                "</color>"
            );

            player.Reply(
                "Статус: " +
                (report.Closed ? "Закрыт" : "Открыт")
            );

            player.Reply(
                "Репортер: " +
                report.ReporterName
            );

            player.Reply(
                "SteamID репортера: " +
                report.ReporterId
            );

            player.Reply(
                "Цель: " +
                report.TargetName
            );

            player.Reply(
                "SteamID цели: " +
                report.TargetId
            );

            player.Reply(
                "Тип: " +
                report.Type
            );

            player.Reply(
                "Тема: " +
                report.Subject
            );

            player.Reply(
                "Сообщение: " +
                report.Message
            );

            player.Reply(
                "Создан: " +
                report.CreatedAt
            );

            if (report.Closed)
            {
                player.Reply(
                    "Закрыл: " +
                    report.ClosedBy
                );

                player.Reply(
                    "Закрыт: " +
                    report.ClosedAt
                );
            }
        }

        private void CommandReportClose(
            IPlayer player,
            string command,
            string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(
                player.Id,
                PermissionAdmin))
            {
                player.Reply(
                    "<color=#ff4444>[РЕПОРТ]</color> " +
                    "Нет доступа."
                );

                return;
            }

            if (args == null || args.Length == 0)
            {
                player.Reply(
                    "Использование: /reportclose ID"
                );

                return;
            }

            int reportId;

            if (!int.TryParse(
                args[0],
                out reportId))
            {
                player.Reply(
                    "<color=#ff4444>" +
                    "Неверный ID репорта." +
                    "</color>"
                );

                return;
            }

            ReportData report = data.Reports.FirstOrDefault(
                x => x.Id == reportId
            );

            if (report == null)
            {
                player.Reply(
                    "<color=#ff4444>" +
                    "Репорт не найден." +
                    "</color>"
                );

                return;
            }

            if (report.Closed)
            {
                player.Reply(
                    "<color=#ffaa00>" +
                    "Этот репорт уже закрыт." +
                    "</color>"
                );

                return;
            }

            report.Closed = true;

            report.ClosedBy = CleanText(
                player.Name,
                80
            );

            report.ClosedAt = DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss"
            );

            SaveData();

            player.Reply(
                "<color=#55ff22>" +
                "[РЕПОРТ #" +
                report.Id +
                "]" +
                "</color> Закрыт."
            );

            BasePlayer reporter = BasePlayer.FindByID(
                report.ReporterId
            );

            if (reporter != null &&
                reporter.IsConnected)
            {
                reporter.ChatMessage(
                    "<color=#55ff22>" +
                    "[РЕПОРТ #" +
                    report.Id +
                    "]" +
                    "</color> " +
                    "Ваша жалоба была закрыта администрацией."
                );
            }

            Puts(
                "[REPORT #" +
                report.Id +
                "] закрыт администратором " +
                player.Name
            );
        }

        private void NotifyReporter(
            BasePlayer reporter,
            ReportData report)
        {
            if (!config.NotifyReporter)
                return;

            if (reporter == null)
                return;

            reporter.ChatMessage(
                "<color=#55ff22>" +
                "[РЕПОРТ #" +
                report.Id +
                "]" +
                "</color> " +
                "Жалоба на " +
                "<color=#ffffff>" +
                report.TargetName +
                "</color> отправлена."
            );
        }

        private void NotifyAdmins(
            ReportData report)
        {
            if (report == null)
                return;

            if (config.ConsoleNotifyAdmins)
            {
                Puts(
                    "[НОВЫЙ РЕПОРТ #" +
                    report.Id +
                    "] " +
                    report.ReporterName +
                    " -> " +
                    report.TargetName +
                    " | " +
                    report.Subject +
                    " | " +
                    report.Message
                );
            }

            if (!config.ChatNotifyAdmins)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null)
                    continue;

                if (!permission.UserHasPermission(
                    player.UserIDString,
                    PermissionAdmin))
                    continue;

                player.ChatMessage(
                    "<color=#ff4444>" +
                    "[НОВЫЙ РЕПОРТ #" +
                    report.Id +
                    "]" +
                    "</color> " +
                    report.ReporterName +
                    " -> " +
                    report.TargetName
                );

                player.ChatMessage(
                    "Причина: " +
                    report.Subject +
                    " | " +
                    report.Message
                );

                player.ChatMessage(
                    "<color=#aaaaaa>/reportinfo " +
                    report.Id +
                    "</color> — открыть"
                );
            }
        }

        private BasePlayer FindPlayer(
            string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId))
                return null;

            ulong id;

            if (ulong.TryParse(
                nameOrId,
                out id))
            {
                BasePlayer player = BasePlayer.FindByID(id);

                if (player != null)
                    return player;

                player = BasePlayer.FindSleeping(id);

                if (player != null)
                    return player;
            }

            string search = nameOrId.ToLowerInvariant();

            BasePlayer exact = null;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null)
                    continue;

                if (player.displayName.Equals(
                    nameOrId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return player;
                }

                if (player.displayName
                    .ToLowerInvariant()
                    .Contains(search))
                {
                    if (exact != null)
                        return null;

                    exact = player;
                }
            }

            return exact;
        }

        private string CleanText(
            string value,
            int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = value.Replace("\n", " ");
            value = value.Replace("\r", " ");

            while (value.Contains("  "))
                value = value.Replace("  ", " ");

            value = value.Trim();

            if (value.Length > maxLength)
                value = value.Substring(0, maxLength);

            return value;
        }
    }
}
