using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
using KamiLib.Extensions;
using KamiLib.Window.SelectionWindows;
using NetStone;

namespace KamiLib.Configuration;

public class ConfigurationManagerWindow : Window.Window, IDisposable {
    [PluginService] private ITextureProvider TextureProvider { get; set; } = null!;
    [PluginService] private INotificationManager NotificationManager { get; set; } = null!;
    [PluginService] private IPluginLog Log { get; set; } = null!;
    [PluginService] private IClientState ClientState { get; set; } = null!;
    [PluginService] private IChatGui ChatGui { get; set; } = null!;

    private List<CharacterConfiguration> characters = [];

    private CharacterConfiguration? selectedSourceCharacter;
    private List<CharacterConfiguration>? destinationCharacters;

    private readonly CancellationTokenSource cancellationTokenSource = new();

    // 預設關、不存檔（函式庫沒有自己的設定檔）：只在本插件這次載入期間有效，關閉視窗再開仍保留。
    // 關著時不建立 LodestoneClient（它一建立就會連 GitHub 抓解析規則）、不查角色、不下載頭像。
    private static bool lodestoneLookupEnabled;

    // ### 之後是 ImGui ID：只換顯示文字，之後再改譯名也不會讓視窗位置與釘選狀態跟著重置。
    public ConfigurationManagerWindow() : base("設定管理器###Configuration Manager", new Vector2(750.0f, 500.0f)) {
        AdditionalInfoTooltip = "把一個角色的插件設定複製到其他多個角色";

        TitleBarButtons.Add(new TitleBarButton {
            Icon = FontAwesomeIcon.Recycle,
            ShowTooltip = () => ImGui.SetTooltip(lodestoneLookupEnabled ? "重新載入角色頭像" : "重新載入角色頭像（只讀本機已存的頭像；要從 Lodestone 重抓請先勾選上方選項）"),
            IconOffset = new Vector2(1.5f, 2.0f),
            Click = _ => {
                var allowLodestone = lodestoneLookupEnabled;
                foreach (var character in characters) {
                    // 沒開查詢時不清本機頭像與 Lodestone ID：清了也抓不回來，只會讓頭像消失。
                    if (allowLodestone) {
                        character.PurgeProfilePicture = true;
                        character.LodestoneId = null;
                    }
                    character.ProfilePicture = null;
                }
                Task.Run(LoadCharacterPortraits, cancellationTokenSource.Token);
            },
            Priority = 2,
        });
    }
    
    public void Dispose() {
        cancellationTokenSource.Cancel();
    }

    public override void Load() {
        characters = PluginInterface.GetAllCharacterConfigurations().ToList();

        Task.Run(LoadCharacterPortraits, cancellationTokenSource.Token);
    }

    protected override void DrawContents() {
        DrawLodestoneToggle();

        var leftSize = new Vector2(ImGui.GetContentRegionAvail().X * 0.35f, ImGui.GetContentRegionAvail().Y);
        var centerSize = new Vector2(ImGui.GetContentRegionAvail().X * 0.3025f - ImGui.GetStyle().ItemSpacing.X * 2.0f, ImGui.GetContentRegionAvail().Y);
        var rightSize = new Vector2(ImGui.GetContentRegionAvail().X * 0.35f, ImGui.GetContentRegionAvail().Y);
        
        DrawSourceSelect(leftSize);
        ImGui.SameLine();
        DrawCenterPane(centerSize);
        ImGui.SameLine();
        DrawTargetSelect(rightSize);
    }
    
