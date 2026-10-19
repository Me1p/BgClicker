using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BgClicker
{
    public sealed class MainForm : Form
    {
        private const int HotkeyId = 0xB001;

        private readonly AppSettings _cfg;
        private readonly ClickEngine _engine = new ClickEngine();
        private readonly Timer _uiTimer = new Timer();
        private readonly ToolTip _tip = new ToolTip();

        private CdpConnection _cdp;
        private string _cdpUrl;

        private FlowLayoutPanel _tabBar;
        private Panel _pageHost;
        private Panel _pageBrowser;
        private Panel _pageWindow;
        private Panel _pageSettings;
        private Button _btnTabBrowser;
        private Button _btnTabWindow;
        private Button _btnTabSettings;
        private RadioButton _rbThemeDark;
        private RadioButton _rbThemeLight;
        private RadioButton _rbThemeSystem;
        private ThemePalette _palette;
        private int _selectedTab;
        private int _lastMode;
        private string _lastStatusText = "Остановлено";
        private bool _lastStatusError;

        private Label _lblCdp;
        private ComboBox _cmbTabs;
        private Button _btnRefresh;
        private Button _btnLaunch;
        private CheckBox _chkSeparate;
        private Button _btnPickBrowser;
        private Button _btnTestBrowser;
        private NumericUpDown _numX;
        private NumericUpDown _numY;
        private Label _lblPointBrowser;

        private Button _btnPickWindow;
        private Button _btnTestWindow;
        private Label _lblWindow;
        private ComboBox _cmbPart;

        private NumericUpDown _numCps;
        private NumericUpDown _numJitter;
        private ComboBox _cmbHotkey;
        private Button _btnStart;
        private Label _lblStatus;

        private NotifyIcon _tray;
        private ToolStripMenuItem _menuToggle;
        private Icon _appIcon;
        private Icon _trayIcon;

        private IntPtr _hwndDeep;
        private IntPtr _hwndRoot;
        private Point _ptDeep;
        private Point _ptRoot;
        private string _winTitle = "";
        private string _winClass = "";

        private bool _loading;
        private bool _starting;
        private bool _exiting;
        private bool _wasRunning;
        private bool _balloonShown;
        private readonly bool _restrictedEnvironment;
        private readonly string _environmentError;

        public MainForm()
        {
            _cfg = AppSettings.Load();
            _palette = ThemeManager.Palette(false);
            _restrictedEnvironment = BrowserLauncher.IsRestrictedEnvironment(out _environmentError);
            Log("startup: lowIntegrity=" + Native.IsLowIntegrity() +
                " appDataBlocked=" + _restrictedEnvironment +
                (string.IsNullOrEmpty(_environmentError) ? "" : " error=" + _environmentError));

            BuildUi();
            ApplySettings();
            CreateTray();
            RegisterHotkey();
            ApplyTheme();

            if (Native.IsLowIntegrity())
            {
                SetStatus("Ограниченный режим (запущено из чата). Перезапустите из меню «Пуск».", true);
            }

            _uiTimer.Interval = 250;
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();
        }

        private void BuildUi()
        {
            Text = "Фоновый кликер";
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(760, 460);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            DoubleBuffered = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8, 8, 8, 8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            BuildTabBar();
            BuildPages();
            BuildBrowserTab();
            BuildWindowTab();
            BuildSettingsTab();

            TableLayoutPanel bottom = BuildBottomPanel();

            BuildTabBarButtons();
            SelectTab(0, false);

            root.Controls.Add(_tabBar, 0, 0);
            root.Controls.Add(_pageHost, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);
        }

        private void BuildTabBar()
        {
            _tabBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4, 6, 4, 4),
                Margin = new Padding(0)
            };
        }

        private void BuildTabBarButtons()
        {
            _btnTabBrowser = MakeTabButton("Браузер (Chrome / Edge)");
            _btnTabWindow = MakeTabButton("Любое окно");
            _btnTabSettings = MakeTabButton("Настройки");

            _btnTabBrowser.Click += delegate { SelectTab(0, true); };
            _btnTabWindow.Click += delegate { SelectTab(1, true); };
            _btnTabSettings.Click += delegate { SelectTab(2, true); };

            _tabBar.Controls.Add(_btnTabBrowser);
            _tabBar.Controls.Add(_btnTabWindow);
            _tabBar.Controls.Add(_btnTabSettings);
        }

        private static Button MakeTabButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(12, 4, 12, 4),
                Margin = new Padding(0, 0, 6, 0),
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
        }

        private void BuildPages()
        {
            _pageHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
            _pageBrowser = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            _pageWindow = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            _pageSettings = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            _pageHost.Controls.Add(_pageBrowser);
            _pageHost.Controls.Add(_pageWindow);
            _pageHost.Controls.Add(_pageSettings);
        }

        private void SelectTab(int index, bool save)
        {
            _selectedTab = index;
            if (index == 0)
            {
                _lastMode = 0;
            }
            else if (index == 1)
            {
                _lastMode = 1;
            }

            if (_pageBrowser != null)
            {
                _pageBrowser.Visible = index == 0;
                _pageWindow.Visible = index == 1;
                _pageSettings.Visible = index == 2;
            }

            if (index == 0)
            {
                SetCpsMaximum(200);
            }
            else if (index == 1)
            {
                SetCpsMaximum(1000);
            }

            UpdateTabVisuals();

            if (save)
            {
                SaveSettings();
            }
        }

        private void SetCpsMaximum(int maximum)
        {
            if (_numCps == null)
            {
                return;
            }

            _numCps.Maximum = maximum;
            if (_numCps.Value > _numCps.Maximum)
            {
                _numCps.Value = _numCps.Maximum;
            }
        }

        private void UpdateTabVisuals()
        {
            if (_btnTabBrowser == null || _palette == null)
            {
                return;
            }

            Button[] buttons = { _btnTabBrowser, _btnTabWindow, _btnTabSettings };
            for (int i = 0; i < buttons.Length; i++)
            {
                bool selected = i == _selectedTab;
                Button button = buttons[i];
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = selected ? _palette.Accent : _palette.Surface;
                button.ForeColor = selected ? _palette.AccentText : _palette.Muted;
                button.FlatAppearance.MouseOverBackColor = selected ? _palette.Accent : _palette.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = selected ? _palette.Accent : _palette.ButtonPressed;
            }

            if (_tabBar != null)
            {
                _tabBar.BackColor = _palette.Surface;
            }
        }

        private void BuildBrowserTab()
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var groupConnection = new ThemedGroupBox
            {
                Text = "Подключение",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 8),
                Margin = new Padding(0, 0, 0, 8)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 5,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            var lblStatusCaption = new Label
            {
                Text = "Статус:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 3)
            };
            _lblCdp = new Label
            {
                Text = "Не подключено",
                AutoSize = false,
                Height = 22,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = Color.DimGray
            };

            var lblTabCaption = new Label
            {
                Text = "Вкладка:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 7, 0, 3)
            };
            _cmbTabs = new ThemedComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                MaxDropDownItems = 14,
                IntegralHeight = false
            };
            _cmbTabs.SelectedIndexChanged += delegate
            {
                if (_loading || _cmbTabs.SelectedItem == null)
                {
                    return;
                }

                _cfg.BrowserUrl = ((CdpTarget)_cmbTabs.SelectedItem).Url;
                SaveSettings();
            };

            _btnRefresh = new Button
            {
                Text = "Подключить / обновить список",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 5, 0, 0)
            };
            _btnRefresh.Click += OnRefreshClick;

            var launchFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 4, 0, 0)
            };
            _btnLaunch = new Button
            {
                Text = "Запустить Chrome с отладкой",
                AutoSize = true,
                Margin = new Padding(0)
            };
            _btnLaunch.Click += OnLaunchClick;
            _chkSeparate = new CheckBox
            {
                Text = "Отдельный профиль (для Chrome 136+)",
                AutoSize = true,
                Margin = new Padding(12, 7, 0, 0)
            };
            _tip.SetToolTip(
                _chkSeparate,
                "Современный Chrome не разрешает отладку для основного профиля. " +
                "Кликер запускает отдельную копию браузера со своей папкой профиля. " +
                "Войдите на нужный сайт в ней один раз — вход сохранится.");
            launchFlow.Controls.Add(_btnLaunch);
            launchFlow.Controls.Add(_chkSeparate);

            var hint = new Label
            {
                Text = "Современный Chrome не отдаёт отладку для основного профиля. Кликер запускает " +
                       "отдельную копию браузера: войдите на нужный сайт в ней один раз, вход сохранится. " +
                       "Основной Chrome можно не закрывать.",
                AutoSize = true,
                MaximumSize = new Size(680, 0),
                ForeColor = Color.DimGray,
                Tag = "hint",
                Margin = new Padding(0, 6, 0, 0)
            };

            grid.Controls.Add(lblStatusCaption, 0, 0);
            grid.Controls.Add(_lblCdp, 1, 0);
            grid.Controls.Add(lblTabCaption, 0, 1);
            grid.Controls.Add(_cmbTabs, 1, 1);
            grid.Controls.Add(_btnRefresh, 1, 2);
            grid.Controls.Add(launchFlow, 1, 3);
            grid.Controls.Add(hint, 1, 4);
            groupConnection.Controls.Add(grid);

            var groupPoint = new ThemedGroupBox
            {
                Text = "Точка клика",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 8),
                Margin = new Padding(0)
            };

            var pointGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 3
            };
            pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++)
            {
                pointGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            var pointFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };
            pointFlow.Controls.Add(new Label
            {
                Text = "X",
                AutoSize = true,
                Margin = new Padding(0, 9, 4, 0)
            });
            _numX = new NumericUpDown
            {
                Width = 70,
                Minimum = 0,
                Maximum = 20000,
                Value = 100,
                Margin = new Padding(0, 5, 12, 0)
            };
            _numX.ValueChanged += delegate { UpdatePointLabel(); };
            pointFlow.Controls.Add(_numX);
            pointFlow.Controls.Add(new Label
            {
                Text = "Y",
                AutoSize = true,
                Margin = new Padding(0, 9, 4, 0)
            });
            _numY = new NumericUpDown
            {
                Width = 70,
                Minimum = 0,
                Maximum = 20000,
                Value = 100,
                Margin = new Padding(0, 5, 12, 0)
            };
            _numY.ValueChanged += delegate { UpdatePointLabel(); };
            pointFlow.Controls.Add(_numY);

            _btnPickBrowser = new Button
            {
                Text = "Указать точку на странице",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0)
            };
            _btnPickBrowser.Click += OnPickBrowserClick;
            pointFlow.Controls.Add(_btnPickBrowser);

            _lblPointBrowser = new Label
            {
                Text = "Координаты в окне браузера: 100, 100",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            var testFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 6, 0, 0)
            };
            _btnTestBrowser = new Button
            {
                Text = "Проверить клик",
                AutoSize = true,
                Margin = new Padding(0)
            };
            _btnTestBrowser.Click += OnTestBrowserClick;
            testFlow.Controls.Add(_btnTestBrowser);
            testFlow.Controls.Add(new Label
            {
                Text = "Клики идут в выбранную вкладку, даже если она неактивна или окно перекрыто.",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Tag = "hint",
                Margin = new Padding(12, 7, 0, 0)
            });

            pointGrid.Controls.Add(pointFlow, 0, 0);
            pointGrid.Controls.Add(_lblPointBrowser, 0, 1);
            pointGrid.Controls.Add(testFlow, 0, 2);
            groupPoint.Controls.Add(pointGrid);

            page.Controls.Add(groupConnection, 0, 0);
            page.Controls.Add(groupPoint, 0, 1);
            _pageBrowser.Controls.Add(page);
        }

        private void BuildWindowTab()
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 5
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++)
            {
                page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            var hint = new Label
            {
                Text = "Выберите окно и точку. Окно может быть перекрыто другими окнами, но не должно быть свёрнуто. " +
                       "Клики отправляются сообщениями Windows — курсор и активное окно не меняются.",
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                ForeColor = Color.DimGray,
                Tag = "hint",
                Margin = new Padding(0, 0, 0, 8)
            };

            _btnPickWindow = new Button
            {
                Text = "Выбрать окно и точку",
                AutoSize = true,
                Height = 34,
                Margin = new Padding(0, 0, 0, 8)
            };
            _btnPickWindow.Click += OnPickWindowClick;

            _lblWindow = new Label
            {
                Text = "Окно не выбрано",
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Margin = new Padding(0, 0, 0, 8)
            };

            var partFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            partFlow.Controls.Add(new Label
            {
                Text = "Куда кликать:",
                AutoSize = true,
                Margin = new Padding(0, 8, 6, 0)
            });
            _cmbPart = new ThemedComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 270,
                Margin = new Padding(0, 4, 0, 0)
            };
            _cmbPart.Items.Add("Дочерний элемент (рекомендуется)");
            _cmbPart.Items.Add("Главное окно");
            _cmbPart.SelectedIndex = 0;
            _cmbPart.SelectedIndexChanged += delegate { SaveSettings(); };
            partFlow.Controls.Add(_cmbPart);

            _btnTestWindow = new Button
            {
                Text = "Проверить клик",
                AutoSize = true,
                Margin = new Padding(0)
            };
            _btnTestWindow.Click += OnTestWindowClick;

            page.Controls.Add(hint, 0, 0);
            page.Controls.Add(_btnPickWindow, 0, 1);
            page.Controls.Add(_lblWindow, 0, 2);
            page.Controls.Add(partFlow, 0, 3);
            page.Controls.Add(_btnTestWindow, 0, 4);
            _pageWindow.Controls.Add(page);
        }

        private void BuildSettingsTab()
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 1
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var group = new ThemedGroupBox
            {
                Text = "Оформление",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 10),
                Margin = new Padding(0)
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _rbThemeDark = new RadioButton
            {
                Text = "Тёмная",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4)
            };
            _rbThemeLight = new RadioButton
            {
                Text = "Светлая",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4)
            };
            _rbThemeSystem = new RadioButton
            {
                Text = "Как в системе",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4)
            };

            _rbThemeDark.CheckedChanged += delegate
            {
                if (!_loading && _rbThemeDark.Checked)
                {
                    SetTheme("dark");
                }
            };
            _rbThemeLight.CheckedChanged += delegate
            {
                if (!_loading && _rbThemeLight.Checked)
                {
                    SetTheme("light");
                }
            };
            _rbThemeSystem.CheckedChanged += delegate
            {
                if (!_loading && _rbThemeSystem.Checked)
                {
                    SetTheme("system");
                }
            };

            flow.Controls.Add(_rbThemeDark);
            flow.Controls.Add(_rbThemeLight);
            flow.Controls.Add(_rbThemeSystem);
            flow.Controls.Add(new Label
            {
                Text = "Тема применяется сразу и сохраняется.",
                AutoSize = true,
                Tag = "hint",
                Margin = new Padding(0, 10, 0, 0)
            });

            group.Controls.Add(flow);
            page.Controls.Add(group, 0, 0);
            _pageSettings.Controls.Add(page);
        }

        private TableLayoutPanel BuildBottomPanel()
        {
            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 6, 0, 0)
            };
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var settingsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            settingsFlow.Controls.Add(new Label
            {
                Text = "Скорость:",
                AutoSize = true,
                Margin = new Padding(0, 9, 4, 0)
            });
            _numCps = new NumericUpDown
            {
                Width = 64,
                Minimum = 1,
                Maximum = 200,
                Value = 10,
                Margin = new Padding(0, 5, 4, 0)
            };
            settingsFlow.Controls.Add(_numCps);
            settingsFlow.Controls.Add(new Label
            {
                Text = "кликов/с",
                AutoSize = true,
                Margin = new Padding(0, 9, 16, 0)
            });

            settingsFlow.Controls.Add(new Label
            {
                Text = "Разброс:",
                AutoSize = true,
                Margin = new Padding(0, 9, 4, 0)
            });
            _numJitter = new NumericUpDown
            {
                Width = 52,
                Minimum = 0,
                Maximum = 50,
                Value = 0,
                Margin = new Padding(0, 5, 4, 0)
            };
            settingsFlow.Controls.Add(_numJitter);
            settingsFlow.Controls.Add(new Label
            {
                Text = "%",
                AutoSize = true,
                Margin = new Padding(0, 9, 16, 0)
            });

            settingsFlow.Controls.Add(new Label
            {
                Text = "Клавиша:",
                AutoSize = true,
                Margin = new Padding(0, 9, 4, 0)
            });
            _cmbHotkey = new ThemedComboBox
            {
                Width = 64,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 0, 0)
            };
            for (int i = 1; i <= 12; i++)
            {
                _cmbHotkey.Items.Add("F" + i);
            }
            _cmbHotkey.SelectedIndexChanged += OnHotkeyChanged;
            settingsFlow.Controls.Add(_cmbHotkey);

            var startRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            startRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            startRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            startRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnStart = new Button
            {
                Text = "▶  Старт",
                Dock = DockStyle.Fill,
                Height = 42,
                MinimumSize = new Size(0, 42),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 115, 200),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Margin = new Padding(0, 2, 12, 2),
                UseVisualStyleBackColor = false
            };
            _btnStart.FlatAppearance.BorderSize = 0;
            _btnStart.Click += delegate { ToggleClick(); };

            _lblStatus = new Label
            {
                Text = "Остановлено",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = Color.DimGray
            };

            startRow.Controls.Add(_btnStart, 0, 0);
            startRow.Controls.Add(_lblStatus, 1, 0);

            bottom.Controls.Add(settingsFlow, 0, 0);
            bottom.Controls.Add(startRow, 0, 1);
            return bottom;
        }

        private void ApplySettings()
        {
            _loading = true;
            try
            {
                int hotkeyIndex = _cfg.HotkeyVk - 0x70;
                _cmbHotkey.SelectedIndex = (hotkeyIndex >= 0 && hotkeyIndex < 12) ? hotkeyIndex : 7;
                _chkSeparate.Checked = _cfg.SeparateProfile;
                _numX.Value = Clamp(_cfg.PointX, 0, 20000);
                _numY.Value = Clamp(_cfg.PointY, 0, 20000);
                _cmbPart.SelectedIndex = _cfg.UseDeepChild ? 0 : 1;

                if (_cfg.HwndRoot != 0)
                {
                    _hwndRoot = new IntPtr(_cfg.HwndRoot);
                    _hwndDeep = new IntPtr(_cfg.HwndDeep);
                    _ptRoot = new Point(_cfg.RootX, _cfg.RootY);
                    _ptDeep = new Point(_cfg.DeepX, _cfg.DeepY);
                    _winTitle = _cfg.WindowTitle ?? "";
                    _winClass = "";
                }

                _lastMode = _cfg.Mode == "window" ? 1 : 0;
                _rbThemeDark.Checked = _cfg.Theme == "dark";
                _rbThemeLight.Checked = _cfg.Theme == "light";
                _rbThemeSystem.Checked = _cfg.Theme != "dark" && _cfg.Theme != "light";
                SelectTab(_lastMode, false);
                _numCps.Maximum = _lastMode == 0 ? 200 : 1000;
                _numCps.Value = Clamp(_cfg.Cps, 1, (int)_numCps.Maximum);
                _numJitter.Value = Clamp(_cfg.Jitter, 0, 50);
            }
            finally
            {
                _loading = false;
            }

            UpdatePointLabel();
            UpdateWindowLabel();
        }

        private void SaveSettings()
        {
            if (_loading)
            {
                return;
            }

            _cfg.Mode = _lastMode == 0 ? "browser" : "window";
            _cfg.Cps = (int)_numCps.Value;
            _cfg.Jitter = (int)_numJitter.Value;
            _cfg.PointX = (int)_numX.Value;
            _cfg.PointY = (int)_numY.Value;
            _cfg.HwndDeep = _hwndDeep.ToInt64();
            _cfg.HwndRoot = _hwndRoot.ToInt64();
            _cfg.DeepX = _ptDeep.X;
            _cfg.DeepY = _ptDeep.Y;
            _cfg.RootX = _ptRoot.X;
            _cfg.RootY = _ptRoot.Y;
            _cfg.WindowTitle = _winTitle;
            _cfg.SeparateProfile = _chkSeparate.Checked;
            _cfg.UseDeepChild = _cmbPart.SelectedIndex != 1;
            if (_cmbTabs.SelectedItem != null)
            {
                _cfg.BrowserUrl = ((CdpTarget)_cmbTabs.SelectedItem).Url;
            }

            _cfg.Save();
        }

        private void CreateTray()
        {
            _appIcon = LoadAppIcon();
            Icon = _appIcon;
            _trayIcon = new Icon(_appIcon, SystemInformation.SmallIconSize);

            _tray = new NotifyIcon
            {
                Icon = _trayIcon,
                Visible = true,
                Text = "Фоновый кликер — остановлен"
            };

            var menu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("Показать окно", null, delegate { ShowFromTray(); });
            _menuToggle = new ToolStripMenuItem("Старт", null, delegate { ToggleClick(); });
            var trayCheck = new ToolStripMenuItem("Сворачивать в трей")
            {
                CheckOnClick = true,
                Checked = _cfg.MinimizeToTray
            };
            trayCheck.CheckedChanged += delegate
            {
                _cfg.MinimizeToTray = trayCheck.Checked;
                SaveSettings();
            };
            var exitItem = new ToolStripMenuItem("Выход", null, delegate
            {
                _exiting = true;
                Close();
            });

            menu.Items.Add(showItem);
            menu.Items.Add(_menuToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(trayCheck);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowFromTray(); };
            _tip.SetToolTip(_btnPickBrowser, "Нажмите, затем кликните в нужную точку на странице браузера.");
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (extracted != null)
                {
                    return extracted;
                }
            }
            catch
            {
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        private bool RegisterHotkey()
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            bool ok = Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_NOREPEAT, (uint)_cfg.HotkeyVk);
            if (!ok)
            {
                SetStatus("Клавиша " + HotkeyName() + " занята другой программой — выберите другую", true);
            }

            return ok;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_SHOW_APP)
            {
                ShowFromTray();
                return;
            }

            if (message.Msg == Native.WM_HOTKEY && message.WParam.ToInt32() == HotkeyId)
            {
                ToggleClick();
                return;
            }

            base.WndProc(ref message);
        }

        private async void ToggleClick()
        {
            if (_starting)
            {
                return;
            }

            if (_engine.IsRunning)
            {
                StopClicking();
                return;
            }

            _starting = true;
            _btnStart.Enabled = false;
            try
            {
                if (_lastMode == 0)
                {
                    await StartBrowserAsync();
                }
                else
                {
                    StartWindowClicking();
                }
            }
            finally
            {
                _starting = false;
                _btnStart.Enabled = true;
            }
        }

        private async Task StartBrowserAsync()
        {
            SetStatus("Подключаюсь к браузеру...", false);
            if (!await EnsureConnectedAsync())
            {
                return;
            }

            CdpConnection connection = _cdp;
            int x = (int)_numX.Value;
            int y = (int)_numY.Value;
            int cps = (int)_numCps.Value;
            int jitter = (int)_numJitter.Value;

            bool started = _engine.Start(
                delegate
                {
                    connection.ClickSync(x, y);
                    return true;
                },
                cps,
                jitter);

            if (!started)
            {
                SetStatus("Не удалось запустить: предыдущий поток ещё завершается", true);
                return;
            }

            _wasRunning = true;
            UpdateRunUi();
            SetStatus("Работает · 0 кликов", false);
            SaveSettings();
        }

        private void StartWindowClicking()
        {
            IntPtr target;
            Point point;
            if (!ResolveWindow(out target, out point))
            {
                SetStatus("Сначала выберите окно и точку", true);
                return;
            }

            int cps = (int)_numCps.Value;
            int jitter = (int)_numJitter.Value;

            bool started = _engine.Start(
                delegate { return Native.PostClick(target, point.X, point.Y); },
                cps,
                jitter);

            if (!started)
            {
                SetStatus("Не удалось запустить: предыдущий поток ещё завершается", true);
                return;
            }

            _wasRunning = true;
            UpdateRunUi();
            SetStatus("Работает · 0 кликов", false);
            SaveSettings();
        }

        private void StopClicking()
        {
            _engine.Stop();
            _wasRunning = false;
            UpdateRunUi();
            SetStatus("Остановлено · " + _engine.Count.ToString("N0") + " кликов", false);
        }

        private bool ResolveWindow(out IntPtr target, out Point point)
        {
            bool preferDeep = _cmbPart.SelectedIndex != 1;
            if (preferDeep && Native.IsWindow(_hwndDeep))
            {
                target = _hwndDeep;
                point = _ptDeep;
                return true;
            }

            if (Native.IsWindow(_hwndRoot))
            {
                target = _hwndRoot;
                point = _ptRoot;
                return true;
            }

            if (Native.IsWindow(_hwndDeep))
            {
                target = _hwndDeep;
                point = _ptDeep;
                return true;
            }

            target = IntPtr.Zero;
            point = Point.Empty;
            return false;
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            var target = _cmbTabs.SelectedItem as CdpTarget;
            if (target == null)
            {
                await RefreshTargetsAsync(false);
                target = _cmbTabs.SelectedItem as CdpTarget;
            }

            if (target == null)
            {
                SetStatus("Браузер с отладкой не найден. Нажмите «Запустить Chrome с отладкой».", true);
                return false;
            }

            if (_cdp != null && _cdp.IsConnected && _cdpUrl == target.WsUrl)
            {
                return true;
            }

            CloseCdp();
            try
            {
                var connection = new CdpConnection();
                await connection.ConnectAsync(target);
                _cdp = connection;
                _cdpUrl = target.WsUrl;
                _lblCdp.Text = "Подключено: " + (target.Title ?? target.Url);
                return true;
            }
            catch (Exception ex)
            {
                CloseCdp();
                SetStatus("Не удалось подключиться: " + ex.Message, true);
                return false;
            }
        }

        private void CloseCdp()
        {
            if (_cdp != null)
            {
                _cdp.Dispose();
                _cdp = null;
            }

            _cdpUrl = null;
        }

        private async Task RefreshTargetsAsync(bool verbose)
        {
            string previous = _cmbTabs.SelectedItem != null
                ? ((CdpTarget)_cmbTabs.SelectedItem).Url
                : _cfg.BrowserUrl;

            try
            {
                List<CdpTarget> targets = await CdpConnection.ListTargetsAsync(BrowserLauncher.DebugPort);

                _loading = true;
                try
                {
                    _cmbTabs.Items.Clear();
                    foreach (CdpTarget target in targets)
                    {
                        _cmbTabs.Items.Add(target);
                    }
                }
                finally
                {
                    _loading = false;
                }

                if (targets.Count == 0)
                {
                    _lblCdp.Text = "Порт открыт, но вкладок нет";
                    return;
                }

                int index = 0;
                if (!string.IsNullOrEmpty(previous))
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (targets[i].Url == previous)
                        {
                            index = i;
                            break;
                        }
                    }
                }

                _cmbTabs.SelectedIndex = index;
                _lblCdp.Text = "Порт 9222 открыт · вкладок: " + targets.Count;
            }
            catch (Exception ex)
            {
                _loading = true;
                try
                {
                    _cmbTabs.Items.Clear();
                }
                finally
                {
                    _loading = false;
                }

                _lblCdp.Text = "Chrome с отладкой не найден";
                if (verbose)
                {
                    MessageBox.Show(
                        this,
                        "Не удалось получить список вкладок.\n\n" +
                        "1. Нажмите «Запустить Chrome с отладкой».\n" +
                        "2. Если Chrome уже был открыт, полностью закройте его и запустите снова.\n" +
                        "3. Либо включите «Отдельный профиль», чтобы не закрывать основной Chrome.\n\n" +
                        "Техническая подробность: " + ex.Message,
                        "Фоновый кликер",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private static async Task<bool> WaitForPortAsync(int port, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    await CdpConnection.ListTargetsAsync(port);
                    return true;
                }
                catch
                {
                    await Task.Delay(400);
                }
            }

            return false;
        }

        private async void OnRefreshClick(object sender, EventArgs e)
        {
            _btnRefresh.Enabled = false;
            try
            {
                await RefreshTargetsAsync(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
            }
            finally
            {
                _btnRefresh.Enabled = true;
            }
        }

        private async void OnLaunchClick(object sender, EventArgs e)
        {
            _btnLaunch.Enabled = false;
            try
            {
                Log("launch requested: separate=" + _chkSeparate.Checked);

                string browser = BrowserLauncher.FindBrowser();
                if (browser == null)
                {
                    Log("browser not found");
                    MessageBox.Show(
                        this,
                        "Chrome или Edge не найден на этом компьютере.",
                        "Фоновый кликер",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                Log("browser=" + browser);
                BrowserLauncher.Launch(browser, _chkSeparate.Checked);
                Log("browser process started; profile=" + BrowserLauncher.LastProfileDirectory);
                _lblCdp.Text = "Запускаю браузер...";

                bool ready = await WaitForPortAsync(BrowserLauncher.DebugPort, 15000);
                Log("port ready=" + ready);
                if (!ready)
                {
                    _lblCdp.Text = "Отладка недоступна";
                    string text =
                        "Браузер запустился, но порт отладки 9222 не открылся.\n\n" +
                        "В Chrome 136+ отладка для основного профиля запрещена. Включите " +
                        "«Отдельный профиль (для Chrome 136+)» и нажмите кнопку ещё раз.\n\n" +
                        "Если отдельный профиль уже включён, закройте окно Chrome, которое открыл " +
                        "кликер, и повторите запуск. На некоторых корпоративных ПК удалённая " +
                        "отладка запрещена политикой.";

                    if (_restrictedEnvironment)
                    {
                        text += "\n\nДиагностика: кликер не смог писать в AppData (" +
                                _environmentError + "). Если вы запускали его из окна чата, " +
                                "закройте кликер и запустите ярлыком с рабочего стола.";
                    }

                    MessageBox.Show(
                        this,
                        text,
                        "Фоновый кликер",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                await RefreshTargetsAsync(false);
            }
            catch (Exception ex)
            {
                Log("launch error: " + ex);
                SetStatus("Не удалось запустить браузер: " + ex.Message, true);
            }
            finally
            {
                _btnLaunch.Enabled = true;
            }
        }

        private async void OnPickBrowserClick(object sender, EventArgs e)
        {
            if (!await EnsureConnectedAsync())
            {
                return;
            }

            Hide();
            try
            {
                await BringPageToFrontAsync();
                SetStatus("Кликните в точку на странице (Esc — отмена)", false);

                object value = await _cdp.EvaluateAsync(CdpScripts.PickPoint, true, 180000);
                var point = value as Dictionary<string, object>;
                if (point != null && point.ContainsKey("x") && point.ContainsKey("y"))
                {
                    int x = Convert.ToInt32(point["x"]);
                    int y = Convert.ToInt32(point["y"]);
                    _numX.Value = Clamp(x, 0, 20000);
                    _numY.Value = Clamp(y, 0, 20000);
                    SetStatus("Точка сохранена: " + x + ", " + y, false);
                    SaveSettings();
                }
                else
                {
                    SetStatus("Выбор точки отменён", false);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось выбрать точку: " + ex.Message, true);
            }
            finally
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private async Task BringPageToFrontAsync()
        {
            try
            {
                Dictionary<string, object> info = await _cdp.SendAsync(
                    "Browser.getWindowForTarget", new Dictionary<string, object>(), 3000);
                var result = info.ContainsKey("result")
                    ? info["result"] as Dictionary<string, object>
                    : null;

                if (result != null && result.ContainsKey("windowId"))
                {
                    var bounds = result.ContainsKey("bounds")
                        ? result["bounds"] as Dictionary<string, object>
                        : null;
                    string state = bounds != null && bounds.ContainsKey("windowState")
                        ? Convert.ToString(bounds["windowState"])
                        : "";

                    if (state == "minimized")
                    {
                        var parameters = new Dictionary<string, object>
                        {
                            { "windowId", result["windowId"] },
                            { "bounds", new Dictionary<string, object> { { "windowState", "normal" } } }
                        };
                        await _cdp.SendAsync("Browser.setWindowBounds", parameters, 3000);
                    }
                }
            }
            catch
            {
            }

            try
            {
                await _cdp.SendAsync("Page.bringToFront", new Dictionary<string, object>(), 3000);
            }
            catch
            {
            }
        }

        private async void OnTestBrowserClick(object sender, EventArgs e)
        {
            if (!await EnsureConnectedAsync())
            {
                return;
            }

            try
            {
                await _cdp.ClickAsync((double)_numX.Value, (double)_numY.Value);
                SetStatus("Тестовый клик отправлен", false);
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка клика: " + ex.Message, true);
            }
        }

        private void OnPickWindowClick(object sender, EventArgs e)
        {
            Point? picked = null;
            Hide();
            try
            {
                using (var picker = new PickerForm())
                {
                    if (picker.ShowDialog() == DialogResult.OK)
                    {
                        picked = picker.PickedPoint;
                    }
                }

                if (picked == null)
                {
                    return;
                }

                System.Threading.Thread.Sleep(80);
                Point screenPoint = picked.Value;
                var probe = new Native.POINT(screenPoint.X, screenPoint.Y);
                IntPtr deep = Native.WindowFromPoint(probe);
                if (deep == IntPtr.Zero)
                {
                    SetStatus("Не удалось определить окно под точкой", true);
                    return;
                }

                IntPtr root = Native.GetAncestor(deep, Native.GA_ROOT);
                if (root == IntPtr.Zero)
                {
                    root = deep;
                }

                var deepPoint = new Native.POINT(screenPoint.X, screenPoint.Y);
                var rootPoint = new Native.POINT(screenPoint.X, screenPoint.Y);
                Native.ScreenToClient(deep, ref deepPoint);
                Native.ScreenToClient(root, ref rootPoint);

                _hwndDeep = deep;
                _hwndRoot = root;
                _ptDeep = new Point(deepPoint.X, deepPoint.Y);
                _ptRoot = new Point(rootPoint.X, rootPoint.Y);
                _winTitle = Native.GetWindowTitle(root);
                _winClass = Native.GetWindowClass(deep);

                UpdateWindowLabel();
                SaveSettings();
                SetStatus("Окно выбрано: " + (_winTitle.Length > 0 ? _winTitle : "(без названия)"), false);
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка выбора окна: " + ex.Message, true);
            }
            finally
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private void OnTestWindowClick(object sender, EventArgs e)
        {
            IntPtr target;
            Point point;
            if (!ResolveWindow(out target, out point))
            {
                SetStatus("Сначала выберите окно и точку", true);
                return;
            }

            if (Native.PostClick(target, point.X, point.Y))
            {
                SetStatus("Тестовый клик отправлен", false);
            }
            else
            {
                SetStatus("Окно больше не существует — выберите заново", true);
            }
        }

        private void OnUiTick(object sender, EventArgs e)
        {
            if (_engine.IsRunning)
            {
                _wasRunning = true;
                SetStatus(string.Format("Работает · {0:N0} кликов", _engine.Count), false);
            }
            else if (_wasRunning)
            {
                _wasRunning = false;
                UpdateRunUi();

                if (!string.IsNullOrEmpty(_engine.LastError))
                {
                    SetStatus("Остановлено: " + _engine.LastError, true);
                    _tray.ShowBalloonTip(
                        3000,
                        "Кликер остановлен",
                        _engine.LastError,
                        ToolTipIcon.Warning);
                }
                else
                {
                    SetStatus("Остановлено · " + _engine.Count.ToString("N0") + " кликов", false);
                }
            }
        }

        private void UpdateRunUi()
        {
            bool running = _engine.IsRunning;
            _btnStart.Text = running ? "■  Стоп" : "▶  Старт";
            _btnStart.BackColor = running ? _palette.Danger : _palette.Accent;
            _btnStart.ForeColor = _palette.AccentText;
            _tabBar.Enabled = !running;

            if (_menuToggle != null)
            {
                _menuToggle.Text = running ? "Стоп" : "Старт";
            }

            if (_tray != null)
            {
                _tray.Text = running
                    ? "Фоновый кликер — работает"
                    : "Фоновый кликер — остановлен";
            }
        }

        private void SetStatus(string text, bool error)
        {
            _lastStatusText = text;
            _lastStatusError = error;
            _lblStatus.Text = text;
            _lblStatus.ForeColor = error ? _palette.Danger : _palette.Muted;
        }

        private void SetTheme(string mode)
        {
            _cfg.Theme = mode;
            ApplyTheme();
            SaveSettings();
        }

        private void ApplyTheme()
        {
            bool dark = ThemeManager.IsDark(_cfg.Theme);
            _palette = ThemeManager.Palette(dark);
            ThemeManager.Apply(this, _palette);
            Native.SetDarkTitleBar(Handle, dark);
            StyleSpecialControls();
            UpdateTabVisuals();
        }

        private void StyleSpecialControls()
        {
            if (_btnStart != null)
            {
                _btnStart.FlatStyle = FlatStyle.Flat;
                _btnStart.FlatAppearance.BorderSize = 0;
                _btnStart.BackColor = _engine.IsRunning ? _palette.Danger : _palette.Accent;
                _btnStart.ForeColor = _palette.AccentText;
            }

            if (_lblStatus != null)
            {
                _lblStatus.ForeColor = _lastStatusError ? _palette.Danger : _palette.Muted;
            }

            if (_lblCdp != null)
            {
                _lblCdp.ForeColor = _palette.Text;
            }

            if (_tabBar != null)
            {
                _tabBar.BackColor = _palette.Surface;
            }
        }

        private void UpdatePointLabel()
        {
            _lblPointBrowser.Text = string.Format(
                "Координаты в окне браузера: {0}, {1}",
                (int)_numX.Value,
                (int)_numY.Value);
        }

        private void UpdateWindowLabel()
        {
            if (_hwndRoot == IntPtr.Zero)
            {
                _lblWindow.Text = "Окно не выбрано";
                return;
            }

            _lblWindow.Text = string.Format(
                "Окно: {0}\nКласс: {1}\nТочка: {2}, {3}   (hwnd 0x{4:X})",
                string.IsNullOrEmpty(_winTitle) ? "(без названия)" : _winTitle,
                string.IsNullOrEmpty(_winClass) ? "—" : _winClass,
                _ptDeep.X,
                _ptDeep.Y,
                _hwndDeep.ToInt64());
        }

        private void OnHotkeyChanged(object sender, EventArgs e)
        {
            if (_loading || _cmbHotkey.SelectedIndex < 0)
            {
                return;
            }

            _cfg.HotkeyVk = 0x70 + _cmbHotkey.SelectedIndex;
            RegisterHotkey();
            SaveSettings();
        }

        private string HotkeyName()
        {
            return ((Keys)_cfg.HotkeyVk).ToString();
        }

        private void HideToTray()
        {
            if (!Visible)
            {
                return;
            }

            Hide();
            if (!_balloonShown && _tray != null)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(
                    3000,
                    "Фоновый кликер",
                    "Приложение свёрнуто в трей. " + HotkeyName() + " — старт/стоп.",
                    ToolTipIcon.Info);
            }

            SaveSettings();
        }

        private void ShowFromTray()
        {
            if (!Visible)
            {
                Show();
            }

            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && _cfg.MinimizeToTray && Visible && IsHandleCreated)
            {
                BeginInvoke(new Action(HideToTray));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && _cfg.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _exiting = true;
            _uiTimer.Stop();
            _engine.Stop();
            try
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
            }
            catch
            {
            }

            SaveSettings();

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            CloseCdp();

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_appIcon != null)
            {
                _appIcon.Dispose();
                _appIcon = null;
            }

            base.OnFormClosing(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetDarkTitleBar(Handle, ThemeManager.IsDark(_cfg.Theme));

            // Safety net for small or heavily scaled screens: never open larger
            // than the available work area.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Width > area.Width - 16 || Height > area.Height - 16)
            {
                Size = new Size(
                    Math.Min(Width, area.Width - 16),
                    Math.Min(Height, area.Height - 16));
            }

            // If the browser is already running with the debug port, populate
            // the tab list right away.
            BeginInvoke(new Action(async delegate
            {
                try
                {
                    await RefreshTargetsAsync(false);
                }
                catch
                {
                }
            }));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launch.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
