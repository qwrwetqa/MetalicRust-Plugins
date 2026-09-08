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
    [Info("InfoMenu", "MetalicRust", "2.1.0")]
    [Description("Modern MetalicRust information menu.")]
    public class InfoMenu : RustPlugin
    {
        private PluginConfig config;
        private const string MainLayer = "MetalicRust_InfoMenu";
        private const string DataFile = "InfoMenu";

        [PluginReference] private Plugin ImageLibrary;
        [PluginReference] private Plugin WipeBlock;

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
            public VersionNumber Version = new VersionNumber(2, 1, 0);

            [JsonProperty("Команды открытия меню")]
            public List<string> Commands = new List<string> { "info", "menu", "help" };

            [JsonProperty("Открывать при подключении")]
            public bool OpenOnConnection = true;

            [JsonProperty("Открывать только при первом подключении")]
            public bool FirstConnectionOnly = false;

            [JsonProperty("Показывать фон сервера")]
            public bool ShowBackground = false;

            [JsonProperty("Фоновое изображение URL")]
            public string BackgroundImage = "";

            [JsonProperty("Цвет затемнения фона")]
            public string BackgroundOverlay = "0 0 0 0.45";

            [JsonProperty("Основной цвет панели")]
            public string MainPanelColor = "0.025 0.03 0.04 0.98";

            [JsonProperty("Цвет боковой панели")]
            public string SidebarColor = "0.015 0.018 0.023 0.98";

            [JsonProperty("Цвет карточек")]
            public string CardColor = "0.06 0.07 0.08 0.96";

            [JsonProperty("Цвет активной кнопки")]
            public string ActiveColor = "0.78 0.20 0.05 1";

            [JsonProperty("Цвет кнопки")]
            public string ButtonColor = "0.08 0.09 0.10 1";

            [JsonProperty("Цвет кнопки при наведении")]
            public string ButtonHoverColor = "0.13 0.14 0.16 1";

            [JsonProperty("Основной цвет текста")]
            public string TextColor = "1 1 1 1";

            [JsonProperty("Вторичный цвет текста")]
            public string SecondaryTextColor = "0.62 0.65 0.69 1";

            [JsonProperty("Акцентный цвет текста")]
            public string AccentTextColor = "1 0.43 0.15 1";

            [JsonProperty("Цвет линии")]
            public string LineColor = "0.78 0.20 0.05 0.85";

            [JsonProperty("Шрифт")]
            public string Font = "robotocondensed-bold.ttf";

            [JsonProperty("Размер заголовка")]
            public int TitleSize = 30;

            [JsonProperty("Размер подзаголовка")]
            public int SubtitleSize = 14;

            [JsonProperty("Размер текста кнопок")]
            public int ButtonFontSize = 16;

            [JsonProperty("Размер основного текста")]
            public int BodyFontSize = 16;

            [JsonProperty("Размер статистики")]
            public int StatValueSize = 23;

            [JsonProperty("Вкладки")]
            public List<Tab> Tabs = new List<Tab>();

            public static PluginConfig DefaultConfig()
            {
                var cfg = new PluginConfig();

                cfg.Tabs.Add(new Tab
                {
                    Title = "ГЛАВНАЯ",
                    Icon = "◆",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "ОСНОВНЫЕ КОМАНДЫ",
                            Subtitle = "ВСЁ НЕОБХОДИМОЕ ДЛЯ ИГРЫ НА METALICRUST",
                            Text = "Команды сервера отображаются в прокручиваемом списке."
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "СЕРВЕР",
                    Icon = "●",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "О СЕРВЕРЕ",
                            Subtitle = "ОСНОВНЫЕ ОСОБЕННОСТИ",
                            Text =
                                "MetalicRust — сервер для тех, кто хочет играть, а не читать десятки правил.\n\n" +
                                "Режим: Vanilla+\n" +
                                "Сбор: X2\n" +
                                "Wipe: каждые 2 недели\n" +
                                "Kits: доступны\n" +
                                "Skins: доступны бесплатно\n" +
                                "DLC: бесплатно\n\n" +
                                "Администрация следит за порядком и старается не мешать нормальной игре."
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "KITS",
                    Icon = "▣",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "НАБОРЫ",
                            Subtitle = "БЫСТРЫЙ СТАРТ",
                            Text =
                                "Стартовый набор выдаётся автоматически при входе.\n\n" +
                                "Дополнительные наборы доступны через систему Kits.\n\n" +
                                "Используйте команду:\n" +
                                "/kit\n\n" +
                                "Если на сервере настроены VIP-наборы, они отображаются в этом разделе."
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "КВЕСТЫ",
                    Icon = "✦",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "КВЕСТЫ",
                            Subtitle = "DAILY  •  WEEKLY  •  WIPE",
                            Text =
                                "Выполняйте задания и получайте награды.\n\n" +
                                "DAILY — задания на каждый день.\n" +
                                "WEEKLY — более серьёзные задания.\n" +
                                "WIPE — задания на текущий вайп.\n\n" +
                                "За выполнение можно получать Economics и RP."
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "ЭКОНОМИКА",
                    Icon = "$",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "ЭКОНОМИКА",
                            Subtitle = "ECONOMICS  •  RP",
                            Text =
                                "На сервере работают две основные валюты.\n\n" +
                                "ECONOMICS — деньги сервера.\n" +
                                "RP — очки награды.\n\n" +
                                "Баланс Economics:\n/balance\n\n" +
                                "Баланс RP зависит от установленной системы ServerRewards."
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "КЛАНЫ",
                    Icon = "◈",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "КЛАНЫ И ДРУЗЬЯ",
                            Subtitle = "ИГРАЙТЕ КОМАНДОЙ",
                            Text =
                                "Создавайте кланы, приглашайте друзей и играйте вместе.\n\n" +
                                "Доступные команды зависят от установленной версии Clans/Friends.\n\n" +
                                "Для просмотра доступных команд используйте:\n" +
                                "/clan\n" +
                                "/friend"
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "ТОП",
                    Icon = "★",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "ТОП ИГРОКОВ",
                            Subtitle = "РЕЙТИНГ",
                            Text =
                                "ТОП ИГРОКОВ\n\n" +
                                "Команда: /top — открыть общий рейтинг игроков.\n" +
                                "Команда: /toptime — посмотреть рейтинг по времени игры.\n\n" +
                                "🏆 НАГРАДЫ ЗА ТОП 1, 2, 3\n\n" +
                                "🥇 ТОП 1 — награда на следующий вайп\n" +
                                "🥈 ТОП 2 — награда на следующий вайп\n" +
                                "🥉 ТОП 3 — награда на следующий вайп\n\n" +
                                "Занимайте места в рейтинге до конца вайпа и получайте награду на следующем вайпе."
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "SKINS",
                    Icon = "◇",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "БЕСПЛАТНЫЕ SKINS",
                            Subtitle = "SKINBOX",
                            Text =
                                "На MetalicRust доступны бесплатные скины.\n\n" +
                                "Выбирайте внешний вид предметов прямо при крафте.\n\n" +
                                "Если скин не отображается, обратитесь к администрации."
                        }
                    }
                });

                cfg.Tabs.Add(new Tab
                {
                    Title = "ПРАВИЛА",
                    Icon = "!",
                    Pages = new List<Page>
                    {
                        new Page
                        {
                            Title = "ПРАВИЛА METALICRUST",
                            Subtitle = "КОРОТКО И ПО ДЕЛУ",
                            Text =
                                "1. Не используйте читы и сторонний софт.\n" +
                                "2. Не мешайте работе администрации.\n" +
                                "3. Не используйте баги для получения преимущества.\n" +
                                "4. Соблюдайте правила чата.\n" +
                                "5. Не выдавайте себя за администрацию.\n\n" +
                                "Подробные правила могут быть дополнены администрацией."
                        }
                    }
                });

                return cfg;
            }
        }

        private class Tab
        {
            [JsonProperty("Название")]
            public string Title = "ВКЛАДКА";

            [JsonProperty("Иконка")]
            public string Icon = "◆";

            [JsonProperty("Страницы")]
            public List<Page> Pages = new List<Page>();
        }

        private class Page
        {
            [JsonProperty("Заголовок")]
            public string Title = "";

            [JsonProperty("Подзаголовок")]
            public string Subtitle = "";

            [JsonProperty("Текст")]
            public string Text = "";

            [JsonProperty("Изображения")]
            public List<ImageData> Images = new List<ImageData>();
        }

        private class ImageData
        {
            [JsonProperty("URL")]
            public string URL = "";

            [JsonProperty("X")]
            public float X = 0.05f;

            [JsonProperty("Y")]
            public float Y = 0.10f;

            [JsonProperty("Ширина")]
            public float Width = 0.90f;

            [JsonProperty("Высота")]
            public float Height = 0.35f;
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

            if (ImageLibrary != null)
            {
                foreach (var tab in config.Tabs)
                {
                    foreach (var page in tab.Pages)
                    {
                        foreach (var image in page.Images)
                        {
                            if (!string.IsNullOrEmpty(image.URL))
                                ImageLibrary.Call("AddImage", image.URL, image.URL);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(config.BackgroundImage))
                    ImageLibrary.Call("AddImage", config.BackgroundImage, config.BackgroundImage);
            }
            else if (config.ShowBackground || config.Tabs.Any(t => t.Pages.Any(p => p.Images.Count > 0)))
            {
                PrintWarning("ImageLibrary не найден. Изображения будут пропущены.");
            }

            foreach (var command in config.Commands.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(command))
                    cmd.AddChatCommand(command.TrimStart('/'), this, nameof(CmdOpen));
            }

            foreach (var player in BasePlayer.activePlayerList.ToList())
                HandleConnection(player);
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
            if (player == null)
                return;

            if (!config.OpenOnConnection)
                return;

            if (config.FirstConnectionOnly)
            {
                if (!players.ContainsKey(player.userID))
                {
                    players[player.userID] = new PlayerData { FirstSeen = true };
                    timer.Once(1f, () =>
                    {
                        if (player != null && player.IsConnected)
                            OpenMenu(player, 0, 0);
                    });
                }
            }
            else
            {
                timer.Once(1f, () =>
                {
                    if (player != null && player.IsConnected)
                        OpenMenu(player, 0, 0);
                });
            }
        }

        #endregion

        #region Commands

        private void CmdOpen(BasePlayer player, string command, string[] args)
        {
            OpenMenu(player, 0, 0);
        }

        [ConsoleCommand("infomenu.open")]
        private void ConsoleOpen(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
                OpenMenu(player, 0, 0);
        }

        [ConsoleCommand("infomenu.tab")]
        private void ConsoleTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;

            int tab = arg.GetInt(0, 0);
            int page = arg.GetInt(1, 0);

            OpenMenu(player, tab, page);
        }

        [ConsoleCommand("infomenu.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
                DestroyMenu(player);
        }

        #endregion

        #region Menu

        private void OpenMenu(BasePlayer player, int tabIndex, int pageIndex)
        {
            if (player == null || !player.IsConnected)
                return;

            if (config.Tabs == null || config.Tabs.Count == 0)
                return;

            tabIndex = Mathf.Clamp(tabIndex, 0, config.Tabs.Count - 1);

            if (config.Tabs[tabIndex].Pages == null || config.Tabs[tabIndex].Pages.Count == 0)
                return;

            pageIndex = Mathf.Clamp(pageIndex, 0, config.Tabs[tabIndex].Pages.Count - 1);

            DestroyMenu(player);

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image =
                {
                    Color = "0 0 0 0.78"
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                }
            }, "Overlay", MainLayer);

            if (config.ShowBackground && ImageLibrary != null && !string.IsNullOrEmpty(config.BackgroundImage))
            {
                var background = (string)ImageLibrary.Call("GetImage", config.BackgroundImage);

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
                                Color = "1 1 1 0.42"
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

            // Main card
            container.Add(new CuiPanel
            {
                Image =
                {
                    Color = ParseColor(config.MainPanelColor)
                },
                RectTransform =
                {
                    AnchorMin = "0.075 0.065",
                    AnchorMax = "0.925 0.935"
                }
            }, MainLayer, MainLayer + ".Card");

            // Accent top line
            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.ActiveColor) },
                RectTransform =
                {
                    AnchorMin = "0 0.985",
                    AnchorMax = "1 1"
                }
            }, MainLayer + ".Card");

            // Sidebar
            container.Add(new CuiPanel
            {
                Image =
                {
                    Color = ParseColor(config.SidebarColor)
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "0.255 1"
                }
            }, MainLayer + ".Card", MainLayer + ".Sidebar");

            // Logo
            AddText(container, MainLayer + ".Sidebar",
                "METALICRUST", 22, TextAnchor.MiddleCenter,
                config.TextColor, "0.08 0.925", "0.92 0.98", "logo");

            AddText(container, MainLayer + ".Sidebar",
                "VANILLA+  •  X2  •  NOLIMIT", 10, TextAnchor.MiddleCenter,
                config.SecondaryTextColor, "0.06 0.875", "0.94 0.92", "subtitle");

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.LineColor) },
                RectTransform =
                {
                    AnchorMin = "0.08 0.865",
                    AnchorMax = "0.92 0.868"
                }
            }, MainLayer + ".Sidebar");

            float top = 0.835f;
            float height = 0.075f;
            float gap = 0.012f;

            for (int i = 0; i < config.Tabs.Count; i++)
            {
                var tab = config.Tabs[i];
                float bottom = top - height;

                var buttonName = MainLayer + ".Tab." + i;

                container.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"infomenu.tab {i} 0",
                        Color = i == tabIndex ? ParseColor(config.ActiveColor) : ParseColor(config.ButtonColor),
                        Material = "Assets/Content/UI/UI.Background.Tile.psd"
                    },
                    Text = { Text = "" },
                    RectTransform =
                    {
                        AnchorMin = $"0.06 {bottom}",
                        AnchorMax = $"0.94 {top}"
                    }
                }, MainLayer + ".Sidebar", buttonName);

                AddText(container, buttonName, tab.Icon, 16, TextAnchor.MiddleCenter,
                    i == tabIndex ? config.TextColor : config.SecondaryTextColor,
                    "0.04 0", "0.18 1", "icon");

                AddText(container, buttonName, tab.Title, config.ButtonFontSize, TextAnchor.MiddleLeft,
                    config.TextColor, "0.22 0", "0.96 1", "title");

                top = bottom - gap;

                if (top < 0.12f)
                    break;
            }

            // Content
            var contentParent = MainLayer + ".Content";

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform =
                {
                    AnchorMin = "0.275 0.04",
                    AnchorMax = "0.985 0.94"
                }
            }, MainLayer + ".Card", contentParent);

            var selectedTab = config.Tabs[tabIndex];
            var page = selectedTab.Pages[pageIndex];

            AddText(container, contentParent,
                selectedTab.Title, 12, TextAnchor.MiddleLeft,
                config.AccentTextColor, "0.025 0.91", "0.65 0.98", "section");

            bool isCommandsHome = tabIndex == 0 &&
                                  string.Equals(selectedTab.Title, "ГЛАВНАЯ", StringComparison.OrdinalIgnoreCase);

            string displayTitle = isCommandsHome ? "ОСНОВНЫЕ КОМАНДЫ" : page.Title;
            string displaySubtitle = isCommandsHome
                ? "ВСЁ НЕОБХОДИМОЕ ДЛЯ ИГРЫ НА METALICRUST"
                : page.Subtitle;

            AddText(container, contentParent,
                displayTitle, config.TitleSize, TextAnchor.MiddleLeft,
                config.TextColor, "0.025 0.79", "0.94 0.92", "pageTitle");

            AddText(container, contentParent,
                displaySubtitle, config.SubtitleSize, TextAnchor.MiddleLeft,
                config.SecondaryTextColor, "0.027 0.745", "0.94 0.81", "pageSubtitle");

            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.LineColor) },
                RectTransform =
                {
                    AnchorMin = "0.027 0.715",
                    AnchorMax = "0.32 0.719"
                }
            }, contentParent);

            // Статистика ONLINE / QUEUE / WIPE удалена, чтобы не перекрывать содержимое меню.

            // Main content card
            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.CardColor) },
                RectTransform =
                {
                    AnchorMin = "0.025 0.09",
                    AnchorMax = "0.955 0.575"
                }
            }, contentParent, contentParent + ".TextCard");

            if (isCommandsHome)
            {
                AddCommandsScroll(container, contentParent + ".TextCard");
            }
            else
            {
                AddText(container, contentParent + ".TextCard",
                    ReplaceVariables(page.Text, player), config.BodyFontSize,
                    TextAnchor.UpperLeft, config.TextColor,
                    "0.035 0.06", "0.965 0.94", "body");
            }

            // Page navigation
            if (selectedTab.Pages.Count > 1)
            {
                string left = pageIndex > 0 ? $"infomenu.tab {tabIndex} {pageIndex - 1}" : "";
                string right = pageIndex < selectedTab.Pages.Count - 1 ? $"infomenu.tab {tabIndex} {pageIndex + 1}" : "";

                if (!string.IsNullOrEmpty(left))
                {
                    container.Add(new CuiButton
                    {
                        Button = { Command = left, Color = ParseColor(config.ButtonColor) },
                        Text = { Text = "‹", FontSize = 24, Align = TextAnchor.MiddleCenter },
                        RectTransform = { AnchorMin = "0.025 0.015", AnchorMax = "0.075 0.075" }
                    }, contentParent);
                }

                if (!string.IsNullOrEmpty(right))
                {
                    container.Add(new CuiButton
                    {
                        Button = { Command = right, Color = ParseColor(config.ButtonColor) },
                        Text = { Text = "›", FontSize = 24, Align = TextAnchor.MiddleCenter },
                        RectTransform = { AnchorMin = "0.905 0.015", AnchorMax = "0.955 0.075" }
                    }, contentParent);
                }

                AddText(container, contentParent,
                    $"{pageIndex + 1} / {selectedTab.Pages.Count}", 11,
                    TextAnchor.MiddleCenter, config.SecondaryTextColor,
                    "0.40 0.015", "0.60 0.075", "pages");
            }

            // Optional images from ImageLibrary
            if (ImageLibrary != null && page.Images != null)
            {
                foreach (var image in page.Images)
                {
                    if (string.IsNullOrEmpty(image.URL))
                        continue;

                    string png = (string)ImageLibrary.Call("GetImage", image.URL);
                    if (string.IsNullOrEmpty(png))
                        continue;

                    container.Add(new CuiElement
                    {
                        Parent = contentParent,
                        Components =
                        {
                            new CuiRawImageComponent
                            {
                                Png = png,
                                Color = "1 1 1 0.95"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = $"{image.X} {image.Y}",
                                AnchorMax = $"{image.X + image.Width} {image.Y + image.Height}"
                            }
                        }
                    });
                }
            }

            // Close button — создаём последним, чтобы он всегда был поверх всех элементов меню.
            container.Add(new CuiButton
            {
                Button =
                {
                    Command = "infomenu.close",
                    Close = MainLayer,
                    Color = "0.10 0.10 0.11 0.98"
                },
                Text =
                {
                    Text = "×",
                    FontSize = 26,
                    Align = TextAnchor.MiddleCenter,
                    Color = config.TextColor
                },
                RectTransform =
                {
                    AnchorMin = "0.948 0.942",
                    AnchorMax = "0.992 0.989"
                }
            }, MainLayer + ".Card", MainLayer + ".Close");

            CuiHelper.AddUi(player, container);

            if (selectedTab.Title.IndexOf("БЛОК", StringComparison.OrdinalIgnoreCase) >= 0)
                WipeBlock?.Call("DrawBlockGUI", player);
        }

        private class CommandEntry
        {
            public string Text;
            public bool Header;

            public CommandEntry(string text, bool header)
            {
                Text = text;
                Header = header;
            }
        }

        private void AddCommandsScroll(CuiElementContainer container, string parent)
        {
            var entries = new List<CommandEntry>
            {
                new CommandEntry("🏠  ТЕЛЕПОРТАЦИЯ", true),
                new CommandEntry("/tpr ник              — запрос телепортации к игроку", false),
                new CommandEntry("/tpa                  — принять запрос телепортации", false),
                new CommandEntry("/tpc                  — отменить запрос телепортации", false),
                new CommandEntry("/home                 — открыть список домов", false),
                new CommandEntry("/home имя             — телепортироваться домой", false),
                new CommandEntry("/sethome имя          — установить дом", false),
                new CommandEntry("/removehome имя       — удалить дом", false),
                new CommandEntry("", false),

                new CommandEntry("🎁  НАБОРЫ", true),
                new CommandEntry("/kit                  — открыть список наборов", false),
                new CommandEntry("", false),

                new CommandEntry("📜  КВЕСТЫ", true),
                new CommandEntry("/quests               — открыть меню квестов", false),
                new CommandEntry("", false),

                new CommandEntry("💰  ЭКОНОМИКА", true),
                new CommandEntry("/balance              — посмотреть баланс", false),
                new CommandEntry("/transfer ник сумма   — передать деньги игроку", false),
                new CommandEntry("", false),

                new CommandEntry("👥  КЛАНЫ И ДРУЗЬЯ", true),
                new CommandEntry("/clan                 — меню клана", false),
                new CommandEntry("/friend               — меню друзей", false),
                new CommandEntry("", false),

                new CommandEntry("🏆  РЕЙТИНГ", true),
                new CommandEntry("/top                  — рейтинг игроков", false),
                new CommandEntry("/rank                 — ваш рейтинг", false),
                new CommandEntry("", false),

                new CommandEntry("🛒  МАГАЗИН", true),
                new CommandEntry("/shop                 — открыть магазин", false),
                new CommandEntry("/sr                   — магазин за RP", false),
                new CommandEntry("", false),

                new CommandEntry("ℹ️  ИНФОРМАЦИЯ", true),
                new CommandEntry("/info                 — открыть меню MetalicRust", false),
                new CommandEntry("/rules                — правила сервера", false),
                new CommandEntry("/wipe                 — информация о вайпе", false),
                new CommandEntry("/players              — список игроков", false),
                new CommandEntry("", false),

                new CommandEntry("⚙  ПРОЧЕЕ", true),
                new CommandEntry("/help                 — помощь и доступные команды", false)
            };

            string scrollName = parent + ".CommandsScroll";

            // Сначала рассчитываем реальную высоту контента по тем же размерам,
            // которые используются ниже при отрисовке. Это не даёт ScrollRect
            // останавливаться раньше последней строки.
            float contentHeight = 20f;
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Text))
                    contentHeight += 14f;
                else if (entry.Header)
                    contentHeight += 38f;
                else
                    contentHeight += 29f;
            }

            contentHeight = Mathf.Max(contentHeight, 420f);

            container.Add(new CuiElement
            {
                Name = scrollName,
                Parent = parent,
                Components =
                {
                    new CuiScrollViewComponent
                    {
                        ContentTransform = new CuiRectTransform
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"0 -{contentHeight:0.##}",
                            OffsetMax = "0 0",
                            Pivot = "0.5 1"
                        },
                        Horizontal = false,
                        Vertical = true,
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Clamped,
                        Elasticity = 0.08f,
                        Inertia = true,
                        DecelerationRate = 0.08f,
                        ScrollSensitivity = 32f,
                        VerticalNormalizedPosition = 1f,
                        VerticalScrollbar = new CuiScrollbar
                        {
                            AutoHide = true,
                            Size = 18f,
                            HandleColor = ParseColor(config.ActiveColor),
                            HighlightColor = ParseColor(config.AccentTextColor),
                            PressedColor = ParseColor(config.AccentTextColor),
                            TrackColor = "0 0 0 0.20"
                        }
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.02 0.02",
                        AnchorMax = "0.98 0.98",
                        Pivot = "0.5 0.5"
                    }
                }
            });

            float y = -12f;

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Text))
                {
                    y -= 14f;
                    continue;
                }

                float height = entry.Header ? 38f : 29f;
                string color = entry.Header ? config.AccentTextColor : config.TextColor;
                int size = entry.Header ? 15 : 13;

                container.Add(new CuiElement
                {
                    Parent = scrollName,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = entry.Text,
                            FontSize = size,
                            Align = TextAnchor.MiddleLeft,
                            Color = ParseColor(color),
                            Font = config.Font,
                            FadeIn = 0.05f
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 1",
                            AnchorMax = "1 1",
                            OffsetMin = $"12 {y - height:0.##}",
                            OffsetMax = $"-24 {y:0.##}"
                        }
                    }
                });

                y -= height;
            }
        }

        private void AddText(
            CuiElementContainer container,
            string parent,
            string text,
            int fontSize,
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
                        FontSize = fontSize,
                        Align = align,
                        Color = ParseColor(color),
                        Font = config.Font,
                        FadeIn = 0.12f
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax
                    }
                }
            });
        }

        private void AddStat(
            CuiElementContainer container,
            string parent,
            string label,
            string value,
            string anchorMin,
            string anchorMax,
            string id)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = ParseColor(config.CardColor) },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax
                }
            }, parent, id);

            AddText(container, id, label, 9, TextAnchor.UpperLeft,
                config.SecondaryTextColor, "0.08 0.55", "0.92 0.92", "label");

            AddText(container, id, value, config.StatValueSize, TextAnchor.LowerLeft,
                config.TextColor, "0.08 0.05", "0.92 0.62", "value");
        }

        private void DestroyMenu(BasePlayer player)
        {
            if (player != null)
                CuiHelper.DestroyUi(player, MainLayer);
        }

        #endregion

        #region Helpers

        private string ReplaceVariables(string text, BasePlayer player)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string wipe = GetWipeDate();

            return text
                .Replace("{name}", player != null ? player.displayName : "Игрок")
                .Replace("{online}", BasePlayer.activePlayerList.Count.ToString())
                .Replace("{maxplayers}", ConVar.Server.maxplayers.ToString())
                .Replace("{queue}", (ServerMgr.Instance.connectionQueue.Joining + ServerMgr.Instance.connectionQueue.Queued).ToString())
                .Replace("{datewipe}", wipe);
        }

        private string GetWipeDate()
        {
            try
            {
                var date = SaveRestore.SaveCreatedTime.ToLocalTime();
                return $"{date.Day:00}.{date.Month:00}";
            }
            catch
            {
                return "--.--";
            }
        }

        private string ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "1 1 1 1";

            var parts = value.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4)
                return "1 1 1 1";

            float r, g, b, a;

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out r) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out g) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                return "1 1 1 1";

            // Supports both 0-1 and old 0-255 style configs.
            if (r > 1f || g > 1f || b > 1f || a > 1f)
            {
                r /= 255f;
                g /= 255f;
                b /= 255f;
                a /= 255f;
            }

            return $"{Mathf.Clamp01(r):0.###} {Mathf.Clamp01(g):0.###} {Mathf.Clamp01(b):0.###} {Mathf.Clamp01(a):0.###}";
        }

        private void LoadData()
        {
            try
            {
                players = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>(DataFile);
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
    }
}
