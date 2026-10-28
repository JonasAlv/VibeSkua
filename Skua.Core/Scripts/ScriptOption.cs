using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq.Expressions;
using System.Reflection;

namespace Skua.Core.Scripts;

public partial class ScriptOption : ObservableRecipient, IScriptOption, IOptionDictionary
{
    private ScriptOption(Lazy<IFlashUtil> flash)
    {
        _lazyFlash = flash;
    }

    public ScriptOption(
        Lazy<IFlashUtil> flash,
        ISettingsService settingsService)
    {
        _lazyFlash = flash;
        _settingsService = settingsService;
        _userLagKiller = _settingsService?.Get<bool>("UserLagKiller", false) ?? false;
        _lagKiller = _userLagKiller;
        _isInitializing = true;
        GetOptions();
        _pickupACDropsAndSendToBank = settingsService?.Get<bool>("VibeSkuffle.PickupACDropsAndSendToBank") ?? false;
        _rejectNonACDrops = settingsService?.Get<bool>("VibeSkuffle.RejectNonACDrops") ?? false;
        OptionDictionary = GenerateDictionary().ToImmutableDictionary();
        StrongReferenceMessenger.Default.Register<ScriptOption, ScriptStartedMessage, int>(this, (int)MessageChannels.ScriptStatus, ScriptStarted);
        StrongReferenceMessenger.Default.Register<ScriptOption, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, ScriptStopped);
        StrongReferenceMessenger.Default.Register<ScriptOption, MapChangedMessage, int>(this, (int)MessageChannels.GameEvents, MapChanged);
        _isInitializing = false;
    }

    private void ScriptStarted(ScriptOption recipient, ScriptStartedMessage message)
    {
        recipient._scriptRunning = true;
        recipient._userLagKiller = recipient._lagKiller;

        if (!recipient.AutoCrowdMode)
            return;

        try
        {
            var flash = recipient._lazyFlash.Value;
            if (flash != null)
            {
                string current = flash.GetGameObject<string>("litePreference.data.sCrowd") ?? "off";
                recipient._priorCrowdMode = current;
                recipient.ApplyCrowdMode(true);
            }
        }
        catch { }
    }

    private void MapChanged(ScriptOption recipient, MapChangedMessage message)
    {
        if (recipient.AutoCrowdMode && recipient._priorCrowdMode != null)
        {
            Task.Run(async () =>
            {
                await Task.Delay(500);
                recipient.ApplyCrowdMode(true);
            });
        }

        if (recipient.StreamerMode)
        {
            recipient.ApplyStreamerMode(true);
        }
    }

    private void ScriptStopped(ScriptOption recipient, ScriptStoppedMessage message)
    {
        recipient.AutoRelogin = false;
        recipient._scriptRunning = false;
        recipient.LagKiller = recipient._userLagKiller;
        recipient.AggroAllMonsters = false;
        recipient.AggroMonsters = false;
        recipient.SkipCutscenes = false;

        if (recipient.AutoCrowdMode)
        {
            try
            {
                string restore = !string.IsNullOrEmpty(recipient._priorCrowdMode)
                    ? recipient._priorCrowdMode
                    : "off";

                recipient.ApplyCrowdMode(restore == "on", restore);
                recipient._priorCrowdMode = null;
            }
            catch { }
        }
    }

    private void ApplyCrowdMode(bool enabled, string? modeOverride = null)
    {
        try
        {
            var flash = _lazyFlash.Value;
            if (flash == null || !flash.IsWorldLoaded)
                return;

            string mode = modeOverride ?? (enabled ? "on" : "off");
            flash.SetGameObject("litePreference.data.sCrowd", mode);
            flash.SetGameObject("world.rootClass.crowdOn", enabled);
            flash.CallGameFunction("world.rootClass.applyPlayerCache");
        }
        catch { }
    }

    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly ISettingsService _settingsService;
    private Dictionary<string, string>? _userOptions;
    private bool _isInitializing;

    private IFlashUtil Flash => _lazyFlash.Value;

    public ImmutableDictionary<string, Func<object>> OptionDictionary { get; }

    [ObservableProperty]
    private bool _attackWithoutTarget;

    private bool _acceptACDrops;

    public bool AcceptACDrops
    {
        get => _acceptACDrops;
        set => SetProperty(ref _acceptACDrops, value, true);
    }

    private bool _pickupACDropsAndSendToBank;
    public bool PickupACDropsAndSendToBank
    {
        get => _pickupACDropsAndSendToBank;
        set { if (SetProperty(ref _pickupACDropsAndSendToBank, value, true) && !_isInitializing) _settingsService?.Set("VibeSkuffle.PickupACDropsAndSendToBank", value); }
    }

    private bool _rejectNonACDrops;
    public bool RejectNonACDrops
    {
        get => _rejectNonACDrops;
        set { if (SetProperty(ref _rejectNonACDrops, value, true) && !_isInitializing) _settingsService?.Set("VibeSkuffle.RejectNonACDrops", value); }
    }

    [ObservableProperty]
    private bool _ignoreScriptRejectElse;

    private bool _acceptAllDrops;

    public bool AcceptAllDrops
    {
        get => _acceptAllDrops;
        set
        {
            if (SetProperty(ref _acceptAllDrops, value, true) && value)
                RejectAllDrops = false;
        }
    }

    private bool _rejectAllDrops;

    public bool RejectAllDrops
    {
        get => _rejectAllDrops;
        set
        {
            if (SetProperty(ref _rejectAllDrops, value, true) && value)
                AcceptAllDrops = false;
        }
    }

    [ObservableProperty]
    private bool _restPackets;

    [ObservableProperty]
    private bool _safeTimings = true;

    [CallBinding("skipCutscenes", UseValue = false, Get = false, HasSetter = true)]
    private bool _skipCutscenes;

    [ObservableProperty]
    private bool _privateRooms;

    [CallBinding("magnetize", UseValue = false, Get = false, HasSetter = true)]
    private bool _magnetise;

    private bool _lagKiller;
    private bool _userLagKiller;
    private bool _scriptRunning;

    public bool LagKiller
    {
        get => _lagKiller;
        set
        {
            if (!_scriptRunning && !_isInitializing && _settingsService != null)
            {
                _userLagKiller = value;
                _settingsService.Set("UserLagKiller", value);
            }

            try
            {
                var flash = _lazyFlash?.Value;
                if (flash != null)
                    flash.Call("killLag", value);
            }
            catch { }

            SetProperty(ref _lagKiller, value);
        }
    }

    [ObjectBinding("stage.frameRate", Get = false, HasSetter = true)]
    private int _setFPS = 24;

    [ObjectBinding("ui.mcFPS.visible", HasSetter = true)]
    private bool _showFPS = false;

    [ObservableProperty]
    private bool _aggroMonsters;

    [ObservableProperty]
    private bool _aggroAllMonsters;

    [CallBinding("infiniteRange", UseValue = false, Get = false, HasSetter = true)]
    private bool _infiniteRange;

    [ModuleBinding("DisableFX")]
    private bool _disableFX;

    [ObservableProperty]
    private bool _autoRelogin;

    [ObservableProperty]
    private bool _autoReloginAny;

    [ObservableProperty]
    private bool _retryRelogin = true;

    [ObservableProperty]
    private bool _safeRelogin;

    [ModuleBinding("DisableCollisions")]
    private bool _disableCollisions;

    [CallBinding("disableDeathAd", Get = false, HasSetter = true)]
    private bool _disableDeathAds;

    [ObservableProperty]
    private bool _headlessMode;

    partial void OnHeadlessModeChanged(bool value)
    {
        try
        {
            _lazyFlash.Value.Call("headless", value);
            if (value)
            {
                _lazyFlash.Value.Call("setGameObject", "stage.frameRate", 2);
                _lazyFlash.Value.Call("setGameObject", "stage.quality", "LOW");
                _lazyFlash.Value.Call("world.rootClass.mixer.soundTransform.volume", 0);
                Skua.Core.Utils.MemoryUtils.TrimWorkingSet();
            }
            else
            {
                _lazyFlash.Value.Call("setGameObject", "stage.frameRate", SetFPS > 2 ? SetFPS : 24);
                _lazyFlash.Value.Call("setGameObject", "stage.quality", "HIGH");
                _lazyFlash.Value.Call("world.rootClass.mixer.soundTransform.volume", 1);
            }
        }
        catch { }
    }

    private bool _streamerMode;
    public bool StreamerMode
    {
        get => _streamerMode;
        set
        {
            if (SetProperty(ref _streamerMode, value, true))
            {
                ApplyStreamerMode(value);
                if (!_isInitializing)
                    Save();
            }
        }
    }

    private string _originalName = string.Empty;
    private string _originalGuild = string.Empty;
    private bool _originalHidePlayers = false;

    [ObservableProperty]
    private string _streamerName = string.Empty;

    partial void OnStreamerNameChanged(string value)
    {
        if (StreamerMode)
        {
            ApplyStreamerMode(true);
        }
    }

    private void ApplyStreamerMode(bool enabled)
    {
        try
        {
            var flash = _lazyFlash.Value;
            if (flash == null)
                return;

            string hideName = StreamerName ?? string.Empty;
            CustomName = hideName;
            CustomGuild = hideName;

            if (enabled)
            {
                if (string.IsNullOrEmpty(_originalName))
                {
                    string realName = flash.GetGameObject<string>("loginInfo.strUsername")
                        ?? flash.GetGameObject<string>("world.myAvatar.dataLeaf.strUsername")
                        ?? flash.GetGameObject<string>("world.myAvatar.objData.strUsername")
                        ?? CustomName;
                    if (!string.IsNullOrEmpty(realName) && realName != "Hidden" && realName != "VibeSkuaUser" && realName != StreamerName)
                        _originalName = realName;
                }
                if (string.IsNullOrEmpty(_originalGuild))
                {
                    string realGuild = flash.GetGameObject<string>("world.myAvatar.objData.strGuildName")
                        ?? flash.GetGameObject<string>("world.myAvatar.pMC.pname.tg.text")
                        ?? CustomGuild;
                    if (!string.IsNullOrEmpty(realGuild) && realGuild != "Hidden" && realGuild != StreamerName)
                        _originalGuild = realGuild;
                }

                _originalHidePlayers = HidePlayers;
                HidePlayers = true;

                flash.Call("setGameObject", "world.rootClass.streamerName", hideName);
                flash.Call("modEnable", "StreamerMode");
            }
            else
            {
                flash.Call("modDisable", "StreamerMode");
                flash.Call("setGameObject", "world.rootClass.streamerName", "");

                CustomName = _originalName;
                CustomGuild = _originalGuild;
                HidePlayers = _originalHidePlayers;
                _originalName = string.Empty;
                _originalGuild = string.Empty;
            }
        }
        catch { }
    }

    [ObservableProperty]
    private bool _useFunctionBasedSkills;

    [ModuleBinding("OptimizePlayers")]
    private bool _optimizePlayers = true;

    [ModuleBinding("HidePlayers")]
    private bool _hidePlayers;

    [ObservableProperty]
    private bool _autoCrowdMode = true;

    private string? _priorCrowdMode;

    private string? _reloginServer = "Twilly";

    public string? ReloginServer
    {
        get => _reloginServer;
        set => SetProperty(ref _reloginServer, value, true);
    }

    [ObjectBinding("world.myAvatar.objData.strUsername", "world.rootClass.ui.mcPortrait.strName.text", "world.myAvatar.pMC.pname.ti.text", Get = false, HasSetter = true, Default = "string.Empty")]
    private string _customName = string.Empty;

    [ObjectBinding("world.myAvatar.pMC.pname.ti.textColor", Get = false, HasSetter = true, Default = "0xFFFFFF")]
    private int _nameColor = 0xFFFFFF;

    [ObjectBinding("world.myAvatar.pMC.pname.tg.text", Get = false, HasSetter = true, Default = "string.Empty")]
    private string _customGuild = string.Empty;

    [ObjectBinding("world.myAvatar.pMC.pname.tg.textColor", Get = false, HasSetter = true)]
    public int _guildColor;

    [ObjectBinding("world.WALKSPEED", Get = false, HasSetter = true, Default = "8")]
    private int _walkSpeed = 8;

    [ObservableProperty]
    private int _loadTimeout = 30000;

    [ObservableProperty]
    private int _huntDelay = 1000;

    [ObservableProperty]
    private int _huntBuffer = 1;

    [ObservableProperty]
    private int _maximumTries = 10;

    [ObservableProperty]
    private int _actionDelay = 800;

    [ObservableProperty]
    private int _privateNumber = 0;

    [ObservableProperty]
    private int _joinMapTries = 3;

    [ObservableProperty]
    private int _questAcceptAndCompleteTries = 30;

    [ObservableProperty]
    private int _reloginTries = 5;

    [ObservableProperty]
    private int _reloginTryDelay = 800;

    [ObservableProperty]
    private int _loginTimeout = 30000;

    [ObservableProperty]
    private HuntPriorities _HuntPriority = HuntPriorities.None;

    public void Save()
    {
        StringCollection saveOptions = new();
        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name is nameof(OptionDictionary))
                continue;
            string key = pi.Name;
            object? value = pi.GetValue(this);
            saveOptions.Add($"{key}={value}");
        }
        _settingsService.Set("UserOptions", saveOptions);
    }

    public void Reset()
    {
        if (_userOptions is null)
        {
            ResetToDefault();
            return;
        }

        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name is nameof(OptionDictionary))
                continue;
            if (_userOptions.ContainsKey(pi.Name))
            {
                if (pi.PropertyType.BaseType == typeof(Enum))
                {
                    pi.SetValue(this, Enum.Parse(pi.PropertyType, _userOptions[pi.Name], true));
                    continue;
                }
                pi.SetValue(this, Convert.ChangeType(_userOptions[pi.Name], pi.PropertyType));
            }
        }
    }

    public void ResetToDefault()
    {
        ScriptOption defaults = new(_lazyFlash);
        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name is nameof(OptionDictionary))
                continue;

            pi.SetValue(this, pi.GetValue(defaults), null);
        }
    }

    private void GetOptions()
    {
        StringCollection? userOptions = _settingsService.Get<StringCollection>("UserOptions");
        if (userOptions is null)
            return;
        _userOptions = new();
        foreach (string? option in userOptions)
        {
            if (string.IsNullOrEmpty(option))
                continue;
            string[] optionKeyValue = option.Split('=', StringSplitOptions.TrimEntries);
            _userOptions.Add(optionKeyValue[0], optionKeyValue[1]);
        }
        Reset();
    }

    private Dictionary<string, Func<object>> GenerateDictionary()
    {
        Dictionary<string, Func<object>> dict = new();
        foreach (PropertyInfo pi in GetType().GetProperties())
        {
            if (pi.Name == nameof(OptionDictionary) || pi.GetGetMethod() == null)
                continue;
            MethodCallExpression methodCall = Expression.Call(Expression.Constant(this), pi.GetGetMethod()!, null);
            UnaryExpression convertedExpression = Expression.Convert(methodCall, typeof(object));
            Func<object> function = Expression.Lambda<Func<object>>(convertedExpression).Compile();
            dict.Add(pi.Name, function);
        }
        return dict;
    }
}
