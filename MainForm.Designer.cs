using MaterialSkin;
using MaterialSkin.Controls;

namespace DownloadManager 
{
    public partial class MainForm : MaterialForm
    {

        private MaterialSwitch themeSwitch;
        private Panel dropdownMenu;  
        private MaterialTextBox2 urlTextBox;
        private MaterialButton downloadButton;
        private MaterialButton pauseButton;
        private MaterialButton resumeButton;
        private MaterialButton cancelButton;
        private ProgressBar progressBar;
        private MaterialLabel statusLabel;
        private Label historyButton;
        private MaterialButton chooseFolderButton;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool allowExit = false;
        
        public MainForm()
        {
            this.Icon = new Icon(typeof(MainForm).Assembly.GetManifestResourceStream("DownloadManager.Resources.iconArrow.ico"));
            InitializeTrayIcon();
            this.Resize += MainForm_Resize;
            this.FormClosing += MainForm_FormClosing;
            InitializeComponent();
            LoadSettings();
            ThemeManager.Initialize(this);
            themeSwitch.CheckedChanged -= ThemeSwitch_CheckedChanged; 
            themeSwitch.Checked = MaterialSkinManager.Instance.Theme == MaterialSkinManager.Themes.LIGHT;
            themeSwitch.CheckedChanged += ThemeSwitch_CheckedChanged;
            LoadHistory();
            CheckForRecovery();
            UpdateButtonStates();
        }  

        private void InitializeComponent()
        {
            this.Text = "Менеджер загрузок";
            this.Size = new System.Drawing.Size(700, 350);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimumSize = new Size(700, 350); 
            this.MaximumSize = new Size(700, 350);

            urlTextBox = new MaterialTextBox2()
            {
                Location = new System.Drawing.Point(20, 80),
                Size = new System.Drawing.Size(520, 48),
                Hint = "Вставьте ссылку на файл...",
                MaxLength = 500
            };

            themeSwitch = new MaterialSwitch()
            {
                BackColor = Color.Transparent,
                Location = new Point(Width - 90, 130), 
                MouseState = MaterialSkin.MouseState.HOVER,
                Size = new Size(58, 36),
                Ripple = true
            };

           historyButton = new Label()
            {
                Text = "▼",   
                Font = new Font("Segoe UI", 18, FontStyle.Bold), 
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(538, 80),
                Size = new Size(48, 48),
                BackColor = Color.Transparent,
                ForeColor = MaterialSkinManager.Instance.ColorScheme.TextColor,
                Cursor = Cursors.Hand
            };

            historyButton.MouseEnter += (s, e) =>
            {
                historyButton.ForeColor = Color.LightGray;
                historyButton.BackColor = Color.DimGray;
            };

            historyButton.MouseLeave += (s, e) =>
            {
                historyButton.ForeColor = MaterialSkinManager.Instance.ColorScheme.TextColor;
                historyButton.BackColor = Color.Transparent;
            };

            dropdownMenu = new Panel()
            {
                Size = new Size(urlTextBox.Width, 120),  
                Location = new Point(urlTextBox.Left, urlTextBox.Bottom),
                BackColor = Color.DimGray,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                ForeColor = MaterialSkinManager.Instance.ColorScheme.TextColor,
                AutoScroll = true
            };

            historyButton.Click += (s, e) =>
            {
                if (dropdownMenu.Visible)
                    dropdownMenu.Visible = false;
                else
                    ShowDropdownMenu();
            };

            downloadButton = new MaterialButton()
            { 
                Text = "СКАЧАТЬ", 
                Location = new System.Drawing.Point(590, 80), 
                Size = new System.Drawing.Size(80, 36)
            };

            pauseButton = new MaterialButton() 
            { 
                Text = "ПАУЗА", 
                Location = new System.Drawing.Point(20, 140), 
                Size = new System.Drawing.Size(120, 36)
            };

            resumeButton = new MaterialButton() 
            { 
                Text = "ВОЗОБНОВИТЬ", 
                Location = new System.Drawing.Point(100, 140), 
                Size = new System.Drawing.Size(140, 36)
            };

            cancelButton = new MaterialButton() 
            { 
                Text = "ОТМЕНА", 
                Location = new System.Drawing.Point(240, 140), 
                Size = new System.Drawing.Size(120, 36)
            };

            progressBar = new ProgressBar()
            {
                Location = new System.Drawing.Point(20, 200),
                Size = new System.Drawing.Size(650, 20),
                Style = ProgressBarStyle.Continuous
            };

            statusLabel = new MaterialLabel()
            {
                Location = new System.Drawing.Point(20, 230),
                Size = new System.Drawing.Size(650, 40),
                Text = "Готов к загрузке",
                AutoSize = false
            };

            var moonIcon = new PictureBox()
            {
            Size = new Size(20, 20),
            Location = new Point(themeSwitch.Left - 28, themeSwitch.Top + (themeSwitch.Height - 24) / 2),
            Image = LoadEmbeddedImage("DownloadManager.Resources.moon.png"), 
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Transparent
            };

            var sunIcon = new PictureBox()
            {
            Size = new Size(20, 20),
            Location = new Point(themeSwitch.Right + 4, themeSwitch.Top + (themeSwitch.Height - 24) / 2),
            Image = LoadEmbeddedImage("DownloadManager.Resources.sun.png"), 
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Transparent
            };

            chooseFolderButton = new MaterialButton()
            {
            Text = "Выбрать папку",
            Location = new Point(397, 140),
            Size = new Size(140, 36)
            };
            
            this.Controls.AddRange(new Control[]
            {
                urlTextBox,
                downloadButton,
                pauseButton,
                resumeButton,
                cancelButton,
                progressBar,
                statusLabel,
                historyButton,
                dropdownMenu,
                themeSwitch,
                sunIcon,
                moonIcon,
                chooseFolderButton
            });

            downloadButton.Click += DownloadButton_Click;
            pauseButton.Click += PauseButton_Click;
            resumeButton.Click += ResumeButton_Click;
            cancelButton.Click += CancelButton_Click;
            themeSwitch.CheckedChanged += ThemeSwitch_CheckedChanged;
            chooseFolderButton.Click += ChooseFolderButton_Click;
            UpdateButtonStates();

            var controls = new Control[]
            {
                downloadButton, pauseButton, resumeButton, cancelButton,
                chooseFolderButton, themeSwitch, urlTextBox, this
            };

            foreach (var ctrl in controls)
            {
                ctrl.Click += (s, e) =>
                {
                    var clicked = (Control)s;
                    if (clicked == historyButton) return;
                    if (dropdownMenu.Visible && !dropdownMenu.Bounds.Contains(PointToClient(Cursor.Position)))
                    {
                        dropdownMenu.Visible = false;
                    }
                };
            }
        }     

