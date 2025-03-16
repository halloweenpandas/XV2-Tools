﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using Xv2CoreLib;
using Xv2CoreLib.ACB;
using Xv2CoreLib.EffectContainer;

namespace LB_Mod_Installer.Installer
{
    /// <summary>
    /// Stores parsed and binary files that are destined to be installed.
    /// </summary>
    public class FileCacheManager
    {
        public List<CachedFile> cachedFiles = new List<CachedFile>();
        public string lastSaved;
        public InstallerXml installerXml;

        /// <summary>
        /// Add a parsed file. Will overwrite existing files if path matches.
        /// </summary>
        public void AddParsedFile(string path, object data)
        {
            path = Utils.SanitizePath(path);
            CachedFile existing = GetCachedFile(path);

            if (existing != null)
            {
                if (existing.FileType != CachedFileType.Parsed)
                {
                    throw new InvalidDataException(string.Format("File \"{0}\" is being used as both binary and xml.", path));
                }
                else
                {
                    //Exists so overwrite it.
                    existing.Data = data;
                }
            }
            else
            {
                //Doesn't exist., Add it.
                Add(path, data, CachedFileType.Parsed);
            }
        }

        public void AddStreamFile(string path, ZipArchiveEntry zipEntry, bool allowOverwrite = true)
        {
            //Its a directory, skip
            if (string.IsNullOrWhiteSpace(zipEntry.Name))
                return;

            path = Utils.SanitizePath(path);
            if (GeneralInfo.JungleBlacklist.Contains(Path.GetFileName(path)))
            {
                throw new InvalidDataException(string.Format("File \"{0}\" cannot be copied to the game dir because it is blacklisted.", path));
            }

            CachedFile existing = GetCachedFile(path);

            if (existing != null)
            {
                if (existing.FileType != CachedFileType.Stream)
                {
                    throw new InvalidDataException(string.Format("File \"{0}\" is being used as both binary and xml.", path));
                }
                else
                {
                    //Exists so overwrite it.
                    existing.zipEntry = zipEntry;
                }
            }
            else
            {
                //Doesn't exist., Add it.
                cachedFiles.Add(new CachedFile(path, zipEntry, allowOverwrite));

                if (allowOverwrite)
                {
                    //Only track files if allowOverwrite is true.
                    GeneralInfo.Tracker.AddJungleFile(path);
                }
            }


        }

        /// <summary>
        /// Returns a parsed cachedFile, if it has previously been loaded. Otherwise it returns null.
        /// </summary>
        /// <returns></returns>
        public T GetParsedFile<T>(string path) where T : new()
        {
            path = Utils.SanitizePath(path);
            CachedFile existing = GetCachedFile(path);

            if (existing == null)
            {
                return default(T);
            }
            else
            {
                if (existing.FileType == CachedFileType.Parsed)
                {
                    return (T)existing.Data;
                }
                else
                {
                    //It exists but in binary form, and we are trying to load it from xml...
                    throw new InvalidDataException(string.Format("File \"{0}\" is being used as both binary and xml.", path));
                }
            }
        }

        public object GetParsedFile_NonGeneric(string path)
        {
            path = Utils.SanitizePath(path);
            CachedFile existing = GetCachedFile(path);

            if (existing == null)
            {
                return null;
            }
            else
            {
                if (existing.FileType == CachedFileType.Parsed)
                {
                    return existing.Data;
                }
                else
                {
                    //It exists but in binary form, and we are trying to load it from xml...
                    throw new InvalidDataException(string.Format("File \"{0}\" is being used as both binary and xml.", path));
                }
            }
        }

        private void Add(string path, object data, CachedFileType type, bool allowOverwrite = true)
        {
            path = Utils.SanitizePath(path);
            var cachedFile = new CachedFile(path, data, allowOverwrite);

            if (data.GetType() == typeof(EffectContainerFile))
            {
                EffectContainerFile ecf = EffectContainerFile.New();
                ecf.AddEffects(((EffectContainerFile)data).Effects);
                cachedFile.backupEffectContainerFile = ecf;
            }
            else if (data.GetType() == typeof(ACB_File))
            {
                //Might be better to change this to a shallow-copy
                ACB_File acb = ACB_File.NewXv2Acb();

                foreach (var cue in ((ACB_File)data).Cues)
                {
                    acb.CopyCue((int)cue.ID, (ACB_File)data, false);
                }

                cachedFile.backupBgmFile = acb;
            }

            cachedFiles.Add(cachedFile);
        }

