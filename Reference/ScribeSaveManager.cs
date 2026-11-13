using System.IO;
using System.Linq;
using UnityEngine;
// using Lonize.Scribe;
// using Kernel.Save.Items;

using System.Collections.Generic;
using Lonize.Scribe;
using Lonize.Logging;
using Kernel.GameState;
using Kernel.Building;
using static Kernel.World.WorldChunkMeshGenerator;
using Kernel.World;
using Kernel.Storage;
using Kernel.UI;

/// <summary>
/// 放到场景里一个物体上；Awake 自动注册多态条目并加载；对外暴露 Save()/Load() 与 Items。
/// </summary>
namespace Kernel
{


    public sealed class ScribeSaveManager : MonoBehaviour
    {
        public const string DefaultSaveRootFolderName = "Saves";
        public const int CurrentSaveVersion = 3;

        public static ScribeSaveManager Instance { get; private set; }

        [Header("Save Settings")]
        private string fileName = "save.save";
        [SerializeField] private bool autoSaveOnQuit = true;

        public PolySaveData Data { get; private set; } = new PolySaveData();
        public string FilePath { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // —— 注册可反序列化的条目类型（白名单）——
            RegisterSaveItems();

            FilePath = BuildSavePath(fileName);
            // LoadOrCreate();
            Data = new PolySaveData();
            // GameDebug.Log($"[ScribeSaveManager] Ready. Path = {FilePath}");
            
        }
        private void Start()
        {
            // Kernel.Building.BuildingIdGenerator.InitializeFromSave();
        }
        private static void RegisterSaveItems()
        {
            ScribeBootstrap.InitializeDefaults();

            // 在这里注册你允许进存档的条目类型；新增类型时只需补一行
            PolymorphRegistry.Register<SaveBool>("Bool");
            PolymorphRegistry.Register<SaveInt>("Int");
            PolymorphRegistry.Register<SaveFloat>("Float");
            

            PolymorphRegistry.Register<SaveStatus>("StatusNames");
            PolymorphRegistry.Register<SaveAllBuildings>("AllBuildings");
            PolymorphRegistry.Register<SaveBlueprintLibrary>("BlueprintLibrary");
            PolymorphRegistry.Register<SaveMainToolbarBindings>("MainToolbarBindings");
            PolymorphRegistry.Register<SaveMapInfo>("WorldChunkInfo");
            PolymorphRegistry.Register<SavePlanetSector>(SavePlanetSector.SaveItemTypeId);
            // PolymorphRegistry.Register<SaveStorageInventory>("StorageInventory");

            CodecRegistry.Register(new DictStrEnumInt32Codec<KeyCode>());


            BuildingIDManager.RegisterSaveType();

            // PolymorphRegistry.Register<SaveControlCommand>("ControlCommandPref");
           

            // PolymorphRegistry.Register<SaveDictionaryStringKeyCode>("DictionaryStringKeyCode");
            // PolymorphRegistry.Register<SaveRefThing>("ref-thing");
            // ...
        }

        /// <summary>
        /// summary: 获取默认存档根目录（persistentDataPath/Saves）。
        /// </summary>
        /// <returns>存档根目录绝对路径</returns>
        private static string GetSaveRootPath()
        {
            return Path.Combine(Application.persistentDataPath, DefaultSaveRootFolderName);
        }

        /// <summary>
        /// summary: 基于默认存档根目录拼接具体存档文件路径。
        /// </summary>
        /// <param name="targetFileName">存档文件名</param>
        /// <returns>存档文件绝对路径</returns>
        private static string BuildSavePath(string targetFileName)
        {
            return Path.Combine(GetSaveRootPath(), targetFileName);
        }

