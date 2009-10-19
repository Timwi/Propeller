using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Propeller;
using RT.Servers;
using RT.Util.Xml;
using RT.Util;
using RT.Util.ExtensionMethods;
using System.IO;
using System.Reflection;

namespace Propeller.Modules
{
    public class DocGen : MarshalByRefObject, IPropellerModule
    {
        public class Settings
        {
            public string Url = "/doc";
            public string[] Paths = new string[] { Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "DocGen") };
        }

        private Settings _settings;
        private DocumentationGenerator _doc;

        public PropellerModuleInitResult Init(string configFilePath, LoggerBase log)
        {
            try
            {
                _settings = XmlClassify.LoadObjectFromXmlFile<Settings>(configFilePath);
            }
            catch (Exception e)
            {
                log.Warn("Error reading configuration file: {0}".Fmt(configFilePath));
                log.Warn(e.Message);
                if (File.Exists(configFilePath))
                {
                    string renameTo = configFilePath;
                    int i = 1;
                    while (File.Exists(renameTo))
                    {
                        i++;
                        renameTo = Path.Combine(Path.GetDirectoryName(configFilePath), Path.GetFileNameWithoutExtension(configFilePath) + " (" + i + ")" + Path.GetExtension(configFilePath));
                    }
                    try
                    {
                        File.Move(configFilePath, renameTo);
                    }
                    catch (Exception e2)
                    {
                        log.Warn("Error renaming configuration file to: {0}".Fmt(renameTo));
                        log.Warn(e2.Message);
                        return null;
                    }
                }
                log.Warn("Creating new configuration file with default values.");
                _settings = new Settings();
                foreach (var path in _settings.Paths)
                    Directory.CreateDirectory(path);
                XmlClassify.SaveObjectToXmlFile(_settings, configFilePath);
            }

            foreach (var invalid in _settings.Paths.Where(d => !Directory.Exists(d)))
                lock (log)
                    log.Warn("Warning: The folder {0} specified in the configuration file {1} does not exist.".Fmt(invalid, configFilePath));

            // Try to clean up old folders we've created before
            var tempPath = Path.GetTempPath();
            foreach (var pth in Directory.GetDirectories(tempPath, "docgen-tmp-*"))
            {
                foreach (var file in Directory.GetFiles(pth))
                    try { File.Delete(file); }
                    catch { }
                try { Directory.Delete(pth); }
                catch { }
            }

            // Find a new folder to put the DLL files into
            int j = 1;
            var copyToPath = Path.Combine(tempPath, "docgen-tmp-" + j);
            while (Directory.Exists(copyToPath))
            {
                j++;
                copyToPath = Path.Combine(tempPath, "docgen-tmp-" + j);
            }
            Directory.CreateDirectory(copyToPath);

            _doc = new DocumentationGenerator(_settings.Paths.Where(d => Directory.Exists(d)).ToArray(), copyToPath);
            log.Info("DocGen initialised successfully.");
            return new PropellerModuleInitResult
            {
                FoldersToMonitor = _settings.Paths,
                HandlerHooks = new HttpRequestHandlerHook[] { new HttpRequestHandlerHook(_settings.Url, _doc.GetRequestHandler()) }
            };
        }

        public void Shutdown()
        {
        }
    }
}
