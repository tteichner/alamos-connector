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
using Telegram.Bot.Types.InputFiles;
using System.Net;

namespace AlamosConnector
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private List<CustomFolderSettings> _listFolders;
        private List<FileSystemWatcher> listFileSystemWatcher;
        private bool _stop = false;
        private CustomFolderSettings customFolder;

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

                // Create an instance of XMLSerializer
                XmlSerializer deserializer = new XmlSerializer(typeof(List<CustomFolderSettings>));
                TextReader reader = new StreamReader(fileNameXML);
                object obj = deserializer.Deserialize(reader);
                reader.Close();

                // Obtain a list of CustomFolderSettings from XML Input data
                _listFolders = obj as List<CustomFolderSettings>;
            }
            catch (Exception e)
            {
                logger.LogWarning(e.Message);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                this._stop = true;
            }
            
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

        private string downloadFtpFile(string name)
        {
            using (WebClient r = new WebClient())
            {
                r.Credentials = new NetworkCredential(customFolder.TargetFolderUser, customFolder.TargetFolderPass);
                string url = $"{customFolder.FolderPath}{name}";
                byte[] fileData = r.DownloadData(url);
                string tmp = Path.GetTempPath();
                string target = Path.Combine(tmp, name);

                using (FileStream file = File.Create(target))
                {
                    file.Write(fileData, 0, fileData.Length);
                    file.Close();
                    _logger.LogInformation($"File downloaded: {target}");
                }

                return target;
            }
        }

        private bool deleteFtpFile(string name)
        {
            var request = this.connect($"{customFolder.FolderPath}{name}");
            request.Method = WebRequestMethods.Ftp.DeleteFile;
            using (FtpWebResponse r2 = (FtpWebResponse)request.GetResponse())
            {
                _logger.LogInformation($"File deleted: {r2.StatusCode}, {r2.StatusDescription}");
                return r2.StatusCode == FtpStatusCode.FileActionOK;
            }
        }

        private FtpWebRequest connect(string path)
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(path);
            request.Credentials = new NetworkCredential(customFolder.TargetFolderUser, customFolder.TargetFolderPass);
            return request;
        }

        /// <summary>Start the file system watcher for each of the file
        /// specification and folders found on the List<>/// </summary>
        private async Task startFileSystemWatcher()
        {
            // Creates a new instance of the list
            this.listFileSystemWatcher = new List<FileSystemWatcher>();
            customFolder = _listFolders[0];

            // Loop the list to process each of the folder specifications found
            DirectoryInfo dir = new DirectoryInfo(customFolder.TargetFolder);
            if (customFolder.FolderEnabled && dir.Exists)
            {
                // Record a log entry into Windows Event Log
                _logger.LogInformation(String.Format("Starting to monitor files with extension ({0}) in the folder ({1})", customFolder.FolderFilter, customFolder.FolderPath));
                while (!this._stop)
                {
                    try
                    {
                        FtpWebRequest request = this.connect(customFolder.FolderPath);
                        request.Method = WebRequestMethods.Ftp.ListDirectory;

                        FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                        Stream responseStream = response.GetResponseStream();
                        StreamReader reader = new StreamReader(responseStream);
                        string names = reader.ReadToEnd();
                        var list = names.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        reader.Close();
                        response.Close();

                        foreach (string name in list)
                        {
                            if (name.EndsWith(customFolder.FolderFilter, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation($"File found: {name}");

                                string target = this.downloadFtpFile(name);
                                _logger.LogInformation($"File downloaded: {target}");

                                this.deleteFtpFile(name);

                                var fi2 = new FileInfo(target);
                                if (String.Compare(fi2.Extension, customFolder.FolderFilter, StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    await this.fileWatch(fi2, customFolder);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogInformation($"File action failed: {e.Message}");
                    }

                    await Task.Delay(7000);
                }
            }
        }

        private async Task fileWatch(FileInfo fileSysArgs, CustomFolderSettings folder)
        {
            string tmp = Path.GetTempPath();
            string n = Path.GetFileNameWithoutExtension(fileSysArgs.Name);
            string target = Path.Combine(tmp, String.Format("{0}-{1}.pdf", n, DateTime.Now.ToString("yyyy-MM-dd-HHmm")));
            _logger.LogInformation(String.Format("File found {0}", fileSysArgs.Name));

            File.Copy(fileSysArgs.FullName, target, true);
            _logger.LogInformation(String.Format("Copy to {0}", target));

            string finalTarget = Path.Combine(folder.TargetFolder, fileSysArgs.Name);
            File.Move(fileSysArgs.FullName, finalTarget, true);
            _logger.LogInformation(String.Format("Move to {0}", finalTarget));

            // print the document with defined printer
            if (!String.IsNullOrWhiteSpace(folder.PrinterName))
            {
                await this.printPDF(folder.PrinterName, target);
            }

            // notify the configured group
            if (!String.IsNullOrWhiteSpace(folder.TelegramBotToken) && !String.IsNullOrWhiteSpace(folder.TelegramBotChannel))
            {
                await this.sendMessage(folder.TelegramBotToken, folder.TelegramBotChannel, "Alarm für die Feuerwehr", target);
            }
        }

        private async Task sendMessage(string token, string destID, string text, string filename)
        {
            try
            {
                var bot = new Telegram.Bot.TelegramBotClient(token);
                var result = await bot.SendTextMessageAsync(destID, text);
                if (result.MessageId < 0)
                {
                    _logger.LogWarning($"Failed to send message to {destID}");
                }
                else
                {
                    _logger.LogInformation($"Sent message to telegram channel {destID}");

                    using (FileStream fs = File.OpenRead(filename))
                    {
                        InputOnlineFile inputOnlineFile = new InputOnlineFile(fs, "Alarmfax.pdf");
                        result = await bot.SendDocumentAsync(destID, inputOnlineFile, null, "Alarmfax");
                        if (result.MessageId < 0)
                        {
                            _logger.LogWarning($"Failed to send file to {destID}");
                        }
                        else
                        {
                            _logger.LogInformation($"Sent pdf to telegram channel {destID}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning($"Failed to send message to {destID}");
                _logger.LogWarning(e.Message);
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
