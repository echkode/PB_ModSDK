using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;

using UnityEngine;
using Sirenix.OdinInspector;

#if UNITY_EDITOR
using System;
using System.Reflection;
using PhantomBrigade.ModTools;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
#endif

namespace PhantomBrigade.SDK.ModTools
{
    using Area;
    using Data;
    using Mods;

    [ExecuteInEditMode]
    [LabelWidth (160f), HideMonoScript]
    public class DataManagerMod : MonoBehaviour
    {
        #if UNITY_EDITOR

        static DataManagerMod ins;
        static bool initialized;
        static bool loadedOnce;

        const string filenameMain = "project";
        const string extensionYAML = ".yaml";

        void Update ()
        {
            if (!initialized)
            {
                initialized = true;
                ins = this;
                // gameObject.name = "ModManager";
            }

            if (!loadedOnce)
                LoadAll ();
        }

        [HideReferenceObjectPicker]
        public class ModToolsSourcePath
        {
            [HorizontalGroup (20f)]
            [PropertySpace (2f)]
            [OnValueChanged (nameof(Reload))]
            [HideLabel]
            public bool enabled = true;

            [HorizontalGroup]
            [FolderPath (UseBackslashes = false, RequireExistingPath = true, AbsolutePath = true)]
            [HideLabel]
            public string path;

            static void Reload ()
            {
                DataManagerMod.LoadCustomSourceDirectories ();
                DataManagerMod.LoadAll ();
                if (DataManagerMod.modSelected != null && !DataManagerMod.mods.ContainsKey (DataManagerMod.modSelected.id))
                {
                    DataManagerMod.modSelectedID = "";
                }
            }
        }

        public class ModToolsSettings
        {
            [LabelText ("Custom project folders")]
            [PropertyTooltip ("Use this list to save directories where you store mod source files. By default, the game only loads mod projects from PhantomBrigade/ModsSource.")]
            [ListDrawerSettings (ShowPaging = false, CustomAddFunction = "@new ModToolsSourcePath ()")]
            public List<ModToolsSourcePath> customSourceDirectories = new List<ModToolsSourcePath> ();
        }

        [ShowInInspector]
        [PropertyOrder (OdinGroup.Order.Title)]
        [Title ("Mod project manager", TitleAlignment = TitleAlignments.Centered)]
        [PropertyTooltip ("The folder where mod projects are stored. If this folder does not exist, it will be created when you add your first mod.")]
        [PropertySpace (0f, 3f)]
        [LabelText ("Default source folder"), LabelWidth (160f), ReadOnly, ElidedPath]
        public static string folderPathProjectsDefault;

        #region Settings

        static readonly List<string> folderPathsProjects = new List<string> ();

        static void InitializeSettings ()
        {
            var settingsPath = DataPathHelper.GetCombinedCleanPath (DataPathHelper.GetApplicationFolder (), "ConfigsModTools");
            settings = UtilitiesYAML.LoadDataFromFile<ModToolsSettings> (settingsPath, "user_settings.yaml", false, false);
            settings ??= new ModToolsSettings
            {
                customSourceDirectories = new List<ModToolsSourcePath>()
            };
            settings.customSourceDirectories ??= new List<ModToolsSourcePath>();

            folderPathProjectsDefault = DataPathHelper.GetCombinedCleanPath (DataPathHelper.GetUserFolder (), "ModsSource");
        }

        static void LoadCustomSourceDirectories ()
        {
            folderPathsProjects.Clear ();
            folderPathsProjects.Add (folderPathProjectsDefault);

            foreach (var p in settings.customSourceDirectories)
            {
                if (p == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty (p.path))
                {
                    continue;
                }
                if (!p.enabled)
                {
                    continue;
                }
                if (!Directory.Exists (p.path))
                {
                    continue;
                }
                folderPathsProjects.Add (p.path);
            }
        }

        [FoldoutGroup (OdinGroup.Name.Settings, OdinGroup.Order.Settings)]
        [HorizontalGroup (OdinGroup.Name.SettingsButtons, Order = OdinGroup.SubOrder.SettingsButtons)]
        [Button (SdfIconType.ArrowUpCircle, IconAlignment.LeftOfText, ButtonHeight = 32, Name = "Load settings")]
        static void LoadSettings ()
        {
            InitializeSettings ();
            LoadCustomSourceDirectories ();
            modSetup.pathSource = folderPathsProjects.FirstOrDefault ();
        }

        [HorizontalGroup (OdinGroup.Name.SettingsButtons)]
        [Button (SdfIconType.ArrowDownCircle, IconAlignment.LeftOfText, ButtonHeight = 32, Name = "Save settings")]
        static void SaveSettings ()
        {
            settings ??= new ModToolsSettings
            {
                customSourceDirectories = new List<ModToolsSourcePath>()
            };
            var settingsPath = DataPathHelper.GetCombinedCleanPath (DataPathHelper.GetApplicationFolder (), "ConfigsModTools");
            UtilitiesYAML.SaveToFile (settingsPath, "user_settings.yaml", settings);
        }

        [ShowInInspector]
        [FoldoutGroup (OdinGroup.Name.Settings)]
        [PropertyOrder (OdinGroup.SubOrder.SettingsList)]
        [HideReferenceObjectPicker]
        [HideDuplicateReferenceBox]
        [HideLabel]
        static ModToolsSettings settings;

        #endregion

        // Parts providing a way to add a new mod
        // Wrapped in a subclass to separate utility fields like error strings, reduce the need for top level grouping attributes etc.
        #region ModCreation

        [HideReferenceObjectPicker]
        public class ModSetup
        {
            [FoldoutGroup (OdinGroup.Name.NewMod, false)]
            [HorizontalGroup (OdinGroup.Name.ModName, Order = OdinGroup.Order.ModID)]
            [InfoBoxBottom ("@" + nameof(idError), InfoMessageType.Error, VisibleIf = nameof(IsIDErrorVisible), OverlayColor = "#FFCCCC")]
            [OnValueChanged (nameof(ValidateNewID))]
            [HideLabel, SuffixLabel ("Unique ID & folder name", true)]
            public string id;

