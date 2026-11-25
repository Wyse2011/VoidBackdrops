using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VoidBackdrops.Windows;

namespace VoidBackdrops;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string Command = "/voidbackdrops";
    private const string CommandShort = "/void";

    public Configuration Configuration { get; }
    public Controller Controller { get; }
    public ColorService Colors { get; }

    public NoteEditorWindow NoteEditor { get; }
    public FileDialogManager FileDialog { get; } = new();

    private readonly WindowSystem windowSystem = new("VoidBackdrops");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var penumbra  = new PenumbraBridge(PluginInterface);
        Controller    = new Controller(penumbra, Configuration);
        Colors        = new ColorService(penumbra, Controller, Configuration,
            System.IO.Path.Combine(PluginInterface.GetPluginConfigDirectory(), "colors"));

        // Sounds were removed: delete the plugin's copies of audio files from 1.9-1.11.
        try
        {
            var oldAudio = System.IO.Path.Combine(PluginInterface.GetPluginConfigDirectory(), "audio");
            if (System.IO.Directory.Exists(oldAudio)) System.IO.Directory.Delete(oldAudio, true);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Couldn't remove the old audio folder");
        }
        Controller.Colors = Colors;

        mainWindow = new MainWindow(this);
        NoteEditor = new NoteEditorWindow(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(NoteEditor);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Void Backdrops. \"/voidbackdrops <preset name>\" applies a preset, \"/voidbackdrops reset\" clears every slot, \"/voidbackdrops list\" lists presets, \"/voidbackdrops note <text>\" adds a quick note.",
        });
        CommandManager.AddHandler(CommandShort, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Void Backdrops (same as /voidbackdrops, takes the same options).",
        });

        PluginInterface.UiBuilder.Draw         += windowSystem.Draw;
        PluginInterface.UiBuilder.Draw         += FileDialog.Draw;
        Framework.Update                       += OnFrameworkUpdate;

        // Colors live in a temporary Penumbra mod that disappears on restart; bring them back.
        if (Configuration.Color.Enabled)
            Colors.MarkDirty(3);
        PluginInterface.UiBuilder.OpenMainUi   += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
    }

    public void Dispose()
    {
        Framework.Update                       -= OnFrameworkUpdate;
        Colors.Dispose();
        PluginInterface.UiBuilder.Draw         -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw         -= FileDialog.Draw;
        PluginInterface.UiBuilder.OpenMainUi   -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(Command);
        CommandManager.RemoveHandler(CommandShort);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            Colors.Update();
        }
        catch (Exception e)
        {
            Log.Error(e, "Color update failed");
        }
    }

    public void ToggleMainUi()
        => mainWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (args.Length == 0)
        {
            mainWindow.Toggle();
            return;
        }

        if (args.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            Controller.ResetAll();
            Print();
            return;
        }

        if (args.StartsWith("note ", StringComparison.OrdinalIgnoreCase) || args.Equals("note", StringComparison.OrdinalIgnoreCase))
        {
            var text = args.Length > 4 ? args[5..].Trim() : string.Empty;
            if (text.Length == 0)
            {
                Chat.PrintError("[Void] Usage: /voidbackdrops note <text>   (links the effects in your current setup)");
                return;
            }

            var note = new Note { Kind = NoteKind.Fix, Text = text };
            foreach (var (slot, effect) in Controller.CurrentEffects())
                note.Effects.Add(new EffectLink { Effect = effect, Slot = slot });
            Configuration.Notes.Add(note);
            Configuration.Save();
            Chat.Print($"[Void] Note saved, linked to {note.Effects.Count} effect(s) in your current setup. Edit it in /voidbackdrops > Notes.");
            return;
        }

        if (args.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Chat.Print(Configuration.Presets.Count == 0
                ? "[Void] No presets saved yet. Open /voidbackdrops to make one."
                : "[Void] Presets: " + string.Join(", ", Configuration.Presets.Select(p => p.Name)));
            return;
        }

        var preset = Controller.FindPreset(args);
        if (preset == null)
        {
            Chat.PrintError($"[Void] No preset named \"{args}\". Use /voidbackdrops list to see them.");
            return;
        }

        Controller.Apply(preset);
        Print();
    }

    private void Print()
    {
        if (Controller.StatusIsError) Chat.PrintError("[Void] " + Controller.Status);
        else Chat.Print("[Void] " + Controller.Status);
    }
}
