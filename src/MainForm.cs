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
        private readonly Dictionary<string, CdpConnection> _connections =
            new Dictionary<string, CdpConnection>();

        private FlowLayoutPanel _tabBar;
        private Panel _pageHost;
        private Panel _pageBrowser;
        private Panel _pageWindow;
        private Panel _pageSettings;
        private Panel _pageTargets;
        private Button _btnTabBrowser;
        private Button _btnTabWindow;
        private Button _btnTabSettings;
        private Button _btnTabTargets;
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
        private Button _btnAddBrowserTarget;
        private Button _btnNewTab;
        private NumericUpDown _numX;
        private NumericUpDown _numY;
        private Label _lblPointBrowser;

        private Button _btnPickWindow;
        private Button _btnTestWindow;
        private Button _btnAddWindowTarget;
        private Label _lblWindow;
        private ComboBox _cmbPart;

        private CheckBox _chkAutoReload;
        private NumericUpDown _numReload;
        private CheckBox _chkReliable;
        private Label _lblLogPath;
        private ListBox _lstTargets;
        private Label _lblTargetsCount;
        private Button _btnTargetPick;
        private readonly List<ClickTarget> _targets = new List<ClickTarget>();

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
        private int _skippedTargets;
        private DateTime _rateLastTime;
        private long _rateLastCount;
        private double _rateValue;
        private bool _reliableClicks;
        private DateTime _lastDiagLog;
        private readonly bool _restrictedEnvironment;
        private readonly string _environmentError;

        public MainForm()
        {
            _cfg = AppSettings.Load();
            RemoveLegacyLog();
            CdpConnection.Logger = delegate(string message) { Log("cdp: " + message); };
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
            BuildTargetsTab();

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
            _btnTabTargets = MakeTabButton("Цели");

            _btnTabBrowser.Click += delegate { SelectTab(0, true); };
            _btnTabWindow.Click += delegate { SelectTab(1, true); };
            _btnTabSettings.Click += delegate { SelectTab(2, true); };
            _btnTabTargets.Click += delegate { SelectTab(3, true); };

            _tabBar.Controls.Add(_btnTabBrowser);
            _tabBar.Controls.Add(_btnTabWindow);
            _tabBar.Controls.Add(_btnTabSettings);
            _tabBar.Controls.Add(_btnTabTargets);
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
            _pageTargets = new Panel { Dock = DockStyle.Fill };

            _pageHost.Controls.Add(_pageBrowser);
            _pageHost.Controls.Add(_pageWindow);
            _pageHost.Controls.Add(_pageSettings);
            _pageHost.Controls.Add(_pageTargets);
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
                _pageTargets.Visible = index == 3;
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

            _numCps.Maximum = 100000;
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

            Button[] buttons = { _btnTabBrowser, _btnTabWindow, _btnTabSettings, _btnTabTargets };
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

                var selected = (CdpTarget)_cmbTabs.SelectedItem;
                _cfg.BrowserUrl = selected.Url;

                ClickTarget target = FindTargetForTab(selected);
                if (target != null)
                {
                    _loading = true;
                    try
                    {
                        _numX.Value = Clamp((int)target.Config.X, 0, 20000);
                        _numY.Value = Clamp((int)target.Config.Y, 0, 20000);
                    }
                    finally
                    {
                        _loading = false;
                    }

                    UpdatePointLabel();
                }

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
                RowCount = 4
            };
            pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 4; i++)
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

            var addFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 6, 0, 0)
            };
            _btnAddBrowserTarget = new Button
            {
                Text = "Добавить эту вкладку",
                AutoSize = true,
                Margin = new Padding(0)
            };
            _btnAddBrowserTarget.Click += OnAddBrowserTargetClick;
            addFlow.Controls.Add(_btnAddBrowserTarget);

            _btnNewTab = new Button
            {
                Text = "Открыть вторую вкладку",
                AutoSize = true,
                Margin = new Padding(8, 0, 0, 0)
            };
            _btnNewTab.Click += OnNewTabClick;
            addFlow.Controls.Add(_btnNewTab);

            addFlow.Controls.Add(new Label
            {
                Text = "Новая вкладка откроется с той же страницей.",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Tag = "hint",
                Margin = new Padding(12, 7, 0, 0)
            });
            pointGrid.Controls.Add(addFlow, 0, 3);

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
                RowCount = 6
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++)
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

            _btnAddWindowTarget = new Button
            {
                Text = "Добавить окно в цели",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            _btnAddWindowTarget.Click += OnAddWindowTargetClick;

            page.Controls.Add(hint, 0, 0);
            page.Controls.Add(_btnPickWindow, 0, 1);
            page.Controls.Add(_lblWindow, 0, 2);
            page.Controls.Add(partFlow, 0, 3);
            page.Controls.Add(_btnTestWindow, 0, 4);
            page.Controls.Add(_btnAddWindowTarget, 0, 5);
            _pageWindow.Controls.Add(page);
        }

        private void BuildSettingsTab()
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 4
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

            var reloadGroup = new ThemedGroupBox
            {
                Text = "Автообновление страницы",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 10),
                Margin = new Padding(0, 8, 0, 0)
            };

            var reloadFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _chkAutoReload = new CheckBox
            {
                Text = "Обновлять страницу автоматически",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4)
            };

            var intervalFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0)
            };
            intervalFlow.Controls.Add(new Label
            {
                Text = "Интервал:",
                AutoSize = true,
                Margin = new Padding(0, 7, 6, 0)
            });
            _numReload = new NumericUpDown
            {
                Width = 70,
                Minimum = 10,
                Maximum = 3600,
                Value = 120,
                Margin = new Padding(0, 3, 6, 0)
            };
            intervalFlow.Controls.Add(_numReload);
            intervalFlow.Controls.Add(new Label
            {
                Text = "сек",
                AutoSize = true,
                Margin = new Padding(0, 7, 0, 0)
            });

            _chkAutoReload.CheckedChanged += delegate
            {
                _numReload.Enabled = _chkAutoReload.Checked;
            };

            reloadFlow.Controls.Add(_chkAutoReload);
            reloadFlow.Controls.Add(intervalFlow);
            reloadFlow.Controls.Add(new Label
            {
                Text = "Для целей-вкладок. На время загрузки клики приостанавливаются.",
                AutoSize = true,
                Tag = "hint",
                Margin = new Padding(0, 8, 0, 0)
            });
            reloadGroup.Controls.Add(reloadFlow);
            page.Controls.Add(reloadGroup, 0, 1);

            var clicksGroup = new ThemedGroupBox
            {
                Text = "Клики",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 10),
                Margin = new Padding(0, 8, 0, 0)
            };

            var clicksFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _chkReliable = new CheckBox
            {
                Text = "Совместимый режим (надёжнее для зачисления)",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4)
            };
            _chkReliable.CheckedChanged += delegate
            {
                if (_loading)
                {
                    return;
                }

                _reliableClicks = _chkReliable.Checked;
                SaveSettings();
            };
            clicksFlow.Controls.Add(_chkReliable);
            clicksFlow.Controls.Add(new Label
            {
                Text = "Двигает мышь перед каждым кликом и ждёт ответа браузера. Медленнее, " +
                       "но сайты, которые проверяют наведение/нажатие, засчитывают клики.",
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Tag = "hint",
                Margin = new Padding(0, 4, 0, 0)
            });
            clicksGroup.Controls.Add(clicksFlow);
            page.Controls.Add(clicksGroup, 0, 2);

            var diagGroup = new ThemedGroupBox
            {
                Text = "Диагностика",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 10),
                Margin = new Padding(0, 8, 0, 0)
            };

            var diagFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _lblLogPath = new Label
            {
                Text = "Лог: " + (GetLogPath() ?? "недоступен"),
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Tag = "hint",
                Margin = new Padding(0, 4, 0, 6)
            };
            var openLogs = new Button
            {
                Text = "Открыть папку логов",
                AutoSize = true,
                Margin = new Padding(0)
            };
            openLogs.Click += OnOpenLogsClick;
            diagFlow.Controls.Add(_lblLogPath);
            diagFlow.Controls.Add(openLogs);
            diagGroup.Controls.Add(diagFlow);
            page.Controls.Add(diagGroup, 0, 3);

            _pageSettings.Controls.Add(page);
        }

        private void BuildTargetsTab()
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            page.Controls.Add(new Label
            {
                Text = "Кликер будет кликать по всем целям за один цикл. Выберите цель в списке " +
                       "и нажмите «Указать точку», чтобы задать ей свою точку.",
                AutoSize = true,
                MaximumSize = new Size(720, 0),
                ForeColor = Color.DimGray,
                Tag = "hint",
                Margin = new Padding(0, 0, 0, 6)
            }, 0, 0);

            _lstTargets = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                SelectionMode = SelectionMode.One,
                Margin = new Padding(0, 0, 0, 6)
            };
            page.Controls.Add(_lstTargets, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };
            _btnTargetPick = new Button
            {
                Text = "Указать точку",
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            _btnTargetPick.Click += OnPickTargetClick;
            buttons.Controls.Add(_btnTargetPick);

            var remove = new Button
            {
                Text = "Удалить выбранную",
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            remove.Click += OnRemoveTargetClick;
            var clear = new Button
            {
                Text = "Очистить",
                AutoSize = true,
                Margin = new Padding(0)
            };
            clear.Click += OnClearTargetsClick;
            _lblTargetsCount = new Label
            {
                Text = "Целей: 0",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Tag = "hint",
                Margin = new Padding(12, 7, 0, 0)
            };
            buttons.Controls.Add(remove);
            buttons.Controls.Add(clear);
            buttons.Controls.Add(_lblTargetsCount);
            page.Controls.Add(buttons, 0, 2);

            _pageTargets.Controls.Add(page);
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
                Width = 78,
                Minimum = 1,
                Maximum = 100000,
                Value = 10,
                Margin = new Padding(0, 5, 4, 0)
            };
            settingsFlow.Controls.Add(_numCps);
            _tip.SetToolTip(_numCps, "Кликов в секунду на каждую цель");
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
                _chkAutoReload.Checked = _cfg.AutoReload;
                _numReload.Value = Clamp(_cfg.ReloadSeconds, 10, 3600);
                _numReload.Enabled = _chkAutoReload.Checked;
                _reliableClicks = _cfg.ClickMode != "fast";
                _chkReliable.Checked = _reliableClicks;

                _targets.Clear();
                if (_cfg.Targets != null)
                {
                    foreach (ClickTargetSettings saved in _cfg.Targets)
                    {
                        if (saved != null)
                        {
                            _targets.Add(new ClickTarget { Config = saved });
                        }
                    }
                }

                DeduplicateTargets();
                RefreshTargetList();

                SelectTab(_lastMode, false);
                _numCps.Maximum = 100000;
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
            _cfg.AutoReload = _chkAutoReload.Checked;
            _cfg.ReloadSeconds = (int)_numReload.Value;
            _cfg.ClickMode = _chkReliable.Checked ? "reliable" : "fast";
            _cfg.Targets = new List<ClickTargetSettings>();
            foreach (ClickTarget target in _targets)
            {
                _cfg.Targets.Add(target.Config);
            }

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
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowFromTray();
                }
            };
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
                if (_targets.Count > 0)
                {
                    await StartTargetsAsync();
                }
                else if (_lastMode == 0)
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

        private async Task StartTargetsAsync()
        {
            DeduplicateTargets();
            RefreshTargetList();
            ResetRate();

            var snapshot = new List<ClickTarget>(_targets);
            if (snapshot.Count == 0)
            {
                return;
            }

            SetStatus("Обновляю список вкладок...", false);
            await RefreshTargetsAsync(false);
            SetStatus("Подключаюсь к целям...", false);

            var used = new HashSet<string>();
            var active = new List<ClickTarget>();
            int skipped = 0;

            foreach (ClickTarget target in snapshot)
            {
                if (target.Config.Browser)
                {
                    if (await EnsureTargetConnectedAsync(target, used))
                    {
                        active.Add(target);
                    }
                    else
                    {
                        skipped++;
                    }
                }
                else
                {
                    IntPtr hWnd = target.Config.UseRoot
                        ? new IntPtr(target.Config.Root)
                        : new IntPtr(target.Config.Deep);
                    if (Native.IsWindow(hWnd))
                    {
                        active.Add(target);
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }

            if (active.Count == 0)
            {
                SetStatus(
                    "Не найдено ни одной доступной цели. Проверьте, что вкладки открыты, " +
                    "и удалите лишние цели в «Целях».",
                    true);
                return;
            }

            foreach (ClickTarget target in active)
            {
                if (target.Config.Browser)
                {
                    string under = await DescribePointAsync(
                        target.Connection,
                        target.Config.X,
                        target.Config.Y);
                    Log("target " + target + " reliable=" + _reliableClicks + " under=" + under);
                }
            }

            int cps = (int)_numCps.Value;
            int jitter = (int)_numJitter.Value;
            int reloadSeconds = _chkAutoReload.Checked ? (int)_numReload.Value : 0;
            Action reload = null;

            if (reloadSeconds > 0)
            {
                reload = delegate
                {
                    foreach (ClickTarget target in active)
                    {
                        if (!target.Config.Browser || target.Connection == null)
                        {
                            continue;
                        }

                        try
                        {
                            target.Connection.ReloadSync();
                        }
                        catch
                        {
                        }
                    }
                };
            }

            bool started = _engine.Start(
                delegate
                {
                    foreach (ClickTarget target in active)
                    {
                        if (target.Config.Browser)
                        {
                            target.Connection.ClickSync(target.Config.X, target.Config.Y, _reliableClicks);
                        }
                        else
                        {
                            IntPtr hWnd = target.Config.UseRoot
                                ? new IntPtr(target.Config.Root)
                                : new IntPtr(target.Config.Deep);
                            Point point = target.Config.UseRoot
                                ? new Point(target.Config.RootX, target.Config.RootY)
                                : new Point(target.Config.DeepX, target.Config.DeepY);
                            if (!Native.PostClick(hWnd, point.X, point.Y))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                },
                cps,
                jitter,
                reload,
                reloadSeconds,
                active.Count);

            if (!started)
            {
                SetStatus("Не удалось запустить: предыдущий поток ещё завершается", true);
                return;
            }

            _wasRunning = true;
            _skippedTargets = skipped;
            Log("start targets=" + active.Count + " skipped=" + skipped +
                " cps=" + cps + " reliable=" + _reliableClicks + " reload=" + reloadSeconds);
            UpdateRunUi();
            SetStatus(
                "Работает · 0 кликов" + (skipped > 0 ? " · пропущено целей: " + skipped : ""),
                false);
            SaveSettings();
        }

        private void RefreshTargetList()
        {
            if (_lstTargets == null)
            {
                return;
            }

            _lstTargets.BeginUpdate();
            _lstTargets.Items.Clear();
            foreach (ClickTarget target in _targets)
            {
                _lstTargets.Items.Add(target);
            }
            _lstTargets.EndUpdate();

            if (_lblTargetsCount != null)
            {
                _lblTargetsCount.Text = "Целей: " + _targets.Count;
            }
        }

        private bool DeduplicateTargets()
        {
            var seenWs = new HashSet<string>();
            var seenId = new HashSet<string>();
            bool removed = false;

            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                ClickTargetSettings config = _targets[i].Config;
                if (!config.Browser)
                {
                    string windowKey = config.Root == 0 ? null : "w:" + config.Root;
                    if (windowKey != null && !seenWs.Add(windowKey))
                    {
                        _targets.RemoveAt(i);
                        removed = true;
                    }

                    continue;
                }

                bool duplicate = false;
                if (!string.IsNullOrEmpty(config.Id) && !seenId.Add(config.Id))
                {
                    duplicate = true;
                }

                if (!string.IsNullOrEmpty(config.WsUrl) && !seenWs.Add(config.WsUrl))
                {
                    duplicate = true;
                }

                if (duplicate)
                {
                    _targets.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        private bool HasBrowserTargetForUrl(string url)
        {
            string normalized = NormalizeUrl(url);
            return _targets.Exists(delegate(ClickTarget target)
            {
                return target.Config.Browser &&
                       NormalizeUrl(target.Config.Url) == normalized;
            });
        }

        private ClickTarget FindTargetForTab(CdpTarget tab)
        {
            if (tab == null)
            {
                return null;
            }

            return _targets.Find(delegate(ClickTarget target)
            {
                if (!target.Config.Browser)
                {
                    return false;
                }

                return (!string.IsNullOrEmpty(tab.Id) && target.Config.Id == tab.Id) ||
                       target.Config.WsUrl == tab.WsUrl;
            });
        }

        private void OnAddBrowserTargetClick(object sender, EventArgs e)
        {
            var tab = _cmbTabs.SelectedItem as CdpTarget;
            if (tab == null)
            {
                SetStatus("Сначала выберите вкладку", true);
                return;
            }

            double x = (double)_numX.Value;
            double y = (double)_numY.Value;
            AddOrUpdateBrowserTarget(tab, x, y);
            DeduplicateTargets();
            RefreshTargetList();
            SaveSettings();
            SetStatus("Цель добавлена: " + tab.Title, false);
        }

        private void AddOrUpdateBrowserTarget(CdpTarget tab, double x, double y)
        {
            ClickTarget existing = _targets.Find(delegate(ClickTarget target)
            {
                return target.Config.Browser &&
                       ((!string.IsNullOrEmpty(tab.Id) && target.Config.Id == tab.Id) ||
                        target.Config.WsUrl == tab.WsUrl);
            });

            if (existing != null)
            {
                existing.Config.X = x;
                existing.Config.Y = y;
                existing.Config.Id = tab.Id;
                existing.Config.Title = tab.Title;
                existing.Config.Url = tab.Url;
            }
            else
            {
                _targets.Add(new ClickTarget
                {
                    Config = new ClickTargetSettings
                    {
                        Browser = true,
                        Id = tab.Id,
                        Title = tab.Title,
                        Url = tab.Url,
                        WsUrl = tab.WsUrl,
                        X = x,
                        Y = y
                    }
                });
            }
        }

        private async void OnNewTabClick(object sender, EventArgs e)
        {
            string url = GetPageUrlForNewTab();
            if (string.IsNullOrEmpty(url))
            {
                SetStatus("Сначала откройте нужный сайт в браузере", true);
                return;
            }

            _btnNewTab.Enabled = false;
            try
            {
                double x = (double)_numX.Value;
                double y = (double)_numY.Value;

                var selected = _cmbTabs.SelectedItem as CdpTarget;
                if (selected != null && IsWebUrl(selected.Url) &&
                    !HasBrowserTargetForUrl(selected.Url))
                {
                    AddOrUpdateBrowserTarget(selected, x, y);
                }

                CdpTarget created = await CdpConnection.CreateTabAsync(BrowserLauncher.DebugPort, url);
                string title = string.IsNullOrEmpty(created.Title) ? "Новая вкладка" : created.Title;
                var config = new ClickTargetSettings
                {
                    Browser = true,
                    Id = created.Id,
                    Title = title,
                    Url = created.Url,
                    WsUrl = created.WsUrl,
                    X = x,
                    Y = y
                };
                _targets.Add(new ClickTarget { Config = config });
                DeduplicateTargets();
                RefreshTargetList();
                await RefreshTargetsAsync(false);
                SelectComboTarget(created.WsUrl);
                RefreshTargetList();
                SaveSettings();
                SetStatus("Открыта вторая вкладка: " + title, false);
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось открыть вторую вкладку: " + ex.Message, true);
            }
            finally
            {
                _btnNewTab.Enabled = true;
            }
        }

        private string GetPageUrlForNewTab()
        {
            var selected = _cmbTabs.SelectedItem as CdpTarget;
            if (selected != null && IsWebUrl(selected.Url))
            {
                return selected.Url;
            }

            foreach (ClickTarget target in _targets)
            {
                if (target.Config.Browser && IsWebUrl(target.Config.Url))
                {
                    return target.Config.Url;
                }
            }

            return null;
        }

        private static bool IsWebUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("file://", StringComparison.OrdinalIgnoreCase));
        }

        private void SelectComboTarget(string wsUrl)
        {
            for (int i = 0; i < _cmbTabs.Items.Count; i++)
            {
                var item = _cmbTabs.Items[i] as CdpTarget;
                if (item != null && item.WsUrl == wsUrl)
                {
                    _cmbTabs.SelectedIndex = i;
                    return;
                }
            }
        }

        private async void OnPickTargetClick(object sender, EventArgs e)
        {
            var target = _lstTargets.SelectedItem as ClickTarget;
            if (target == null)
            {
                SetStatus("Выберите цель в списке", true);
                return;
            }

            if (!target.Config.Browser)
            {
                SetStatus("Для окна точка задаётся при добавлении цели", true);
                return;
            }

            _btnTargetPick.Enabled = false;
            Hide();
            try
            {
                if (!await EnsureTargetConnectedAsync(target))
                {
                    SetStatus("Вкладка «" + target + "» не найдена. Обновите список вкладок.", true);
                    return;
                }

                await BringPageToFrontAsync(target.Connection);
                object value = await target.Connection.EvaluateAsync(CdpScripts.PickPoint, true, 180000);
                var point = value as Dictionary<string, object>;
                if (point == null || !point.ContainsKey("x") || !point.ContainsKey("y"))
                {
                    SetStatus("Точка не выбрана", true);
                    return;
                }

                target.Config.X = Convert.ToDouble(point["x"]);
                target.Config.Y = Convert.ToDouble(point["y"]);
                _numX.Value = Clamp((int)target.Config.X, 0, 20000);
                _numY.Value = Clamp((int)target.Config.Y, 0, 20000);
                RefreshTargetList();
                SaveSettings();
                SetStatus("Точка цели: " + (int)target.Config.X + ", " + (int)target.Config.Y, false);
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось указать точку: " + ex.Message, true);
            }
            finally
            {
                _btnTargetPick.Enabled = true;
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private async Task<bool> EnsureTargetConnectedAsync(ClickTarget target, HashSet<string> used = null)
        {
            if (target.Connection != null && target.Connection.IsConnected &&
                (used == null || !used.Contains(target.Config.WsUrl)))
            {
                if (used != null)
                {
                    used.Add(target.Config.WsUrl);
                }

                return true;
            }

            List<CdpTarget> targets;
            try
            {
                targets = await CdpConnection.ListTargetsAsync(BrowserLauncher.DebugPort);
            }
            catch
            {
                return false;
            }

            CdpTarget match = FindTarget(targets, target.Config, used);
            if (match == null)
            {
                return false;
            }

            target.Config.Id = match.Id;
            target.Config.WsUrl = match.WsUrl;
            target.Config.Url = match.Url;
            if (!string.IsNullOrEmpty(match.Title))
            {
                target.Config.Title = match.Title;
            }

            if (target.Connection != null)
            {
                target.Connection.Dispose();
            }

            try
            {
                target.Connection = await GetConnectionAsync(match);
                if (used != null)
                {
                    used.Add(match.WsUrl);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static CdpTarget FindTarget(
            List<CdpTarget> targets,
            ClickTargetSettings config,
            HashSet<string> used = null)
        {
            if (!string.IsNullOrEmpty(config.Id))
            {
                CdpTarget byId = targets.Find(delegate(CdpTarget item)
                {
                    return item.Id == config.Id &&
                           (used == null || !used.Contains(item.WsUrl));
                });
                if (byId != null)
                {
                    return byId;
                }
            }

            CdpTarget exact = targets.Find(delegate(CdpTarget item)
            {
                return item.WsUrl == config.WsUrl &&
                       (used == null || !used.Contains(item.WsUrl));
            });
            if (exact != null)
            {
                return exact;
            }

            string url = NormalizeUrl(config.Url);
            List<CdpTarget> matches = targets.FindAll(delegate(CdpTarget item)
            {
                return NormalizeUrl(item.Url) == url &&
                       (used == null || !used.Contains(item.WsUrl));
            });

            if (used != null)
            {
                return matches.Count > 0 ? matches[0] : null;
            }

            return matches.Count == 1 ? matches[0] : null;
        }

        private static string NormalizeUrl(string url)
        {
            string value = url ?? "";
            int cut = value.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
            {
                value = value.Substring(0, cut);
            }

            return value.TrimEnd('/');
        }

        private void OnAddWindowTargetClick(object sender, EventArgs e)
        {
            if (!Native.IsWindow(_hwndRoot))
            {
                SetStatus("Сначала выберите окно и точку", true);
                return;
            }

            IntPtr deep = Native.IsWindow(_hwndDeep) ? _hwndDeep : _hwndRoot;
            Point deepPoint = deep == _hwndDeep ? _ptDeep : _ptRoot;
            long rootHandle = _hwndRoot.ToInt64();
            ClickTarget existing = _targets.Find(delegate(ClickTarget target)
            {
                return !target.Config.Browser && target.Config.Root == rootHandle;
            });

            var config = new ClickTargetSettings
            {
                Browser = false,
                Title = _winTitle,
                Deep = deep.ToInt64(),
                Root = rootHandle,
                DeepX = deepPoint.X,
                DeepY = deepPoint.Y,
                RootX = _ptRoot.X,
                RootY = _ptRoot.Y,
                UseRoot = _cmbPart.SelectedIndex == 1
            };

            if (existing != null)
            {
                existing.Config = config;
            }
            else
            {
                _targets.Add(new ClickTarget { Config = config });
            }

            DeduplicateTargets();
            RefreshTargetList();
            SaveSettings();
            SetStatus("Цель добавлена: " + (_winTitle.Length > 0 ? _winTitle : "(без названия)"), false);
        }

        private void OnRemoveTargetClick(object sender, EventArgs e)
        {
            int index = _lstTargets.SelectedIndex;
            if (index < 0 || index >= _targets.Count)
            {
                return;
            }

            _targets.RemoveAt(index);
            RefreshTargetList();
            SaveSettings();
        }

        private void OnClearTargetsClick(object sender, EventArgs e)
        {
            _targets.Clear();
            RefreshTargetList();
            SaveSettings();
        }

        private void OnOpenLogsClick(object sender, EventArgs e)
        {
            string dir = GetLogDirectory();
            if (string.IsNullOrEmpty(dir))
            {
                SetStatus("Папка логов недоступна", true);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + dir + "\"",
                    UseShellExecute = true
                });
                SetStatus("Логи: " + Path.Combine(dir, "launch.log"), false);
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось открыть папку логов: " + ex.Message, true);
            }
        }

        private async Task StartBrowserAsync()
        {
            _skippedTargets = 0;
            ResetRate();
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
            int reloadSeconds = _chkAutoReload.Checked ? (int)_numReload.Value : 0;
            Action reload = reloadSeconds > 0
                ? new Action(delegate { connection.ReloadSync(); })
                : null;

            string under = await DescribePointAsync(connection, x, y);
            Log("start browser x=" + x + " y=" + y + " reliable=" + _reliableClicks + " under=" + under);

            bool started = _engine.Start(
                delegate
                {
                    connection.ClickSync(x, y, _reliableClicks);
                    return true;
                },
                cps,
                jitter,
                reload,
                reloadSeconds);

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
            _skippedTargets = 0;
            ResetRate();
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
            Log("start window cps=" + cps + " reliable=" + _reliableClicks);
            SetStatus("Работает · 0 кликов", false);
            SaveSettings();
        }

        private void StopClicking()
        {
            _engine.Stop();
            _wasRunning = false;
            _rateValue = 0;
            Log("stop count=" + _engine.Count);
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

            try
            {
                _cdp = await GetConnectionAsync(target);
                _cdpUrl = target.WsUrl;
                _lblCdp.Text = "Подключено: " + (target.Title ?? target.Url);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось подключиться: " + ex.Message, true);
                return false;
            }
        }

        private async Task<CdpConnection> GetConnectionAsync(CdpTarget target)
        {
            CdpConnection connection;
            if (_connections.TryGetValue(target.WsUrl, out connection) && connection.IsConnected)
            {
                return connection;
            }

            if (connection != null)
            {
                connection.Dispose();
                _connections.Remove(target.WsUrl);
            }

            connection = new CdpConnection();
            await connection.ConnectAsync(target);
            _connections[target.WsUrl] = connection;
            return connection;
        }

        private void CloseCdp()
        {
            foreach (CdpConnection connection in _connections.Values)
            {
                connection.Dispose();
            }

            _connections.Clear();
            _cdp = null;
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

                foreach (ClickTarget clickTarget in _targets)
                {
                    if (!clickTarget.Config.Browser)
                    {
                        continue;
                    }

                    CdpTarget match = FindTarget(targets, clickTarget.Config);
                    if (match != null)
                    {
                        clickTarget.Config.Id = match.Id;
                        clickTarget.Config.WsUrl = match.WsUrl;
                        clickTarget.Config.Url = match.Url;
                        if (!string.IsNullOrEmpty(match.Title))
                        {
                            clickTarget.Config.Title = match.Title;
                        }
                    }
                }

                DeduplicateTargets();
                RefreshTargetList();
                if (!_engine.IsRunning)
                {
                    SetStatus("Список обновлён · целей: " + _targets.Count, false);
                }
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

                    var selected = _cmbTabs.SelectedItem as CdpTarget;
                    ClickTarget target = FindTargetForTab(selected);
                    if (target != null)
                    {
                        target.Config.X = x;
                        target.Config.Y = y;
                        RefreshTargetList();
                    }

                    SetStatus(
                        "Точка сохранена: " + x + ", " + y +
                        (target != null ? " (для выбранной цели)" : ""),
                        false);
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

        private Task BringPageToFrontAsync()
        {
            return BringPageToFrontAsync(_cdp);
        }

        private async Task BringPageToFrontAsync(CdpConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            try
            {
                Dictionary<string, object> info = await connection.SendAsync(
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
                        await connection.SendAsync("Browser.setWindowBounds", parameters, 3000);
                    }
                }
            }
            catch
            {
            }

            try
            {
                await connection.SendAsync("Page.bringToFront", new Dictionary<string, object>(), 3000);
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
                double x = (double)_numX.Value;
                double y = (double)_numY.Value;
                await _cdp.ClickAsync(x, y, _reliableClicks);
                string under = await DescribePointAsync(_cdp, x, y);
                SetStatus("Клик отправлен · под точкой: " + under, false);
                Log("test click x=" + x + " y=" + y + " reliable=" + _reliableClicks + " under=" + under);
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка клика: " + ex.Message, true);
            }
        }

        private static async Task<string> DescribePointAsync(CdpConnection connection, double x, double y)
        {
            try
            {
                string expression =
                    "(function(){var e=document.elementFromPoint(" +
                    x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ");" +
                    "if(!e)return 'none';var t=(e.innerText||e.value||'').replace(/\\s+/g,' ').trim().slice(0,70);" +
                    "return e.tagName+'#'+e.id+' class='+(e.className||'')+' text='+t;})()";
                object value = await connection.EvaluateAsync(expression, false, 3000);
                return Convert.ToString(value);
            }
            catch (Exception ex)
            {
                return "? " + ex.Message;
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

        private void ResetRate()
        {
            _rateValue = 0;
            _rateLastCount = 0;
            _rateLastTime = DateTime.UtcNow;
            _lastDiagLog = DateTime.UtcNow;
        }

        private void OnUiTick(object sender, EventArgs e)
        {
            if (_engine.IsRunning)
            {
                _wasRunning = true;
                long count = _engine.Count;
                DateTime now = DateTime.UtcNow;
                double seconds = (now - _rateLastTime).TotalSeconds;
                if (seconds >= 0.5)
                {
                    double current = (count - _rateLastCount) / seconds;
                    _rateValue = _rateValue <= 0 ? current : (_rateValue * 0.65) + (current * 0.35);
                    _rateLastCount = count;
                    _rateLastTime = now;
                }

                if ((now - _lastDiagLog).TotalSeconds >= 10)
                {
                    _lastDiagLog = now;
                    Log(string.Format(
                        "run count={0} rate={1:F0}/s skipped={2}",
                        count,
                        _rateValue,
                        _skippedTargets));
                }

                SetStatus(
                    string.Format(
                        "Работает · {0:N0} кликов · ~{1:N0}/с{2}",
                        count,
                        _rateValue,
                        _skippedTargets > 0 ? " · пропущено целей: " + _skippedTargets : ""),
                    false);
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

        private sealed class ClickTarget
        {
            public ClickTargetSettings Config;
            public CdpConnection Connection;

            public override string ToString()
            {
                string title = string.IsNullOrEmpty(Config.Title) ? (Config.Url ?? "") : Config.Title;
                if (title.Length > 60)
                {
                    title = title.Substring(0, 57) + "...";
                }

                double x = Config.Browser ? Config.X : Config.DeepX;
                double y = Config.Browser ? Config.Y : Config.DeepY;
                return string.Format(
                    "[{0}] {1}  ({2:0}, {3:0})",
                    Config.Browser ? "Браузер" : "Окно",
                    title,
                    x,
                    y);
            }
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

        private static string GetLogDirectory()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BgClicker",
                    "logs");
                if (IsWritableDirectory(dir))
                {
                    return dir;
                }
            }
            catch
            {
            }

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BgClicker",
                    "logs");
                if (IsWritableDirectory(dir))
                {
                    return dir;
                }
            }
            catch
            {
            }

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BgClicker", "logs");
                if (IsWritableDirectory(dir))
                {
                    return dir;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsWritableDirectory(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test");
                using (File.Create(probe))
                {
                }

                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetLogPath()
        {
            string dir = GetLogDirectory();
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "launch.log");
        }

        private static void RemoveLegacyLog()
        {
            try
            {
                string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launch.log");
                if (File.Exists(legacy))
                {
                    File.Delete(legacy);
                }
            }
            catch
            {
            }
        }

        private static void Log(string message)
        {
            string path = GetLogPath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