            [HorizontalGroup (OdinGroup.Name.ModName, 80f)]
            [EnableIf (nameof(CreationPossible))]
            [Button ("Create", 21)]
            void CreateMod ()
            {
                ValidateNewID ();
                if (!idValid)
                {
                    return;
                }
                if (useAlternateDirectory && !directoryNameValid)
                {
                    return;
                }
                var pathProject = string.IsNullOrEmpty (pathSource) ? folderPathProjectsDefault : pathSource;
                var useAlternate = useAlternateDirectory && !string.IsNullOrEmpty (directoryName);
                var key = useAlternate ? directoryName : id;
                pathProject = DataPathHelper.GetCombinedCleanPath(pathProject, key);
                if ((useAlternate || pathProject != folderPathProjectsDefault) && !Directory.Exists (pathProject))
                {
                    Directory.CreateDirectory (pathProject);
                }
                var modData = new DataContainerModData ()
                {
                    key = key,
                    projectPath = pathProject,
                    metadata = new ModMetadata
                    {
                        id = id,
                        gameVersionMin = "2.0",
                        ver = "0.1",
                        name = "New Mod Name",
                        desc = descDefault
                    }
                };

                SaveMod (modData);
                mods[id] = modData;
                modSelectedID = id;

                id = string.Empty;
                idError = null;
                idValid = true;
                directoryName = "";
                directoryNameValid = true;
                directoryNameError = "";
                useAlternateDirectory = false;
            }

            [VerticalGroup (OdinGroup.Name.SourcePath, Order = OdinGroup.Order.SourcePath)]
            [ValueDropdown (nameof(GetSourcePaths), FlattenTreeView = true)]
            [LabelText ("Source Directory")]
            public string pathSource;

            [VerticalGroup (OdinGroup.Name.SourcePath)]
            [OnValueChanged (nameof(PopulateDirectoryName))]
            public bool useAlternateDirectory;

            [VerticalGroup (OdinGroup.Name.SourcePath)]
            [ShowIf(nameof(useAlternateDirectory))]
            [OnValueChanged (nameof(ValidateDirectoryName))]
            [InfoBoxBottom ("@" + nameof(directoryNameError), InfoMessageType.Error, VisibleIf = nameof(IsDirectoryNameErrorVisible), OverlayColor = "#FFCCCC")]
            public string directoryName;

            void ValidateNewID ()
            {
                idValid = ModToolsHelper.ValidateModID (id, null, mods, out idError);
            }

            void PopulateDirectoryName ()
            {
                if (useAlternateDirectory && string.IsNullOrEmpty (directoryName))
                {
                    directoryName = id;
                }
            }

            void ValidateDirectoryName ()
            {
                directoryNameValid = ModToolsHelper.ValidateModID (directoryName, null, null, out var err);
                if (err.StartsWith ("Mod ID"))
                {
                    err = "Directory name" + err.Substring("Mod ID".Length);
                }
                directoryNameError = err;
            }

            List<string> GetSourcePaths () => DataManagerMod.folderPathsProjects;

            bool CreationPossible => !string.IsNullOrEmpty (id) && idValid && (!useAlternateDirectory || (!string.IsNullOrEmpty (directoryName) && directoryNameValid));
            bool IsIDErrorVisible => !idValid && !string.IsNullOrEmpty (idError);
            bool IsDirectoryNameErrorVisible => !directoryNameValid && !string.IsNullOrEmpty (directoryNameError);

            bool idValid;
            string idError;
            bool directoryNameValid = true;
            string directoryNameError;

            const string descDefault = "Enter your description here. You can use some BBCode tags here, such as [b]bold[/b], [i]italic[/i] and [u]underlined[/u] text.\n\nYou can also embed more links if the URL field above is not enough:\n- [url=www.google.com][u]Example text[/u][/url]";

            static class OdinGroup
            {
                public static class Name
                {
                    public const string NewMod = "New Mod";
                    public const string ModName = NewMod + "/Name";
                    public const string SourcePath = NewMod + "/Path";
                }

                public static class Order
                {
                    public const float ModID = 0f;
                    public const float SourcePath = 1f;
                }
            }
        }

        [ShowInInspector, HideLabel]
        public static readonly ModSetup modSetup = new ModSetup ();

        #endregion

        // Parts providing options for selected mod (save/load/delete/rename/duplicate and more)
        // Wrapped in a subclass to separate utility fields like error strings, reduce the need for top level grouping attributes etc.
        #region ModOptions

