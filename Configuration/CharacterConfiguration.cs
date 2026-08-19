using System.Text.Json.Serialization;
using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Math;

namespace KamiLib.Configuration;

public class CharacterConfiguration  : IPluginConfiguration {
    public int Version { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string CharacterWorld { get; set; } = string.Empty;
    public ulong ContentId { get; set; }
    public string? LodestoneId { get; set; }

    [JsonIgnore] public bool PurgeProfilePicture { get; set; }
    [JsonIgnore] public ISharedImmediateTexture? ProfilePicture { get; set; }

    public unsafe void UpdateCharacterData() {
        // AgentLobby.Instance() 是 CS 的 [Agent] 產生器版本，展開後逐字是
        // `agentModule == null ? null : (AgentLobby*)agentModule->GetAgentByInternalId(...)`
        // ——兩層都合法會回 null（UIModule 尚未建立、代理人尚未配置）。這支就是拿來判斷
        // 「現在登入了沒」的，換句話說它本來就會在還沒登入的時候被呼叫到。
        // 解參考 null 是 AccessViolation，而 AVE 在 .NET Core 是 corrupted-state exception，
        // try/catch 完全攔不到 ⇒ 只能在解參考之前擋。
        // 取一次本地指標、判空後重用（原本裸呼叫兩次），取不到就當作未登入、不動設定。
        var lobby = AgentLobby.Instance();
        if (lobby is null) return;

        if (lobby->IsLoggedIn) {
            CharacterName = PlayerState.Instance()->CharacterNameString;
            CharacterWorld = lobby->LobbyData.HomeWorldName.ToString();
        }
    }

    public void Draw(ITextureProvider textureProvider) {
        using var id = ImRaii.PushId(ContentId.ToString());

        DrawPortrait(textureProvider);
        ImGui.SameLine();
        DrawCharacterInfo();
    }

    private void DrawPortrait(ITextureProvider textureProvider) {
        using var portrait = ImRaii.Child("portrait", ImGuiHelpers.ScaledVector2(75.0f, 75.0f), false, ImGuiWindowFlags.NoInputs);
        if (!portrait) return;

        if (ProfilePicture is not null) {
            ImGui.Image(ProfilePicture.GetWrapOrEmpty().Handle, ImGuiHelpers.ScaledVector2(75.0f, 75.0f), new Vector2(0.25f, 0.10f), new Vector2(0.75f, 0.47f));
        }
        else {
            ImGui.Image(textureProvider.GetFromGameIcon(60042).GetWrapOrEmpty().Handle, ImGuiHelpers.ScaledVector2(75.0f, 75.0f));
        }
    }
    
    private void DrawCharacterInfo() {
        using var info = ImRaii.Child("info", new Vector2(ImGui.GetContentRegionAvail().X, 75.0f * ImGuiHelpers.GlobalScale), false, ImGuiWindowFlags.NoInputs);
        if (!info) return;

        ImGuiHelpers.ScaledDummy(5.0f);
        ImGui.TextUnformatted(CharacterName);
        ImGui.TextUnformatted(CharacterWorld);

        using (ImRaii.PushColor(ImGuiCol.Text, Vector4.One * 0.75f)) {
            ImGui.TextUnformatted(ContentId.ToString());
        }
    }
}