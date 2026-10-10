using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP.Commands
{
    /// <summary>
    /// 切換 MCP 服務狀態命令 (開/關)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToggleServiceCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 以 IsRunning 判斷開/關 (而非 IsConnected)：服務已啟動但尚無 client 連入時 IsConnected=false，
                // 若用 IsConnected 判斷，再按一下會誤判為「啟動」而非「停止」。
                bool running = Application.SocketService != null && Application.SocketService.IsRunning;

                if (running)
                {
                    // 已在執行 → 停止
                    Application.StopMCPService();
                    Logger.Info("使用者手動停止 MCP 服務");
                    TaskDialog.Show("MCP 服務", "🔴 MCP 服務已關閉。");
                }
                else
                {
                    // 未執行 → 啟動
                    Logger.Info("使用者手動啟動 MCP 服務");
                    Application.StartMCPService(commandData.Application);
                    TaskDialog.Show("MCP 服務",
                        "🟢 MCP 服務已啟動。\n\n" +
                        "正在 localhost:8964 監聽，等待 AI 客戶端連入。\n" +
                        "（連上後可在「MCP 設定」查看目前佔用連線的客戶端）");
                }

                RibbonStatus.Refresh();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "切換服務狀態失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }


    /// <summary>
    /// 開啟設定視窗命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var settings = Configuration.ConfigManager.Instance.Settings;
                string info = $"目前設定:\n\n" +
                    $"主機: {settings.Host}\n" +
                    $"埠號: {settings.Port}\n" +
                    $"服務狀態: {(settings.IsEnabled ? "啟用" : "停用")}\n\n" +
                    $"配置檔位置:\n" +
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\RevitMCP\\config.json";

                if (Application.SocketService?.IsRunning == true)
                {
                    var (locked, clientName, remote, sinceUtc) = Application.SocketService.GetStatusSnapshot();
                    string clientDisplay = string.IsNullOrEmpty(clientName)
                        ? (remote ?? "—")
                        : (clientName + " (" + (remote ?? "?") + ")");
                    info += "\n\n" +
                        "連線狀態: " + (locked ? "已鎖定" : "閒置(等待連入)") + "\n" +
                        "目前客戶端: " + clientDisplay + "\n" +
                        "連線時間: " + (sinceUtc.HasValue ? sinceUtc.Value.ToLocalTime().ToString() : "—");
                }

                TaskDialog.Show("MCP 設定", info);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "開啟設定失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 切換/釋放目前連線命令：釋放後讓下一個重新連線的 client 取得鎖。
    /// 連線為匿名，無法保證釋放後由哪個 client 取得連線。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SwitchConnectionCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var svc = Application.SocketService;
                if (svc == null || !svc.IsRunning)
                {
                    TaskDialog.Show("切換/釋放連線", "MCP 服務尚未啟動。");
                    return Result.Succeeded;
                }

                var (released, prev) = svc.SwitchConnection();
                TaskDialog.Show("切換/釋放連線", released
                    ? ("已釋放連線（原客戶端 " + prev + "）。\n\n下一個重新連線的客戶端將取得連線。\n無法保證是特定客戶端——若要切換到另一個客戶端，請先讓目前客戶端停止重連。")
                    : "目前沒有已鎖定的連線。下一個連入的客戶端會直接取得連線。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "切換/釋放連線失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 指定允許連入的 AI 客戶端。選定後若目前連線的是另一個客戶端，立即釋放它，
    /// 讓指定的客戶端在下次呼叫工具時取得連線；被排除的客戶端重連會收到 409。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SelectClientCommand : IExternalCommand
    {
        private static string Label(string preferred) =>
            preferred == "claude" ? "Claude" : preferred == "codex" ? "Codex" : "任一 AI";

        public static string ButtonText(string preferred)
        {
            switch (preferred)
            {
                case "claude": return "AI 客戶端\n(Claude)";
                case "codex": return "AI 客戶端\n(Codex)";
                default: return "AI 客戶端\n(不限)";
            }
        }

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var settings = Configuration.ConfigManager.Instance.Settings;
                var dialog = new TaskDialog("指定 AI 客戶端")
                {
                    MainInstruction = "要讓哪個 AI 連 Revit？",
                    MainContent = "目前：" + ButtonText(settings.PreferredClient).Replace("\n", " "),
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Claude", "只允許 Claude Code / Claude Desktop 連入");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Codex", "只允許 Codex 連入");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "不限", "先連上的先用（原本行為）");

                string chosen;
                switch (dialog.Show())
                {
                    case TaskDialogResult.CommandLink1: chosen = "claude"; break;
                    case TaskDialogResult.CommandLink2: chosen = "codex"; break;
                    case TaskDialogResult.CommandLink3: chosen = "any"; break;
                    default: return Result.Cancelled;
                }

                settings.PreferredClient = chosen;
                Configuration.ConfigManager.Instance.SaveSettings();
                Logger.Info("使用者指定 AI 客戶端: " + chosen);

                string note = "";
                var svc = Application.SocketService;
                if (svc != null && svc.IsRunning)
                {
                    var (locked, clientName, _, _) = svc.GetStatusSnapshot();
                    if (locked && !settings.IsClientAllowed(clientName))
                    {
                        svc.SwitchConnection();
                        note = "\n\n已中斷 " + (clientName ?? "unknown") + " 的連線（它不會自動重連）。\n"
                             + Label(chosen) + " 會在下一次呼叫工具時自動連上，燈號屆時才會出現白點。";
                    }
                    else if (locked)
                    {
                        note = "\n\n目前連線的 " + (clientName ?? "unknown") + " 就是允許的客戶端，連線維持不變。";
                    }
                    else
                    {
                        note = "\n\n目前沒有 AI 連線；" + Label(chosen) + " 會在下一次呼叫工具時連上。";
                    }
                }
                else
                {
                    note = "\n\n（MCP 服務目前未啟動，記得按「MCP 服務(開/關)」。）";
                }

                RibbonStatus.Refresh();
                TaskDialog.Show("指定 AI 客戶端", "已設定為：" + ButtonText(chosen).Replace("\n", " ") + note);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "指定 AI 客戶端失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
