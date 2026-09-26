using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("InfoMenu", "MetalicRust", "2.2.0")]
    [Description("MetalicRust information menu redesigned to match the supplied reference image.")]
    public class InfoMenu : RustPlugin
    {
        private PluginConfig config;
        private const string MainLayer = "MetalicRust_InfoMenu";
        private const string DataFile = "InfoMenu";

        [PluginReference] private Plugin ImageLibrary;

        private Dictionary<ulong, PlayerData> players = new Dictionary<ulong, PlayerData>();

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            config = PluginConfig.DefaultConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<PluginConfig>();
            }
            catch
            {
                PrintWarning("Config is invalid. Creating a new default configuration.");
                LoadDefaultConfig();
                return;
            }

            if (config == null)
                config = PluginConfig.DefaultConfig();

            if (config.Version != Version)
            {
                config.Version = Version;
                SaveConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private class PluginConfig
        {
            [JsonProperty("Версия конфигурации")]
            public VersionNumber Version = new VersionNumber(2, 2, 0);

            [JsonProperty("Команды открытия меню")]
            public List<string> Commands = new List<string> { "info", "menu", "help" };

            [JsonProperty("Открывать при подключении")]
            public bool OpenOnConnection = true;

            [JsonProperty("Открывать только при первом подключении")]
            public bool FirstConnectionOnly = false;

            [JsonProperty("Заголовок сервера")]
            public string ServerTitle = "METALICRUST";

            [JsonProperty("Подзаголовок сервера")]
            public string ServerSubtitle = "VANILLA+  •  X2  •  NOLIMIT";

            [JsonProperty("Приветствие")]
            public string WelcomeTitle = "Добро пожаловать!";

            [JsonProperty("Приветствие. Строка 2")]
            public string WelcomeLine2 = "На сервер METALICRUST";

            [JsonProperty("Приветствие. Строка 3")]
            public string WelcomeLine3 = "Онлайн магазин: metalicrust.com";

            [JsonProperty("Информация справа в баннере")]
            public string BannerRightText =
                "ВАЙПЫ ПРОХОДЯТ ПО ВТОРНИКАМ В 14:00 ПО МСК\nПО СУББОТАМ В 13:00 ПО МСК";

            [JsonProperty("Показывать фоновое изображение")]
            public bool ShowBackground = false;

            [JsonProperty("Фоновое изображение URL")]
            public string BackgroundImage = "";

            [JsonProperty("Цвет затемнения")]
            public string BackgroundOverlay = "0 0 0 0.55";

            [JsonProperty("Цвет главной панели")]
            public string MainPanelColor = "0.10 0.13 0.14 0.97";

            [JsonProperty("Цвет боковой панели")]
            public string SidebarColor = "0.07 0.09 0.095 0.98";

            [JsonProperty("Цвет верхней панели")]
            public string TopBarColor = "0.15 0.18 0.19 0.98";

            [JsonProperty("Цвет карточек")]
            public string CardColor = "0.15 0.20 0.23 0.96";

            [JsonProperty("Зеленый акцент")]
            public string GreenColor = "0.20 1.00 0.08 1";

            [JsonProperty("Синий акцент")]
            public string BlueColor = "0.05 0.55 1.00 1";

            [JsonProperty("Цвет кнопок")]
            public string ButtonColor = "0.12 0.15 0.16 1";

            [JsonProperty("Цвет кнопок при наведении")]
            public string ButtonHoverColor = "0.18 0.22 0.23 1";

            [JsonProperty("Цвет кнопки закрытия")]
            public string CloseColor = "0.95 0.08 0.10 1";

            [JsonProperty("Цвет текста")]
            public string TextColor = "1 1 1 1";

            [JsonProperty("Вторичный цвет текста")]
            public string SecondaryTextColor = "0.75 0.78 0.80 1";

            [JsonProperty("Шрифт")]
            public string Font = "robotocondensed-bold.ttf";

            [JsonProperty("Размер кнопок")]
            public int ButtonFontSize = 15;

            [JsonProperty("Размер основного текста")]
            public int BodyFontSize = 14;

            [JsonProperty("Баннер URL")]
            public string BannerImage = "";

            [JsonProperty("Иконка магазина URL")]
            public string ShopIcon = "";

            [JsonProperty("Иконка Discord URL")]
            public string DiscordIcon = "";

            [JsonProperty("Иконка VK URL")]
            public string VkIcon = "";

            [JsonProperty("Иконка Telegram URL")]
            public string TelegramIcon = "";

            [JsonProperty("Иконка Steam URL")]
            public string SteamIcon = "";

            [JsonProperty("QR магазина URL")]
            public string ShopQr = "";

            [JsonProperty("QR Discord URL")]
            public string DiscordQr = "";

            [JsonProperty("QR VK URL")]
            public string VkQr = "";

            [JsonProperty("QR Telegram URL")]
            public string TelegramQr = "";

            [JsonProperty("QR Steam URL")]
            public string SteamQr = "";

            [JsonProperty("Ссылка магазина")]
            public string ShopUrl = "metalicrust.com";

            [JsonProperty("Ссылка Discord")]
            public string DiscordUrl = "discord.gg/metalicrust";

            [JsonProperty("Ссылка VK")]
            public string VkUrl = "vk.com/metalicrust";

            [JsonProperty("Ссылка Telegram")]
            public string TelegramUrl = "t.me/metalicrust";

            [JsonProperty("Ссылка Steam")]
            public string SteamUrl = "steamcommunity.com";

            [JsonProperty("Левое меню")]
            public List<SideButton> SideButtons = new List<SideButton>();

            [JsonProperty("Верхние вкладки")]
            public List<TopTab> TopTabs = new List<TopTab>();

            public static PluginConfig DefaultConfig()
            {
                var cfg = new PluginConfig();

                cfg.SideButtons = new List<SideButton>
                {
                    new SideButton("ЧАТ", "chat"),
                    new SideButton("КИТЫ", "kit"),
                    new SideButton("РЕПОРТЫ", "report"),
                    new SideButton("МАГАЗИН", "shop"),
                    new SideButton("ВАЙПБЛОК", "wipeblock"),
                    new SideButton("СТАТИСТИКА", "top"),
                    new SideButton("УВЕДОМЛЕНИЯ", "notifications"),
                    new SideButton("КРАФТЫ", "craft"),
                    new SideButton("КАЛЕНДАРЬ ВАЙПОВ", "wipe"),
                    new SideButton("ОБРАТНАЯ СВЯЗЬ", "feedback"),
                    new SideButton("КОРЗИНА", "cart")
                };

                cfg.TopTabs = new List<TopTab>
                {
                    new TopTab("ИНФОРМАЦИЯ", "info"),
                    new TopTab("КОМАНДЫ", "commands"),
                    new TopTab("БИНДЫ", "binds"),
                    new TopTab("ПРАВИЛА", "rules")
                };

                return cfg;
            }
        }

        private class SideButton
        {
            [JsonProperty("Название")]
            public string Title;

            [JsonProperty("Команда")]
            public string Command;

            public SideButton() { }

            public SideButton(string title, string command)
            {
                Title = title;
                Command = command;
            }
        }

        private class TopTab
        {
            [JsonProperty("Название")]
            public string Title;

            [JsonProperty("Ключ")]
            public string Key;

            public TopTab() { }

            public TopTab(string title, string key)
            {
                Title = title;
                Key = key;
            }
        }

        private class PlayerData
        {
            public bool FirstSeen;
        }

        #endregion

        #region Initialization

        private void OnServerInitialized()
        {
            LoadData();
            RegisterImages();

            foreach (var command in config.Commands.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(command))
                    cmd.AddChatCommand(command.TrimStart('/'), this, nameof(CmdOpen));
            }

            foreach (var player in BasePlayer.activePlayerList.ToList())
                HandleConnection(player);
        }

        private void RegisterImages()
        {
            if (ImageLibrary == null)
            {
                PrintWarning("ImageLibrary не найден. Изображения меню будут пропущены.");
                return;
            }

            string[] images =
            {
                config.BackgroundImage,
                config.BannerImage,
                config.ShopIcon,
                config.DiscordIcon,
                config.VkIcon,
                config.TelegramIcon,
                config.SteamIcon,
                config.ShopQr,
                config.DiscordQr,
                config.VkQr,
                config.TelegramQr,
                config.SteamQr
            };

            foreach (string url in images)
            {
                if (!string.IsNullOrEmpty(url))
                    ImageLibrary.Call("AddImage", url, url);
            }
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList.ToList())
                DestroyMenu(player);

            SaveData();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            HandleConnection(player);
        }

        private void HandleConnection(BasePlayer player)
        {
            if (player == null || !config.OpenOnConnection)
                return;

            if (config.FirstConnectionOnly)
            {
                if (players.ContainsKey(player.userID))
                    return;

                players[player.userID] = new PlayerData { FirstSeen = true };

                timer.Once(1f, () =>
                {
                    if (player != null && player.IsConnected)
                        OpenMenu(player, 0);
                });
            }
            else
            {
                timer.Once(1f, () =>
                {
                    if (player != null && player.IsConnected)
                        OpenMenu(player, 0);
                });
            }
        }

        #endregion

        #region Commands

        private void CmdOpen(BasePlayer player, string command, string[] args)
        {
            if (player != null)
                OpenMenu(player, 0);
        }

        [ConsoleCommand("infomenu.open")]
        private void ConsoleOpen(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player != null)
                OpenMenu(player, 0);
        }

        [ConsoleCommand("infomenu.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player != null)
                DestroyMenu(player);
        }

        [ConsoleCommand("infomenu.top")]
        private void ConsoleTop(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null)
                return;

            int index = arg.GetInt(0, 0);
            OpenMenu(player, index);
        }

        [ConsoleCommand("infomenu.side")]
        private void ConsoleSide(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null)
                return;

            int index = arg.GetInt(0, 0);
            HandleSideButton(player, index);
        }

        [ConsoleCommand("infomenu.command")]
        private void ConsoleCommandButton(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null)
                return;

            string command = arg.GetString(0, "");

            if (string.IsNullOrEmpty(command))
                return;

            player.SendConsoleCommand("chat.say", "/" + command.TrimStart('/'));
            DestroyMenu(player);
        }

        #endregion

        #region Menu

        private void OpenMenu(BasePlayer player, int topIndex)
        {
            if (player == null || !player.IsConnected)
                return;

            if (config.TopTabs == null || config.TopTabs.Count == 0)
                return;

            topIndex = Mathf.Clamp(topIndex, 0, config.TopTabs.Count - 1);

            DestroyMenu(player);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0.60" },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                }
            }, "Overlay", MainLayer);

            if (config.ShowBackground && ImageLibrary != null && !string.IsNullOrEmpty(config.BackgroundImage))
            {
                string background = GetImage(config.BackgroundImage);

                if (!string.IsNullOrEmpty(background))
                {
                    container.Add(new CuiElement
                    {
                        Parent = MainLayer,
                        Components =
                        {
                            new CuiRawImageComponent
                            {
                                Png = background,
                                Color = "1 1 1 0.45"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1"
                            }
                        }
                    });

                    container.Add(new CuiPanel
                    {
                        Image = { Color = ParseColor(config.BackgroundOverlay) },
                        RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1"
                        }
                    }, MainLayer);
                }
            }

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.MainPanelColor) },
                RectTransform =
                {
                    AnchorMin = "0.045 0.075",
                    AnchorMax = "0.955 0.925"
                }
            }, MainLayer, MainLayer + ".Main");

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.SidebarColor) },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "0.175 1"
                }
            }, MainLayer + ".Main", MainLayer + ".Side");

            AddText(
                container,
                MainLayer + ".Side",
                config.ServerTitle,
                22,
                TextAnchor.MiddleCenter,
                config.TextColor,
                "0.04 0.92",
                "0.96 0.985",
                "Logo"
            );

            AddText(
                container,
                MainLayer + ".Side",
                config.ServerSubtitle,
                10,
                TextAnchor.MiddleCenter,
                config.SecondaryTextColor,
                "0.03 0.885",
                "0.97 0.925",
                "Sub"
            );

            float top = 0.845f;
            const float height = 0.055f;
            const float gap = 0.014f;

            for (int i = 0; i < config.SideButtons.Count; i++)
            {
                float bottom = top - height;

                if (bottom < 0.045f)
                    break;

                string buttonName = MainLayer + ".Side." + i;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"infomenu.side {i}",
                        Color = ParseColor(config.ButtonColor),
                        HighlightedColor = ParseColor(config.ButtonHoverColor),
                        Material = "Assets/Content/UI/UI.Background.Tile.psd"
                    },
                    Text =
                    {
                        Text = config.SideButtons[i].Title,
                        Font = config.Font,
                        FontSize = config.ButtonFontSize,
                        Align = TextAnchor.MiddleCenter,
                        Color = ParseColor(config.TextColor)
                    },
                    RectTransform =
                    {
                        AnchorMin = $"0.055 {bottom:0.###}",
                        AnchorMax = $"0.945 {top:0.###}"
                    }
                }, MainLayer + ".Side", buttonName);

                top = bottom - gap;
            }

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.TopBarColor) },
                RectTransform =
                {
                    AnchorMin = "0.175 0.90",
                    AnchorMax = "1 1"
                }
            }, MainLayer + ".Main", MainLayer + ".Top");

            float tabX = 0.015f;
            const float tabWidth = 0.18f;
            const float tabGap = 0.012f;

            for (int i = 0; i < config.TopTabs.Count; i++)
            {
                float tabX2 = tabX + tabWidth;
                bool selected = i == topIndex;
                string tabName = MainLayer + ".Top." + i;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"infomenu.top {i}",
                        Color = selected ? ParseColor(config.GreenColor) : "0 0 0 0",
                        HighlightedColor = selected
                            ? ParseColor(config.GreenColor)
                            : ParseColor(config.ButtonHoverColor)
                    },
                    Text =
                    {
                        Text = config.TopTabs[i].Title,
                        Font = config.Font,
                        FontSize = 15,
                        Align = TextAnchor.MiddleCenter,
                        Color = selected
                            ? "0 0 0 1"
                            : ParseColor(config.TextColor)
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{tabX:0.###} 0.12",
                        AnchorMax = $"{tabX2:0.###} 0.88"
                    }
                }, MainLayer + ".Top", tabName);

                tabX = tabX2 + tabGap;
            }

            container.Add(new CuiButton
            {
                Button =
                {
                    Command = "infomenu.close",
                    Close = MainLayer,
                    Color = ParseColor(config.CloseColor),
                    HighlightedColor = "1 0.15 0.15 1"
                },
                Text =
                {
                    Text = "✕  ЗАКРЫТЬ",
                    Font = config.Font,
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.885 0.915",
                    AnchorMax = "0.985 0.975"
                }
            }, MainLayer + ".Main", MainLayer + ".Close");

            string content = MainLayer + ".Content";

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform =
                {
                    AnchorMin = "0.195 0.045",
                    AnchorMax = "0.985 0.895"
                }
            }, MainLayer + ".Main", content);

            string key = config.TopTabs[topIndex].Key ?? "info";

            switch (key.ToLowerInvariant())
            {
                case "commands":
                    DrawCommands(container, content);
                    break;

                case "binds":
                    DrawBinds(container, content);
                    break;

                case "rules":
                    DrawRules(container, content);
                    break;

                default:
                    DrawInformation(container, content);
                    break;
            }

            CuiHelper.AddUi(player, container);
        }

        private void DrawInformation(CuiElementContainer container, string parent)
        {
            string banner = parent + ".Banner";

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.CardColor) },
                RectTransform =
                {
                    AnchorMin = "0.015 0.76",
                    AnchorMax = "0.985 0.975"
                }
            }, parent, banner);

            DrawImage(
                container,
                banner,
                config.BannerImage,
                "0 0",
                "1 1",
                0.25f
            );

            AddText(
                container,
                banner,
                config.WelcomeTitle,
                22,
                TextAnchor.UpperLeft,
                config.TextColor,
                "0.025 0.48",
                "0.57 0.90",
                "WelcomeTitle"
            );

            AddText(
                container,
                banner,
                config.WelcomeLine2,
                14,
                TextAnchor.MiddleLeft,
                config.TextColor,
                "0.025 0.28",
                "0.60 0.55",
                "Welcome2"
            );

            AddText(
                container,
                banner,
                config.WelcomeLine3,
                14,
                TextAnchor.LowerLeft,
                config.GreenColor,
                "0.025 0.07",
                "0.60 0.34",
                "Welcome3"
            );

            AddText(
                container,
                banner,
                config.BannerRightText,
                12,
                TextAnchor.MiddleCenter,
                config.TextColor,
                "0.69 0.10",
                "0.975 0.90",
                "BannerRight"
            );

            float startX = 0.015f;
            const float gap = 0.012f;
            float width = (0.97f - gap * 4f) / 5f;

            DrawSocialCard(
                container,
                parent,
                startX + (width + gap) * 0,
                width,
                "МАГАЗИН",
                config.ShopIcon,
                config.ShopQr,
                config.ShopUrl,
                config.GreenColor
            );

            DrawSocialCard(
                container,
                parent,
                startX + (width + gap) * 1,
                width,
                "DISCORD",
                config.DiscordIcon,
                config.DiscordQr,
                config.DiscordUrl,
                config.BlueColor
            );

            DrawSocialCard(
                container,
                parent,
                startX + (width + gap) * 2,
                width,
                "ВКОНТАКТЕ",
                config.VkIcon,
                config.VkQr,
                config.VkUrl,
                config.BlueColor
            );

            DrawSocialCard(
                container,
                parent,
                startX + (width + gap) * 3,
                width,
                "TELEGRAM",
                config.TelegramIcon,
                config.TelegramQr,
                config.TelegramUrl,
                config.BlueColor
            );

            DrawSocialCard(
                container,
                parent,
                startX + (width + gap) * 4,
                width,
                "STEAM",
                config.SteamIcon,
                config.SteamQr,
                config.SteamUrl,
                config.BlueColor
            );
        }

        private void DrawSocialCard(
            CuiElementContainer container,
            string parent,
            float x,
            float width,
            string title,
            string icon,
            string qr,
            string link,
            string accent)
        {
            string card = CuiHelper.GetGuid();

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.CardColor) },
                RectTransform =
                {
                    AnchorMin = $"{x:0.###} 0.02",
                    AnchorMax = $"{(x + width):0.###} 0.72"
                }
            }, parent, card);

            DrawImage(
                container,
                card,
                icon,
                "0.18 0.56",
                "0.82 0.94",
                1f
            );

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(accent) },
                RectTransform =
                {
                    AnchorMin = "0.06 0.48",
                    AnchorMax = "0.94 0.56"
                }
            }, card);

            AddText(
                container,
                card,
                title,
                12,
                TextAnchor.MiddleCenter,
                "0 0 0 1",
                "0.08 0.48",
                "0.92 0.56",
                "Title"
            );

            AddText(
                container,
                card,
                link,
                11,
                TextAnchor.MiddleCenter,
                accent,
                "0.04 0.405",
                "0.96 0.47",
                "Link"
            );

            DrawImage(
                container,
                card,
                qr,
                "0.10 0.04",
                "0.90 0.38",
                1f
            );
        }

        private void DrawCommands(
            CuiElementContainer container,
            string parent)
        {
            AddSectionTitle(
                container,
                parent,
                "КОМАНДЫ",
                "ВСЁ НЕОБХОДИМОЕ ДЛЯ ИГРЫ НА METALICRUST"
            );

            DrawCommandButton(
                container,
                parent,
                "/kit",
                "КИТЫ",
                "Открыть наборы",
                0.72f
            );

            DrawCommandButton(
                container,
                parent,
                "/shop",
                "МАГАЗИН",
                "Открыть магазин",
                0.61f
            );

            DrawCommandButton(
                container,
                parent,
                "/quests",
                "КВЕСТЫ",
                "Открыть квесты",
                0.50f
            );

            DrawCommandButton(
                container,
                parent,
                "/clan",
                "КЛАН",
                "Открыть меню клана",
                0.39f
            );

            DrawCommandButton(
                container,
                parent,
                "/toptime",
                "ТОП ВРЕМЕНИ",
                "Рейтинг времени игры",
                0.28f
            );

            DrawCommandButton(
                container,
                parent,
                "/rules",
                "ПРАВИЛА",
                "Правила сервера",
                0.17f
            );
        }

        private void DrawBinds(
            CuiElementContainer container,
            string parent)
        {
            AddSectionTitle(
                container,
                parent,
                "БИНДЫ",
                "УДОБНЫЕ КОМАНДЫ ДЛЯ ИГРЫ"
            );

            AddText(
                container,
                parent,
                "Для собственного бинда используйте стандартные команды Rust.\n\n" +
                "Основные команды MetalicRust:\n\n" +
                "/info\n" +
                "/kit\n" +
                "/shop\n" +
                "/quests\n" +
                "/clan\n" +
                "/toptime",
                config.BodyFontSize,
                TextAnchor.UpperLeft,
                config.TextColor,
                "0.04 0.08",
                "0.96 0.76",
                "BindsText"
            );
        }

        private void DrawRules(
            CuiElementContainer container,
            string parent)
        {
            AddSectionTitle(
                container,
                parent,
                "ПРАВИЛА",
                "КОРОТКО И ПО ДЕЛУ"
            );

            AddText(
                container,
                parent,
                "1. Не используйте читы и сторонний софт.\n" +
                "2. Не мешайте работе администрации.\n" +
                "3. Не используйте баги для получения преимущества.\n" +
                "4. Соблюдайте правила чата.\n" +
                "5. Не выдавайте себя за администрацию.\n\n" +
                "Подробные правила могут быть дополнены администрацией.",
                config.BodyFontSize,
                TextAnchor.UpperLeft,
                config.TextColor,
                "0.04 0.10",
                "0.96 0.80",
                "RulesText"
            );
        }

        private void AddSectionTitle(
            CuiElementContainer container,
            string parent,
            string title,
            string subtitle)
        {
            AddText(
                container,
                parent,
                title,
                25,
                TextAnchor.UpperLeft,
                config.TextColor,
                "0.03 0.86",
                "0.95 0.97",
                "SectionTitle"
            );

            AddText(
                container,
                parent,
                subtitle,
                12,
                TextAnchor.UpperLeft,
                config.SecondaryTextColor,
                "0.032 0.80",
                "0.95 0.87",
                "SectionSubtitle"
            );

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.GreenColor) },
                RectTransform =
                {
                    AnchorMin = "0.03 0.775",
                    AnchorMax = "0.30 0.782"
                }
            }, parent);
        }

        private void DrawCommandButton(
            CuiElementContainer container,
            string parent,
            string command,
            string title,
            string subtitle,
            float y)
        {
            string id = CuiHelper.GetGuid();

            container.Add(new CuiButton
            {
                Button =
                {
                    Command = "infomenu.command " + command.TrimStart('/'),
                    Color = ParseColor(config.ButtonColor),
                    HighlightedColor = ParseColor(config.ButtonHoverColor)
                },
                Text =
                {
                    Text = title + "\n" + subtitle,
                    Font = config.Font,
                    FontSize = 13,
                    Align = TextAnchor.MiddleLeft,
                    Color = ParseColor(config.TextColor)
                },
                RectTransform =
                {
                    AnchorMin = $"0.03 {y:0.###}",
                    AnchorMax = $"0.97 {(y + 0.085f):0.###}"
                }
            }, parent, id);
        }

        private void HandleSideButton(
            BasePlayer player,
            int index)
        {
            if (player == null)
                return;

            if (index < 0 || index >= config.SideButtons.Count)
                return;

            string command = config.SideButtons[index].Command ?? "";

            if (string.IsNullOrEmpty(command))
                return;

            switch (command.ToLowerInvariant())
            {
                case "chat":
                    player.SendConsoleCommand("chat.say", "/chat");
                    DestroyMenu(player);
                    break;

                case "kit":
                    player.SendConsoleCommand("chat.say", "/kit");
                    DestroyMenu(player);
                    break;

                case "report":
                    player.SendConsoleCommand("chat.say", "/report");
                    DestroyMenu(player);
                    break;

                case "shop":
                    player.SendConsoleCommand("chat.say", "/shop");
                    DestroyMenu(player);
                    break;

                case "top":
                    player.SendConsoleCommand("chat.say", "/top");
                    DestroyMenu(player);
                    break;

                case "wipe":
                    player.ChatMessage(
                        $"<color=#55ff22>{config.ServerTitle}</color>: информация о вайпе доступна в разделе КАЛЕНДАРЬ ВАЙПОВ."
                    );
                    break;

                case "notifications":
                    player.ChatMessage(
                        $"<color=#55ff22>{config.ServerTitle}</color>: уведомления сервера."
                    );
                    break;

                case "craft":
                    player.ChatMessage(
                        $"<color=#55ff22>{config.ServerTitle}</color>: раздел КРАФТЫ."
                    );
                    break;

                case "feedback":
                    player.ChatMessage(
                        $"<color=#55ff22>{config.ServerTitle}</color>: обратная связь с администрацией."
                    );
                    break;

                case "cart":
                    player.ChatMessage(
                        $"<color=#55ff22>{config.ServerTitle}</color>: корзина магазина."
                    );
                    break;

                case "wipeblock":
                    player.ChatMessage(
                        $"<color=#55ff22>{config.ServerTitle}</color>: информация о вайпблоке."
                    );
                    break;

                default:
                    player.SendConsoleCommand(
                        "chat.say",
                        "/" + command.TrimStart('/')
                    );
                    DestroyMenu(player);
                    break;
            }
        }

        #endregion

        #region Image / Text Helpers

        private string GetImage(string key)
        {
            if (ImageLibrary == null || string.IsNullOrEmpty(key))
                return null;

            return ImageLibrary.Call("GetImage", key) as string;
        }

        private void DrawImage(
            CuiElementContainer container,
            string parent,
            string key,
            string anchorMin,
            string anchorMax,
            float alpha)
        {
            string png = GetImage(key);

            if (string.IsNullOrEmpty(png))
                return;

            container.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = png,
                        Color = $"1 1 1 {alpha.ToString("0.##", CultureInfo.InvariantCulture)}"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax
                    }
                }
            });
        }

        private void AddText(
            CuiElementContainer container,
            string parent,
            string text,
            int size,
            TextAnchor align,
            string color,
            string anchorMin,
            string anchorMax,
            string name)
        {
            container.Add(new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = parent,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = text ?? "",
                        FontSize = size,
                        Align = align,
                        Color = ParseColor(color),
                        Font = config.Font,
                        FadeIn = 0.05f
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax
                    }
                }
            });
        }

        private string ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "1 1 1 1";

            string[] parts = value
                .Trim()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4)
                return "1 1 1 1";

            float r;
            float g;
            float b;
            float a;

            if (!float.TryParse(
                    parts[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out r) ||
                !float.TryParse(
                    parts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out g) ||
                !float.TryParse(
                    parts[2],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out b) ||
                !float.TryParse(
                    parts[3],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out a))
            {
                return "1 1 1 1";
            }

            if (r > 1f || g > 1f || b > 1f || a > 1f)
            {
                r /= 255f;
                g /= 255f;
                b /= 255f;
                a /= 255f;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###} {1:0.###} {2:0.###} {3:0.###}",
                Mathf.Clamp01(r),
                Mathf.Clamp01(g),
                Mathf.Clamp01(b),
                Mathf.Clamp01(a)
            );
        }

        #endregion

        #region Data

        private void LoadData()
        {
            try
            {
                players =
                    Interface.Oxide.DataFileSystem
                        .ReadObject<Dictionary<ulong, PlayerData>>(DataFile);
            }
            catch
            {
                players = new Dictionary<ulong, PlayerData>();
            }

            if (players == null)
                players = new Dictionary<ulong, PlayerData>();
        }

        private void SaveData()
        {
            if (players != null)
                Interface.Oxide.DataFileSystem.WriteObject(DataFile, players);
        }

        #endregion

        private void DestroyMenu(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, MainLayer);
        }
    }
}