        [HideReferenceObjectPicker]
        public class ModOptions
        {
            [HorizontalGroup (OdinGroup.Name.LoadSave, 0.3333f)]
            [Button (SdfIconType.JournalArrowUp, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Reload")]
            public static void LoadProjectSelected ()
            {
                var id = modSelectedID;
                LoadProject (id);
            }

            static void LoadProject (string id)
            {
                if (string.IsNullOrEmpty (id))
                {
                    Debug.LogWarning ($"Failed to load mod: no ID provided");
                    return;
                }

                var projectPathFound = modsLoadedPaths.TryGetValue (id, out var projectPath);
                if (!projectPathFound)
                {
                    Debug.LogWarning ($"Failed to load mod {id}: no recorded path exists, try to Load All again...");
                    return;
                }

                var filePath = DataPathHelper.GetCombinedCleanPath (projectPath, filenameMain + extensionYAML);
                // Debug.Log ($"Loading project {id} | Cached project file path: {filePath}");

                var modData = UtilitiesYAML.LoadDataFromFile<DataContainerModData> (filePath, true, false);
                if (modData == null)
                {
                    Debug.LogWarning ($"Can't load project: data not found using ID {id} | Full path: {filePath}");
                    return;
                }

                modData.projectPath = projectPath;
                modData.OnAfterDeserialization (id);

                if (mods.ContainsKey (id))
                {
                    Debug.Log ($"Reloaded project {id} | Full path: {filePath}");
                }
                else
                {
                    Debug.Log ($"Loaded new project {id} | Full path: {filePath}");
                }
                mods[id] = modData;
                ModdedDatabase.Find (modData, DataManagerMod.moddedDatabases);
            }

            [HorizontalGroup (OdinGroup.Name.LoadSave, 0.3333f)]
            [Button (SdfIconType.JournalArrowDown, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Save")]
            public static void SaveProjectSelected ()
            {
                var modData = modSelected;
                SaveProject (modData);
            }

            public static void SaveProject (DataContainerModData modData)
            {
                if (modData == null)
                {
                    Debug.LogWarning ("Can't save project: nothing selected");
                    return;
                }

                var projectPath = modData.projectPath;
                if (string.IsNullOrEmpty (projectPath))
                {
                    Debug.LogWarning ($"Can't save project {modData.id}: project path doesn't exist");
                    return;
                }

                var id = modData.key;
                if (!ModToolsHelper.ValidateModID (id, modData, mods, out var idError))
                {
                    if (string.IsNullOrEmpty (idError))
                    {
                        Debug.LogWarning ($"Can't save project: invalid ID \"{id}\"");
                    }
                    else
                    {
                        Debug.LogWarning ($"Can't save project: \"{id}\" {idError}");
                    }
                    return;
                }

                if (!Directory.Exists (projectPath))
                {
                    if (projectPath.Contains (folderPathProjectsDefault))
                    {
                        // If a user is using the default project path, the path would be inside PhantomBrigade user folder.
                        // It should be ok to auto-create one: if something goes wrong with the operation, the consequences might be limited
                        UtilitiesYAML.PrepareClearDirectory (projectPath, true, false);
                        Debug.Log ("Created mod project folder: " + projectPath);
                    }
                    else
                    {
                        // I'm fairly uncomfortable with the idea of ever automatically creating folder outside of PB user folder.
                        // Someone unfamiliar with the tools can accidentally create folders in unintended places or worse.
                        // If a user is using a custom path, they likely used a picker UI and directory already exists (e.g. for a Git repo).
                        Debug.LogWarning ("Couldn't save metadata.yaml, project folder doesn't exist: " + projectPath);
                        return;
                    }
                }

                modData.OnBeforeSerialization ();

                var filePath = DataPathHelper.GetCombinedCleanPath (modData.projectPath, filenameMain + extensionYAML);
                Debug.Log ($"Saving mod {id}: {modData.projectPath}\n{filePath}");
                UtilitiesYAML.SaveToFile (filePath, modData);
            }

            [HorizontalGroup (OdinGroup.Name.LoadSave)]
            [Button (SdfIconType.JournalX, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Delete")]
            [PropertySpace (0f, 3f)]
            public static void DeleteProjectSelected ()
            {
                var modData = modSelected;
                DeleteProject (modData);
            }

            static void DeleteProject (DataContainerModData modData)
            {
                if (modData == null)
                {
                    Debug.LogWarning ("Can't delete project: nothing selected | Selected ID: " + modSelectedID);
                    return;
                }

                var projectPath = modData.GetModPathProject ();
                if (!Directory.Exists (projectPath))
                {
                    Debug.LogWarning ($"Can't delete project | {modSelectedID} has no valid path or path doesn't exist: {projectPath}");
                    return;
                }

                var pathGit = DataPathHelper.GetCombinedCleanPath (projectPath, ".git");
                if (Directory.Exists (pathGit))
                {
                    var textWarning = "This project appears to be a git repo. Are you sure you want to permanently delete it?";
                    textWarning += "\n\nMod ID: " + modData.id;
                    textWarning += "\nProject folder: " + projectPath;
                    if (!EditorUtility.DisplayDialog ("Delete Mod Project", textWarning, "Confirm", "Cancel"))
                    {
                        return;
                    }
                }

                var id = modData.key;
                var text = $"Are you sure you'd like to delete this mod project (ID {modData.id}) in its entirety? This operation can not be reverted.";

                text += "\n\nProject folder: " + projectPath;
                text += "\n- Includes project.yaml with this config, metadata.yaml and all mod files.";

                var configPath = modData.GetModPathConfigs ();
                if (Directory.Exists (configPath))
                {
                    text += "\n- Includes the Configs folder storing any changes made to game databases.";
                }
                if (!EditorUtility.DisplayDialog ("Delete Mod Project", text, "Confirm", "Cancel"))
                {
                    return;
                }

                EditorCoroutineUtility.StartCoroutineOwnerless (DeleteProjectFolderIE (projectPath));

                mods.Remove (id);
                modSelectedID = "";
            }

            [ShowInInspector]
            [FoldoutGroup (OdinGroup.Name.Rename, false, Order = OdinGroup.Order.Rename)]
            [HorizontalGroup (OdinGroup.Name.RenameButtons)]
            [InfoBoxBottom ("@" + nameof(idError), InfoMessageType.Error, VisibleIf = nameof(isIDErrorVisible), OverlayColor = "#FFCCCC")]
            [OnValueChanged (nameof(OnIDChange))]
            [HideLabel, SuffixLabel ("New ID", true)]
            public static string idNew;

            bool idValid;
            string idError;
            bool isIDErrorVisible => !idValid && !string.IsNullOrEmpty (idError);

            void OnIDChange ()
            {
                if (!string.IsNullOrEmpty (idNew) && idNew == modSelectedID)
                {
                    idValid = false;
                    idError = null;
                }
                else
                    idValid = ModToolsHelper.ValidateModID (idNew, modSelected, mods, out idError);
            }

            [HorizontalGroup (OdinGroup.Name.RenameButtons, 80f)]
            [EnableIf (nameof (idValid))]
            [Button ("Rename", 21)]
            public static void RenameConfigSelected ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    Debug.LogWarning ("Can't rename project: nothing selected");
                    return;
                }
                if (idNew == modData.id)
                {
                    Debug.LogWarning ("Can't rename project: no new name provided");
                    return;
                }
                if (!ModToolsHelper.ValidateModID (idNew, modData, mods, out var idError))
                {
                    if (string.IsNullOrEmpty (idError))
                    {
                        Debug.LogWarning ($"Can't rename project: invalid ID \"{idNew}\"");
                    }
                    else
                    {
                        Debug.LogWarning ($"Can't rename project: \"{idNew}\" {idError}");
                    }
                    return;
                }

                var idOld = modData.id;
                if (modData.id == modData.key)
                {
                    var (moved, pathNew) = MoveProject (modData, idNew);
                    if (!moved)
                    {
                        return;
                    }
                    modsLoadedPaths.Remove (idOld);
                    modsLoadedPaths[idNew] = pathNew;
                    modData.key = idNew;
                }
                else
                {
                    modsLoadedPaths[idNew] = modsLoadedPaths[idOld];
                    modsLoadedPaths.Remove (idOld);
                }

                mods.Remove (idOld);
                modData.metadata.id = idNew;
                mods[idNew] = modData;
                modSelectedID = idNew;

                SaveProject (modData);
                LoadProject (modData.id);
            }

            static (bool, string) MoveProject (DataContainerModData modData, string keyNew)
            {
                var keyOld = modData.key;
                var pathSource = modData.GetModPathProject ();

                if (!Directory.Exists (pathSource))
                {
                    return (false, "");
                }
                var pathTarget = DataPathHelper.GetCombinedCleanPath (Path.GetDirectoryName (pathSource), keyNew);
                if (Directory.Exists (pathTarget))
                {
                    Debug.LogWarning ($"Can't move project from \"{keyOld}\" to \"{keyNew}\": directory with the same name discovered on disk | Full path: {pathSource}");
                    return (false, "");
                }

                try
                {
                    Debug.Log ($"Trying to move folder:\n- Source: {pathSource}\n- Target: {pathTarget}");
                    Directory.Move (pathSource, pathTarget);
                    return (true, pathTarget);
                }
                catch (IOException ioe)
                {
                    Debug.LogError ("Key not changed -- error while renaming mod project directory: " + ioe.Message);
                }
                return (false, "");
            }

            [HorizontalGroup (OdinGroup.Name.RenameButtons, 80f)]
            [EnableIf (nameof(idValid))]
            [Button ("Duplicate", 21)]
            public static void DuplicateConfigSelected ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    Debug.LogWarning ("Can't duplicate project: nothing selected");
                    return;
                }
                if (!ModToolsHelper.ValidateModID (idNew, modData, mods, out var idError))
                {
                    if (string.IsNullOrEmpty (idError))
                    {
                        Debug.LogWarning ($"Can't duplicate project: invalid ID \"{idNew}\"");
                    }
                    else
                    {
                        Debug.LogWarning ($"Can't duplicate project: \"{idNew}\" {idError}");
                    }
                    return;
                }

                var idOld = modData.key;
                var sourcePath = modData.GetModPathProject ();
                var targetPath = DataPathHelper.GetCombinedCleanPath (Path.GetDirectoryName (sourcePath), idNew);

                if (!Directory.Exists (sourcePath))
                {
                    Debug.LogWarning ($"Can't duplicate project from ID \"{idOld}\" to \"{idNew}\": source directory not found on disk | Full path: {sourcePath}");
                    return;
                }

                if (Directory.Exists (targetPath))
                {
                    Debug.LogWarning ($"Can't duplicate project from ID \"{idOld}\" to \"{idNew}\": target directory with the same name discovered on disk | Full path: {targetPath}");
                    return;
                }

                UtilitiesYAML.CopyDirectory (sourcePath, targetPath, true);

                var modDataCopy = UtilitiesYAML.CloneThroughYaml (modData);
                modDataCopy.key = idNew;
                modDataCopy.metadata = UtilitiesYAML.CloneThroughYaml (modData.metadata);
                modDataCopy.metadata.id = idNew;
                modDataCopy.SyncMetadata ();

                if (modDataCopy.workshop != null)
                {
                    modDataCopy.workshop.publishedID = "";
                }
                mods[idNew] = modDataCopy;
                modsLoadedPaths[idNew] = targetPath;
                modSelectedID = idNew;

                SaveProject (modDataCopy);
                LoadProject (modDataCopy.id);
            }

            [ShowInInspector]
            [HorizontalGroup (OdinGroup.Name.RenameMove)]
            [InfoBoxBottom ("@" + nameof(dirNameError), InfoMessageType.Error, VisibleIf = nameof(isDirNameErrorVisible), OverlayColor = "#FFCCCC")]
            [OnValueChanged (nameof(OnDirectoryNameChange))]
            [HideLabel, SuffixLabel ("New Folder", true)]
            public static string directoryNameNew;

            bool dirNameValid;
            string dirNameError;
            bool isDirNameErrorVisible => !dirNameValid && !string.IsNullOrEmpty (dirNameError);

            void OnDirectoryNameChange ()
            {
                dirNameValid = ModToolsHelper.ValidateModID (directoryNameNew, modSelected, mods, out var error);
                if (error.StartsWith ("Mod ID"))
                {
                    error = "Directory name " + error.Substring("Mod ID".Length);
                }
                dirNameError = error;
            }

            [HorizontalGroup (OdinGroup.Name.RenameMove, 80f)]
            [EnableIf (nameof(dirNameValid))]
            [Button ("Move", 21)]
            public static void MoveConfigSelected ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    Debug.LogWarning ("Can't move project: nothing selected");
                    return;
                }
                if (directoryNameNew == modData.key)
                {
                    Debug.LogWarning ("Can't move project: no new name provided");
                    return;
                }
                var (ok, pathNew) = MoveProject (modSelected, directoryNameNew);
                if (!ok)
                {
                    return;
                }
                modsLoadedPaths.Remove (modData.id);
                modsLoadedPaths[modData.id] = pathNew;
                modData.key = directoryNameNew;
                modData.projectPath = pathNew;
            }

