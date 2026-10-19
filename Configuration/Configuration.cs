using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Plugin;
using Dalamud.Utility;
using KamiLib.Classes;

namespace KamiLib.Configuration;

public static class Configuration {
    public static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true,
        IncludeFields = true,
    };

    // 每次登入都整批重讀角色設定檔：讀過的內容記在記憶體裡，檔案的修改時間與大小都沒變才沿用，
    // 所以外部改檔、複製設定也會照常讀到新內容。經 SaveFile 寫入時當場換成寫進去的內容。
    private readonly record struct CachedFileText(DateTime LastWriteTimeUtc, long Length, string Text);

    private static readonly ConcurrentDictionary<string, CachedFileText> FileTextCache = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Load a configuration file for a specific character.
    /// </summary>
    /// <param name="pluginInterface">Dalamud Plugin Interface used to find the correct directory.</param>
    /// <param name="contentId">ContentId of the desired character file.</param>
    /// <param name="fileName">Specific name of the file you wish to load.</param>
    /// <param name="createFunc">A function to create that file if loading fails.</param>
    /// <typeparam name="T">Type of file to load, needed for serialization.</typeparam>
    /// <returns>Either a loaded config file, or a new'd file of type T.</returns>
    public static T LoadCharacterFile<T>(this IDalamudPluginInterface pluginInterface, ulong contentId, string fileName, Func<T> createFunc) where T : new() 
        => LoadFile(pluginInterface, pluginInterface.GetCharacterFileInfo(contentId, fileName).FullName, createFunc);

    /// <summary>
    /// Load a configuration file from the plugin specific config directory.
    /// </summary>
    /// <param name="pluginInterface">Dalamud Plugin Interface used to find the correct directory.</param>
    /// <param name="fileName">Specific name of the file you wish you load.</param>
    /// <param name="createFunc">A function to create the file if loading fails.</param>
    /// <typeparam name="T">Type of file to load, needed for serialization.</typeparam>
    /// <returns>Either a loaded config file, or a new'd file of type T.</returns>
    public static T LoadConfigFile<T>(this IDalamudPluginInterface pluginInterface, string fileName, Func<T> createFunc) where T : new() 
        => LoadFile(pluginInterface, Path.Combine(pluginInterface.ConfigDirectory.FullName, fileName), createFunc);

    /// <summary>
    /// Save a configuration file for a specific character.
    /// </summary>
    /// <param name="pluginInterface">Dalamud Plugin Interface used to find the correct directory.</param>
    /// <param name="contentId">ContentId of the desired character file.</param>
    /// <param name="fileName">Specific name of the file you wish to save.</param>
    /// <param name="file">The object to write to a file.</param>
    /// <typeparam name="T">Type of file to load, needed for serialization.</typeparam>
    public static void SaveCharacterFile<T>(this IDalamudPluginInterface pluginInterface, ulong contentId, string fileName, T file) 
        => SaveFile(pluginInterface, pluginInterface.GetCharacterFileInfo(contentId, fileName).FullName, file);

    /// <summary>
    /// Save a configuration file to the plugin specific config directory.
    /// </summary>
    /// <param name="pluginInterface">Dalamud Plugin Interface used to find the correct directory.</param>
    /// <param name="fileName">Specific name of the file you wish to save.</param>
    /// <param name="file">The object to write to a file.</param>
    /// <typeparam name="T">Type of file to load, needed for serialization.</typeparam>
    public static void SaveConfigFile<T>(this IDalamudPluginInterface pluginInterface, string fileName, T file)
        => SaveFile(pluginInterface, Path.Combine(pluginInterface.GetPluginConfigDirectory(), fileName), file);

    private static T LoadFile<T>(IDalamudPluginInterface pluginInterface, string filePath, Func<T> createFunc) where T : new() {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo is { Exists: true }) {
            try {
                var fileText = ReadFileText(fileInfo);
                var dataObject = JsonSerializer.Deserialize<T>(fileText, SerializerOptions);

                // If deserialize result is null, create a new instance instead and save it.
                if (dataObject is null) {
                    dataObject = createFunc();
                    SaveFile(pluginInterface, filePath, dataObject);
                }
                
                return dataObject;
            }
            catch (Exception e) {
                // If there is any kind of error loading the file, generate a new one instead and save it.
                var localLog = pluginInterface.Create<LogWrapper>();
                localLog?.Log.Error(e, $"Error trying to load file {filePath}, creating a new one instead.");
                
                SaveFile(pluginInterface, filePath, createFunc());
            }
        }

        var newFile = createFunc();
        SaveFile(pluginInterface, filePath, newFile);
        
        return newFile;
    }

    private static void SaveFile<T>(IDalamudPluginInterface pluginInterface, string filePath, T file) {
        try {
            var fileText = JsonSerializer.Serialize(file, file!.GetType(), SerializerOptions);
            FilesystemUtil.WriteAllTextSafe(filePath, fileText);
            RememberWrittenFile(filePath, fileText);
        }
        catch (Exception e) {
            ForgetCachedFile(filePath);
            var localLog = pluginInterface.Create<LogWrapper>();
            localLog?.Log.Error(e, $"Error trying to save file {filePath}");
        }
    } 

    /// <param name="fileInfo">剛判過 Exists 的 FileInfo：修改時間與大小沿用那一次查詢，不再多問一次檔案系統。</param>
    private static string ReadFileText(FileInfo fileInfo) {
        var lastWrite = fileInfo.LastWriteTimeUtc;
        var length = fileInfo.Length;

        if (FileTextCache.TryGetValue(fileInfo.FullName, out var cached) && cached.LastWriteTimeUtc == lastWrite && cached.Length == length) {
            return cached.Text;
        }

        var text = File.ReadAllText(fileInfo.FullName);
        FileTextCache[fileInfo.FullName] = new CachedFileText(lastWrite, length, text);
        return text;
    }

    private static void RememberWrittenFile(string filePath, string text) {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Exists) {
            FileTextCache[fileInfo.FullName] = new CachedFileText(fileInfo.LastWriteTimeUtc, fileInfo.Length, text);
        }
        else {
            FileTextCache.TryRemove(fileInfo.FullName, out _);
        }
    }

    /// <summary>不經 SaveFile 改動角色檔的地方（例如複製設定）改完要呼叫。</summary>
    internal static void ForgetCachedFile(string filePath)
        => FileTextCache.TryRemove(Path.GetFullPath(filePath), out _);

    internal static DirectoryInfo GetCharacterDirectoryInfo(this IDalamudPluginInterface pluginInterface, ulong contentId) {
        var directoryInfo = new DirectoryInfo(Path.Combine(pluginInterface.ConfigDirectory.FullName, contentId.ToString()));
        
        if (directoryInfo is { Exists: false } && contentId is not 0) {
            directoryInfo.Create();
        }
        
        directoryInfo.Refresh();

        return directoryInfo;
    }

    public static FileInfo GetCharacterFileInfo(this IDalamudPluginInterface pluginInterface, ulong contentId, string fileName) 
        => new(Path.Combine(pluginInterface.GetCharacterDirectoryInfo(contentId).FullName, fileName));

    internal static IEnumerable<CharacterConfiguration> GetAllCharacterConfigurations(this IDalamudPluginInterface pluginInterface) 
        => pluginInterface.GetAllCharacterContentIds().Select(pluginInterface.LoadCharacterConfiguration);

    private static CharacterConfiguration LoadCharacterConfiguration(this IDalamudPluginInterface pluginInterface, ulong contentId) {
        var loadedConfiguration = pluginInterface.LoadCharacterFile(contentId, "System.config.json", () => CreateNewCharacterConfig(contentId));
        if (loadedConfiguration is { Version: not 2, ContentId: 0 }) {
            loadedConfiguration.Version = 2;
            loadedConfiguration.ContentId = contentId;
            pluginInterface.SaveCharacterConfiguration(loadedConfiguration);
        }

        return loadedConfiguration;
    }
    
    /// <summary>
    /// 把 <see cref="CharacterConfiguration"/> 自己的欄位寫進該角色的 System.config.json，檔案裡其他的鍵原樣保留。
    /// </summary>
    /// <remarks>
    /// 那個檔是插件以自己的衍生型別（例如 DailyDuty／SortaKinda 的 SystemConfig）存的，
    /// 以基底型別整份寫回會丟掉衍生欄位。檔案不存在或不是 JSON 物件時才整份寫；讀檔失敗就不寫。
    /// </remarks>
    internal static void SaveCharacterConfiguration(this IDalamudPluginInterface pluginInterface, CharacterConfiguration configuration) {
        var filePath = pluginInterface.GetCharacterFileInfo(configuration.ContentId, "System.config.json").FullName;

        JsonObject? existing;
        try {
            existing = File.Exists(filePath) ? JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject : null;
        }
        catch (JsonException) {
            existing = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            var localLog = pluginInterface.Create<LogWrapper>();
            localLog?.Log.Error(e, $"Error trying to read file {filePath}, leaving it unchanged.");
            return;
        }

        if (existing is null) {
            SaveFile(pluginInterface, filePath, configuration);
            return;
        }

        var baseFields = JsonSerializer.SerializeToNode(configuration, typeof(CharacterConfiguration), SerializerOptions)!.AsObject();
        foreach (var (key, value) in baseFields) {
            existing[key] = value?.DeepClone();
        }

        SaveFile(pluginInterface, filePath, existing);
    }

    private static IEnumerable<ulong> GetAllCharacterContentIds(this IDalamudPluginInterface pluginInterface) {
        if (pluginInterface.ConfigDirectory is { Exists: true } directoryInfo) {
            foreach (var directory in directoryInfo.EnumerateDirectories()) {
                if (ulong.TryParse(directory.Name, out var contentId)) {
                    yield return contentId;
                }
            }
        }
    }

    private static CharacterConfiguration CreateNewCharacterConfig(ulong contentId) 
        => new() {
            CharacterName = "未知角色",
            ContentId = contentId,
            Version = 2,
            CharacterWorld = "未知 World",
        };
}