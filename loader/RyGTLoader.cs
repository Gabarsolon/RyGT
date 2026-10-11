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
        public static readonly Color Accent = Color.FromArgb(14, 99, 156);
        public static readonly Color AccentHover = Color.FromArgb(28, 120, 180);
        public static readonly Color Border = Color.FromArgb(70, 70, 74);
        public static readonly Color Text = Color.FromArgb(230, 230, 230);
        public static readonly Color DimText = Color.FromArgb(150, 150, 150);

        public static void Apply(Control control)
        {
            control.BackColor = Back;
            control.ForeColor = Text;
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
            if (check != null) check.FlatStyle = FlatStyle.Flat;
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
            ClientSize = new Size(720, 580);
            MinimumSize = new Size(600, 420);
            StartPosition = FormStartPosition.CenterScreen;

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 8, Padding = new Padding(12) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(table);

            var intro = new Label
            {
                Text = "Pick your own decrypted GT Sport dump (CUSA03220), its 1.69 update, the system modules " +
                       "from your PS4 firmware and its fonts. Set up converts the game for Windows; Play starts it.",
                AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(0, 0, 0, 10)
            };
            table.Controls.Add(intro, 0, 0);
            table.SetColumnSpan(intro, 3);

            game = AddRow(table, 1, "Game (CUSA03220)", settings.Game);
            update = AddRow(table, 2, "Update 1.69 (if separate)", settings.Update);
            modules = AddRow(table, 3, "System modules", settings.Modules);
            fonts = AddRow(table, 4, "Fonts", settings.Fonts);

            AddOption(table, 5, "Show the game's splash screen while it starts", "show_splash", false);
            AddOption(table, 6, "Show an FPS counter", "show_fps_counter", false);
            AddOption(table, 7, "Show the game on Discord (Rich Presence)", "discord_rpc_enabled", true);

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
            setup = new Button { Text = "Set up", AutoSize = true };
            play = new Button { Text = "Play", AutoSize = true, Tag = "accent" };
            var open = new Button { Text = "Open folder", AutoSize = true };
            setup.Click += delegate { StartSetup(); };
            play.Click += delegate { Play(); };
            open.Click += delegate { Process.Start("explorer.exe", "\"" + root + "\""); };
            buttons.Controls.AddRange(new Control[] { setup, play, open });
            table.Controls.Add(buttons, 0, 8);
            table.SetColumnSpan(buttons, 3);

            status = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            table.Controls.Add(status, 0, 9);
            table.SetColumnSpan(status, 3);

            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
            table.Controls.Add(log, 0, 10);
            table.SetColumnSpan(log, 3);
            table.RowCount = 11;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 1; i < 10; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Dark.Apply(this);
            intro.ForeColor = Dark.DimText;
            HandleCreated += delegate { Dark.TitleBar(this); };
            Shown += delegate { ActiveControl = play.Enabled ? (Control)play : setup; };
            RefreshState();
        }

        TextBox AddRow(TableLayoutPanel table, int row, string label, string value)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, row);
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

        /// A shadGT setting in the GT Sport config's General section (show_splash,
        /// show_fps_counter, discord_rpc_enabled).
        bool ReadGeneral(string key, bool fallback)
        {
            try
            {
                var config = LoadConfig();
                object general, value;
                var section = config.TryGetValue("General", out general) ? general as Dictionary<string, object> : null;
                if (section != null && section.TryGetValue(key, out value) && value is bool) return (bool)value;
            }
            catch { }
            return fallback;
        }

        void WriteGeneral(string key, bool on)
        {
            var config = LoadConfig();
            object general;
            var section = config.TryGetValue("General", out general) ? general as Dictionary<string, object> : null;
            if (section == null) { section = new Dictionary<string, object>(); config["General"] = section; }
            section[key] = on;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(config),
                              new UTF8Encoding(false));
        }

        CheckBox AddOption(TableLayoutPanel table, int row, string text, string key, bool fallback)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Checked = ReadGeneral(key, fallback), Margin = new Padding(0, 4, 0, 0) };
            box.CheckedChanged += delegate
            {
                try { WriteGeneral(key, box.Checked); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the setting: " + e.Message); }
            };
            table.Controls.Add(box, 0, row);
            table.SetColumnSpan(box, 3);
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
            var info = new ProcessStartInfo("cmd.exe", "/c \"\"" + Exe + "\" > stdout.log 2> stderr.log\"")
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
            };
            info.EnvironmentVariables["RTY_LENIENT_UNIMPLEMENTED"] = "1";
            info.EnvironmentVariables["APS5_NO_WRITE_WATCH"] = "1";
            info.EnvironmentVariables["VK_LOADER_LAYERS_DISABLE"] = "~implicit~";
            info.EnvironmentVariables["SHADGT_DRAW_PIPE"] = "1";
            info.EnvironmentVariables["SHADGT_PREWARM"] = "1";
            Process.Start(info);
            status.Text = "GT Sport is starting. Logs: stdout.log and stderr.log in the RyGT folder.";
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--setup")) return new LoaderForm().SetupWithoutWindow();
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
