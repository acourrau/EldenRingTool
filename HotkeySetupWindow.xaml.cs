using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static EldenRingTool.MainWindow;

namespace EldenRingTool
{
    public partial class HotkeySetupWindow : Window
    {
        public ObservableCollection<HotkeyAssignmentViewModel> HotkeyAssignments { get; private set; }

        public Dictionary<string, Modifiers> ModMap { get; private set; }
        public Dictionary<string, HOTKEY_ACTIONS> ActionMap { get; private set; }
        public Dictionary<string, Key> KeyMap { get; private set; }
        private MainWindow OwnerAsMainWindow => this.Owner as MainWindow;

        public List<HOTKEY_ACTIONS> AllActions { get; private set; }

        public HotkeySetupWindow(
            Dictionary<string, Modifiers> modMap,
            Dictionary<string, Key> keyMap,
            Dictionary<string, HOTKEY_ACTIONS> actionMap,
            string existingHotkeyLines = null)
        {
            InitializeComponent();

            ModMap = new Dictionary<string, Modifiers>(modMap);
            KeyMap = new Dictionary<string, Key>(keyMap);
            ActionMap = new Dictionary<string, HOTKEY_ACTIONS>(actionMap);

            AllActions = new List<HOTKEY_ACTIONS>(
                (HOTKEY_ACTIONS[])Enum.GetValues(typeof(HOTKEY_ACTIONS))
            );

            HotkeyAssignments = new ObservableCollection<HotkeyAssignmentViewModel>(
                AllActions.Select(a => new HotkeyAssignmentViewModel { Action = a })
            );

            DataContext = this;

            if (!string.IsNullOrWhiteSpace(existingHotkeyLines))
            {
                LoadAssignments(existingHotkeyLines);
            }
        }

        private void LoadAssignments(string linesStr)
        {
            var lines = linesStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                var spl = line.Split(' ');

                Key parsedKey = Key.None;
                ModifierKeys parsedMods = ModifierKeys.None;
                HOTKEY_ACTIONS parsedAction = 0;
                string parsedParam = null;

                for (int j = 0; j < spl.Length; j++)
                {
                    var s = spl[j];

                    if (ModMap.ContainsKey(s))
                    {
                        if (ModMap[s].HasFlag(Modifiers.CTRL)) parsedMods |= ModifierKeys.Control;
                        if (ModMap[s].HasFlag(Modifiers.SHIFT)) parsedMods |= ModifierKeys.Shift;
                        if (ModMap[s].HasFlag(Modifiers.ALT)) parsedMods |= ModifierKeys.Alt;
                        if (ModMap[s].HasFlag(Modifiers.WIN)) parsedMods |= ModifierKeys.Windows;
                    }
                    else if (KeyMap.ContainsKey(s))
                    {
                        parsedKey = KeyMap[s];
                    }
                    else if (ActionMap.ContainsKey(s))
                    {
                        parsedAction = ActionMap[s];
                        if (j + 1 < spl.Length)
                        {
                            parsedParam = spl[j + 1];
                            j++;
                        }
                    }
                }

                if (parsedAction != 0)
                {
                    var vm = HotkeyAssignments.FirstOrDefault(h => h.Action == parsedAction);
                    if (vm != null)
                    {
                        vm.HotkeyKey = parsedKey;
                        vm.HotkeyModifiers = parsedMods;
                        vm.Param = parsedParam;
                    }
                }
            }
        }

        private void AddHotkey_Click(object sender, RoutedEventArgs e)
        {
            HotkeyAssignments.Add(new HotkeyAssignmentViewModel());
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var vm = button?.DataContext as HotkeyAssignmentViewModel;
            if (vm != null)
            {
                HotkeyAssignments.Remove(vm);
            }
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            var tb = sender as TextBox;
            if (tb == null) return;

            var vm = tb.DataContext as HotkeyAssignmentViewModel;
            if (vm == null) return;

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                vm.HotkeyKey = Key.None;
                vm.HotkeyModifiers = ModifierKeys.None;
                tb.Text = "";
                return;
            }

            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;

            vm.HotkeyKey = key;
            vm.HotkeyModifiers = mods;

