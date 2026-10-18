// RyGT loader: the Controller ports window. Picks which connected controller is player 1-4
// (GT Sport's splitscreen) and shows a live preview of the chosen controller, like shadPS4.
// Controllers are found and read through Windows' joystick API (winmm), and remembered by USB
// vendor/product id and their place among connected controllers of that id
// (Input.controller_ports in the GT Sport config, read by the renderer's input bridge).
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RyGT
{
    static class Joysticks
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct JoyCaps
        {
            public ushort Mid, Pid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public uint XMin, XMax, YMin, YMax, ZMin, ZMax, NumButtons, PeriodMin, PeriodMax;
            public uint RMin, RMax, UMin, UMax, VMin, VMax, Caps, MaxAxes, NumAxes, MaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JoyInfoEx
        {
            public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int joyGetDevCapsW(IntPtr id, ref JoyCaps caps, int size);

        [DllImport("winmm.dll")]
        static extern int joyGetPosEx(int id, ref JoyInfoEx info);

        public class Device
        {
            public int Id;
            public ushort Vendor, Product;
            public int Nth;
            public string Name;
            public string Key { get { return string.Format("{0:x4}:{1:x4}#{2}", Vendor, Product, Nth); } }
            public bool PlayStation { get { return Vendor == 0x054c; } }
            public bool Xbox { get { return Vendor == 0x045e; } }
        }

        public static string FriendlyName(ushort vendor, ushort product, string fallback)
        {
            if (vendor == 0x054c)
            {
                if (product == 0x0ce6) return "DualSense";
                if (product == 0x0df2) return "DualSense Edge";
                if (product == 0x05c4 || product == 0x09cc || product == 0x0ba0) return "DualShock 4";
                return "PlayStation controller";
            }
            if (vendor == 0x045e) return "Xbox controller";
            if (vendor == 0x057e) return "Nintendo Switch Pro controller";
            string oem = null;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(string.Format(
                    @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{0:X4}&PID_{1:X4}", vendor, product)))
                {
                    if (key != null) oem = key.GetValue("OEMName") as string;
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(oem)) return oem;
            return string.IsNullOrEmpty(fallback) || fallback.StartsWith("Microsoft PC-joystick")
                ? string.Format("Controller {0:X4}:{1:X4}", vendor, product) : fallback;
        }

        /// The connected controllers, in Windows' joystick order.
        public static List<Device> Connected()
        {
            var list = new List<Device>();
            var seen = new Dictionary<uint, int>();
            for (int id = 0; id < 16; ++id)
            {
                var info = new JoyInfoEx { Size = (uint)Marshal.SizeOf(typeof(JoyInfoEx)), Flags = 0xff };
                if (joyGetPosEx(id, ref info) != 0) continue;
                var caps = new JoyCaps();
                if (joyGetDevCapsW(new IntPtr(id), ref caps, Marshal.SizeOf(typeof(JoyCaps))) != 0) continue;
                uint key = ((uint)caps.Mid << 16) | caps.Pid;
                int nth;
                seen.TryGetValue(key, out nth);
                seen[key] = nth + 1;
                list.Add(new Device { Id = id, Vendor = caps.Mid, Product = caps.Pid, Nth = nth, Name = FriendlyName(caps.Mid, caps.Pid, caps.Name) });
            }
            return list;
        }

        public static bool Read(int id, out JoyInfoEx info)
        {
            info = new JoyInfoEx { Size = (uint)Marshal.SizeOf(typeof(JoyInfoEx)), Flags = 0xff };
            return joyGetPosEx(id, ref info) == 0;
        }
    }

    /// A PS4 controller drawn with the chosen controller's live state.
    class ControllerPreview : Control
    {
        public bool Connected;
        public string Caption = "";
        // Pressed state of the PS4 controls, sticks (-1..1) and triggers (0..1).
        public bool Cross, Circle, Square, Triangle, L1, R1, L3, R3, Share, Options, Ps, Touchpad, PadUp, PadDown, PadLeft, PadRight;
        public float LX, LY, RX, RY, L2, R2;

        public ControllerPreview()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        static readonly Color Body = Color.FromArgb(58, 58, 64);
        static readonly Color Edge = Color.FromArgb(95, 95, 104);
        static readonly Color Off = Color.FromArgb(34, 34, 38);
        static readonly Color On = Color.FromArgb(206, 40, 52);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Dark.Back);
            // Drawn on a 400 x 260 canvas, scaled to fit.
            float scale = Math.Min(Width / 400f, (Height - 24) / 260f);
            g.TranslateTransform((Width - 400 * scale) / 2, 0);
            g.ScaleTransform(scale, scale);
            using (var text = new SolidBrush(Dark.Text))
            using (var font = new Font("Segoe UI", 9f))
            {
                var format = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(Caption, font, text, new RectangleF(0, 236, 400, 24), format);
            }
            float alpha = Connected ? 1f : 0.45f;
            Func<Color, Color> fade = c => Color.FromArgb((int)(255 * alpha), c);

            // Triggers (fill = how far pressed) and bumpers.
            Bar(g, new RectangleF(70, 6, 70, 16), L2, fade);
            Bar(g, new RectangleF(260, 6, 70, 16), R2, fade);
            Pill(g, new RectangleF(66, 26, 78, 12), L1, fade);
            Pill(g, new RectangleF(256, 26, 78, 12), R1, fade);

            // Body: two grips and the middle.
            using (var path = new GraphicsPath())
            {
                path.AddArc(40, 40, 120, 70, 180, 90);
                path.AddLine(100, 40, 300, 40);
                path.AddArc(240, 40, 120, 70, 270, 90);
                path.AddBezier(360, 75, 385, 140, 395, 215, 350, 225);
                path.AddBezier(350, 225, 315, 232, 300, 180, 270, 160);
                path.AddLine(270, 160, 130, 160);
                path.AddBezier(130, 160, 100, 180, 85, 232, 50, 225);
                path.AddBezier(50, 225, 5, 215, 15, 140, 40, 75);
                path.CloseFigure();
                using (var fill = new SolidBrush(fade(Body))) g.FillPath(fill, path);
                using (var pen = new Pen(fade(Edge), 2)) g.DrawPath(pen, path);
            }

            // Touchpad, share, options, PS.
            Rounded(g, new RectangleF(140, 44, 120, 58), 8, Touchpad, fade);
            Pill(g, new RectangleF(118, 52, 12, 22), Share, fade);
            Pill(g, new RectangleF(270, 52, 12, 22), Options, fade);
            Round(g, new RectangleF(190, 122, 20, 20), Ps, fade);

            // D-pad.
            Rounded(g, new RectangleF(80, 64, 18, 22), 3, PadUp, fade);
            Rounded(g, new RectangleF(80, 104, 18, 22), 3, PadDown, fade);
            Rounded(g, new RectangleF(58, 86, 22, 18), 3, PadLeft, fade);
            Rounded(g, new RectangleF(98, 86, 22, 18), 3, PadRight, fade);

            // Face buttons, with their symbols.
            Face(g, 312, 66, Triangle, fade, Color.FromArgb(64, 196, 160), 0);
            Face(g, 312, 120, Cross, fade, Color.FromArgb(120, 150, 230), 1);
            Face(g, 285, 93, Square, fade, Color.FromArgb(220, 130, 190), 2);
            Face(g, 339, 93, Circle, fade, Color.FromArgb(235, 90, 90), 3);

            // Sticks: the ring, and the cap where the stick points.
            Stick(g, 140, 150, LX, LY, L3, fade);
            Stick(g, 260, 150, RX, RY, R3, fade);
        }

        static void Bar(Graphics g, RectangleF r, float amount, Func<Color, Color> fade)
        {
            using (var off = new SolidBrush(fade(Off))) g.FillRectangle(off, r);
            var filled = new RectangleF(r.X, r.Y, r.Width * Math.Max(0, Math.Min(1, amount)), r.Height);
            using (var on = new SolidBrush(fade(On))) g.FillRectangle(on, filled);
            using (var pen = new Pen(fade(Edge))) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }

        static void Pill(Graphics g, RectangleF r, bool pressed, Func<Color, Color> fade)
        {
            Rounded(g, r, Math.Min(r.Width, r.Height) / 2, pressed, fade);
        }

        static void Rounded(Graphics g, RectangleF r, float radius, bool pressed, Func<Color, Color> fade)
        {
            using (var path = new GraphicsPath())
            {
                float d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (var fill = new SolidBrush(fade(pressed ? On : Off))) g.FillPath(fill, path);
                using (var pen = new Pen(fade(Edge))) g.DrawPath(pen, path);
            }
        }

        static void Round(Graphics g, RectangleF r, bool pressed, Func<Color, Color> fade)
        {
            using (var fill = new SolidBrush(fade(pressed ? On : Off))) g.FillEllipse(fill, r);
            using (var pen = new Pen(fade(Edge))) g.DrawEllipse(pen, r);
        }

        static void Face(Graphics g, float x, float y, bool pressed, Func<Color, Color> fade, Color symbol, int kind)
        {
            Round(g, new RectangleF(x - 12, y - 12, 24, 24), pressed, fade);
            using (var pen = new Pen(fade(pressed ? Color.White : symbol), 2))
            {
                switch (kind)
                {
                    case 0: g.DrawPolygon(pen, new[] { new PointF(x, y - 6), new PointF(x + 6, y + 5), new PointF(x - 6, y + 5) }); break;
                    case 1: g.DrawLine(pen, x - 5, y - 5, x + 5, y + 5); g.DrawLine(pen, x + 5, y - 5, x - 5, y + 5); break;
                    case 2: g.DrawRectangle(pen, x - 5, y - 5, 10, 10); break;
                    default: g.DrawEllipse(pen, x - 6, y - 6, 12, 12); break;
                }
            }
        }

        static void Stick(Graphics g, float x, float y, float sx, float sy, bool pressed, Func<Color, Color> fade)
        {
            using (var fill = new SolidBrush(fade(Off))) g.FillEllipse(fill, x - 24, y - 24, 48, 48);
            using (var pen = new Pen(fade(Edge))) g.DrawEllipse(pen, x - 24, y - 24, 48, 48);
            float cx = x + sx * 12, cy = y + sy * 12;
            Round(g, new RectangleF(cx - 14, cy - 14, 28, 28), pressed, fade);
        }
    }

    class PortsForm : Form
    {
        readonly ComboBox[] ports = new ComboBox[4];
        readonly Label[] status = new Label[4];
        readonly ControllerPreview preview;
        readonly Timer timer;
        readonly Action<string> save;
        List<Joysticks.Device> devices = new List<Joysticks.Device>();
        readonly string[] chosen = new string[4];
        int shownPlayer;
        int rescanTicks;

        public PortsForm(string current, Action<string> save)
        {
            this.save = save;
            var parts = (current ?? "").Split(',');
            for (int i = 0; i < 4; ++i) chosen[i] = i < parts.Length ? parts[i].Trim() : "";

            Text = "ryGT - Controller ports";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(880, 470);
            MinimumSize = new Size(760, 420);
            MinimizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(14, 10, 14, 10) };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(outer);

            var intro = new Label
            {
                AutoSize = true, MaximumSize = new Size(840, 0), Margin = new Padding(0, 0, 0, 10),
                Text = "Pick which controller plays as each player (GT Sport: Arcade, Split Screen for 2 players). " +
                       "\"Next connected\" takes the controllers nobody picked, in the order Windows lists them. " +
                       "Press buttons to see them light up in the preview; click a player to preview their controller."
            };
            outer.Controls.Add(intro, 0, 0);
            outer.SetColumnSpan(intro, 2);

            var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; ++i)
            {
                int player = i;
                table.Controls.Add(new Label { Text = "Player " + (i + 1), AutoSize = true, Margin = new Padding(0, 9, 10, 0), Font = new Font("Segoe UI Semibold", 9.5f) }, 0, i * 2);
                ports[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 0, 0) };
                ports[i].SelectedIndexChanged += delegate { OnPick(player); };
                ports[i].Enter += delegate { shownPlayer = player; };
                ports[i].DropDown += delegate { shownPlayer = player; };
                table.Controls.Add(ports[i], 1, i * 2);
                status[i] = new Label { AutoSize = true, ForeColor = Dark.DimText, Tag = "heading", Margin = new Padding(0, 2, 0, 6) };
                table.Controls.Add(status[i], 1, i * 2 + 1);
            }
            outer.Controls.Add(table, 0, 1);

            preview = new ControllerPreview { Dock = DockStyle.Fill, Margin = new Padding(16, 0, 0, 0), MinimumSize = new Size(300, 220) };
            outer.Controls.Add(preview, 1, 1);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            var ok = new Button { Text = "Save", AutoSize = true, Tag = "accent" };
            var reset = new Button { Text = "All automatic", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            ok.Click += delegate
            {
                try { save(string.Join(",", chosen)); DialogResult = DialogResult.OK; Close(); }
                catch (Exception e) { MessageBox.Show(this, "Could not save the ports: " + e.Message); }
            };
            reset.Click += delegate { for (int i = 0; i < 4; ++i) chosen[i] = ""; Fill(); };
            buttons.Controls.AddRange(new Control[] { ok, reset, cancel });
            outer.Controls.Add(buttons, 0, 2);
            outer.SetColumnSpan(buttons, 2);
            CancelButton = cancel;

            Dark.Apply(this);
            preview.BackColor = Dark.Back;
            HandleCreated += delegate { Dark.TitleBar(this); };

            devices = Joysticks.Connected();
            Fill();
            timer = new Timer { Interval = 33 };
            timer.Tick += delegate { Tick(); };
            timer.Start();
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
        }

        bool filling;

        /// The combo boxes from the connected controllers and the choices.
        void Fill()
        {
            filling = true;
            for (int i = 0; i < 4; ++i)
            {
                var box = ports[i];
                box.Items.Clear();
                box.Items.Add("Next connected (automatic)");
                foreach (var d in devices) box.Items.Add(DeviceLabel(d));
                int index = 0;
                if (!string.IsNullOrEmpty(chosen[i]))
                {
                    int found = devices.FindIndex(d => d.Key == chosen[i]);
                    if (found >= 0) index = found + 1;
                    else { box.Items.Add("Not connected: " + chosen[i]); index = box.Items.Count - 1; }
                }
                box.SelectedIndex = index;
            }
            filling = false;
            UpdateStatus();
        }

        string DeviceLabel(Joysticks.Device d)
        {
            int same = devices.Count(x => x.Vendor == d.Vendor && x.Product == d.Product);
            return same > 1 ? string.Format("{0} ({1})", d.Name, d.Nth + 1) : d.Name;
        }

        void OnPick(int player)
        {
            if (filling) return;
            shownPlayer = player;
            int index = ports[player].SelectedIndex;
            if (index <= 0) chosen[player] = "";
            else if (index <= devices.Count)
            {
                var key = devices[index - 1].Key;
                // A controller plays as one player: picking it here frees it elsewhere.
                for (int i = 0; i < 4; ++i) if (i != player && chosen[i] == key) chosen[i] = "";
                chosen[player] = key;
            }
            Fill();
        }

        /// Which controller each player gets, as the game will assign them: the picked ones
        /// first, then the others in Windows' order.
        Joysticks.Device[] Assignment()
        {
            var result = new Joysticks.Device[4];
            var used = new HashSet<string>();
            for (int i = 0; i < 4; ++i)
            {
                var d = devices.FirstOrDefault(x => x.Key == chosen[i]);
                if (d != null) { result[i] = d; used.Add(d.Key); }
            }
            var rest = devices.Where(d => !used.Contains(d.Key)).ToList();
            for (int i = 0; i < 4 && rest.Count > 0; ++i)
            {
                if (result[i] == null) { result[i] = rest[0]; rest.RemoveAt(0); }
            }
            return result;
        }

        void UpdateStatus()
        {
            var assigned = Assignment();
            for (int i = 0; i < 4; ++i)
            {
                status[i].Text = assigned[i] != null ? "Plays with: " + DeviceLabel(assigned[i]) :
                                 i == 0 ? "Plays with: the keyboard (no controller)" : "No controller: this player stays out";
            }
        }

        void Tick()
        {
            if (++rescanTicks >= 60)
            {
                rescanTicks = 0;
                var now = Joysticks.Connected();
                if (!now.Select(d => d.Key + d.Id).SequenceEqual(devices.Select(d => d.Key + d.Id)))
                {
                    devices = now;
                    Fill();
                }
            }
            // A controller with a button held shows up in the preview too.
            var assigned = Assignment();
            var device = assigned[shownPlayer];
            Joysticks.JoyInfoEx info;
            preview.Connected = device != null && Joysticks.Read(device.Id, out info) && Apply(device, info);
            if (!preview.Connected) Clear();
            preview.Caption = "Player " + (shownPlayer + 1) + ": " + (device != null ? DeviceLabel(device) : "no controller");
            preview.Invalidate();
        }

        void Clear()
        {
            preview.Cross = preview.Circle = preview.Square = preview.Triangle = preview.L1 = preview.R1 = false;
            preview.L3 = preview.R3 = preview.Share = preview.Options = preview.Ps = preview.Touchpad = false;
            preview.PadUp = preview.PadDown = preview.PadLeft = preview.PadRight = false;
            preview.LX = preview.LY = preview.RX = preview.RY = preview.L2 = preview.R2 = 0;
        }

        static float Axis(uint value) { return (value - 32767.5f) / 32767.5f; }

        bool Apply(Joysticks.Device device, Joysticks.JoyInfoEx info)
        {
            Func<int, bool> b = n => (info.Buttons & (1u << n)) != 0;
            uint pov = info.Pov;
            bool povActive = pov != 0xffff && pov <= 36000;
            preview.PadUp = povActive && (pov >= 31500 || pov <= 4500);
            preview.PadRight = povActive && pov >= 4500 && pov <= 13500;
            preview.PadDown = povActive && pov >= 13500 && pov <= 22500;
            preview.PadLeft = povActive && pov >= 22500 && pov <= 31500;
            preview.LX = Axis(info.X);
            preview.LY = Axis(info.Y);
            if (device.Xbox)
            {
                // XInput through the joystick API: A B X Y LB RB Back Start LS RS; both triggers on Z.
                preview.Cross = b(0); preview.Circle = b(1); preview.Square = b(2); preview.Triangle = b(3);
                preview.L1 = b(4); preview.R1 = b(5); preview.Share = b(6); preview.Options = b(7);
                preview.L3 = b(8); preview.R3 = b(9); preview.Ps = false; preview.Touchpad = false;
                float z = Axis(info.Z);
                preview.L2 = Math.Max(0, z);
                preview.R2 = Math.Max(0, -z);
                preview.RX = Axis(info.U);
                preview.RY = Axis(info.R);
            }
            else
            {
                // DualShock 4, DualSense and most other pads: Square Cross Circle Triangle L1 R1 L2 R2
                // Share Options L3 R3 PS Touchpad; right stick on Z/R, triggers on U/V.
                preview.Square = b(0); preview.Cross = b(1); preview.Circle = b(2); preview.Triangle = b(3);
                preview.L1 = b(4); preview.R1 = b(5);
                preview.Share = b(8); preview.Options = b(9); preview.L3 = b(10); preview.R3 = b(11);
                preview.Ps = b(12); preview.Touchpad = b(13);
                preview.RX = Axis(info.Z);
                preview.RY = Axis(info.R);
                preview.L2 = Math.Max(b(6) ? 1f : 0f, (Axis(info.U) + 1) / 2);
                preview.R2 = Math.Max(b(7) ? 1f : 0f, (Axis(info.V) + 1) / 2);
                if (!device.PlayStation && info.U == 0 && info.V == 0) { preview.L2 = b(6) ? 1 : 0; preview.R2 = b(7) ? 1 : 0; }
            }
            return true;
        }
    }
}
