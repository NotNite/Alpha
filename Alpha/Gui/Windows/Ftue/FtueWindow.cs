using System.Numerics;
using Alpha.Gui.Windows.Ftue.Steps;
using Alpha.Services;
using ImGuiNET;

namespace Alpha.Gui.Windows.Ftue;

[Window("Welcome to Alpha!", SingleInstance = true, ShowInMenu = false)]
public class FtueWindow : Window {
    private List<FtueStep> steps;
    private int currentStep;

    private GuiService gui;
    private Config config;

    public FtueWindow(GuiService gui, Config config, GameDataService gameData, PathService path) {
        this.gui = gui;
        this.config = config;

        this.steps = [
            new IntroFtueStep(),
            new GameFtueStep(this.config, gameData),
            new AssetsFtueStep(path),
            new OutroFtueStep()
        ];

        this.IsOpen = true;
        this.Scene = GuiService.GuiScene.Ftue;
        this.Flags = ImGuiWindowFlags.NoDecoration;
    }

    public override void PreDraw() {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);

        var size = ImGui.GetIO().DisplaySize;
        this.MinSize = size;
        this.MaxSize = size;
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
    }

    protected override void Draw() {
        ImGui.TextUnformatted("Alpha Setup");
        ImGui.SameLine();

        var first = this.currentStep == 0;
        if (first) ImGui.BeginDisabled();
        if (ImGui.Button("<")) this.currentStep = Math.Max(0, this.currentStep - 1);
        if (first) ImGui.EndDisabled();
        ImGui.SameLine();

        var locked = this.steps[this.currentStep].IsLocked;
        if (locked) ImGui.BeginDisabled();
        if (ImGui.Button(">")) {
            if (this.currentStep == this.steps.Count - 1) {
                this.IsOpen = false;
                this.gui.SetScene(GuiService.GuiScene.Main);
                this.config.FtueComplete = true;
                this.config.Save();
                return;
            }
            this.currentStep = Math.Min(this.steps.Count - 1, this.currentStep + 1);
        }
        if (locked) ImGui.EndDisabled();

        ImGui.Separator();

        if (ImGui.BeginChild("##FtueContent", ImGui.GetContentRegionAvail())) {
            this.steps[this.currentStep].Draw();
            ImGui.EndChild();
        }
    }
}