        private CachedFile GetCachedFile(string path)
        {
            path = Utils.SanitizePath(path);

            foreach (var file in cachedFiles)
            {
                if (Path.Equals(file.Path, path)) return file;
            }
            return null;
        }

        public void SaveParsedFiles(MainWindow parent)
        {
            UpdateProgessBarText($"Saving parsed files...", false, 0, false, parent: parent);
            foreach (var file in cachedFiles)
            {
                lastSaved = file.Path + " (xml)";
                Directory.CreateDirectory(Path.GetDirectoryName(GeneralInfo.GetPathInGameDir(file.Path)));

                if (file.FileType == CachedFileType.Parsed && file.Data.GetType() == typeof(EffectContainerFile))
                {
                    string savePath = GeneralInfo.GetPathInGameDir(file.Path);
                    EffectContainerFile ecf = (EffectContainerFile)file.Data;
                    ecf.Directory = Path.GetDirectoryName(savePath);
                    ecf.Name = Path.GetFileNameWithoutExtension(savePath);
                    ecf.saveFormat = Xv2CoreLib.EffectContainer.SaveFormat.EEPK;
                    ecf.Save();
                }
                else if (file.FileType == CachedFileType.Parsed && file.Data.GetType() == typeof(ACB_File))
                {
                    string path = GeneralInfo.GetPathInGameDir(file.Path);
                    string acbPath = string.Format("{0}/{1}", Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                    ACB_File acbFile = (ACB_File)file.Data;
                    acbFile.Save(acbPath, true);
                }
                else if (file.FileType == CachedFileType.Parsed)
                {
                    string path = GeneralInfo.GetPathInGameDir(file.Path);

                    if (file.Data is IIsNull isNull)
                    {
                        //Do not save the file if it has nothing in it, and if it already exists, delete it.
                        if (isNull.IsNull())
                        {
                            try
                            {
                                if (File.Exists(path))
                                    File.Delete(path);
                            }
                            catch { }

                            continue;
                        }
                    }
                    File.WriteAllBytes(path, file.GetBytes());
                }
            }
        }

        public void SaveStreamFiles(MainWindow parent)
        {
            int currentProgress = 1;
            foreach (var file in cachedFiles)
            {
                lastSaved = file.Path + " (binary)";
                Directory.CreateDirectory(Path.GetDirectoryName(GeneralInfo.GetPathInGameDir(file.Path)));

                if (file.FileType == CachedFileType.Stream)
                {
                    file.WriteStream();
                }
                UpdateProgessBarText($"Saving files...", true, currentProgress, false, parent: parent);
                currentProgress++;
            }
        }

        public void RestoreBackups()
        {
            foreach (var file in this.cachedFiles)
            {
                if (!file.alreadyExists && file.backupEffectContainerFile == null)
                {
                    //File didn't exist previously, so delete it
                    if (File.Exists(GeneralInfo.GetPathInGameDir(file.Path)))
                    {
                        File.Delete(GeneralInfo.GetPathInGameDir(file.Path));
                    }
                }
                else if (file.backupEffectContainerFile == null && file.backupBgmFile == null)
                {
                    //Restore backup
                    File.Delete(GeneralInfo.GetPathInGameDir(file.Path));

                    //If backup is null then the file was too big so it was skipped OR it is a stream/binary file
                    if (file.backupBytes != null)
                    {
                        File.WriteAllBytes(GeneralInfo.GetPathInGameDir(file.Path), file.backupBytes);
                    }
                }
                else if (file.backupEffectContainerFile != null)
                {
                    //Restore EEPK
                    //Since backing up every single EEPK file would be problematic, we will just reinstall the original EEPK state instead.

                    string savePath = GeneralInfo.GetPathInGameDir(file.Path);
                    file.backupEffectContainerFile.Directory = Path.GetDirectoryName(savePath);
                    file.backupEffectContainerFile.Name = Path.GetFileNameWithoutExtension(savePath);
                    file.backupEffectContainerFile.saveFormat = Xv2CoreLib.EffectContainer.SaveFormat.EEPK;
                    file.backupEffectContainerFile.Save();
                }
                else if (file.backupBgmFile != null)
                {
                    //Restore CAR_BGM.acb
                    string path = GeneralInfo.GetPathInGameDir(file.Path);
                    path = string.Format("{0}/{1}", Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                    file.backupBgmFile.Save(path, true);
                }
            }
        }

        public void NukeEmptyDirectories(MainWindow parent)
        {
            List<string> dirs = new List<string>();

            int currentProgress = 0;
            foreach (var file in cachedFiles)
            {
                string dir = Path.GetDirectoryName(file.Path);

                if (!dirs.Contains(dir))
                    dirs.Add(dir);

                UpdateProgessBarText($"Nuking empty directories...", false, currentProgress, false, parent: parent);
                currentProgress++;
            }

            currentProgress = 0;
            foreach(var dir in dirs)
            {
                string actualPath = GeneralInfo.GetPathInGameDir(dir);

                if(Directory.GetFiles(actualPath).Length == 0)
                {
                    try
                    {
                        Directory.Delete(actualPath);
                    }
                    catch { }
                }

                UpdateProgessBarText($"Nuking empty directories...", false, currentProgress, false, parent: parent);
                currentProgress++;
            }
        }
        private void UpdateProgessBarText(string text, bool count = true, int currentProgress = -1, bool overwriteShowProgress = false, MainWindow parent = null)
        {
            double percentage = (double)currentProgress / cachedFiles.Count * 100;
            parent.Dispatcher.BeginInvoke((System.Action)(() =>
            {
                if (count)
                    parent.ProgressBar_Label.Content = $"_{text} ({currentProgress}/{cachedFiles.Count})";
                if (!count)
                    parent.ProgressBar_Label.Content = $"_{text}";
            }));
        }
    }

    public class CachedFile
    {
        public string Path;
        public object Data;
        public bool allowOverwrite = true;
        public CachedFileType FileType;
        public ZipArchiveEntry zipEntry;

        public bool alreadyExists = false; //If false and install fails, delete the file from disk
        public byte[] backupBytes = null; //If the file already exists, back it up into this array
        public EffectContainerFile backupEffectContainerFile = null;
        public ACB_File backupBgmFile = null;


        public CachedFile(string _path, object _data, bool _allowOverwrite)
        {
            Path = _path;
            Data = _data;
            allowOverwrite = _allowOverwrite;

            if (File.Exists(GeneralInfo.GetPathInGameDir(Path)))
            {
                alreadyExists = true;
                var fileInfo = new FileInfo(GeneralInfo.GetPathInGameDir(Path));

                if (fileInfo.Length < 500000000)
                {
                    //Only back it up if its less than 500mb (unlikely....)
                    backupBytes = File.ReadAllBytes(GeneralInfo.GetPathInGameDir(Path));
                }
            }
        }

        public CachedFile(string _path, ZipArchiveEntry _zipEntry, bool _allowOverwrite)
        {
            Path = _path;
            zipEntry = _zipEntry;
            allowOverwrite = _allowOverwrite;
            FileType = CachedFileType.Stream;
        }

        public byte[] GetBytes()
        {
            if (FileType == CachedFileType.Parsed)
            {
                return Install.GetBytesFromParsedFile(Path, Data);
            }
            else
            {
                throw new Exception("CachedFile.GetBytes(): Invalid FileType = \n\nUse WriteSteam() for FileType.Stream." + FileType);
            }
        }

        public void WriteStream()
        {
            if (File.Exists(GeneralInfo.GetPathInGameDir(Path)) && !allowOverwrite) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GeneralInfo.GetPathInGameDir(Path)));

            zipEntry.ExtractToFile(GeneralInfo.GetPathInGameDir(Path), true);
        }
    }

    public enum CachedFileType
    {
        Parsed,
        Stream
    }
}
