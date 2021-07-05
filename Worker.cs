using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace AlamosConnector
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private List<CustomFolderSettings> _listFolders;
        private List<FileSystemWatcher> listFileSystemWatcher;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;

            // TODO: Get this from registry
            string fileNameXML = "C:\\Users\\webma\\projects\\AlamosConnector\\XMLFileFolderSettings.xml";

            // Create an instance of XMLSerializer
            XmlSerializer deserializer = new XmlSerializer(typeof(List<CustomFolderSettings>));
            TextReader reader = new StreamReader(fileNameXML);
            object obj = deserializer.Deserialize(reader);
            reader.Close();

            // Obtain a list of CustomFolderSettings from XML Input data
            _listFolders = obj as List<CustomFolderSettings>;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Worker running at: {time}", DateTimeOffset.Now);

                // Start
                if (this.listFileSystemWatcher == null)
                {
                    startFileSystemWatcher();
                }


                await Task.Delay(10000, stoppingToken);
            }
        }

        /// <summary>Start the file system watcher for each of the file
        /// specification and folders found on the List<>/// </summary>
        private void startFileSystemWatcher()
        {
            // Creates a new instance of the list
            this.listFileSystemWatcher = new List<FileSystemWatcher>();
            // Loop the list to process each of the folder specifications found
            foreach (CustomFolderSettings customFolder in _listFolders)
            {
                DirectoryInfo dir = new DirectoryInfo(customFolder.FolderPath);
                // Checks whether the folder is enabled and
                // also the directory is a valid location
                if (customFolder.FolderEnabled && dir.Exists)
                {
                    // Creates a new instance of FileSystemWatcher
                    FileSystemWatcher fileSWatch = new FileSystemWatcher();
                    // Sets the filter
                    fileSWatch.Filter = customFolder.FolderFilter;
                    // Sets the folder location
                    fileSWatch.Path = customFolder.FolderPath;
                    // Sets the action to be executed
                    StringBuilder targetFolder = new StringBuilder(customFolder.TargetFolder);
                    // List of arguments
                    // Subscribe to notify filters
                    fileSWatch.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
                    // Associate the event that will be triggered when a new file
                    // is added to the monitored folder, using a lambda expression                   
                    fileSWatch.Created += (senderObj, fileSysArgs) => fileSWatch_Created(senderObj, fileSysArgs, targetFolder.ToString());
                    // Begin watching
                    fileSWatch.EnableRaisingEvents = true;
                    // Add the systemWatcher to the list
                    listFileSystemWatcher.Add(fileSWatch);

                    // Record a log entry into Windows Event Log
                    // New-EventLog -LogName Application -Source MyApp
                    _logger.LogInformation(String.Format("Starting to monitor files with extension ({0}) in the folder ({1})", fileSWatch.Filter, fileSWatch.Path));
                }
            }
        }

        private void fileSWatch_Created(object senderObj, FileSystemEventArgs fileSysArgs, string targetFolder)
        {
            string tmp = Path.GetTempPath();
            string target = Path.Combine(tmp, fileSysArgs.Name);

            File.Copy(fileSysArgs.FullPath, target, true);
            File.Move(fileSysArgs.FullPath, Path.Combine(targetFolder, fileSysArgs.Name), true);

        }
    }
}