            tb.Text = vm.HotkeyString;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var missingParam = HotkeyAssignments
                .FirstOrDefault(h => h.HotkeyKey != Key.None && h.NeedsParam && string.IsNullOrWhiteSpace(h.Param));

            if (missingParam != null)
            {
                MessageBox.Show(
                    $"The action '{missingParam.ActionDisplay}' requires a parameter because a hotkey is assigned. Please enter it before saving.",
                    "Parameter Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            if (OwnerAsMainWindow != null)
            {
                OwnerAsMainWindow.registeredHotkeys.Clear();

                var lines = new List<string>();
                foreach (var assignment in HotkeyAssignments)
                {
                    if (assignment.HotkeyKey == Key.None)
                        continue;

                    var parts = new List<string>();

                    var modTokens = GetModifierTokens(assignment.HotkeyModifiers);
                    if (!string.IsNullOrWhiteSpace(modTokens))
                        parts.Add(modTokens);

                    parts.Add(GetKeyTokenFor(assignment.HotkeyKey));
                    parts.Add(assignment.Action.ToString());

                    if (assignment.NeedsParam)
                        parts.Add(assignment.Param);

                    lines.Add(string.Join(" ", parts));
                }

                string joined = string.Join(Environment.NewLine, lines);

                File.WriteAllText(MainWindow.hotkeyFile(), joined);
                OwnerAsMainWindow.parseHotkeys(joined);
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private string GetKeyTokenFor(Key key)
        {
            foreach (var kvp in KeyMap)
            {
                if (kvp.Value == key)
                    return kvp.Key;
            }
            return key.ToString();
        }

        private string GetModifierTokens(ModifierKeys mk)
        {
            var customMods = ModifierKeysToModifiers(mk);
            var tokens = new List<string>();

            foreach (var kvp in ModMap)
            {
                if (kvp.Value != Modifiers.NO_MOD && (customMods & kvp.Value) == kvp.Value)
                {
                    tokens.Add(kvp.Key);
                }
            }

            return string.Join(" ", tokens);
        }

        private Modifiers ModifierKeysToModifiers(ModifierKeys mk)
        {
            var m = Modifiers.NO_MOD;
            if ((mk & ModifierKeys.Control) != 0) m |= Modifiers.CTRL;
            if ((mk & ModifierKeys.Shift) != 0) m |= Modifiers.SHIFT;
            if ((mk & ModifierKeys.Alt) != 0) m |= Modifiers.ALT;
            if ((mk & ModifierKeys.Windows) != 0) m |= Modifiers.WIN;
            return m;
        }
    }

    public class HotkeyAssignmentViewModel : INotifyPropertyChanged
    {
        public HotkeyAction HotkeyAction { get; private set; }

        private string _hotkeyText;
        public string HotkeyText
        {
            get => HotkeyString;
            set
            {
                _hotkeyText = value;
                OnPropertyChanged(nameof(HotkeyText));
            }
        }

        public string ActionDisplay
        {
            get
            {
                if (HotkeyActionNames.Names.TryGetValue(Action, out var display))
                    return display;
                return Action.ToString(); 
            }
        }

        private Key _hotkeyKey = Key.None;
        public Key HotkeyKey
        {
            get => _hotkeyKey;
            set { _hotkeyKey = value; OnPropertyChanged(nameof(HotkeyKey)); OnPropertyChanged(nameof(HotkeyString)); }
        }

        private ModifierKeys _hotkeyModifiers = ModifierKeys.None;
        public ModifierKeys HotkeyModifiers
        {
            get => _hotkeyModifiers;
            set { _hotkeyModifiers = value; OnPropertyChanged(nameof(HotkeyModifiers)); OnPropertyChanged(nameof(HotkeyString)); }
        }

        public string HotkeyString
        {
            get
            {
                if (HotkeyKey == Key.None) return "";
                string text = "";
                if ((HotkeyModifiers & ModifierKeys.Control) != 0) text += "Ctrl+";
                if ((HotkeyModifiers & ModifierKeys.Alt) != 0) text += "Alt+";
                if ((HotkeyModifiers & ModifierKeys.Shift) != 0) text += "Shift+";
                if ((HotkeyModifiers & ModifierKeys.Windows) != 0) text += "Win+";
                text += HotkeyKey.ToString();
                return text;
            }
        }

        public HOTKEY_ACTIONS Action
        {
            get => HotkeyAction.actID;
            set
            {
                HotkeyAction.actID = value;
                OnPropertyChanged(nameof(Action));
                OnPropertyChanged(nameof(ActionDisplay));
                OnPropertyChanged(nameof(NeedsParam));
            }
        }

        public string Param
        {
            get => HotkeyAction.someParam;
            set { HotkeyAction.someParam = value; OnPropertyChanged(nameof(Param)); }
        }

        public bool NeedsParam => HotkeyAction.needsParam();

        public HotkeyAssignmentViewModel()
        {
            HotkeyAction = new HotkeyAction();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public static class HotkeyActionNames
    {
        public static readonly Dictionary<HOTKEY_ACTIONS, string> Names = new Dictionary<HOTKEY_ACTIONS, string>
        {
            { HOTKEY_ACTIONS.QUITOUT, "Quitout" },
            { HOTKEY_ACTIONS.NO_DEATH, "No Death" },
            { HOTKEY_ACTIONS.ONE_HP, "1 HP" },
            { HOTKEY_ACTIONS.MAX_HP, "Max HP" },
            { HOTKEY_ACTIONS.RUNE_ARC, "Rune Arc" },
            { HOTKEY_ACTIONS.INF_STAM, "Infinite Stamina" },
            { HOTKEY_ACTIONS.INF_FP, "Infinite FP" },
            { HOTKEY_ACTIONS.INF_CONSUM, "Infinite Consumables" },
            { HOTKEY_ACTIONS.TELEPORT_SAVE, "Teleport Save" },
            { HOTKEY_ACTIONS.TELEPORT_LOAD, "Teleport Load" },
            { HOTKEY_ACTIONS.GREAT_RUNE, "Select Great Rune" },
            { HOTKEY_ACTIONS.PHYSICK, "Mix Physick" },
            { HOTKEY_ACTIONS.ASHES, "Ashes of War" },
            { HOTKEY_ACTIONS.SPELLS, "Memorize Spells" },
            { HOTKEY_ACTIONS.QUICK_SAVE, "Quick Save" },
            { HOTKEY_ACTIONS.KILL_TARGET, "Kill Target" },
            { HOTKEY_ACTIONS.FREEZE_TARGET_HP, "Freeze Target HP" },
            { HOTKEY_ACTIONS.GAME_SPEED_25PC, "Game Speed 25%" },
            { HOTKEY_ACTIONS.GAME_SPEED_50PC, "Game Speed 50%" },
            { HOTKEY_ACTIONS.GAME_SPEED_75PC, "Game Speed 75%" },
            { HOTKEY_ACTIONS.GAME_SPEED_100PC, "Game Speed 100%" },
            { HOTKEY_ACTIONS.GAME_SPEED_150PC, "Game Speed 150%" },
            { HOTKEY_ACTIONS.GAME_SPEED_200PC, "Game Speed 200%" },
            { HOTKEY_ACTIONS.GAME_SPEED_300PC, "Game Speed 300%" },
            { HOTKEY_ACTIONS.GAME_SPEED_500PC, "Game Speed 500%" },
            { HOTKEY_ACTIONS.GAME_SPEED_1000PC, "Game Speed 1000%" },
            { HOTKEY_ACTIONS.DISABLE_AI, "Disable AI" },
            { HOTKEY_ACTIONS.NO_CLIP, "No Clip" },
            { HOTKEY_ACTIONS.POISE_VIEW, "Poise View" },
            { HOTKEY_ACTIONS.SOUND_VIEW, "Sound View" },
            { HOTKEY_ACTIONS.TARGETING_VIEW, "Targeting View" },
            { HOTKEY_ACTIONS.EVENT_VIEW, "Event Viewer" },
            { HOTKEY_ACTIONS.EVENT_STOP, "Disable Events" },
            { HOTKEY_ACTIONS.ALLOW_MAP_COMBAT, "Allow Map in Combat" },
            { HOTKEY_ACTIONS.TORRENT_ANYWHERE, "Torrent Anywhere" },
            { HOTKEY_ACTIONS.COL_MESH_A, "Collision Mesh A" },
            { HOTKEY_ACTIONS.COL_MESH_B, "Collision Mesh B" },
            { HOTKEY_ACTIONS.COL_MESH_CYCLE, "Cycle Collision Mesh" },
            { HOTKEY_ACTIONS.CHAR_MESH, "Character Mesh" },
            { HOTKEY_ACTIONS.HIDE_MODELS, "Hide Models" },
            { HOTKEY_ACTIONS.HITBOX_A, "Hitbox A" },
            { HOTKEY_ACTIONS.HITBOX_B, "Hitbox B" },
            { HOTKEY_ACTIONS.ALL_NO_DEATH, "No Death All Entities" },
            { HOTKEY_ACTIONS.DIE, "Die" },
            { HOTKEY_ACTIONS.SET_HP_LAST, "Set HP Last" },
            { HOTKEY_ACTIONS.REPEAT_ENEMY_ACTIONS, "Repeat Action" },
            { HOTKEY_ACTIONS.ONE_SHOT, "One-Shot" },
            { HOTKEY_ACTIONS.NO_GRAVITY, "No Gravity" },
            { HOTKEY_ACTIONS.NO_MAP_COL, "No Map Collision" },
            { HOTKEY_ACTIONS.TORRENT_NO_DEATH, "Torrent No Death" },
            { HOTKEY_ACTIONS.TORRENT_NO_GRAV, "Torrent No Gravity" },
            { HOTKEY_ACTIONS.TORRENT_NO_MAP_COL, "Torrent No Map Collision" },
            { HOTKEY_ACTIONS.TOGGLE_STATS_FULL, "Toggle Full Stats" },
            { HOTKEY_ACTIONS.TOGGLE_RESISTS, "Toggle Resistances" },
            { HOTKEY_ACTIONS.TOGGLE_DEFENSES, "Toggle Defenses" },
            { HOTKEY_ACTIONS.TOGGLE_COORDS, "Toggle Coordinates" },
            { HOTKEY_ACTIONS.ENABLE_TARGET_HOOK, "Enable Target Hook" },
            { HOTKEY_ACTIONS.FPS_30, "FPS 30" },
            { HOTKEY_ACTIONS.FPS_60, "FPS 60" },
            { HOTKEY_ACTIONS.FPS_120, "FPS 120" },
            { HOTKEY_ACTIONS.FPS_144, "FPS 144" },
            { HOTKEY_ACTIONS.FPS_240, "FPS 240" },
            { HOTKEY_ACTIONS.FPS_1000, "FPS 1000" },
            { HOTKEY_ACTIONS.FPS, "FPS Custom" },
            { HOTKEY_ACTIONS.FREE_CAMERA, "Free Camera" },
            { HOTKEY_ACTIONS.FREE_CAMERA_CONTROL, "Player Control in Free Cam" },
            { HOTKEY_ACTIONS.DISABLE_STEAM_INPUT_ENUM, "Stutter Fix" },
            { HOTKEY_ACTIONS.DISABLE_STEAM_ACHIEVEMENTS, "Steam Achievement Freeze Fix" },
            { HOTKEY_ACTIONS.MUTE_MUSIC, "Mute Music" },
            { HOTKEY_ACTIONS.ADD_SOULS, "Add Souls" },
            { HOTKEY_ACTIONS.YEET_FORWARD, "Yeet Forward" },
            { HOTKEY_ACTIONS.YEET_UP, "Yeet Up" },
            { HOTKEY_ACTIONS.YEET_DOWN, "Yeet Down" },
            { HOTKEY_ACTIONS.YEET_PLUS_X, "Yeet +X" },
            { HOTKEY_ACTIONS.YEET_MINUS_X, "Yeet -X" },
            { HOTKEY_ACTIONS.YEET_PLUS_Z, "Yeet +Z" },
            { HOTKEY_ACTIONS.YEET_MINUS_Z, "Yeet -Z" },
            { HOTKEY_ACTIONS.STAY_ON_TOP, "Stay On Top" },
        };
    }

}
