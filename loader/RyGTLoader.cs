// RyGT loader: sets up Gran Turismo Sport from the player's own dump, firmware modules and fonts,
// then starts it. Built with build.cmd (C# for .NET Framework 4.x, which Windows includes).
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RyGT
{
    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateSymbolicLink(string link, string target, int flags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr window, string appName, string idList);
    }

    /// Dark colors for the loader window.
    static class Dark
    {
        public static readonly Color Back = Color.FromArgb(30, 30, 30);
        public static readonly Color Field = Color.FromArgb(45, 45, 48);
        public static readonly Color Button = Color.FromArgb(58, 58, 61);
        public static readonly Color ButtonHover = Color.FromArgb(75, 75, 80);
        public static readonly Color Accent = Color.FromArgb(178, 24, 36);
        public static readonly Color AccentHover = Color.FromArgb(206, 40, 52);
        public static readonly Color Heading = Color.FromArgb(240, 96, 96);
        public static readonly Color Border = Color.FromArgb(70, 70, 74);
        public static readonly Color Text = Color.FromArgb(230, 230, 230);
        public static readonly Color DimText = Color.FromArgb(150, 150, 150);

        public static void Apply(Control control)
        {
            if (control.BackColor.A == 255) control.BackColor = Back;
            if (control.Tag as string != "heading") control.ForeColor = Text;
            var button = control as Button;
            if (button != null)
            {
                bool accent = button.Tag as string == "accent";
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = accent ? Accent : Button;
                button.FlatAppearance.BorderColor = accent ? Accent : Border;
                button.FlatAppearance.MouseOverBackColor = accent ? AccentHover : ButtonHover;
                button.FlatAppearance.MouseDownBackColor = accent ? Accent : Border;
                button.Padding = new Padding(10, 2, 10, 2);
            }
            var box = control as TextBox;
            if (box != null)
            {
                box.BackColor = Field;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.HandleCreated += delegate { Native.SetWindowTheme(box.Handle, "DarkMode_Explorer", null); };
            }
            var check = control as CheckBox;
            if (check != null && !(check is DarkCheckBox)) check.FlatStyle = FlatStyle.Flat;
            var combo = control as ComboBox;
            if (combo != null) combo.BackColor = Field;
            foreach (Control child in control.Controls) Apply(child);
        }

        /// A dark title bar (Windows 10 2004 and later; ignored before).
        public static void TitleBar(Form form)
        {
            int on = 1;
            if (Native.DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }
    }

    /// A check box drawn in the loader's colors: a dark square, red with a white tick when checked.
    class DarkCheckBox : CheckBox
    {
        public DarkCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(text.Width + 24, Math.Max(text.Height, 16) + 2);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            PaintBehind(this, e.Graphics);
        }

        /// The window's picture under `control`, with the translucent layer of the panel it is on.
        public static void PaintBehind(Control control, Graphics g)
        {
            var form = control.FindForm() as LoaderForm;
            if (form == null || !form.PaintBackdrop(control, g))
            {
                using (var back = new SolidBrush(Dark.Back)) g.FillRectangle(back, control.ClientRectangle);
            }
            for (var parent = control.Parent; parent != null; parent = parent.Parent)
            {
                var glass = parent as GlassPanel;
                if (glass != null)
                {
                    using (var overlay = new SolidBrush(glass.Overlay)) g.FillRectangle(overlay, control.ClientRectangle);
                    break;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            OnPaintBackground(e);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int size = 14;
            var box = new Rectangle(1, (Height - size) / 2, size, size);
            bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
            using (var fill = new SolidBrush(!Enabled ? Color.FromArgb(40, 40, 42) : Checked ? Dark.Accent : Color.FromArgb(32, 32, 34)))
                g.FillRectangle(fill, box);
            using (var border = new Pen(!Enabled ? Color.FromArgb(70, 70, 72) : Checked ? Dark.AccentHover : hover ? Color.FromArgb(150, 150, 155) : Color.FromArgb(105, 105, 110)))
                g.DrawRectangle(border, box);
            if (Checked)
            {
                using (var tick = new Pen(Enabled ? Color.White : Color.FromArgb(150, 150, 150), 2f))
                {
                    g.DrawLines(tick, new[]
                    {
                        new PointF(box.Left + 3f, box.Top + 7.5f),
                        new PointF(box.Left + 6f, box.Top + 10.5f),
                        new PointF(box.Left + 11f, box.Top + 4f)
                    });
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Point(box.Right + 7, (Height - TextRenderer.MeasureText(Text, Font).Height) / 2),
                                  Enabled ? ForeColor : Dark.DimText, TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    }

    /// A settings panel over the window's background picture: it paints the picture under it and
    /// a translucent dark layer itself, so the controls on it (transparent) repaint cleanly. A
    /// translucent BackColor left old text behind when a control on it was redrawn.
    class GlassPanel : TableLayoutPanel
    {
        public Color Overlay = Color.FromArgb(150, 12, 12, 14);

        public GlassPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var form = FindForm() as LoaderForm;
            if (form == null || !form.PaintBackdrop(this, e.Graphics))
            {
                using (var back = new SolidBrush(Dark.Back)) e.Graphics.FillRectangle(back, ClientRectangle);
            }
            using (var overlay = new SolidBrush(Overlay)) e.Graphics.FillRectangle(overlay, ClientRectangle);
        }
    }

    /// Unwraps an unencrypted (fake-signed) PS4 SELF into a plain ELF.
    static class Self
    {
        public static bool IsElf(byte[] d)
        {
            return d.Length >= 4 && d[0] == 0x7f && d[1] == (byte)'E' && d[2] == (byte)'L' && d[3] == (byte)'F';
        }

        public static byte[] ToElf(byte[] d, string name)
        {
            if (IsElf(d)) return d;
            if (d.Length < 32 || d[0] != 0x4f || d[1] != 0x15 || d[2] != 0x3d || d[3] != 0x1d)
                throw new Exception(name + " is neither a SELF nor an ELF file.");
            int count = BitConverter.ToUInt16(d, 24);
            var entries = new Dictionary<long, long[]>();
            for (int i = 0; i < count; i++)
            {
                long props = (long)BitConverter.ToUInt64(d, 32 + i * 32);
                long offset = (long)BitConverter.ToUInt64(d, 40 + i * 32);
                long size = (long)BitConverter.ToUInt64(d, 48 + i * 32);
                if ((props & 0xA) != 0)
                    throw new Exception(name + " has encrypted or compressed segments. The dump must be decrypted.");
                if ((props & 0x800) != 0) entries[(props >> 20) & 0xFFF] = new[] { offset, size };
            }
            int elf = 32 + count * 32;
            long phoff = (long)BitConverter.ToUInt64(d, elf + 0x20);
            int phnum = BitConverter.ToUInt16(d, elf + 0x38);
            if (phoff != 0x40) throw new Exception(name + ": unexpected ELF header layout.");
            int headerLength = 0x40 + phnum * 0x38;
            long outSize = headerLength;
            var headers = new List<long[]>();
            for (int i = 0; i < phnum; i++)
            {
                int b = elf + 0x40 + i * 0x38;
                long type = BitConverter.ToUInt32(d, b);
                long offset = (long)BitConverter.ToUInt64(d, b + 8);
                long fileSize = (long)BitConverter.ToUInt64(d, b + 0x20);
                headers.Add(new[] { type, offset, fileSize });
                outSize = Math.Max(outSize, offset + fileSize);
            }
            var output = new byte[outSize];
            Array.Copy(d, elf, output, 0, headerLength);
            long selfSize = (long)BitConverter.ToUInt64(d, 16);
            for (int i = 0; i < headers.Count; i++)
            {
                long type = headers[i][0], offset = headers[i][1], fileSize = headers[i][2];
                if (fileSize == 0) continue;
                long[] entry;
                if (entries.TryGetValue(i, out entry))
                    Array.Copy(d, entry[0], output, offset, Math.Min(entry[1], fileSize));
                else if (type == 0x6fffff01 && selfSize + fileSize <= d.Length)
                    Array.Copy(d, selfSize, output, offset, fileSize); // PT_SCE_VERSION follows the body
            }
            return output;
        }

        /// Rewrites PT_SCE_RELRO (0x61000010) as a read-write PT_LOAD so the converter maps it and
        /// allows relocations into it.
        public static byte[] RelroToLoad(byte[] elf)
        {
            long phoff = (long)BitConverter.ToUInt64(elf, 0x20);
            int phnum = BitConverter.ToUInt16(elf, 0x38);
            for (int i = 0; i < phnum; i++)
            {
                int header = (int)phoff + i * 0x38;
                if (BitConverter.ToUInt32(elf, header) != 0x61000010) continue;
                BitConverter.GetBytes(1u).CopyTo(elf, header);     // PT_LOAD
                BitConverter.GetBytes(6u).CopyTo(elf, header + 4); // read, write
            }
            return elf;
        }
    }

    /// Finishes the converted GTSport.exe: the launcher's icon (RyGT.exe's embedded copy of ryGT.ico),
    /// so Explorer and shortcuts show the game's art instead of a blank program icon, and the Windows
    /// (GUI) subsystem, so a double-click opens only the game window, without a console.
    static class GameExe
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr BeginUpdateResource(string file, bool deleteExisting);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool EndUpdateResource(IntPtr update, bool discard);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

        [DllImport("kernel32.dll")]
        static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr module);

        const int RtIcon = 3, RtGroupIcon = 14;
        const ushort GuiSubsystem = 2;

        public static bool IsPrepared(string exe)
        {
            return HasIcon(exe) && ReadSubsystem(exe) == GuiSubsystem;
        }

        public static void Prepare(string exe)
        {
            if (!HasIcon(exe)) ApplyIcon(exe);
            if (ReadSubsystem(exe) != GuiSubsystem) WriteSubsystem(exe, GuiSubsystem);
        }

        // The subsystem field of the PE optional header: PE header offset at 0x3c, then the "PE\0\0"
        // signature (4 bytes), the file header (20 bytes) and 68 bytes into the optional header.
        static long SubsystemOffset(BinaryReader reader)
        {
            reader.BaseStream.Position = 0x3c;
            return reader.ReadInt32() + 4 + 20 + 68;
        }

        static ushort ReadSubsystem(string exe)
        {
            using (var reader = new BinaryReader(File.OpenRead(exe)))
            {
                reader.BaseStream.Position = SubsystemOffset(reader);
                return reader.ReadUInt16();
            }
        }

        static void WriteSubsystem(string exe, ushort subsystem)
        {
            using (var stream = new FileStream(exe, FileMode.Open, FileAccess.ReadWrite))
            {
                long offset = SubsystemOffset(new BinaryReader(stream));
                stream.Position = offset;
                stream.Write(BitConverter.GetBytes(subsystem), 0, 2);
            }
        }

        static bool HasIcon(string exe)
        {
            IntPtr module = LoadLibraryEx(exe, IntPtr.Zero, 0x2 | 0x20); // as a data file and image resource
            if (module == IntPtr.Zero) return false;
            try { return FindResource(module, (IntPtr)1, (IntPtr)RtGroupIcon) != IntPtr.Zero; }
            finally { FreeLibrary(module); }
        }

        /// Adds every image of the .ico as RT_ICON 1..n and the icon group as RT_GROUP_ICON 1.
        static void ApplyIcon(string exe)
        {
            byte[] ico;
            using (var stream = typeof(GameExe).Assembly.GetManifestResourceStream("ryGT.ico"))
            {
                if (stream == null) throw new Exception("RyGT.exe has no embedded icon.");
                ico = new byte[stream.Length];
                stream.Read(ico, 0, ico.Length);
            }
            int count = BitConverter.ToUInt16(ico, 4);
            IntPtr update = BeginUpdateResource(exe, false);
            if (update == IntPtr.Zero) throw new Exception("Could not open " + exe + " (error " + Marshal.GetLastWin32Error() + ").");
            bool ok = false;
            try
            {
                var group = new MemoryStream();
                var writer = new BinaryWriter(group);
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)count);
                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + 16 * i;
                    int size = BitConverter.ToInt32(ico, entry + 8), offset = BitConverter.ToInt32(ico, entry + 12);
                    var image = new byte[size];
                    Array.Copy(ico, offset, image, 0, size);
                    if (!UpdateResource(update, (IntPtr)RtIcon, (IntPtr)(i + 1), 0, image, (uint)size))
                        throw new Exception("Could not add the icon (error " + Marshal.GetLastWin32Error() + ").");
                    writer.Write(ico, entry, 12);   // width, height, colors, reserved, planes, bit count, size
                    writer.Write((ushort)(i + 1)); // the RT_ICON id instead of the file offset
                }
                var data = group.ToArray();
                if (!UpdateResource(update, (IntPtr)RtGroupIcon, (IntPtr)1, 0, data, (uint)data.Length))
                    throw new Exception("Could not add the icon group (error " + Marshal.GetLastWin32Error() + ").");
                ok = true;
            }
            finally
            {
                if (!EndUpdateResource(update, !ok) && ok)
                    throw new Exception("Could not write the icon into " + exe + " (error " + Marshal.GetLastWin32Error() + ").");
            }
        }
    }

    class Settings
    {
        public string Game = "", Update = "", Modules = "", Fonts = "";
        readonly string path;

        public Settings(string path)
        {
            this.path = path;
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "game") Game = value;
                else if (key == "update") Update = value;
                else if (key == "modules") Modules = value;
                else if (key == "fonts") Fonts = value;
            }
        }

        public void Save()
        {
            File.WriteAllLines(path, new[] { "game=" + Game, "update=" + Update, "modules=" + Modules, "fonts=" + Fonts });
        }
    }

    class LoaderForm : Form
    {
        static readonly string[] FirmwareModules = { "libSceFont", "libSceFontFt", "libSceFreeTypeOt", "libSceJson2", "libSceNgs2" };
        // Used when present: the JPEG decoder (Scapes thumbnails and backgrounds) and PNG encoder (photos).
        // Both are plain software modules needing only libkernel and libSceLibcInternal.
        static readonly string[] OptionalFirmwareModules = { "libSceJpegDec", "libScePngEnc" };

        readonly string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        readonly Settings settings;
        readonly TextBox game, update, modules, fonts, log;
        readonly Button setup, play;
        readonly Label status;
        volatile bool busy;

        public LoaderForm()
        {
            settings = new Settings(Path.Combine(root, "RyGT.ini"));
            Text = "ryGT - Gran Turismo Sport";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(1280, 780);
            MinimumSize = new Size(1060, 700);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            // The background picture (launcher\background.png next to RyGT.exe) fills the window;
            // the settings sit on translucent panels over its dark left side.
            background = LoadBackground();
            if (background != null) ResizeRedraw = true;

            var column = new TableLayoutPanel
            {
                Dock = DockStyle.Left, Width = 800, ColumnCount = 1, Padding = new Padding(24, 18, 10, 14),
                BackColor = Color.Transparent, AutoScroll = true
            };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(column);

            var title = new Label { Text = "ryGT", AutoSize = true, BackColor = Color.Transparent, Font = new Font("Segoe UI Semibold", 22f), Margin = new Padding(0, 0, 0, 0) };
            var subtitle = new Label
            {
                Text = "Gran Turismo Sport on Windows. Pick your own decrypted dump (CUSA03220), its 1.69 update, " +
                       "your PS4 firmware's system modules and fonts; Set up converts the game, Play starts it.",
                AutoSize = true, MaximumSize = new Size(760, 0), BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, 12)
            };
            column.Controls.Add(title);
            column.Controls.Add(subtitle);

            // Game files.
            var files = Section(column, "Game files");
            var fileTable = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent };
            fileTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fileTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            files.Controls.Add(fileTable);
            game = AddRow(fileTable, 0, "Game (CUSA03220)", settings.Game);
            update = AddRow(fileTable, 1, "Update 1.69 (if separate)", settings.Update);
            modules = AddRow(fileTable, 2, "System modules", settings.Modules);
            fonts = AddRow(fileTable, 3, "Fonts", settings.Fonts);

            // Display and shaders on the left, graphics on the right.
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var left = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 0, 5, 0) };
            var right = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(5, 0, 0, 0) };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.Controls.Add(left, 0, 0);
            grid.Controls.Add(right, 1, 0);

            var display = Section(left, "Display");
            // Borderless fullscreen on the window's display (shadGT's full_screen; "Fullscreen" as
            // full_screen_mode would switch the display mode instead).
            AddOption(display, "Start in fullscreen", "GPU", "full_screen", false);
            AddOption(display, "Show an FPS counter", "General", "show_fps_counter", false);
            AddOption(display, "Show the splash screen while the game starts", "General", "show_splash", false);
            AddOption(display, "Show the game on Discord (Rich Presence)", "General", "discord_rpc_enabled", true);

            // Graphics.
            var graphics = Section(right, "Graphics");
            // Sparks, tire smoke and other effects are always on; an older launcher could have
            // turned them off in the config.
            if (!ReadSetting("GPU", "particle_effects", true))
            {
                try { WriteSetting("GPU", "particle_effects", true); } catch { }
            }
            // AMD FSR scales the game's 1920x1080 picture to the window or screen size: FSR 3.1 through
            // amd_fidelityfx_vk.dll (next to GTSport.exe, part of the release), or shadGT's FSR 1 pass.
            var fsr = AddOption(graphics, "Upscale with AMD FSR (above 1080p)", "GPU", "fsr_enabled", false);
            bool fsr3Installed = File.Exists(Path.Combine(root, "amd_fidelityfx_vk.dll"));
            var fsr3 = AddOption(graphics, fsr3Installed
                ? "Use FSR 3.1.4 (off: FSR 1)"
                : "Use FSR 3.1.4 (needs amd_fidelityfx_vk.dll)", "GPU", "fsr3_enabled", true);
            var rcas = AddOption(graphics, "FSR sharpening (RCAS)", "GPU", "rcas_enabled", true);
            foreach (var sub in new[] { fsr3, rcas }) sub.Margin = new Padding(22, 2, 0, 2);
            fsr3.Enabled = fsr.Checked && fsr3Installed;
            rcas.Enabled = fsr.Checked;
            fsr.CheckedChanged += delegate { fsr3.Enabled = fsr.Checked && fsr3Installed; rcas.Enabled = fsr.Checked; };
            // NVIDIA DLSS runs through the optional plugin (plugins/dlss): dlss\ryGT_dlss.dll and NVIDIA's
            // nvngx_dlss.dll next to GTSport.exe. It is not part of the release, so the box stays grey without it.
            bool dlssInstalled = File.Exists(Path.Combine(root, "dlss", "ryGT_dlss.dll")) &&
                                 File.Exists(Path.Combine(root, "dlss", "nvngx_dlss.dll"));
            var dlss = AddOption(graphics, dlssInstalled
                ? "Upscale with NVIDIA DLSS (RTX cards; instead of FSR)"
                : "Upscale with NVIDIA DLSS (needs the DLSS plugin)", "GPU", "dlss_enabled", false);
            dlss.Enabled = dlssInstalled;
            // AMD FSR 3.1 frame generation (amd_fidelityfx_vk.dll) shows a generated frame between
            // each pair of game frames. Works on any GPU; independent of the upscaling choice.
            var framegen = AddOption(graphics, fsr3Installed
                ? "Frame generation (AMD FSR 3.1): about 2x the frames"
                : "Frame generation (needs amd_fidelityfx_vk.dll)", "GPU", "framegen_enabled", false);
            framegen.Enabled = fsr3Installed;
            // GTS-007: in the rain the game renders the scene a second time for the wet track's
            // reflection (about a quarter of a rainy frame's draws). Off: about +30% fps in rain.
            var wet = AddOption(graphics, "Wet-road reflections in the rain (off: much faster in rain)", "GPU",
                                "wet_reflections", true);
            // GTS-008: the rear-view mirror is redrawn every second frame (its picture is kept).
            var mirror = AddOption(graphics, "Rear-view mirror at half rate (a little faster)", "GPU",
                                   "mirror_half_rate", false);
            // D1-D7: shaders read game memory in place instead of copying it each frame
            // (Tokyo rain, 3 races each: 42.7 fps vs 32.1). Its shaders are stored separately,
            // so the first races after turning it on compile shaders.
            AddOption(graphics, "New renderer (much faster; experimental)",
                      "GPU", "direct_renderer", false);
            AddQualityPreset(graphics, fsr, framegen, wet, mirror);

            // Shaders: the stored ones are built while the splash screen shows; a new one pauses
            // the game until it is compiled (instead of a stutter and missing objects).
            var shaders = Section(left, "Shaders");
            AddOption(shaders, "Precompile shaders on the splash screen", "GPU", "precompile_shaders", true);
            // What the game does while a new shader compiles during play (GPU.compile_mode).
            var modeRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 2), WrapContents = false };
            var modeLabel = new Label { Text = "When new shaders compile", AutoSize = false, Width = 196, Height = 20, BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) };
            var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, FlatStyle = FlatStyle.Flat };
            mode.Items.AddRange(new object[] { "Pause the game", "Slow motion", "Keep going (objects pop in)" });
            mode.SelectedIndex = Math.Max(0, Math.Min(2, ReadIntSetting("GPU", "compile_mode", 1)));
            modeRow.Controls.Add(modeLabel);
            modeRow.Controls.Add(mode);
            shaders.Controls.Add(modeRow);
            var batchRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 2), WrapContents = false };
            var batchLabel = new Label { Text = "Compile batching (when pausing)", AutoSize = false, Width = 196, Height = 20, BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) };
            // How long one pause may go on compiling the shaders of what comes next: longer pauses,
            // fewer of them (GPU.compile_batch_ms).
            var batch = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, FlatStyle = FlatStyle.Flat };
            var batchValues = new[] { 0, 100, 300, 1000 };
            batch.Items.AddRange(new object[] { "Off (only what is needed)", "Short (up to 0.1 s)", "Medium (up to 0.3 s)", "Long (up to 1 s)" });
            int current = ReadIntSetting("GPU", "compile_batch_ms", 0);
            int index = Array.IndexOf(batchValues, current);
            batch.SelectedIndex = index >= 0 ? index : 0;
            batch.SelectedIndexChanged += delegate
            {
                try { WriteSetting("GPU", "compile_batch_ms", batchValues[batch.SelectedIndex]); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            batchRow.Controls.Add(batchLabel);
            batchRow.Controls.Add(batch);
            shaders.Controls.Add(batchRow);
            batch.Enabled = mode.SelectedIndex == 0;
            mode.SelectedIndexChanged += delegate
            {
                batch.Enabled = mode.SelectedIndex == 0;
                try { WriteSetting("GPU", "compile_mode", mode.SelectedIndex); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };

            column.Controls.Add(grid);

            // Actions and progress.
            var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 10, 0, 4), WrapContents = false };
            setup = new Button { Text = "Set up", AutoSize = true };
            play = new Button { Text = "Play", AutoSize = true, Tag = "accent", Font = new Font("Segoe UI Semibold", 10f) };
            var open = new Button { Text = "Open folder", AutoSize = true };
            setup.Click += delegate { StartSetup(); };
            play.Click += delegate { Play(); };
            open.Click += delegate { Process.Start("explorer.exe", "\"" + root + "\""); };
            buttons.Controls.AddRange(new Control[] { play, setup, open });
            column.Controls.Add(buttons);

            status = new Label { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(2, 2, 0, 6) };
            column.Controls.Add(status);

            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 110, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f) };
            column.Controls.Add(log);
            foreach (Control control in column.Controls)
            {
                if (control != log) column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Dark.Apply(this);
            subtitle.ForeColor = Dark.DimText;
            title.ForeColor = Color.White;
            HandleCreated += delegate { Dark.TitleBar(this); };
            Shown += delegate { ActiveControl = play.Enabled ? (Control)play : setup; };
            RefreshState();
        }

        readonly Image background;

        Image LoadBackground()
        {
            foreach (var path in new[] { Path.Combine(root, "launcher", "background.png"), Path.Combine(root, "launcher", "background.jpg") })
            {
                try { if (File.Exists(path)) return Image.FromStream(new MemoryStream(File.ReadAllBytes(path))); }
                catch { }
            }
            return null;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!PaintBackdrop(this, e.Graphics)) base.OnPaintBackground(e);
        }

        Bitmap backdrop;

        /// The background picture as it lies under `control` (scaled to cover the window, anchored
        /// right: the picture's subject is on its right). False without a picture.
        public bool PaintBackdrop(Control control, Graphics g)
        {
            if (background == null) return false;
            var client = ClientRectangle;
            if (client.Width <= 0 || client.Height <= 0) return false;
            if (backdrop == null || backdrop.Size != client.Size)
            {
                // Scaled once per window size; panels copy their part of it.
                if (backdrop != null) backdrop.Dispose();
                backdrop = new Bitmap(client.Width, client.Height);
                using (var bg = Graphics.FromImage(backdrop))
                {
                    float scale = Math.Max(client.Width / (float)background.Width, client.Height / (float)background.Height);
                    int width = (int)Math.Ceiling(background.Width * scale), height = (int)Math.Ceiling(background.Height * scale);
                    bg.Clear(Dark.Back);
                    bg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    bg.DrawImage(background, client.Right - width, (client.Height - height) / 2, width, height);
                }
            }
            var offset = control == this ? Point.Empty : PointToClient(control.PointToScreen(Point.Empty));
            g.DrawImage(backdrop, new Rectangle(-offset.X, -offset.Y, backdrop.Width, backdrop.Height));
            return true;
        }

        /// A titled group on a translucent dark panel.
        TableLayoutPanel Section(TableLayoutPanel parent, string heading)
        {
            var panel = new GlassPanel
            {
                ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 10), Margin = new Padding(0, 0, 0, 10)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.Controls.Add(new Label
            {
                Text = heading.ToUpperInvariant(), AutoSize = true, BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 8.5f), ForeColor = Dark.Heading, Margin = new Padding(0, 0, 0, 4), Tag = "heading"
            });
            parent.Controls.Add(panel);
            return panel;
        }

        TextBox AddRow(TableLayoutPanel table, int row, string label, string value)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, BackColor = Color.Transparent, Margin = new Padding(0, 6, 10, 0) }, 0, row);
            var box = new TextBox { Text = value, Dock = DockStyle.Fill };
            table.Controls.Add(box, 1, row);
            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog { Description = label, SelectedPath = box.Text })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
                }
            };
            table.Controls.Add(browse, 2, row);
            return box;
        }

        string Exe { get { return Path.Combine(root, "GTSport.exe"); } }

        string ConfigPath { get { return Path.Combine(root, "user", "custom_configs", "CUSA03220.json"); } }

        /// A shadGT setting in a section of the GT Sport config (General: show_splash,
        /// show_fps_counter, discord_rpc_enabled; GPU: full_screen, particle_effects, fsr_enabled, fsr3_enabled, rcas_enabled, dlss_enabled,
        /// framegen_enabled, precompile_shaders, compile_freeze).
        bool ReadSetting(string sectionName, string key, bool fallback)
        {
            try
            {
                var config = LoadConfig();
                object found, value;
                var section = config.TryGetValue(sectionName, out found) ? found as Dictionary<string, object> : null;
                if (section != null && section.TryGetValue(key, out value) && value is bool) return (bool)value;
            }
            catch { }
            return fallback;
        }

        int ReadIntSetting(string sectionName, string key, int fallback)
        {
            try
            {
                var config = LoadConfig();
                object found, value;
                var section = config.TryGetValue(sectionName, out found) ? found as Dictionary<string, object> : null;
                if (section != null && section.TryGetValue(key, out value) && value is int) return (int)value;
            }
            catch { }
            return fallback;
        }

        void WriteSetting(string sectionName, string key, object on)
        {
            var config = LoadConfig();
            object found;
            var section = config.TryGetValue(sectionName, out found) ? found as Dictionary<string, object> : null;
            if (section == null) { section = new Dictionary<string, object>(); config[sectionName] = section; }
            section[key] = on;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(config),
                              new UTF8Encoding(false));
        }

        // Quality preset (GPU.quality_preset): sets the boxes below it and the memory settings the
        // launcher does not show (the driver's pipeline cache file, which grows past 1 GB and is
        // loaded whole; the pool of reused GPU images). A changed box makes it "Custom".
        //   Low:          reflections off, mirror half rate, no driver cache, 128 MB pool
        //   Medium:       reflections off, no driver cache, 256 MB pool
        //   High:         everything on, driver cache, 512 MB pool
        //   Original PS4: as High, without upscaling or frame generation (the console's picture)
        // Tokyo rain race, committed memory: driver cache on ~11 GB average / 13.5 GB peak, off ~9.2 / 9.7.
        void AddQualityPreset(TableLayoutPanel graphics, CheckBox fsr, CheckBox framegen, CheckBox wet, CheckBox mirror)
        {
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 6), WrapContents = false };
            var label = new Label { Text = "Quality preset", AutoSize = false, Width = 110, Height = 20, BackColor = Color.Transparent, Margin = new Padding(0, 6, 8, 0) };
            var preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, FlatStyle = FlatStyle.Flat };
            preset.Items.AddRange(new object[] { "Custom", "Low (least memory)", "Medium", "High", "Original PS4" });
            preset.SelectedIndex = Math.Max(0, Math.Min(4, ReadIntSetting("GPU", "quality_preset", 0)));
            bool applying = false;
            preset.SelectedIndexChanged += delegate
            {
                int p = preset.SelectedIndex;
                try
                {
                    WriteSetting("GPU", "quality_preset", p);
                    if (p == 0) return;
                    applying = true;
                    wet.Checked = p >= 3;
                    mirror.Checked = p == 1;
                    if (p == 4) { fsr.Checked = false; framegen.Checked = false; }
                    WriteSetting("Vulkan", "pipeline_cache_enabled", p >= 3);
                    WriteSetting("GPU", "image_recycle_mb", p == 1 ? 128 : p == 2 ? 256 : 512);
                }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
                finally { applying = false; }
            };
            foreach (var box in new[] { fsr, framegen, wet, mirror })
            {
                box.CheckedChanged += delegate
                {
                    if (!applying && preset.SelectedIndex != 0) preset.SelectedIndex = 0;
                };
            }
            row.Controls.Add(label);
            row.Controls.Add(preset);
            graphics.Controls.Add(row);
            graphics.Controls.SetChildIndex(row, 1); // under the section title
        }

        CheckBox AddOption(TableLayoutPanel panel, string text, string sectionName, string key, bool fallback)
        {
            var box = new DarkCheckBox { Text = text, AutoSize = true, Checked = ReadSetting(sectionName, key, fallback), Margin = new Padding(0, 2, 0, 2) };
            box.CheckedChanged += delegate
            {
                try { WriteSetting(sectionName, key, box.Checked); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            panel.Controls.Add(box);
            return box;
        }

        Dictionary<string, object> LoadConfig()
        {
            if (!File.Exists(ConfigPath)) return new Dictionary<string, object>();
            var parsed = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(ConfigPath));
            return parsed as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        void RefreshState()
        {
            bool ready = File.Exists(Exe) && Directory.Exists(Path.Combine(root, "app0", "sce_module"));
            play.Enabled = ready && !busy;
            setup.Enabled = !busy;
            if (!busy) status.Text = ready ? "Ready. Press Play." : "Not set up yet. Pick the folders and press Set up.";
        }

        readonly object logLock = new object();

        void Log(string line)
        {
            lock (logLock)
            {
                try { File.AppendAllText(Path.Combine(root, "setup.log"), line + Environment.NewLine); } catch { }
            }
            if (!IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), line); return; }
            log.AppendText(line + Environment.NewLine);
        }

        void SetStatus(string text)
        {
            Log("== " + text);
            if (!IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatusText), text); return; }
            SetStatusText(text);
        }

        void SetStatusText(string text)
        {
            status.Text = text;
        }

        /// RyGT.exe --setup: the same set up without the window, from the folders in RyGT.ini.
        public int SetupWithoutWindow()
        {
            try { File.Delete(Path.Combine(root, "setup.log")); } catch { }
            try { RunSetup(); return 0; }
            catch (Exception e) { Log("ERROR: " + e.Message); return 1; }
        }

        void StartSetup()
        {
            settings.Game = game.Text.Trim();
            settings.Update = update.Text.Trim();
            settings.Modules = modules.Text.Trim();
            settings.Fonts = fonts.Text.Trim();
            try { settings.Save(); } catch { }
            busy = true;
            RefreshState();
            log.Clear();
            try { File.Delete(Path.Combine(root, "setup.log")); } catch { }
            var thread = new Thread(delegate ()
            {
                bool ok = false;
                try { RunSetup(); ok = true; }
                catch (Exception e) { Log("ERROR: " + e.Message); }
                BeginInvoke(new Action(delegate
                {
                    busy = false;
                    RefreshState();
                    if (ok) status.Text = "Set up finished. Press Play.";
                    else status.Text = "Set up failed; see the log.";
                }));
            });
            thread.IsBackground = true;
            thread.Start();
        }

        void RunSetup()
        {
            string baseDir = settings.Game, updateDir = settings.Update;
            if (!Directory.Exists(baseDir)) throw new Exception("Pick the game folder (the one holding gt00.vol and eboot.bin).");
            if (!File.Exists(Path.Combine(baseDir, "gt00.vol")))
                throw new Exception("The game folder has no gt00.vol. Pick the base game folder of CUSA03220.");
            if (updateDir.Length > 0 && !Directory.Exists(updateDir)) throw new Exception("The update folder does not exist.");
            if (updateDir.Length == 0) updateDir = null;
            string gameDir = updateDir ?? baseDir; // the newest executable, modules and data index

            string version = ReadAppVersion(Path.Combine(gameDir, "sce_sys", "param.sfo"));
            Log("Game version: " + (version ?? "unknown"));
            if (version != null && version != "01.69")
                throw new Exception("This build is for update 1.69, but the game is " + version + ". Pick the 1.69 update folder too.");

            string fontDir, font2Dir;
            FindFonts(settings.Fonts, out fontDir, out font2Dir);
            var firmware = new Dictionary<string, string>();
            foreach (var name in FirmwareModules)
            {
                var found = FindModule(settings.Modules, name);
                if (found == null) throw new Exception("System module " + name + " was not found under " + settings.Modules + ".");
                firmware[name] = found;
            }
            foreach (var name in OptionalFirmwareModules)
            {
                var found = FindModule(settings.Modules, name);
                if (found != null) firmware[name] = found;
                else Log("Note: " + name + " is not in the system modules folder; " +
                         (name == "libSceJpegDec" ? "Scapes images will not show." : "saving photos will not work."));
            }

            // 1. The executable and modules to convert, unwrapped to plain ELF.
            SetStatus("Preparing the game files...");
            string work = Path.Combine(root, "convert");
            string workModules = Path.Combine(work, "sce_module");
            if (Directory.Exists(workModules)) Directory.Delete(workModules, true);
            Directory.CreateDirectory(workModules);
            Unwrap(Path.Combine(gameDir, "eboot.bin"), Path.Combine(work, "eboot.elf"));
            foreach (var name in new[] { "libc.prx", "libSceFios2.prx" })
            {
                string source = FirstExisting(Path.Combine(gameDir, "sce_module", name), Path.Combine(baseDir, "sce_module", name));
                if (source == null) throw new Exception("The game's sce_module\\" + name + " is missing.");
                Unwrap(source, Path.Combine(workModules, name));
            }
            foreach (var name in new[] { "libSceSecure.prx", "logiWheel.prx" })
            {
                string source = FirstExisting(Path.Combine(gameDir, name), Path.Combine(baseDir, name));
                if (source != null) Unwrap(source, Path.Combine(workModules, name));
            }
            foreach (var pair in firmware) Unwrap(pair.Value, Path.Combine(workModules, pair.Key + ".prx"));

            // 2. Convert.
            SetStatus("Converting the game for Windows (this takes a while)...");
            string converter = Path.Combine(root, "converter", "ryty.exe");
            if (!File.Exists(converter)) throw new Exception("converter\\ryty.exe is missing from the RyGT folder.");
            Directory.CreateDirectory(Path.Combine(root, "app0", "sce_module"));
            int code = Run(converter, "--windows --ps4 --skip-syscall-check --registry --report \"" +
                Path.Combine(work, "port-report.json") + "\" \"" + Path.Combine(work, "eboot.elf") + "\" \"" + Exe + "\"", work);
            if (code != 0 || !File.Exists(Exe)) throw new Exception("The converter failed (exit code " + code + ").");
            GameExe.Prepare(Exe);
            Log("Gave GTSport.exe the ryGT icon and made it a Windows program.");

            // 3. app0: the game's files, the update's over the base game's, linked rather than copied.
            SetStatus("Linking the game files...");
            LinkGameFiles(baseDir, updateDir);

            // 4. Fonts.
            SetStatus("Copying the fonts...");
            CopyDirectory(fontDir, Path.Combine(root, "sys", "common", "font"));
            CopyDirectory(font2Dir, Path.Combine(root, "sys", "common", "font2"));

            Log("Set up finished.");
        }

        static string ReadAppVersion(string sfo)
        {
            if (!File.Exists(sfo)) return null;
            var d = File.ReadAllBytes(sfo);
            if (d.Length < 20 || BitConverter.ToUInt32(d, 0) != 0x46535000) return null;
            int keys = (int)BitConverter.ToUInt32(d, 8), data = (int)BitConverter.ToUInt32(d, 12), count = (int)BitConverter.ToUInt32(d, 16);
            for (int i = 0; i < count; i++)
            {
                int e = 20 + i * 16;
                int keyOffset = BitConverter.ToUInt16(d, e);
                int length = (int)BitConverter.ToUInt32(d, e + 4);
                int dataOffset = (int)BitConverter.ToUInt32(d, e + 12);
                int end = keys + keyOffset;
                while (end < d.Length && d[end] != 0) end++;
                string key = Encoding.ASCII.GetString(d, keys + keyOffset, end - keys - keyOffset);
                if (key == "APP_VER") return Encoding.ASCII.GetString(d, data + dataOffset, length).TrimEnd('\0');
            }
            return null;
        }

        static void FindFonts(string selected, out string font, out string font2)
        {
            if (!Directory.Exists(selected)) throw new Exception("Pick the fonts folder (the one holding font and font2).");
            if (Directory.Exists(Path.Combine(selected, "font")) && Directory.Exists(Path.Combine(selected, "font2")))
            {
                font = Path.Combine(selected, "font");
                font2 = Path.Combine(selected, "font2");
                return;
            }
            var parent = Directory.GetParent(selected);
            if (parent != null && Path.GetFileName(selected).Equals("font", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(Path.Combine(parent.FullName, "font2")))
            {
                font = selected;
                font2 = Path.Combine(parent.FullName, "font2");
                return;
            }
            throw new Exception("The fonts folder needs font and font2 folders inside it.");
        }

        static string FindModule(string folder, string name)
        {
            if (!Directory.Exists(folder)) throw new Exception("Pick the system modules folder.");
            foreach (var extension in new[] { ".sprx", ".prx", ".elf" })
            {
                var hit = Directory.EnumerateFiles(folder, name + extension, SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null) return hit;
            }
            return null;
        }

        static string FirstExisting(params string[] paths)
        {
            return paths.FirstOrDefault(File.Exists);
        }

        void Unwrap(string source, string destination)
        {
            var bytes = File.ReadAllBytes(source);
            File.WriteAllBytes(destination, Self.RelroToLoad(Self.ToElf(bytes, Path.GetFileName(source))));
            Log("Prepared " + Path.GetFileName(destination) + " from " + source);
        }

        int Run(string exe, string arguments, string directory)
        {
            Log("> " + Path.GetFileName(exe) + " " + arguments);
            var info = new ProcessStartInfo(exe, arguments)
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = info })
            {
                var lines = new List<string>();
                DataReceivedEventHandler collect = delegate (object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (lines) lines.Add(e.Data);
                    if (e.Data.StartsWith("FAIL") || e.Data.StartsWith("error") || e.Data.Contains("Exception")) Log(e.Data);
                };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                lock (lines) File.WriteAllLines(Path.Combine(directory, "convert.log"), lines);
                Log("Converter finished with exit code " + process.ExitCode + " (full output in convert\\convert.log).");
                return process.ExitCode;
            }
        }

        void LinkGameFiles(string baseDir, string updateDir)
        {
            string app0 = Path.Combine(root, "app0");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in new[] { baseDir, updateDir })
            {
                if (source == null) continue;
                foreach (var file in Directory.GetFiles(source))
                {
                    string name = Path.GetFileName(file);
                    if (!name.Equals("eboot.bin", StringComparison.OrdinalIgnoreCase)) files[name] = file;
                }
                foreach (var dir in Directory.GetDirectories(source))
                {
                    string name = Path.GetFileName(dir);
                    if (!name.Equals("sce_module", StringComparison.OrdinalIgnoreCase)) dirs[name] = dir;
                }
            }
            int linked = 0;
            foreach (var pair in files)
            {
                string target = Path.Combine(app0, pair.Key);
                if (File.Exists(target)) File.Delete(target);
                if (Native.CreateHardLink(target, pair.Value, IntPtr.Zero)) { linked++; continue; }
                if (Native.CreateSymbolicLink(target, pair.Value, 2)) { linked++; continue; } // needs Developer Mode
                throw new Exception("Could not link " + pair.Value + ". Put the RyGT folder on the same drive as the game, " +
                                    "or turn on Developer Mode in Windows settings.");
            }
            foreach (var pair in dirs)
            {
                string target = Path.Combine(app0, pair.Key);
                if (Directory.Exists(target)) Directory.Delete(target, false);
                var info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + target + "\" \"" + pair.Value + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var process = Process.Start(info)) process.WaitForExit();
                if (!Directory.Exists(target)) throw new Exception("Could not link the folder " + pair.Value + ".");
            }
            Log("Linked " + linked + " game files and " + dirs.Count + " folders into app0.");
        }

        static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        void Play()
        {
            if (!File.Exists(Exe)) { MessageBox.Show(this, "Press Set up first."); return; }
            StartGame(root);
            status.Text = "GT Sport is starting. Logs: stdout.log and stderr.log in the RyGT folder.";
        }

        /// Starts GTSport.exe from root, as a double-click would: the runtime and renderer default to the
        /// settings GT Sport needs, and without standard output the game writes stdout.log and
        /// stderr.log itself.
        static void StartGame(string root)
        {
            string exe = Path.Combine(root, "GTSport.exe");
            // Folders set up by an earlier RyGT.exe get the icon and the Windows subsystem now.
            try { if (!GameExe.IsPrepared(exe)) GameExe.Prepare(exe); } catch { }
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = root, UseShellExecute = true });
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--setup")) return new LoaderForm().SetupWithoutWindow();
            if (args.Length == 2 && args[0] == "--prepare")
            {
                // Gives an already converted GTSport.exe the ryGT icon and the Windows subsystem.
                try { GameExe.Prepare(Path.GetFullPath(args[1])); return 0; }
                catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
            }
            if (args.Length == 2 && args[0] == "--snapshot")
            {
                // Renders the window's contents to a PNG (for checking the layout).
                using (var form = new LoaderForm())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-4000, -4000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    var content = form.Controls[0];
                    using (var image = new Bitmap(content.Width, content.Height))
                    {
                        content.DrawToBitmap(image, new Rectangle(Point.Empty, content.Size));
                        image.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                }
                return 0;
            }
            Application.Run(new LoaderForm());
            return 0;
        }
    }
}