        /// <summary>先写临时文件再替换主档，并返回真实的写入结果。</summary>
        public GameOperationResult Save(string fileName)
        {
            string tmp = null;
            try
            {
                string targetPath = string.IsNullOrEmpty(fileName) ? FilePath : BuildSavePath(fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                tmp = targetPath + ".tmp";
                using (var fs = File.Create(tmp))
                {
                    Scribe.InitSaving(fs, CurrentSaveVersion);
                    ScribeRefs.Clear();
                    var root = Data;
                    Scribe.Look(ref root);
                    Scribe.FinalizeWriting();
                }
                if (File.Exists(targetPath)) File.Replace(tmp, targetPath, targetPath + ".bak", true);
                else File.Move(tmp, targetPath);
                FilePath = targetPath;
                return GameOperationResult.Ok("保存完成：" + Path.GetFileName(FilePath));
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ScribeSaveManager] Save failed: {ex}");
                Scribe.Abort();
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch (System.Exception) { }
                bool io = ex is IOException || ex is System.UnauthorizedAccessException || ex is System.ArgumentException;
                return GameOperationResult.Fail(io ? GameOperationError.IoFailure : GameOperationError.InvalidData,
                    io ? "保存失败，请检查文件名、磁盘空间和写入权限。" : "保存失败：当前世界数据无法序列化。");
            }
        }

        public bool Load(string fileName) => LoadWithResult(fileName).Success;

        /// <summary>读取结果保留失败类别；世界建筑的异步恢复由会话流程等待。</summary>
        public GameOperationResult LoadWithResult(string fileName)
        {
            try
            {
                string targetPath = string.IsNullOrEmpty(fileName) ? FilePath : BuildSavePath(fileName);
                if (!File.Exists(targetPath)) return GameOperationResult.Fail(GameOperationError.NotFound, "找不到所选存档。");
                if (Path.GetExtension(targetPath).Equals(".tlv", System.StringComparison.OrdinalIgnoreCase))
                    return GameOperationResult.Fail(GameOperationError.UnsupportedVersion, "该存档版本不受支持，请使用当前版本的存档。");
                using (var fs = File.OpenRead(targetPath))
                {
                    Scribe.InitLoading(fs);
                    if (Scribe.FileVersion != CurrentSaveVersion)
                    {
                        Scribe.FinalizeLoading();
                        return GameOperationResult.Fail(GameOperationError.UnsupportedVersion, "该存档版本不受支持，请使用当前版本的存档。");
                    }
                    ScribeRefs.Clear();
                    PolySaveData loaded = null;
                    Scribe.Look(ref loaded);
                    ScribeRefs.ResolveAll();
                    Scribe.FinalizeLoading();
                    if (loaded?.Items == null || loaded.Items.Any(item => item == null)) return GameOperationResult.Fail(GameOperationError.InvalidData, "存档内容为空或已损坏。");
                    Data = loaded;
                    FilePath = targetPath;
                    return GameOperationResult.Ok("存档文件读取完成");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ScribeSaveManager] Load failed: {ex}");
                Scribe.Abort();
                bool io = ex is IOException && !(ex is InvalidDataException) || ex is System.UnauthorizedAccessException;
                return GameOperationResult.Fail(io ? GameOperationError.IoFailure : GameOperationError.InvalidData,
                    io ? "读取失败，请检查文件和读取权限。" : "存档内容无效或已损坏。");
            }
        }

        public void ResetSave()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
            Data = new PolySaveData();
        }

        private void LoadOrCreate()
        {
            var ok = Load(fileName);
            if (!ok) { Data = new PolySaveData(); Save(fileName); }
        }

        private void OnApplicationQuit()
        {
            if (autoSaveOnQuit && MainSceneStartUp.Instance != null && !GameSessionFlow.IsBusy && GameSessionFlow.HasPlayableSession && !GameSessionFlow.IsDemoSession)
                Colo.GameSaveController.Instance?.SaveGame(Path.GetFileName(FilePath));
        }

        // —— 便捷静态入口 ——
        public static void SaveNow(string fileName) => Instance?.Save(fileName);
        public static bool LoadNow(string fileName) => Instance != null && Instance.Load(fileName);

        // —— 简易操作：增删查 ——（可选）
        public void AddItem(ISaveItem item) => Data.Items.Add(item);
        public IEnumerable<T> GetItems<T>() where T : class, ISaveItem
        {
            foreach (var it in Data.Items) if (it is T t) yield return t;
        }
        public void RemoveItems<T>(System.Predicate<T> pred = null) where T : class, ISaveItem
        {
            Data.Items.RemoveAll(it => it is T t && (pred == null || pred(t)));
        }
    }
}