    private void DrawLodestoneToggle() {
        if (ImGui.Checkbox("向 Lodestone 查詢角色頭像", ref lodestoneLookupEnabled) && lodestoneLookupEnabled) {
            Task.Run(LoadCharacterPortraits, cancellationTokenSource.Token);
        }

        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("打開會把角色名與世界送到 Lodestone 查詢；台服角色多半查不到");
        }
    }

    private void DrawSourceSelect(Vector2 leftSize) {
        using var leftPane = ImRaii.Child("leftPane", leftSize);
        if (!leftPane) return;
        
        if (selectedSourceCharacter is null) {
            using var iconFont = ImRaii.PushFont(UiBuilder.IconFont);
            if (ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}##sourceCharacter", ImGui.GetContentRegionAvail())) {
                ShowCharacterSelectWindow();
            }
        }
        else {
            DrawSelectedCharacter(selectedSourceCharacter);
        }
    }
    
    private void DrawCenterPane(Vector2 centerSize) {
        using var centerPane = ImRaii.Child("centerPane", centerSize);
        if (!centerPane) return;
        
        ImGuiHelpers.ScaledDummy(150.0f);
        using var indent = ImRaii.PushIndent(10.0f);
        
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, selectedSourceCharacter is null ? KnownColor.Orange.Vector() : KnownColor.LimeGreen.Vector())) {
            ImGui.BulletText("選擇來源角色");
        }
        
        ImGuiHelpers.ScaledDummy(10.0f);

        using (var _ = ImRaii.PushColor(ImGuiCol.Text, destinationCharacters is null || destinationCharacters.Count == 0 ? KnownColor.Orange.Vector() : KnownColor.LimeGreen.Vector())) {
            ImGui.BulletText("選擇目標角色");
        }
        
        ImGuiHelpers.ScaledDummy(10.0f);
        
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, !(ImGui.GetIO().KeyShift && ImGui.GetIO().KeyCtrl) ? KnownColor.Orange.Vector() : KnownColor.LimeGreen.Vector())) {
            ImGui.BulletText("按住 Shift + Control");
        }

        if (selectedSourceCharacter is not null && destinationCharacters is not null && destinationCharacters.Count > 0) {
            ImGui.SetCursorPos(new Vector2(0.0f, ImGui.GetContentRegionMax().Y - 30.0f * ImGuiHelpers.GlobalScale));
            var keyComboHeld = ImGui.GetIO().KeyShift && ImGui.GetIO().KeyCtrl;

            using (var _ = ImRaii.Disabled(!keyComboHeld)) {
                if (ImGui.Button("複製設定", new Vector2(ImGui.GetContentRegionAvail().X, 30.0f * ImGuiHelpers.GlobalScale)) && keyComboHeld) {
                    CopySelectedConfigurations();
                }
            }
            
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                ImGui.SetTooltip("把來源角色的設定複製到所有目標角色\n此操作無法復原");
            }
        }
    }

    private void DrawTargetSelect(Vector2 rightSize) {
        using var rightPane = ImRaii.Child("rightPane", rightSize);
        if (!rightPane) return;
        
        if (destinationCharacters is null || destinationCharacters.Count == 0) {
            using var iconFont = ImRaii.PushFont(UiBuilder.IconFont);
            if (ImGui.Button($"{FontAwesomeIcon.Plus.ToIconString()}##destinationCharacter", ImGui.GetContentRegionAvail())) {
                ShowCharacterMultiSelectWindow();
            }
        }
        else {
            using (var charactersFrame = ImRaii.Child("charactersFrame", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 35.0f * ImGuiHelpers.GlobalScale))) {
                if (charactersFrame) {
                    foreach (var selectedTarget in destinationCharacters) {
                        selectedTarget.Draw(TextureProvider);
                    }
                }
            }
                    
            ImGui.SetCursorPosY(ImGui.GetContentRegionMax().Y - 30.0f * ImGuiHelpers.GlobalScale);
            if (ImGui.Button("變更##target", new Vector2(ImGui.GetContentRegionAvail().X, 30.0f * ImGuiHelpers.GlobalScale))) {
                ShowCharacterMultiSelectWindow();
            }
        }
    }

    private void ShowCharacterSelectWindow() {
        ParentWindowManager.AddWindow(new CharacterSelectionWindow(false, TextureProvider) {
            SingleSelectionCallback = selectedCharacter => {
                selectedSourceCharacter = selectedCharacter;
                if (selectedSourceCharacter != null && destinationCharacters is not null && destinationCharacters.Contains(selectedSourceCharacter)) {
                    destinationCharacters.Remove(selectedSourceCharacter);
                }
            },
            SelectionOptions = characters.Where(character => character.ContentId is not 0).ToList(),
        });
    }

    private void ShowCharacterMultiSelectWindow() {
        ParentWindowManager.AddWindow(new CharacterSelectionWindow(true, TextureProvider) {
            MultiSelectionCallback = selectedCharacters => {
                destinationCharacters = selectedCharacters;

                destinationCharacters.RemoveAll(character => {
                    if (character == selectedSourceCharacter) {
                        ChatGui.PrintError("來源角色與目標角色不能是同一個角色。", PluginInterface.InternalName, 45);
                        return true;
                    }

                    if (character.ContentId == ClientState.LocalContentId) {
                        ChatGui.PrintError("不能選擇目前登入中的角色作為目標。", PluginInterface.InternalName, 45);
                        return true;
                    }

                    return false;
                });
            },
            SelectionOptions = characters.Where(character => character.ContentId is not 0).ToList(),
        });
    }
    
    private async void LoadCharacterPortraits() {
        try {
            LodestoneClient? lodestoneClient = null;
            HttpClient? httpClient = null;
            if (lodestoneLookupEnabled) {
                lodestoneClient = await LodestoneClient.GetClientAsync();
                httpClient = new HttpClient();
            }

            foreach (var character in characters) {
                _ = Task.Run(() => TryLoadCharacterProfilePicture(lodestoneClient, httpClient, character));
            }
        }
        catch (Exception e) {
            Log.Error(e, "Exception retrieving character portraits.");
        }
    }

    private async void TryLoadCharacterProfilePicture(LodestoneClient? lodestoneClient, HttpClient? httpClient, CharacterConfiguration characterConfiguration) {
        try {
            if (characterConfiguration.ContentId is 0) return;

            var texture = await NetStoneExtensions.TryGetProfilePicture(httpClient, lodestoneClient, PluginInterface, TextureProvider, Log, characterConfiguration);
            characterConfiguration.ProfilePicture = texture;
        }
        catch (Exception e) {
            Log.Error(e, "Exception retrieving character portraits.");
        }
    }

    private void DrawSelectedCharacter(CharacterConfiguration character) {
        if (character.ProfilePicture is {} immediateTexture) {
            var texture = immediateTexture.GetWrapOrEmpty();
            var sizeRatio = ImGui.GetContentRegionAvail().X / texture.Width;
            
            ImGui.Image(texture.Handle, texture.Size * sizeRatio);
        }
        else {
            if (TextureProvider.GetFromGameIcon(60042).GetWrapOrDefault() is { Handle: var handle } unknownTexture) {
                var sizeRatio = ImGui.GetContentRegionAvail().X / unknownTexture.Width;
                ImGui.Image(handle, ImGuiHelpers.ScaledVector2(75.0f, 75.0f) * sizeRatio);
            }
        }
        
        ImGuiHelpers.ScaledDummy(5.0f);
        ImGui.TextUnformatted(character.CharacterName);
        ImGui.TextUnformatted(character.CharacterWorld);

        using (ImRaii.PushColor(ImGuiCol.Text, Vector4.One * 0.75f)) {
            ImGui.TextUnformatted(character.ContentId.ToString());
        }
            
        ImGui.SetCursorPosY(ImGui.GetContentRegionMax().Y - 30.0f * ImGuiHelpers.GlobalScale);
        if (ImGui.Button("變更##source", new Vector2(ImGui.GetContentRegionAvail().X, 30.0f * ImGuiHelpers.GlobalScale))) {
            ShowCharacterSelectWindow();
        }
    }

    private void CopySelectedConfigurations() {
        if (selectedSourceCharacter is null) {
            NotificationManager.AddNotification(new Notification {
                Type = NotificationType.Error,
                Content = "尚未選擇來源角色",
            });
            return;
        }

        if (destinationCharacters is null || destinationCharacters.Count == 0) {
            NotificationManager.AddNotification(new Notification {
                Type = NotificationType.Error,
                Content = "尚未選擇目標角色",
            });
            return;
        }
        
        foreach (var file in PluginInterface.GetCharacterDirectoryInfo(selectedSourceCharacter.ContentId).GetFiles()) {
            if (file is { Exists: true, Name: var fileName } && IsAllowedFileType(fileName) && !fileName.Contains("System", StringComparison.OrdinalIgnoreCase)) {
                foreach (var targetCharacter in destinationCharacters) {
                    var targetPath = PluginInterface.GetCharacterFileInfo(targetCharacter.ContentId, fileName).FullName;
                    file.CopyTo(targetPath, true);
                    Configuration.ForgetCachedFile(targetPath);
                }
            }
        }

        NotificationManager.AddNotification(new Notification {
            Type = NotificationType.Success,
            Content = "設定已複製完成",
        });
        
        Close();
    }

    public override void OnClose() {
        base.OnClose();
        
        ParentWindowManager.RemoveWindow(this);
    }

    private bool IsAllowedFileType(string fileName) {
        if (fileName.Contains("config.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.Contains("style.json", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}