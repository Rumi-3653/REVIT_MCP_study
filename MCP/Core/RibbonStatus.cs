using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using FormattedText = System.Windows.Media.FormattedText;
using Typeface = System.Windows.Media.Typeface;

namespace RevitMCP.Core
{
    /// <summary>
    /// 功能區「AI 客戶端」按鈕圖示：顯示目前連線（未連線時為指定）的 AI。圖示用 WPF 即時繪製，不需圖檔。
    /// （MCP 服務按鈕的開／關圖示由上游 Application.GetToggleIcon 負責，這裡不再碰它。）
    /// 由 Idling 事件在 UI 執行緒呼叫 Refresh()，只在狀態改變時才改按鈕。
    /// </summary>
    public static class RibbonStatus
    {
        public static PushButton ClientButton;

        private static string _lastKey;
        private static DateTime _lastCheckUtc = DateTime.MinValue;

        public static void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if ((DateTime.UtcNow - _lastCheckUtc).TotalMilliseconds < 1000) return;
            _lastCheckUtc = DateTime.UtcNow;
            try { Refresh(); } catch (Exception ex) { Logger.Debug("[RibbonStatus] " + ex.Message); }
        }

        public static void Refresh()
        {
            var svc = Application.SocketService;
            bool running = svc != null && svc.IsRunning;
            bool locked = false;
            string connected = null;
            if (running)
            {
                var snap = svc.GetStatusSnapshot();
                locked = snap.locked;
                connected = snap.clientName;
            }
            string preferred = Configuration.ConfigManager.Instance.Settings.PreferredClient ?? "any";
            // 已連線就顯示實際連上的 AI，否則顯示指定的 AI
            string shown = locked ? Classify(connected) : preferred;

            string key = running + "|" + locked + "|" + connected + "|" + preferred + "|" + shown;
            if (key == _lastKey) return;
            _lastKey = key;

            if (ClientButton != null)
            {
                ClientButton.ItemText = Commands.SelectClientCommand.ButtonText(preferred);
                ClientButton.ToolTip = "指定：" + Label(preferred) + (locked ? "　｜　目前連線：" + (connected ?? "unknown") : "　｜　目前無 AI 連線")
                    + "\n按一下切換 Claude / Codex / 不限。";
                ClientButton.LargeImage = AiIcon(32, shown);
                ClientButton.Image = AiIcon(16, shown);
            }
        }

        private static string Classify(string clientName)
        {
            if (string.IsNullOrEmpty(clientName)) return "any";
            if (clientName.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0) return "claude";
            if (clientName.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0) return "codex";
            return "any";
        }

        private static string Label(string p) => p == "claude" ? "Claude" : p == "codex" ? "Codex" : "不限";

        // ---- 圖示繪製 ----

        private static BitmapSource Render(int size, Action<DrawingContext, double> draw)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) draw(dc, size);
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        /// <summary>Claude＝橘底白色放射星；Codex＝黑底白色 &gt;_ 提示字元；不限＝灰底白色 ⇄。</summary>
        private static BitmapSource AiIcon(int size, string who)
        {
            return Render(size, (dc, s) =>
            {
                var rect = new Rect(s * 0.06, s * 0.06, s * 0.88, s * 0.88);
                double corner = s * 0.2;
                var white = new Pen(Brushes.White, Math.Max(1.2, s / 11.0)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

                if (who == "claude")
                {
                    dc.DrawRoundedRectangle(Brush(0xD9, 0x77, 0x57), null, rect, corner, corner);
                    var c = new Point(s / 2.0, s / 2.0);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        double len = (i % 2 == 0) ? s * 0.30 : s * 0.22;
                        dc.DrawLine(white, new Point(c.X + Math.Cos(a) * s * 0.06, c.Y + Math.Sin(a) * s * 0.06),
                                           new Point(c.X + Math.Cos(a) * len, c.Y + Math.Sin(a) * len));
                    }
                }
                else if (who == "codex")
                {
                    dc.DrawRoundedRectangle(Brush(0x20, 0x20, 0x20), null, rect, corner, corner);
                    // ">"
                    dc.DrawLine(white, new Point(s * 0.26, s * 0.32), new Point(s * 0.46, s * 0.50));
                    dc.DrawLine(white, new Point(s * 0.46, s * 0.50), new Point(s * 0.26, s * 0.68));
                    // "_"
                    dc.DrawLine(white, new Point(s * 0.54, s * 0.70), new Point(s * 0.76, s * 0.70));
                }
                else
                {
                    dc.DrawRoundedRectangle(Brush(0x75, 0x75, 0x75), null, rect, corner, corner);
                    var ft = new FormattedText("⇄", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Symbol"), s * 0.62, Brushes.White, 1.0);
                    dc.DrawText(ft, new Point((s - ft.Width) / 2, (s - ft.Height) / 2));
                }
            });
        }
    }
}