            [HorizontalGroup (OdinGroup.Name.EditingExport, 0.3333f)]
            [ShowIf (nameof(IsConfigSetupAllowed))]
            [GUIColor ("@ModToolsColors." + nameof (ModToolsColors.HighlightNeonBlue))]
            [PropertyTooltip ("Copy config databases from SDK to project folder. Do this if you want to create config overrides or edit levels.")]
            [Button (SdfIconType.Intersect, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Setup config editing")]
            public static void SetupConfigs ()
            {
                var modData = modSelected;
                modData?.EnableConfigs ();
            }

            [HorizontalGroup (OdinGroup.Name.EditingExport, 0.3333f)]
            [ShowIf (nameof(IsConfigEntryAllowed))]
            [GUIColor ("@ModToolsColors." + nameof (ModToolsColors.HighlightNeonGreen))]
            [PropertyTooltip ("Switches all databases to config files copied into your mod project folder, enabling config editing")]
            [Button (SdfIconType.FileEarmarkTextFill, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Enter config editing")]
            public static void SelectForEditing ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    return;
                }
                modData.RefreshConfigsVersion ();
                DataContainerModData.selectedMod = modSelected;
                InitializeExternalAssemblies ();
                ResetArea ();
                ResetDBs ();
            }

            [HorizontalGroup (OdinGroup.Name.EditingExport, 0.3333f)]
            [ShowIf (nameof(IsConfigExitAllowed))]
            [GUIColor ("@ModToolsColors." + nameof(ModToolsColors.HighlightSelectedMod))]
            [PropertyTooltip ("Disables database editing, switching the editor back to reading backed up canonical Configs from the SDK folder.")]
            [Button (SdfIconType.FileX, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Exit config editing")]
            public static void DeselectForEditing ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    return;
                }
                modData.RefreshConfigsVersion ();
                DataContainerModData.selectedMod = null;
                CheckLoadedExternalAssemblies ();
                ResetArea ();
                ResetDBs ();
            }

            static bool IsConfigSetupAllowed () => modSelected != null
               && modSelected.hasProjectFolder
               && !Directory.Exists (modSelected.GetModPathConfigs ());
            public static bool IsConfigExitAllowed () => DataContainerModData.selectedMod != null;
            public static bool IsConfigEntryAllowed () => DataContainerModData.selectedMod == null
                && modSelected != null
                && modSelected.hasProjectFolder
                && Directory.Exists (modSelected.GetModPathConfigs ());

            [HorizontalGroup (OdinGroup.Name.EditingExport, 0.3333f)]
            [PropertyTooltip ("Export the mod into the user folder, allowing you to test it the next time you start the game.\n\nBefore the export, the original data will be compared to the data in the mod project folder: only the modified files will be exported. Make sure to check the appropriate metadata fields.")]
            [Button (SdfIconType.Boxes, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Export to user")]
            public static void ExportToUserFolder ()
            {
                var modData = modSelected;
                if (modData != null)
                {
                    ModToolsExperimental.GenerateModFiles (modSelected, () =>
                    {
                        modData.ExportToUserFolderFinalize ();
                        ModdedDatabase.Find (modData, DataManagerMod.moddedDatabases);
                    });
                }
            }

            [HorizontalGroup (OdinGroup.Name.EditingExport)]
            [PropertySpace (0f, 3f)]
            [PropertyTooltip ("Package the mod into a .zip file, allowing you to share it with other players.\n\nBefore the export, the original data will be compared to the data in the mod project folder: only the modified files will be exported. Make sure to check the appropriate metadata fields.")]
            [Button (SdfIconType.BoxSeam, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Export to archive")]
            public static void ExportToArchive ()
            {
                var modData = modSelected;
                if (modData != null)
                {
                    ModToolsExperimental.GenerateModFiles (modSelected, () =>
                    {
                        modData.ExportToArchiveFinalize ();
                        ModdedDatabase.Find (modData, DataManagerMod.moddedDatabases);
                    });
                }
            }

            [FoldoutGroup(OdinGroup.Name.Utilities)]
            [HorizontalGroup (OdinGroup.Name.UtilityButtons1)]
            [EnableIf (nameof(IsConfigEntryAllowed))]
            [PropertyTooltip ("Replace the Configs folder with the original files from the SDK. Equivalent to setting up config editing for the first time.")]
            [Button (SdfIconType.FileEarmarkBreakFill, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Reset all configs")]
            public static void ResetConfigs ()
            {
                var modData = modSelected;
                if (modData == null)
                    return;

                var projectPath = modData.GetModPathProject ();
                if (!EditorUtility.DisplayDialog
                (
                    "Reset configs from SDK",
                    $"Are you sure you'd like to replace the Configs folder in the selected mod (ID {modData.id}) with the original files from the SDK? This operation can not be reverted. Back up your changes if you are not sure.\n\nProject folder: \n{projectPath}",
                    "Confirm",
                    "Cancel")
                )
                {
                    return;
                }

                ModToolsExperimental.CopyConfigsFromSDK (modData);
                DeselectForEditing ();
            }

            [HorizontalGroup (OdinGroup.Name.UtilityButtons1)]
            [PropertyTooltip ("Export the mod files into the mod project folder without taking any additional step (no export to user folder, archive or Workshop).")]
            [Button (SdfIconType.Box, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Export to source")]
            public static void ExportSimple ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    return;
                }
                ModToolsExperimental.GenerateModFiles (modSelected, () => ModdedDatabase.Find (modData, DataManagerMod.moddedDatabases));
            }

            [HorizontalGroup (OdinGroup.Name.UtilityButtons2)]
            [PropertyTooltip ("Import files from an exported mod into this mod project. An inverse of the standard export operations.")]
            [Button (SdfIconType.Files, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Import from mod")]
            public static void ImportFromFolder ()
            {
                var modData = modSelected;
                if (modData == null)
                   return;

                var pathProject = modData.GetModPathProject ();
                var pathSelected = EditorUtility.OpenFolderPanel ("Select Folder", pathProject, "");
                ModToolsExperimental.CopyConfigsFromExportedMod (modData, pathSelected);
            }

            [HorizontalGroup (OdinGroup.Name.UtilityButtons2)]
            [PropertyTooltip ("Import files from an exported mod into this mod project. An inverse of the standard export operations.")]
            [Button (SdfIconType.Files, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Import from zip")]
            public static void ImportFromZipFile ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    return;
                }
                var pathProject = modData.GetModPathProject ();
                var pathSelected = EditorUtility.OpenFilePanel ("Select Folder", pathProject, "zip");
                ModToolsExperimental.CopyConfigsFromZippedMod (modData, pathSelected);
            }

            [HorizontalGroup (OdinGroup.Name.UtilityButtons2)]
            [PropertyTooltip ("Import files from localized edits.")]
            [Button (SdfIconType.Files, IconAlignment.LeftEdge, ButtonHeight = 32, Name = "Import localizations")]
            public static void ImportLocalizations ()
            {
                var modData = modSelected;
                if (modData == null)
                {
                    return;
                }
                var pathProject = modData.GetModPathProject ();
                ModToolsExperimental.ImportLocalizationEdits (modData, pathProject);
            }

            public void OnSelectionChange ()
            {
                // Reset inputs
                idNew = modSelected != null ? modSelected.id : "";
                directoryNameNew = modSelected != null ? modSelected.key : "";
                OnIDChange ();
            }

            static class OdinGroup
            {
                public static class Name
                {
                    public const string EditingExport = nameof(EditingExport);
                    public const string LoadSave = nameof(LoadSave);
                    public const string Rename = nameof(Rename);
                    public const string RenameButtons = Rename + "/Buttons";
                    public const string RenameMove = Rename + "/Move";
                    public const string Utilities = nameof(Utilities);
                    public const string UtilityButtons1 = Utilities + "/Buttons1";
                    public const string UtilityButtons2 = Utilities + "/Buttons2";
                }

                public static class Order
                {
                    public const float Rename = -1f;
                }
            }
        }


        [ShowInInspector]
        [BoxGroup (OdinGroup.Name.ModOpts, false, Order = OdinGroup.Order.ModOpts)]
        [ShowIf (nameof(IsModSelectionVisible))]
        [HideLabel]
        public static readonly ModOptions modOptions = new ModOptions ();

        #endregion

        // Parts backing and displaying the selected mod
        #region ModSelected

        static readonly SortedDictionary<string, string> modsLoadedPaths = new SortedDictionary<string, string> ();
        static readonly SortedDictionary<string, DataContainerModData> mods = new SortedDictionary<string, DataContainerModData> ();

        public static string GetModCountText () => (mods?.Count ?? 0).ToString ();
        public static IEnumerable<string> GetModKeys () => mods?.Keys;
        public static bool IsModSelectionVisible () => modSelected != null;
        public static bool IsModSelectionPossible () => SteamWorkshopHelper.IsUtilityOperationAvailable;
        public static string GetModSelectionTitle () => modSelected != null ? "Selected mod" : "No mod selected";
        public static Color GetSelectedKeyColor () => DataContainerModData.selectedMod != null && DataContainerModData.selectedMod == modSelected
            ? DataContainerModData.colorSelected
            : Color.white;

        [ShowInInspector]
        [PropertyOrder (OdinGroup.Order.ModSelector)]
        [EnableIf (nameof(IsModSelectionPossible))]
        [ValueDropdown (nameof (GetModKeys))]
        [OnValueChanged (nameof(OnChangeModSelected))]
        [Title ("Selected mod")]
        [HideLabel, SuffixLabel ("$" + nameof (GetModCountText)), GUIColor (nameof (GetSelectedKeyColor))]
        public static string modSelectedID
        {
            get => modSelectedIDInternal;
            set
            {
                // Disable config edits on changes to this key
                DataContainerModData.selectedMod = null;
                modSelectedIDInternal = value;
                modOptions.OnSelectionChange ();
            }
        }

        static string modSelectedIDInternal;

        [ShowInInspector]
        [BoxGroup (OdinGroup.Name.ModSelected, false, false, OdinGroup.Order.ModSelected)]
        [ShowIf (nameof(IsModSelectionVisible))]
        [HideLabel, HideReferenceObjectPicker, HideDuplicateReferenceBox]
        public static DataContainerModData modSelected
        {
            get => !string.IsNullOrEmpty (modSelectedID) && mods != null && mods.TryGetValue (modSelectedID, out var value)
                ? value
                : null;
            set
            {
                // Hack to prevent Odin from treating value as read-only.
            }
        }

        static void OnChangeModSelected ()
        {
            if (modSelected == null)
            {
                return;
            }
            ModdedDatabase.Find (modSelected, moddedDatabases);
        }

        #endregion

        #region Modded Databases
        #if PB_MODSDK
        sealed class ModdedDatabase
        {
            [PropertyOrder (OdinGroup.SubOrder.ModdedDatabaseSelect)]
            [TableColumnWidth (64, false)]
            [Button]
            public void Select ()
            {
                if (!ModOptions.IsConfigExitAllowed ())
                {
                    ModOptions.SelectForEditing ();
                }
                UnityEditor.Selection.activeObject = database;
            }

            [PropertyOrder (OdinGroup.SubOrder.ModdedDatabaseName)]
            [ReadOnly]
            public string name;

            public static void Find (DataContainerModData modData, List<ModdedDatabase> databases)
            {
                databases.Clear ();

                var pathMod = modData.GetModPathProject ();
                var pathOverrides = DataPathHelper.GetCombinedCleanPath (pathMod, DataContainerModData.overridesFolderName);
                var dirOverrides = new DirectoryInfo (pathOverrides);
                if (!dirOverrides.Exists)
                {
                    return;
                }
                var overrides = new HashSet<string> ();
                foreach (var fi in dirOverrides.EnumerateFiles ("*.yaml", SearchOption.AllDirectories))
                {
                    var pathRelative = fi.FullName.Substring (pathOverrides.Length + 1);
                    pathRelative = pathRelative.Substring (0, pathRelative.Length - fi.Name.Length);
                    var typeName = DataPathUtility.GetDataTypeFromPath (pathRelative);
                    if (typeName == null)
                    {
                        Debug.LogWarning ("Unable to resolve path to type: " + pathRelative);
                        continue;
                    }
                    var dataType = FieldReflectionUtility.GetTypeByName (typeName);
                    if (dataType == null)
                    {
                        Debug.LogWarning ("Unable to resolve type name: " + typeName);
                        continue;
                    }
                    var dml = UtilityDatabaseSerialization.GetMultiLinkerForContainer (dataType);
                    if (dml == null)
                    {
                        var dlc = UtilityDatabaseSerialization.GetComponentForDataType (dataType);
                        if (dlc == null)
                        {
                            Debug.LogWarning ("Component for type not found: " + typeName);
                            continue;
                        }
                        if (pathRelative.StartsWith ("Data/"))
                        {
                            pathRelative = "Global/" + pathRelative.Substring ("Data".Length);
                        }
                        else if (pathRelative.StartsWith ("DataDecomposed/"))
                        {
                            pathRelative = "Collections/" +  pathRelative.Substring ("DataDecomposed/".Length);
                        }
                        databases.Add (new ModdedDatabase (
                            pathRelative.TrimEnd ('/'),
                            dlc
                        ));
                        continue;
                    }
                    if (!dml.IsModdable ())
                    {
                        continue;
                    }
                    if (dml.IsUsingDirectories ())
                    {
                        pathRelative = Path.GetDirectoryName (pathRelative.TrimEnd ('/'));
                    }
                    if (!overrides.Add (pathRelative))
                    {
                        continue;
                    }
                    databases.Add (new ModdedDatabase (
                        pathRelative.Replace("DataDecomposed", "Collections").TrimEnd ('/'),
                        (Component)dml
                    ));
                }
                Debug.Log ("Modded databases: " + databases.Count);
            }

            public ModdedDatabase (string name, Component database)
            {
                this.name = name;
                this.database = database;
            }

            readonly Component database;
        }

        [BoxGroup (OdinGroup.Name.ConfigOverrides, VisibleIf = nameof(showConfigOverrides), Order = OdinGroup.Order.ConfigOverrides)]
        [PropertyOrder (OdinGroup.SubOrder.ConfigOverridesSearch)]
        [Button (SdfIconType.Search, IconAlignment.LeftOfText, ButtonHeight = 32, Name = "Search")]
        public void FindModdedDatabases ()
        {
            ModToolsExperimental.GenerateModFiles (modSelected, () => ModdedDatabase.Find (modSelected, moddedDatabases));
        }

        [ShowInInspector]
        [BoxGroup(OdinGroup.Name.ConfigOverrides)]
        [PropertyOrder (OdinGroup.SubOrder.ConfigOverridesList)]
        [TableList (IsReadOnly = true, ShowPaging = false, HideToolbar = true, DrawScrollView = true, AlwaysExpanded = true)]
        static readonly List<ModdedDatabase> moddedDatabases = new List<ModdedDatabase> ();

        static bool showConfigOverrides => modSelected != null
            && modSelected.hasProjectFolder
            && Directory.Exists (modSelected.GetModPathConfigs ());

        #endif
        #endregion

        [HorizontalGroup (OdinGroup.Name.LoadSaveAll, Order = OdinGroup.Order.LoadSaveAll)]
        [Button (SdfIconType.JournalArrowUp, IconAlignment.LeftOfText, ButtonHeight = 32, Name = "Load all")]
        static void LoadAll ()
        {
            if (settings == null)
            {
                LoadSettings ();
            }
            loadedOnce = true;

            mods.Clear ();
            modsLoadedPaths.Clear ();

            foreach (var path in folderPathsProjects)
            {
                var modsLoaded = UtilitiesYAML.LoadDecomposedDictionary<DataContainerModData>
                (
                    path,
                    logExceptions: true,
                    appendApplicationPath: false,
                    directoryMode: true,
                    directoryModeFilename: filenameMain,
                    forceLowerCase: false
                );

                foreach (var kvp in modsLoaded)
                {
                    var mod = kvp.Value;
                    var projectPath = DataPathHelper.GetCombinedCleanPath (path, kvp.Key);
                    mod.projectPath = projectPath;
                    mod.OnAfterDeserialization (kvp.Key);

                    var id = mod.id;
                    var valid = ModToolsHelper.ValidateModID (id, null, null, out var errorDesc);
                    if (!valid)
                    {
                        Debug.LogWarning ($"Mod {id} at path {path} couldn't be loaded due to invalid name (ID): {errorDesc}");
                        continue;
                    }
                    if (mods.ContainsKey (id))
                    {
                        Debug.LogWarning ($"Mod {id} at path {path} hides existing mod from another path. Consider changing ID of one of the mods...");
                        continue;
                    }

                    mods[id] = mod;
                    modsLoadedPaths[id] = projectPath;
                }
            }

            Debug.Log ("Loaded mod projects: " + mods.Count);
        }

        static readonly Dictionary<string, List<Assembly>> assembliesPerMod = new Dictionary<string, List<Assembly>> ();
        static int assembliesExternalLoaded;
        static bool assembliesExternalInitialized;
        static bool assembliesExternalWarned;

        public static void CheckLoadedExternalAssemblies ()
        {
            if (assembliesExternalInitialized && assembliesExternalLoaded > 0 && !assembliesExternalWarned)
            {
                assembliesExternalWarned = false;
                Debug.LogWarning ("Warning! All external assemblies remain loaded and can't be unloaded for now. Recompile the project or restart it if you need to unlock external .dlls");
            }
        }

        public static void InitializeExternalAssemblies ()
        {
            if (assembliesExternalInitialized)
                return;

            assembliesExternalInitialized = true;
            if (mods == null || mods.Count == 0)
                return;

            foreach (var kvp in mods)
            {
                var modData = kvp.Value;
                if (modData?.libraryDLLs?.files == null || modData.libraryDLLs.files.Count == 0)
                    continue;

                var id = kvp.Key;
                var assemblyList = new List<Assembly> ();
                assembliesPerMod[id] = assemblyList;

                foreach (var file in modData.libraryDLLs.files)
                {
                    var filePath = file.GetFinalPath ();
                    if (!File.Exists (filePath))
                        continue;

                    var filename = Path.GetFileName (filePath);
                    try
                    {
                        var assembly = Assembly.LoadFrom (filePath);
                        assemblyList.Add (assembly);
                        Debug.Log ($"{id} | Attempted loading assembly from {filename} | Success: {assembly.Location}");
                        assembliesExternalLoaded += 1;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError ($"{id} | Attempted loading assembly from {filename} | Failed with exception: {e}");
                    }
                }
            }

            if (assembliesPerMod != null && assembliesPerMod.Count > 0)
            {
                var tagMappings = UtilitiesYAML.GetTagMappings ();
                var tagsPrevious = new HashSet<string> (tagMappings.Keys);
                bool tagsChanged = false;
                Debug.Log ($"Scanning for new YAML tags | Mods with assemblies: {assembliesPerMod.Count} | Initial tags: {tagMappings.Count}");

                foreach (var kvp in assembliesPerMod)
                {
                    string id = kvp.Key;
                    var assemblyList = kvp.Value;
                    if (assemblyList != null && assemblyList.Count > 0)
                    {
                        foreach (var assembly in assemblyList)
                        {
                            UtilitiesYAML.AddTagMappingsHintedInAssembly (assembly);

                            var tagsAfter = new HashSet<string> (tagMappings.Keys);
                            tagsAfter.ExceptWith (tagsPrevious);
                            if (tagsAfter.Count == 0)
                            {
                                Debug.Log ($"No tag changes found after scanning assembly {assembly.FullName}");
                                continue;
                            }

                            Debug.Log ($"Loaded tags from {id} assembly {assembly.FullName}\n{tagsAfter.ToStringMultilineDash ()}");
                            tagsPrevious.UnionWith (tagsAfter);
                            tagsChanged = true;
                        }
                    }
                }

                if (tagsChanged)
                {
                    Debug.Log ("Rebuilding YAML serialization...");
                    UtilitiesYAML.RebuildDeserializer ();
                    UtilitiesYAML.RebuildSerializer ();
                }
            }
        }

        [HorizontalGroup (OdinGroup.Name.LoadSaveAll, Order = OdinGroup.Order.LoadSaveAll)]
        [PropertySpace (0f, 3f)]
        [Button (SdfIconType.JournalArrowDown, IconAlignment.LeftOfText, ButtonHeight = 32, Name = "Save all")]
        static void SaveAll ()
        {
            if (mods == null)
            {
                return;
            }
            foreach (var kvp in mods)
            {
                var modData = kvp.Value;
                if (modData != null)
                {
                    SaveMod (modData);
                }
            }
        }

        static void ResetArea ()
        {
            var obj = FindObjectOfType<AreaManager>();
            if (obj == null)
            {
                return;
            }
            obj.UnloadArea (false);
            DataMultiLinkerCombatArea.selectedArea = null;
        }

        static void ResetDBs ()
        {
            var obj = FindObjectOfType<UtilityDatabaseSerialization>();
            if (obj == null)
            {
                Debug.LogError ("Unable to find gameobject for UtilityDatabaseSerialization");
                return;
            }
            obj.ResetLoadedOnce ();

            DataManagerText.ResetLoadedOnce ();
        }

        static IEnumerator DeleteProjectFolderIE (string projectPath)
        {
            var progressID = Progress.Start ("Delete project folder " + Path.GetFileName (projectPath), "Starting...");
            Progress.SetStepLabel(progressID, "items");
            yield return null;

            var directories = GetProjectSubdirectories (projectPath);
            var count = directories.Count;
            for (var i = 0; i < count; i += 1)
            {
                var d = directories[i];
                var n = d.Substring (projectPath.Length + 1);
                Progress.Report (progressID, i, count, "Deleting " + n);
                Directory.Delete (d, true);
                yield return null;
            }
            Progress.Report (progressID, 0.99f, "Deleting " + projectPath);
            Directory.Delete (projectPath, true);
            yield return null;
            Progress.Finish (progressID);
        }

        static List<string> GetProjectSubdirectories (string projectPath)
        {
            var directories = Directory.GetDirectories (projectPath).ToList();
            var i = 0;
            while (i < directories.Count)
            {
                if (Path.GetFileName (directories[i]) != "Configs")
                {
                    i += 1;
                    continue;
                }
                var configsPath = directories[i];
                directories.RemoveAt (i);
                var dpath = Path.Combine (projectPath, "Configs", "DataDecomposed");
                if (Directory.Exists (dpath))
                {
                    directories.AddRange (Directory.GetDirectories (dpath));
                }
                directories.Add (configsPath);
                break;
            }
            return directories;
        }

        public static void SaveMod (DataContainerModData modData)
        {
            ModOptions.SaveProject (modData);
        }

        public static void SelectObject ()
        {
            if (ins != null)
                UnityEditor.Selection.activeObject = ins;
            else
            {
                var obj = GameObject.FindObjectOfType<DataManagerMod> ();
                if (obj != null)
                    UnityEditor.Selection.activeObject = obj;
            }
        }

        public static List<DataContainerModData> GetConfigEditMods () => mods.Values
            .Where (md => md.hasProjectFolder && Directory.Exists (md.GetModPathConfigs ()))
            .ToList ();

        static class OdinGroup
        {
            public static class Name
            {
                public const string ConfigOverrides = "Config Overrides";
                public const string LoadSaveAll = nameof(LoadSaveAll);
                public const string ModOpts = nameof(ModOpts);
                public const string ModSelected = nameof(ModSelected);
                public const string Settings = nameof(Settings);
                public const string SettingsButtons = Settings + "/Buttons";
            }

            public static class Order
            {
                public const float Title = -100f;
                public const float Settings = -90f;
                public const float LoadSaveAll = -89f;
                public const float ModSelector = 21;
                public const float ModOpts = 23;
                public const float ModSelected = 24;
                public const float ConfigOverrides = 30;
            }

            public static class SubOrder
            {
                public const float ConfigOverridesSearch = 0f;
                public const float ConfigOverridesList = 1f;
                public const float ModdedDatabaseSelect = 0f;
                public const float ModdedDatabaseName = 1f;
                public const float SettingsButtons = 0f;
                public const float SettingsList = 1f;
            }
        }
        #endif
    }
}
