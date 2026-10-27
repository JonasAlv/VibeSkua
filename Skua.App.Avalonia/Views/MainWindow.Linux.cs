#if IS_LINUX
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.App.Avalonia.ViewModels;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Views;

public partial class MainWindow : Window
{
    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XSetInputFocus(IntPtr display, IntPtr w, int revert_to, IntPtr time);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    private const int WM_SKUA_JUMP_MAP = 0x0400 + 449;
    private const int WM_SKUA_SET_OPTION = 0x0400 + 450;
    private const int WM_SKUA_LOAD_SCRIPT = 0x0400 + 453;
    private const int WM_SKUA_ARMY_SCHEDULER = 0x0400 + 454;
    private const int WM_SKUA_ARMY_SCHEDULER_STOP = 0x0400 + 455;
    private const int WM_SKUA_CHECK_LOGIN = 0x0400 + 456;
    private const int WM_SKUA_JUMP_PLAYER = 0x0400 + 457;
    private const int WM_SKUA_JUMP_HOUSE = 0x0400 + 465;
    private const int WM_SKUA_LOGIN = 0x0400 + 447;
    private const int WM_SKUA_GRIDVIEW = 0x0400 + 444;
    private const int WM_SKUA_AUTO_ATTACK = 0x0400 + 459;
    private const int WM_SKUA_OPEN_BANK = 0x0400 + 460;

    private int _preThrottleFPS = -1;
    private string? _preThrottleQuality = null;
    private bool _isInGridView = false;

    private readonly HashSet<Key> _pressedKeys = new();

