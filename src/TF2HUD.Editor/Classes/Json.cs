﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using HUDEditor.Models;
using HUDEditor.Properties;
using Newtonsoft.Json;

namespace HUDEditor.Classes
{
    public class Json
    {
        // HUDs to manage
        public HUD[] HUDList;

        public Json()
        {
            var hudList = new List<HUD>();
            foreach (var jsonFile in Directory.EnumerateFiles("JSON"))
            {
                // Extract HUD information from the file path.
                var fileName = jsonFile.Split("\\")[^1];
                var fileInfo = fileName.Split(".");
                var hudName = fileInfo[0];
                var extension = fileInfo[^1];
                if (extension != "json") continue;
                var json = new StreamReader(File.OpenRead(jsonFile), new UTF8Encoding(false)).ReadToEnd();

                // Add the HUD object to the list.
                hudList.Add(new HUD(hudName, JsonConvert.DeserializeObject<HudJson>(json)));
            }

            HUDList = hudList.ToArray();
        }

        public event EventHandler<HUD> SelectionChanged;

        public void SetHUDByName(string name)
        {
            SelectionChanged?.Invoke(this, GetHUDByName(name));
        }

        /// <summary>
        ///     Find and retrieve a HUD object selected by the user.
        /// </summary>
        /// <param name="name">Name of the HUD the user wants to view.</param>
        public HUD GetHUDByName(string name)
        {
            foreach (var hud in HUDList)
                if (string.Equals(hud.Name, name, StringComparison.InvariantCultureIgnoreCase))
                    return hud;
            MainWindow.ShowMessageBox(MessageBoxImage.Error, $"Cannot find HUD {name}!");
            return null;
        }

        /// <summary>
        ///     Synchronize the local HUD schema files with the latest versions on GitHub.
        /// </summary>
        public static bool Update(bool force = false)
        {
            try
            {
                var restartRequired = false;

                // Get the local schema names and file sizes.
                List<Tuple<string, int>> localFiles = new();
                foreach (var file in new DirectoryInfo("JSON").GetFiles().Where(x => x.FullName.EndsWith(".json")))
                    localFiles.Add(new Tuple<string, int>(file.Name.Replace(".json", string.Empty), (int) file.Length));
                if (localFiles.Count <= 0) return false;

                // Setup the WebClient for download remote files.
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                var client = new WebClient();
                client.Headers.Add("User-Agent", "request");
                var remoteList = client.DownloadString(Resources.app_json_list);
                client.Dispose();

                // Get the remote schema names and file sizes.
                List<Tuple<string, int>> remoteFiles = new();
                foreach (var file in JsonConvert.DeserializeObject<List<GitJson>>(remoteList)
                    .Where(x => x.Name.EndsWith(".json")))
                    remoteFiles.Add(new Tuple<string, int>(file.Name.Replace(".json", string.Empty), file.Size));
                if (remoteFiles.Count <= 0) return false;

                // Compare the local and remote files.
                foreach (var (remoteName, remoteSize) in remoteFiles)
                {
                    if (!force)
                    {
                        var downloadFile = true;
                        MainWindow.Logger.Info($"{remoteName}: Checking ...");
                        foreach (var (localName, localSize) in localFiles)
                            if (string.Equals(remoteName, localName))
                            {
                                // The remote file is found locally. Check if the file size has noticeably changed.
                                downloadFile = !Enumerable.Range(remoteSize - 100, remoteSize + 100)
                                    .Contains(localSize);
                                break;
                            }

                        // If the remote file is not found locally, or the size difference is too great - download the latest version.
                        if (!downloadFile)
                        {
                            MainWindow.Logger.Info($"{remoteName}: No updates...");
                            continue;
                        }
                    }

                    var fileName = $"{remoteName}.json";
                    MainWindow.Logger.Info($"{remoteName}: Downloading latest version...");
                    client.DownloadFile(string.Format(Resources.app_json_file, fileName), fileName);
                    client.Dispose();

                    // Move the fresh file into the JSON folder, overwriting the previous version.
                    if (File.Exists(fileName))
                        File.Move(fileName, $"JSON/{fileName}", true);

                    restartRequired = true;
                }

                return restartRequired;
            }
            catch (Exception e)
            {
                // Skip this process is there was an error.
                MainWindow.Logger.Error(e.Message);
                Console.WriteLine(e);
                return false;
            }
        }

        public async Task<bool> UpdateAsync()
        {
            try
            {
                return (await Task.WhenAll(HUDList.Select(async x =>
                {
                    var url = string.Format(Resources.app_json_file, $"{x.Name}.json");
                    MainWindow.Logger.Info($"Requesting {x.Name} from {url}");
                    var response = await Utilities.Fetch(url);
                    if (response == null)
                    {
                        MainWindow.Logger.Info(
                            $"{x.Name}: Received HTTP error, unable to determine whether HUD has been updated!");
                        return false;
                    }

                    var value = JsonConvert.DeserializeObject<HudJson>(response);
                    // Added if !DEBUG to test compare HUDs method
# if !DEBUG
                    File.WriteAllText($"JSON/{x.Name}.json", response);
# endif
                    return !x.TestHUD(new HUD(x.Name, value));
                }))).Contains(true);
            }
            catch (Exception e)
            {
                MainWindow.Logger.Error(e.Message);
                Console.WriteLine(e);
                return false;
            }
        }
    }
}