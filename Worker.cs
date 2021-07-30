using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.Win32;
using System.Net.Http;

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

            // Get this from registry
            string? root = System.AppContext.BaseDirectory;
            string fileNameXML = Path.Combine(root, "XMLFileFolderSettings.xml");
            try
            {
                #pragma warning disable CA1416 // Plattformkompatibilität überprüfen
                RegistryKey rb = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                RegistryKey? rb2 = rb.OpenSubKey(@"SOFTWARE\TmT\AlamosConnector");
                if (rb2 != null)
                {
                    foreach (string vName in rb2.GetValueNames())
                    {
                        logger.LogDebug(vName + "||" + rb2.GetValue(vName));
                        if (vName == "FolderSettings")
                        {
                            fileNameXML = rb2.GetValue(vName).ToString();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e.Message);
            }

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
                    fileSWatch.Created += (senderObj, fileSysArgs) => fileSWatch_Created(senderObj, fileSysArgs, customFolder);
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

        private void fileSWatch_Created(object senderObj, FileSystemEventArgs fileSysArgs, CustomFolderSettings folder)
        {
            string tmp = Path.GetTempPath();
            string n = Path.GetFileNameWithoutExtension(fileSysArgs.Name);
            string target = Path.Combine(tmp, String.Format("{0}-{1}.pdf", n, DateTime.Now.ToString("yyyy-MM-dd-HHmm")));
            _logger.LogInformation(String.Format("File found {0}", fileSysArgs.Name));

            File.Copy(fileSysArgs.FullPath, target, true);
            _logger.LogInformation(String.Format("Copy to {0}", target));

            string finalTarget = Path.Combine(folder.TargetFolder, fileSysArgs.Name);
            File.Move(fileSysArgs.FullPath, finalTarget, true);
            _logger.LogInformation(String.Format("Move to {0}", finalTarget));

            if (!String.IsNullOrWhiteSpace(folder.PrinterName))
            {
                this.printPDF(folder.PrinterName, target);
            }
        }

        /// <summary>
        /// List printers by http://localhost:7000/printers/list
        /// </summary>
        /// <param name="printer"></param>
        /// <param name="filename"></param>
        /// <returns></returns>
        private async Task printPDF(string printer, string filename)
        {
            using HttpClient httpClient = new HttpClient(); // If you can, please get a client from IHttpClientFactory instead
            using var formContent = new MultipartFormDataContent();
            using var printerPathContent = new StringContent(printer);
            using var pageRangeContent = new StringContent(string.Empty);
            using var pdfFileContent = new StreamContent(File.OpenRead(filename));

            formContent.Add(printerPathContent, "printerPath");
            formContent.Add(pdfFileContent, "pdfFile", "file.pdf");

            var endpoint = new Uri("http://localhost:7000/print/from-pdf");
            HttpResponseMessage result = await httpClient.PostAsync(endpoint, formContent);
            if (!result.IsSuccessStatusCode)
            {
                string content = await result.Content.ReadAsStringAsync();
                _logger.LogWarning($"Failed to send PDF for PrintService. StatusCode = {result.StatusCode}, Response = {content}");
            }
            else
            {
                _logger.LogInformation($"Print document {filename} with printer {printer}");
            }
        }
    }
}