    private void InitializeLinuxInput()
    {
        AddHandler(InputElement.KeyDownEvent, OnLinuxKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyUpEvent, OnLinuxKeyUp, RoutingStrategies.Tunnel);
        Skua.App.Avalonia.Controls.WinFormsFlashHost.GameClicked -= OnLinuxGameClicked;
        Skua.App.Avalonia.Controls.WinFormsFlashHost.GameClicked += OnLinuxGameClicked;

        AddHandler(PointerPressedEvent, (s, e) =>
        {
            RestoreFps();
            IntPtr handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                try
                {
                    IntPtr disp = XOpenDisplay(IntPtr.Zero);
                    if (disp != IntPtr.Zero)
                    {
                        XSetInputFocus(disp, handle, 1 /* RevertToParent */, IntPtr.Zero);
                        XFlush(disp);
                        XCloseDisplay(disp);
                    }
                }
                catch { }
            }
        }, RoutingStrategies.Tunnel);
    }

    private void OnLinuxGameClicked()
    {
        if (AutoToggle != null)
            AutoToggle.IsChecked = false;
        if (JumpToggle != null)
            JumpToggle.IsChecked = false;
        MainMenuControl?.Close();
        Activate();
        if (FlashHost != null && FlashHost.IsVisible)
            FlashHost.Focus();
        else
            Focus();

        RestoreFps();
    }

    private void RestoreFps()
    {
        try
        {
            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            if (options.HeadlessMode) return;

            var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
            int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
            int targetFPS = _preThrottleFPS > 2 ? _preThrottleFPS : (currentFPS > 2 ? currentFPS : options.SetFPS);
            if (targetFPS <= 2)
                targetFPS = options.SetFPS > 0 ? options.SetFPS : 24;

            string? targetQuality = _preThrottleQuality;
            
            _preThrottleFPS = -1;
            _preThrottleQuality = null;

            if (targetFPS > 0)
                bot.Flash?.SetGameObject("stage.frameRate", targetFPS);
            if (!string.IsNullOrEmpty(targetQuality))
                bot.Flash?.SetGameObject("stage.quality", targetQuality);
        }
        catch { }
    }

    private void ReleaseAllPressedKeys()
    {
        foreach (var key in _pressedKeys)
        {
            string keyStr = key.ToString();
            string? symbol = TryMapSpecialKeyToSymbol(key);
            string logical = !string.IsNullOrEmpty(symbol) ? symbol : (keyStr.Length == 1 && char.IsLetter(keyStr[0]) ? keyStr.ToLowerInvariant() : keyStr);
            Skua.App.Avalonia.Controls.WinFormsFlashHost.ForwardKey(false, keyStr, logical, false, false, false);
        }
        _pressedKeys.Clear();
        Skua.App.Avalonia.Controls.WinFormsFlashHost.ForwardClearKeys();
    }

    private void OnLinuxKeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        if (FocusManager?.GetFocusedElement() is Button)
            FlashHost?.Focus();

        _pressedKeys.Add(e.Key);
        ForwardLinuxKeyEvent(true, e);
    }

    private void OnLinuxKeyUp(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key);

        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        ForwardLinuxKeyEvent(false, e);
    }

    private static void ForwardLinuxKeyEvent(bool down, global::Avalonia.Input.KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Alt);

        string physical = e.PhysicalKey != global::Avalonia.Input.PhysicalKey.None
            ? e.PhysicalKey.ToString()
            : e.Key.ToString();

        bool isNumPadDigit = e.Key is >= global::Avalonia.Input.Key.NumPad0 and <= global::Avalonia.Input.Key.NumPad9;
        string? symbol = (!string.IsNullOrEmpty(e.KeySymbol) && e.KeySymbol.Length == 1 && !char.IsControl(e.KeySymbol[0]))
            ? e.KeySymbol
            : TryMapSpecialKeyToSymbol(e.Key);

        string logical;
        if (!string.IsNullOrEmpty(symbol))
        {
            logical = (shift && !isNumPadDigit) ? ApplyShiftToSymbol(symbol) : symbol;
        }
        else
        {
            string keyStr = e.Key.ToString();
            logical = (keyStr.Length == 1 && char.IsLetter(keyStr[0]))
                ? (shift ? keyStr.ToUpperInvariant() : keyStr.ToLowerInvariant())
                : keyStr;
        }

        Skua.App.Avalonia.Controls.WinFormsFlashHost.ForwardKey(down, physical, logical, ctrl, shift, alt);
    }

    private static string? TryMapSpecialKeyToSymbol(global::Avalonia.Input.Key key) => key switch
    {
        global::Avalonia.Input.Key.D0 or global::Avalonia.Input.Key.NumPad0 => "0",
        global::Avalonia.Input.Key.D1 or global::Avalonia.Input.Key.NumPad1 => "1",
        global::Avalonia.Input.Key.D2 or global::Avalonia.Input.Key.NumPad2 => "2",
        global::Avalonia.Input.Key.D3 or global::Avalonia.Input.Key.NumPad3 => "3",
        global::Avalonia.Input.Key.D4 or global::Avalonia.Input.Key.NumPad4 => "4",
        global::Avalonia.Input.Key.D5 or global::Avalonia.Input.Key.NumPad5 => "5",
        global::Avalonia.Input.Key.D6 or global::Avalonia.Input.Key.NumPad6 => "6",
        global::Avalonia.Input.Key.D7 or global::Avalonia.Input.Key.NumPad7 => "7",
        global::Avalonia.Input.Key.D8 or global::Avalonia.Input.Key.NumPad8 => "8",
        global::Avalonia.Input.Key.D9 or global::Avalonia.Input.Key.NumPad9 => "9",
        global::Avalonia.Input.Key.Multiply => "*",
        global::Avalonia.Input.Key.Add => "+",
        global::Avalonia.Input.Key.Subtract => "-",
        global::Avalonia.Input.Key.Divide => "/",
        global::Avalonia.Input.Key.Decimal => ".",
        global::Avalonia.Input.Key.Space => " ",
        global::Avalonia.Input.Key.OemMinus => "-",
        global::Avalonia.Input.Key.OemPlus => "=",
        global::Avalonia.Input.Key.OemOpenBrackets => "[",
        global::Avalonia.Input.Key.OemCloseBrackets => "]",
        global::Avalonia.Input.Key.OemPipe or global::Avalonia.Input.Key.OemBackslash => "\\",
        global::Avalonia.Input.Key.OemSemicolon => ";",
        global::Avalonia.Input.Key.OemQuotes => "'",
        global::Avalonia.Input.Key.OemComma => ",",
        global::Avalonia.Input.Key.OemPeriod => ".",
        global::Avalonia.Input.Key.OemQuestion => "/",
        global::Avalonia.Input.Key.OemTilde => "`",
        _ => null
    };

    private static string ApplyShiftToSymbol(string symbol)
    {
        if (symbol.Length == 1 && char.IsLetter(symbol[0]))
            return symbol.ToUpperInvariant();

        return symbol switch
        {
            "1" => "!",
            "2" => "@",
            "3" => "#",
            "4" => "$",
            "5" => "%",
            "6" => "^",
            "7" => "&",
            "8" => "*",
            "9" => "(",
            "0" => ")",
            "-" => "_",
            "=" => "+",
            "[" => "{",
            "]" => "}",
            "\\" => "|",
            ";" => ":",
            "'" => "\"",
            "," => "<",
            "." => ">",
            "/" => "?",
            "`" => "~",
            _ => symbol
        };
    }

    private void OnFlashCall(string function, params object[] args)
    {
        if (function == "loaded" && !_isFlashLoaded)
        {
            _isFlashLoaded = true;
            Dispatcher.UIThread.Post(() =>
            {
                LoadingBar.IsVisible = false;
                FlashHost.IsVisible = !_options.HeadlessMode;
                HeadlessOverlay.IsVisible = _options.HeadlessMode;
                if (FlashHost != null && FlashHost.IsVisible && FocusManager?.GetFocusedElement() is not TextBox)
                    FlashHost.Focus();
                try
                {
                    FlashHost?.SetScaleMode(false);
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }
                try
                {
                    _options.Reset();
                }
                catch { }
            });
        }
    }

    public IntPtr HandleIpcMessage(uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == WM_SKUA_GRIDVIEW)
        {
            bool isGrid = wParam.ToInt32() == 1;
            _isInGridView = isGrid;

            Dispatcher.UIThread.Post(() =>
            {
                var topNav = this.FindControl<global::Avalonia.Controls.Control>("TopNavBorder");
                if (topNav != null)
                    topNav.IsVisible = !isGrid;
                
                var titleBar = this.FindControl<global::Avalonia.Controls.Control>("TitleBar");
                if (titleBar != null && !_isTitleBarPermanentlyHidden)
                    titleBar.IsVisible = !isGrid;
                
                this.MinWidth = 0;
                this.MinHeight = 0;

                if (FlashHost != null)
                    FlashHost.SetScaleMode(false);

                try
                {
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }

                if (isGrid)
                {
                    // In Grid View, all windows are foreground visible windows. Restore full FPS and quality.
                    try
                    {
                        var options = Ioc.Default.GetRequiredService<IScriptOption>();
                        if (!options.HeadlessMode)
                        {
                            var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                            int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                            int targetFPS = _preThrottleFPS > 2 ? _preThrottleFPS : (currentFPS > 2 ? currentFPS : options.SetFPS);
                            if (targetFPS <= 2)
                                targetFPS = options.SetFPS > 0 ? options.SetFPS : 30;

                            string? targetQuality = _preThrottleQuality;
                            _preThrottleFPS = -1;
                            _preThrottleQuality = null;

                            bot.Flash?.SetGameObject("stage.frameRate", targetFPS);
                            if (!string.IsNullOrEmpty(targetQuality))
                                bot.Flash?.SetGameObject("stage.quality", targetQuality);
                        }
                    }
                    catch { }
                }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_LOGIN)
        {
            Task.Run(() => 
            {
                var options = Ioc.Default.GetRequiredService<IScriptOption>();
                var servers = Ioc.Default.GetRequiredService<IScriptServers>();
                string targetServer = string.IsNullOrWhiteSpace(options.ReloginServer) ? "Twilly" : options.ReloginServer;
                servers.Relogin(targetServer);
            });
            return IntPtr.Zero;
        }
        else if (uMsg == 0x0400 + 448) // WM_SKUA_LOGOUT
        {
            Ioc.Default.GetRequiredService<IScriptServers>().Logout();
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_MAP)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string[] lines = System.IO.File.ReadAllLines(tempFile);
                        string map = lines.Length > 0 ? lines[0].Trim() : "";
                        string cell = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : "Enter";
                        string pad = "Spawn";
                        
                        if (cell.Contains(','))
                        {
                            var cellParts = cell.Split(',');
                            cell = cellParts[0].Trim();
                            if (cellParts.Length > 1) pad = cellParts[1].Trim();
                        }
                        
                        var mapService = Ioc.Default.GetRequiredService<IScriptMap>();
                        if (string.IsNullOrWhiteSpace(map) || mapService.Name.Equals(map.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                            mapService.Jump(cell, pad, false);
                        else
                            mapService.Join(map, cell, pad, true, false);
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_PLAYER)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump_player.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string targetPlayer = System.IO.File.ReadAllText(tempFile)?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(targetPlayer))
                            Ioc.Default.GetRequiredService<IScriptPlayer>().Goto(targetPlayer);
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_HOUSE)
        {
            Task.Run(() =>
            {
                try
                {
                    var player = Ioc.Default.GetRequiredService<IScriptPlayer>();
                    if (player.LoggedIn && !string.IsNullOrWhiteSpace(player.Username))
                        Ioc.Default.GetRequiredService<IScriptSend>().Packet($"%xt%zm%house%1%{player.Username}%");
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_LOAD_SCRIPT)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_script.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string targetScript = System.IO.File.ReadAllText(tempFile);
                        if (!string.IsNullOrWhiteSpace(targetScript) && System.IO.File.Exists(targetScript))
                        {
                            var sm = Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel>();
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (sm.LoadScriptCommand.CanExecute(targetScript))
                                    sm.LoadScriptCommand.Execute(targetScript);
                            });
                        }
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_SET_OPTION)
        {
            int optionId = wParam.ToInt32();
            bool value = lParam.ToInt32() == 1;
            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            switch (optionId)
            {
                case 1: options.LagKiller = value; break;
                case 2: options.HeadlessMode = value; break;
                case 3: options.HidePlayers = value; break;
                case 4: options.DisableFX = value; break;
                case 5: options.InfiniteRange = value; break;
                case 6: options.Magnetise = value; break;
                case 7: options.SkipCutscenes = value; break;
                case 8: options.UseFunctionBasedSkills = value; break;
                case 9: options.StreamerMode = value; break;
                case 10: options.AggroMonsters = value; break;
                case 99:
                    var sm = Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel>();
                    var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                    if (value)
                    {
                        if (!sm.ScriptManager.ScriptRunning)
                        {
                            if (sm.ToggleScriptCommand.CanExecute(null))
                                sm.ToggleScriptCommand.Execute(null);
                        }
                    }
                    else
                    {
                        if (scheduler.IsRunningQueue && scheduler.StopQueueCommand.CanExecute(null))
                            scheduler.StopQueueCommand.Execute(null);
                        else if (sm.ScriptManager.ScriptRunning && sm.ToggleScriptCommand.CanExecute(null))
                            sm.ToggleScriptCommand.Execute(null);
                    }
                    break;
            }
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_ARMY_SCHEDULER)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_playlist.json");
                    if (System.IO.File.Exists(tempFile))
                    {
                        var data = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel.SavedScriptItem>>(System.IO.File.ReadAllText(tempFile));
                        var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                        Dispatcher.UIThread.Post(() =>
                        {
                            scheduler.ScriptQueue.Clear();
                            if (data != null)
                            {
                                foreach (var item in data)
                                {
                                    if (System.IO.File.Exists(item.Path))
                                    {
                                        var vm = new Skua.Shared.Avalonia.ViewModels.ScriptItemViewModel(item.Path) { Id = item.Id };
                                        if (!string.IsNullOrEmpty(item.Name)) vm.Name = item.Name;
                                        scheduler.ScriptQueue.Add(vm);
                                    }
                                }
                            }
                            if (scheduler.ScriptQueue.Count > 0 && !scheduler.IsRunningQueue)
                            {
                                if (scheduler.StartQueueCommand.CanExecute(null))
                                    scheduler.StartQueueCommand.Execute(null);
                            }
                        });
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_ARMY_SCHEDULER_STOP)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                if (scheduler.StopQueueCommand.CanExecute(null))
                    scheduler.StopQueueCommand.Execute(null);
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_CHECK_LOGIN)
        {
            var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
            return new IntPtr(bot.Player.LoggedIn ? 1 : 0);
        }
        else if (uMsg == 0x0400 + 458) // WM_SKUA_ACCEPT_QUEST
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_accept_quest.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string[] lines = System.IO.File.ReadAllLines(tempFile);
                        string questIdRaw = lines.Length > 0 ? lines[0].Trim() : "";
                        string rewardIdRaw = lines.Length > 1 ? lines[1].Trim() : "-1";
                        int rewardId = int.TryParse(rewardIdRaw, out int rId) ? rId : -1;
                        if (int.TryParse(questIdRaw, out int questId) && questId > 0)
                        {
                            var quest = Ioc.Default.GetRequiredService<IScriptQuest>();
                            quest.RegisterQuests((questId, rewardId));
                        }
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_AUTO_ATTACK)
        {
            bool start = wParam.ToInt32() == 1;
            Dispatcher.UIThread.Post(() =>
            {
                var autoVm = Ioc.Default.GetRequiredService<AutoViewModel>();
                if (start)
                    StrongReferenceMessenger.Default.Send<StartAutoAttackMessage>();
                else
                    StrongReferenceMessenger.Default.Send<StopAutoMessage>();
            });
            return IntPtr.Zero;
        }
        else if (uMsg == 0x0400 + 452) // WM_SKUA_THROTTLE
        {
            bool throttle = wParam.ToInt32() == 1;
            if (_isInGridView && throttle)
            {
                // In Grid View, all windows are treated as foreground windows - never throttle!
                return IntPtr.Zero;
            }

            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            if (!options.HeadlessMode)
            {
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                try
                {
                    if (throttle)
                    {
                        if (_preThrottleFPS <= 0)
                        {
                            int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                            if (currentFPS > 2)
                                _preThrottleFPS = currentFPS;
                            string? currentQuality = bot.Flash?.GetGameObject<string>("stage.quality");
                            if (!string.IsNullOrEmpty(currentQuality))
                                _preThrottleQuality = currentQuality;
                        }

                        bot.Flash?.SetGameObject("stage.frameRate", 2);
                        bot.Flash?.SetGameObject("stage.quality", "LOW");
                        Skua.Core.Utils.MemoryUtils.TrimWorkingSet();
                    }
                    else
                    {
                        RestoreFps();
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_OPEN_BANK)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Ioc.Default.GetRequiredService<IScriptBank>().Open();
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == HotKeys.WM_SKUA_EXECUTE_HOTKEY)
        {
            int actionId = wParam.ToInt32();
            string? actionName = HotKeys.GetActionNameById(actionId);
            if (!string.IsNullOrEmpty(actionName))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (actionName == "OpenBank")
                            Ioc.Default.GetRequiredService<IScriptBank>().Open();
                        else
                            HotKeys.ExecuteHotkeyActionDirect(actionName);
                    }
                    catch { }
                });
            }
            return IntPtr.Zero;
        }
        else if (uMsg == HotKeys.WM_SKUA_FOCUS)
        {
            bool focused = wParam != IntPtr.Zero;
            Dispatcher.UIThread.Post(() =>
            {
                ReleaseAllPressedKeys();
                Skua.App.Avalonia.Controls.WinFormsFlashHost.ForwardFocus(focused);
                if (focused)
                {
                    RestoreFps();
                }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == HotKeys.WM_SKUA_RELOAD_HOTKEYS)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Ioc.Default.GetRequiredService<IHotKeyService>().Reload();
                }
                catch { }
            });
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

}
#endif