         private void UpdateButtonStates()
        {
            downloadButton.Enabled = !_isDownloading;
            pauseButton.Enabled = _isDownloading && !_isPaused;
            resumeButton.Enabled = _isDownloading && _isPaused;
            cancelButton.Enabled = _isDownloading;
            chooseFolderButton.Enabled = !_isDownloading;
        }

        private void ShowDropdownMenu()
        {
            bool dark = MaterialSkinManager.Instance.Theme == MaterialSkinManager.Themes.DARK;
            dropdownMenu.Controls.Clear();
            dropdownMenu.Visible = true;
            dropdownMenu.BringToFront();

            int itemHeight = 30;
            int y = 0;

            foreach (var link in downloadHistory)
            {
                Label itemLabel = new Label()
                {
                    Text = link,
                    Size = new Size(dropdownMenu.Width - 4, itemHeight),
                    Location = new Point(2, y),
                    BackColor = dark ? Color.DimGray : Color.WhiteSmoke,
                    ForeColor = dark ? MaterialSkinManager.Instance.ColorScheme.TextColor : Color.Black,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(5, 0, 0, 0)
                };

                itemLabel.MouseEnter += (s, e) => itemLabel.BackColor = dark ? Color.Gray : Color.LightGray;
                itemLabel.MouseLeave += (s, e) => itemLabel.BackColor = dark ? Color.DimGray : Color.WhiteSmoke;
                itemLabel.Click += (s, e) =>
                {
                    urlTextBox.Text = link;
                    dropdownMenu.Visible = false;
                };

                dropdownMenu.Controls.Add(itemLabel);
                y += itemHeight;
            }

            dropdownMenu.AutoScroll = true;
        }
        private void ThemeSwitch_CheckedChanged(object sender, EventArgs e)
        {
            ThemeManager.ToggleTheme();
            Invalidate();
            Update();
            ApplyMenuStyling();
        }
        public void ApplyMenuStyling()
        {
            bool dark = MaterialSkinManager.Instance.Theme == MaterialSkinManager.Themes.DARK;

            Color normalBackButton = Color.Transparent;
            Color normalFore = dark ? MaterialSkinManager.Instance.ColorScheme.TextColor : Color.Black;
            Color hoverBack = dark ? Color.FromArgb(60, 60, 60) : Color.LightGray;
            Color hoverFore = dark ? Color.White : Color.Black;

            historyButton.MouseEnter -= HistoryButton_MouseEnter;
            historyButton.MouseLeave -= HistoryButton_MouseLeave;

            historyButton.Tag = new
            {
                NormalBack = normalBackButton,
                HoverBack = hoverBack,
                NormalFore = normalFore,
                HoverFore = hoverFore
            };

            historyButton.BackColor = normalBackButton;
            historyButton.ForeColor = normalFore;

            historyButton.MouseEnter += HistoryButton_MouseEnter;
            historyButton.MouseLeave += HistoryButton_MouseLeave;
        }
        private void HistoryButton_MouseEnter(object sender, EventArgs e)
        {
            dynamic st = historyButton.Tag;
            historyButton.BackColor = st.HoverBack;
            historyButton.ForeColor = st.HoverFore;
        }
        private void HistoryButton_MouseLeave(object sender, EventArgs e)
        {
            dynamic st = historyButton.Tag;
            historyButton.BackColor = st.NormalBack;
            historyButton.ForeColor = st.NormalFore;
        }   
        
        private Image LoadEmbeddedImage(string resourceName)
        {
            var assembly = typeof(MainForm).Assembly;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new Exception("Resource not found: " + resourceName);

                return Image.FromStream(stream);
            }
        }
        private void InitializeTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Открыть", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Выход", null, (s, e) =>
            {
                allowExit = true;
                Application.Exit();
            });

            trayIcon = new NotifyIcon()
            {
                Icon = this.Icon,
                Visible = true,
                Text = "Менеджер загрузок",
                ContextMenuStrip = trayMenu
            };

            trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }
        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!allowExit)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
            }
        }
        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }
    }
}